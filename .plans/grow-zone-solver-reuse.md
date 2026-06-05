# Grow-Zone Solver Reuse Refactor

**Status:** PLANNED - design only; do not implement until the user says to execute it.
**Owner:** Willie placement.
**Scope:** Refactor the landed `GrowZonePlacementSolver` so it reuses Willie's shared placement machinery (`AnchorResolver`, `PlacementEvidence` free-rect search) instead of reimplementing anchors, occupancy, and a brute-force rectangle scan. Fixes a reuse/altitude gap and an efficiency hot-spot found in `/simplify` review of `14ffbcd`.

---

## Goal

`14ffbcd feat(willie): solve grow-zone requests` landed a working, inspect-only grow-zone solver, but it hand-rolls three things Willie already owns:

- **Anchors** — `GrowZonePlacementSolver.ZoneAnchors` ([:162](../Src/Ministers/Willie/GrowZonePlacementSolver.cs)) re-derives storage/kitchen anchors that `AnchorResolver` already resolves.
- **Occupancy** — `BlockedCells` ([:130](../Src/Ministers/Willie/GrowZonePlacementSolver.cs)) re-derives the occupied/zoned cell set that `PlacementEvidence` already builds.
- **Rectangle search** — `EnumerateCandidates` ([:208](../Src/Ministers/Willie/GrowZonePlacementSolver.cs)) brute-forces every `dims × W × H` rect, each re-scanning `width×height` cells, where `PlacementEvidence.BuildFreeRects` already computes maximal empty rectangles in near-linear time.

Cost today: ~250 lines of parallel logic plus two divergent occupancy/scoring semantics to keep in sync, and an `O(dims · W · H · tiles)` scan on a same-cycle `FlagFired` cabinet follow-up. Goal: one shared free-rect/occupancy mechanism parameterized by cell eligibility, with the zone solver reduced to "resolve anchors → build growable evidence → fit tile_count rect in free rects → score → assemble options."

---

## Background — what landed and where the seams are

- `PlacementEvidence.BuildFreeRects` ([:124](../Src/Ministers/Willie/PlacementEvidence.cs)) is the maximal-empty-rectangle engine (`BuildClearRunRight` + `LargestRectFromOrigin` + `ReduceFreeRects`). Its blocked predicate is hard-coded to `occupied.Contains(cell)` ([:159](../Src/Ministers/Willie/PlacementEvidence.cs)). The class even carries a standing TODO: *"terrain affordance still absent; free-space is occupancy-only"* ([:43](../Src/Ministers/Willie/PlacementEvidence.cs)). That TODO is exactly this work.
- `AnchorResolver.ResolveNear(PlacementSpec, WillieBriefing)` ([:26](../Src/Ministers/Willie/Placement/AnchorResolver.cs)) resolves `Near` adjacency hints to room anchors, but is keyed on `PlacementSpec` (building) and only `WillieRoomAnchor` rows — it does not consider stockpile centers, which the zone path anchors on.
- `GrowZonePlacementSolver` output is already `PlacementResult` / `AdviceOption` / `MetricValue`, so it shares the result + scoring value types. The reuse gap is in *evidence and search*, not the result contract.

---

## Locked Decisions

- One free-rect engine, parameterized by a **cell-eligibility predicate** (or blocked-set), not two parallel scanners. The building path passes "not occupied"; the zone path passes "not occupied AND growable AND not zoned/crop/room/stockpile".
- The building `PlacementSolver` path stays **behavior-identical** after the generalization. Existing `PlacementSolver` / `LargestEmptyRectangleGenerator` / `PlacementEvidence` tests must remain green with no fixture edits.
- Zone scoring stays fertility/compactness/anchor-distance with Manhattan distance, labeled as measured. **Do not** introduce path-cost precision the live data does not support (carry-over non-goal from the parent plan).
- The solver stays **inspect-only**: `ApplyReady: Blocked`, no `create_growing_zone` write. Apply is still the parent plan's Slice 5.
- No-compat: if any persisted Willie zone snapshot/trace shape changes, wipe-and-regen; do not add legacy readers.

---

## Slice 1 - Generalize the free-rect engine

Parameterize `PlacementEvidence` free-space search by cell eligibility instead of a fixed occupied-set:

- Change `BuildFreeRects` / `BuildClearRunRight` to take a `Func<MapCell, bool> isBlocked` (or an `IReadOnlySet<MapCell> blocked`) rather than reading `occupied` directly.
- Keep the existing public `Build(map, buildings, anchors, roomAnchors)` overload delegating with `isBlocked = occupied.Contains`, so `PlacementSolver` is untouched.
- Add a second build path (overload or a sibling factory) that accepts an externally computed blocked-set for the zone case.

Alternative if `PlacementEvidence` carries too much building-only state: extract the pure scanner (`BuildClearRunRight` + `LargestRectFromOrigin` + `ReduceFreeRects`) into a standalone `MaximalRectScanner` that both `PlacementEvidence` and the zone evidence call. Prefer whichever keeps the building solver diff smallest.

Tests: existing free-rect tests unchanged; new test that a blocked-mask excludes masked cells from the returned rects.

---

## Slice 2 - Growable zone evidence

Build the zone blocked-set once, reusing existing aggregates rather than the solver's private `BlockedCells`:

- Blocked = building positions + growing-zone cells + stockpile cells/centers + room cells + crop plant positions (the set `BlockedCells` already assembles), **plus** every non-growable terrain cell (`!cell.SupportsGrowing`) and out-of-`TerrainNeed` cells.
- Feed that set into the Slice 1 generalized scanner to get growable free rects directly.
- Search bounds stay Home-area-clamped (reuse the existing `SearchBounds` logic, or fold into the evidence build).

Tests: evidence excludes non-growable and zoned cells; free rects fall only on growable unblocked terrain.

---

## Slice 3 - Anchor reuse

Decouple `AnchorResolver.ResolveNear` from `PlacementSpec` and teach it the zone anchors:

- Add `ResolveNear(IReadOnlyList<AdjacencyHint> adjacency, WillieBriefing briefing)` and have the existing `ResolveNear(PlacementSpec, …)` call it with `spec.Adjacency`.
- Extend anchor resolution to include stockpile centers for `storage`/`stockpile` targets (today only room-class anchors resolve; the zone solver needs stockpiles). Keep the kitchen room-anchor path.
- Replace `GrowZonePlacementSolver.ZoneAnchors` with `AnchorResolver` calls + the existing Home-bounds fallback.

Tests: storage/kitchen/stockpile targets resolve via the shared resolver; building-path `ResolveNear(spec,…)` output unchanged.

---

## Slice 4 - Rewrite the zone solver on shared parts

Reduce `GrowZonePlacementSolver.SolveAsync` to:

1. Guard `ZoneClass.Growing` + `terrain.HasCoordinateGrid` (unchanged).
2. Resolve anchors via `AnchorResolver` (Slice 3).
3. Build growable evidence + free rects via Slices 1-2.
4. For each free rect, place a `tile_count`-sized sub-rect biased toward the nearest anchor (reuse the candidate-dimension shapes only if still needed; the maximal rect already bounds the fit).
5. Score with the existing `MetricValue` fertility/compactness/anchor-distance metrics; select non-overlapping top `MaxOptionCount`.
6. Assemble `AdviceOption`s exactly as today (`zone_cell` blueprint assets, no materials, `ApplyReady: Blocked`).

Delete `EnumerateCandidates`, `TryBuildCandidate`, `CandidateDimensions`, `BlockedCells`, `ZoneAnchors`, and the private `ZoneAnchor` type once their callers move to shared parts.

Tests: solver still picks high-fertility growable cells over low; rejects occupied/zoned cells; returns the same `NoFit` reasons (`NoTerrainGrid`, `NoGrowableCells`, `AllZoneCellsBlocked`, `NoZoneRectangle`); options match the pre-refactor solver on a fixed fixture (behavior-neutral except internal path).

---

## Incidental cleanups (fold in, do not ship separately)

These dead-code items from the `/simplify` review get subsumed by the rewrite; ensure they are gone:

- `GrowZonePlacementSolver.SelectOptions` fallback `: candidates.Take(MaxOptionCount)` ([:366](../Src/Ministers/Willie/GrowZonePlacementSolver.cs)) is unreachable — caller already `NoFit`s on empty candidates and the first candidate never overlaps an empty `selected`.
- `CandidateDimensions` guard `width > 16` ([:426](../Src/Ministers/Willie/GrowZonePlacementSolver.cs)) is impossible — loop bound is `width <= Math.Min(16, maxArea)`. Only `height > 16` can fire.
- `ResourceRequestNormalizer.InferZoneClass` returns `ZoneClass.Growing` in both branches — the `if` + `normalized` computation is dead. Collapse to `=> ZoneClass.Growing` (it is a forward-looking stub while `ZoneClass` has one member).

---

## Risks and Sequencing

- **Shared-file collision (high).** This refactor edits `PlacementEvidence.cs` and `AnchorResolver.cs`, which are also checked out in the in-flight worktrees `willie-freezer-apply-readiness` and `willie-nonfreezer-solver-wiring`. Land or rebase on those first; do not branch this off a master that predates them.
- **Building-solver regression.** The generalization is the one place this could change building behavior. Gate on the full existing Willie placement suite staying green with no fixture edits.
- **Scope creep into scoring.** Resist unifying zone scoring with `WalkablePathCostScorer`; zone criteria (fertility) differ and path-cost precision is a non-goal. Reuse the value types, not the building scorer.

---

## Verification

Run `dotnet test Src\RimBob.sln --filter FullyQualifiedName~Willie` after each slice, then full `dotnet test Src\RimBob.sln` before landing. No dashboard change expected (solver internals only); if any zone-option field shifts, run `npm.cmd run build` and regen affected fixtures under no-compat.

Live proof: run a cabinet rules cycle on a colony with growable terrain, call `GET /api/ministers/willie/zone-requests`, and confirm the grow-zone request still records the same options/no-fit it did before the refactor (behavior-neutral), with the solver now sharing free-rect evidence.

---

## Non-Goals

- No `create_growing_zone` Apply (parent plan Slice 5).
- No new `ZoneClass` members.
- No path-cost scoring for zones.
- No change to the building `PlacementSolver` behavior or its option output.
- Do not merge the zone board / endpoints into the building board; that separation is the parent plan's locked decision.
