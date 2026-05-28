# Placement Solver PS1 — Implementation Plan

> Implementation plan. Lands **code** in RimBob. No fork work.
>
> Implements the **PS1 skeleton** slice of the design in
> [`placement-solver.md`](placement-solver.md) §5, refreshed against the
> now-landed reality (briefing derivation `3235902`, FORK2 group
> validate/place Slice A, FORK3 client wrappers).
>
> Sliced by risk — **gimp one slice at a time**, keep build/tests green
> between. **No compat code; wipe-and-regen on upgrade** for any persisted
> solver-trace/option shape (AGENTS Coding rule).
>
> **Worktree port** for live runs: `.\run-rimbob.ps1 -ListenUrl
> http://127.0.0.1:5101` (5000 reserved for the main checkout).

---

## 0. Motivation

The Placement Solver is the deterministic engine that turns a
`building_request` into 1–3 validated, pickable layout options for Willie. PS1
is the smallest end-to-end cut: **one room class (freezer), one anchor (near
kitchen), one generator, one candidate, one validated option** — proving the
pipeline shape before competition/diversity (PS2+) is added.

PS1 is now unblocked: `WillieBriefing.AnchorInventory` ships from the
derivation (`3235902`), the FORK2 group-`validate` endpoint exists, schema S1
already landed the dormant `BlueprintGroup` / `AdviceOption` types, and the
FORK3 client wrappers exist for the Slice-B scoring upgrade.

**Refresh deltas vs the `placement-solver.md` design (written pre-landing):**

- Anchor source is the concrete `WillieAnchorInventory` record
  (`Src/Common/Briefings/WillieRoomAnchor.cs`), **not** an abstract anchor.
  PS1 reads `briefing.AnchorInventory.Anchors.Where(a => a.Class ==
  RoomClass.Kitchen)`. `Centroid` is populated; `EntryCells` / `RegionId`
  are empty (their fork endpoints have not landed) → PS1 scores by
  **euclidean-from-centroid** (Slice-A fallback, exactly as the locked
  decision allows).
- The FORK2 group-`validate` endpoint exists fork-side, but **RimBob has no
  client method for it yet** (`3235902` added only the FORK3 reach/path-cost
  wrappers). PS1 must add `PostBlueprintGroupValidateAsync` + its DTOs.
- The solver lives under `Src/Ministers/Willie/` (the minister dir after the
  `Construction → Willie` rename `8f21699`), not `Src/Ministers/Construction/`
  as the older design text says.

Per-slice motivation:

- **PS1a** — Contract + I/O boundary. Without `PlacementSpec` + the
  group-validate client method, there is nothing for a generator to target
  and no way to validate a draft.
- **PS1b** — Anchor resolution + the one generator. The `near:kitchen` →
  map-location resolution is the hard sub-problem; PS1 does the simplest
  correct version against the landed anchor inventory.
- **PS1c** — Gates + score + validate + assemble. Produces the single
  `AdviceOption` the dashboard can render, proving the full path.
- **PS1d** — Determinism tests. Same spec + same snapshot + same seed →
  same draft, per the design's test discipline.

---

## 1. Slices (gimp independently, in order)

Naming: solver types are persona-named (`Willie*`) where they are
Willie-owned; the generic packing helpers stay neutral. Minister namespace
`RimBob.Ministers.Willie`.

### PS1a — `PlacementSpec` + group-validate client  *(CONTRACT — gimp first)*

Files (new):

- `Src/Ministers/Willie/PlacementSpec.cs`
  - `PlacementSpec` record — the normalized solver input
    ([`placement-solver.md`](placement-solver.md) §2): `TargetClass`
    (`BuildingClass`), `RoomClass?`, `CapacityNeed?`, `Adjacency[]`,
    `Power?`, `Temperature?`, `Constraints[]`, `MaterialsOnHand?`,
    `Deadline?`, `Priority?`, `Source`. Maps ~1:1 from `BuildingRequest`.
  - `static PlacementSpec FromBuildingRequest(BuildingRequest req, string source)`.
- `Src/GameStateSync/Dtos/BlueprintGroupDto.cs`
  - `BlueprintGroupValidateRequestDto { int MapId, IReadOnlyList<BlueprintAssetDto> Items }`.
  - `BlueprintAssetDto { string DefName, string? StuffDefName, MapCellDto Cell, int Rotation }`
    (reuse `MapCellDto` from `Src/GameStateSync/Dtos/MapReachDto.cs`).
  - `BlueprintGroupValidateResponseDto { bool CanPlaceAll, IReadOnlyList<BlueprintItemResultDto> Items, IReadOnlyList<MaterialCountDto> Cost, IReadOnlyList<int> OverlapItemIndices }`
    — matches FORK2 A.1. Verify field names against the fork DTO
    (`C:\dev\RIMAPI-for-RimBob\...\Models\BuilderDtos.cs`) at touch-time.

Files (modified):

- `Src/GameStateSync/RimApiClient.cs` — add
  `PostBlueprintGroupValidateAsync(BlueprintGroupValidateRequestDto, CancellationToken)`
  via the existing `PostEnvelopedAsync<TReq,TResp>` helper (line 42).
  - `// TODO: add PostBlueprintGroupPlaceAsync when the Apply path lands
    (group place is the player-click write, separate from PS1 validate).`

Tests (new):

- `Src/Tests/Willie/PlacementSolver/BlueprintGroupClientTests.cs` — mock
  HTTP fixture per `RimApiClientFork3Tests`; happy-path validate, envelope
  `success:false` throws `RimApiException`.

Docs to touch: `Docs/design/RimAPI.md` — register the new group-validate
client wrapper.

Risk: **low.** Additive contract + one HTTP wrapper.

### PS1b — anchor resolution + `TemplateAnchoredGenerator`  *(CORE)*

Files (new):

- `Src/Ministers/Willie/Placement/AnchorResolver.cs`
  - `IReadOnlyList<WillieRoomAnchor> ResolveNear(PlacementSpec spec, WillieBriefing briefing)`
    — filter `briefing.AnchorInventory.Anchors` by the `Adjacency` target
    class (PS1: `near kitchen` → `RoomClass.Kitchen`). Returns 0..N anchors.
  - `// TODO: EntryCells/RegionId are empty until rimapi-room-entry-cells /
    rimapi-map-region-at land; PS1 anchors on Centroid only.`
- `Src/Ministers/Willie/Placement/FreezerTemplate.cs`
  - Pure footprint math: `capacity_need.food_units → rect size` (200 →
    5×5), enumerate shell cells (floor + walls + door) + 1 cooler cell on
    a wall. No I/O — unit-testable in isolation.
- `Src/Ministers/Willie/Generators/IPlacementGenerator.cs`
  - `IReadOnlyList<PlacementDraft> Generate(PlacementSpec spec, PlacementEvidence evidence, int budget)`.
- `Src/Ministers/Willie/Generators/TemplateAnchoredGenerator.cs`
  - PS1: emit **one** `PlacementDraft` (a `BlueprintGroup` + source anchor
    + reason summary) per resolved kitchen anchor, placing the freezer
    template adjacent to the anchor `Centroid`.
- `Src/Ministers/Willie/PlacementEvidence.cs`
  - PS1-minimal precompute: map bounds (`MapInfoSnapshot`), occupied cells
    (`BuildingRegistry` positions), anchor list. `// TODO: terrain
    affordance + reachability evidence when rimapi-buildability-layers-read
    / FORK3 ingestion land.`

Tests (new):

- `Src/Tests/Willie/PlacementSolver/FreezerTemplateTests.cs` — 200 food →
  5×5 shell with correct cell enumeration; deterministic.
- `Src/Tests/Willie/PlacementSolver/AnchorResolverTests.cs` — fixture
  briefing with one kitchen anchor → one resolved anchor; no kitchen → empty.

Risk: **medium.** The anchor/footprint logic is the substance of PS1.

### PS1c — gates + score + validate + assemble  *(PIPELINE)*

Files (new):

- `Src/Ministers/Willie/PlacementSolver.cs`
  - `async Task<PlacementResult> SolveAsync(PlacementSpec spec, WillieBriefing briefing, ColonyState snapshot, CancellationToken ct)`.
  - Pipeline (PS1 subset of [`placement-solver.md`](placement-solver.md) §3):
    1. `AnchorResolver.ResolveNear`.
    2. `TemplateAnchoredGenerator.Generate` → drafts.
    3. **Hard gates:** out-of-bounds, occupied-cell overlap (from
       `PlacementEvidence`). Reject before any fork call.
    4. **Cheap score:** `freezer_to_kitchen_distance` =
       `MapDistance.Manhattan(draft_centroid, anchor.Centroid)`; pick best 1.
    5. **Validate:** `PostBlueprintGroupValidateAsync` on the single
       survivor; drop on `!CanPlaceAll` / overlap.
    6. **Assemble:** aggregate `cost[]` → `est_materials`; build one
       `AdviceOption` (id, label, summary, `BlueprintGroup`, est_materials)
       → return on a `WillieAdvice` item with `concern = thermal_control`.
    7. **No-fit fallback:** return a `PlacementResult` flagged no-fit so the
       caller emits `material_bottleneck` / `stalled_builds` / prose
       instead (Rules owns that branch; PS1 just signals it).
- `Src/Ministers/Willie/PlacementResult.cs`
  - `{ IReadOnlyList<AdviceOption> Options, PlacementTrace Trace, NoFitReason? NoFit }`.
    Keep `draftable` / `placement_valid` / `materials_ready` /
    `apply_ready` as typed status fields per the design §3.2.

Tests (new):

- `Src/Tests/Willie/PlacementSolver/PlacementSolverTests.cs` — fixture map +
  spec → one validated option (mock the group-validate call);
  occupied-anchor → no-fit; `!CanPlaceAll` → no-fit.

Docs to touch: none beyond PS1a (design stays in `placement-solver.md`).

Risk: **medium.** Orchestration + the one fork call.

### PS1d — determinism + trace tests  *(VERIFICATION)*

Files (new):

- `Src/Tests/Willie/PlacementSolver/PlacementDeterminismTests.cs`
  - Same spec + same snapshot + same seed → identical draft set + identical
    option ranking (design §6).
  - Score trace carries `raw_value` + `unit` (`tiles`) +
    `normalized` for `freezer_to_kitchen_distance` (design §3.3).
- `Src/Tests/Willie/PlacementSolver/Fixtures/` — canned map-grid + spec
  JSON (AGENTS: fixtures live under `Src/Tests/<MinisterName>/Fixtures/`).

Risk: **low.** Pure test addition.

---

## 2. Keep-green (every slice)

- Sync worktree with `master` before any verification build.
- `dotnet build Src/RimBob.sln`; `dotnet test Src/Tests/RimBob.Tests.csproj`.
- `.\run-rimbob.ps1 -ListenUrl http://127.0.0.1:5101` for any live smoke;
  verify `/api/system/health` + the `RimBob.Host.exe` process path.

---

## 3. Out of scope (separate plans / later PS slices)

- **Willie Rules wiring** that *calls* the solver on an active
  `building_request` — owned by [`willie-rules-slice-a.md`](willie-rules-slice-a.md);
  PS1 exposes `SolveAsync`, Rules wires it (the freezer rule's solver path
  is a TODO in Rules Slice A until PS1 lands).
- **Group `place` (the Apply write)** — `place_blueprint_group` executor;
  bundles with the Assisted Apply path, not PS1.
- **PS2 competition / diversity / multiple options** — design §5.
- **Slice-B walkable scoring** — swap euclidean for FORK3
  `PostPathCostBatchAsync` over `EntryCells`; needs `rimapi-room-entry-cells`
  for entry cells + the region-at endpoint. No solver-contract change.
- **PS3 room classes beyond freezer**, reuse-existing-footprint, richer
  evidence (terrain/reachability ingestion).
- **Dashboard solver-trace panel** — needs the minister registered
  (`willie-rules-slice-a.md` WR3); PS1 only produces the trace.

---

## 4. Verification (per slice, before commit)

- PS1a: client unit test green; manual `curl` of group-validate against a
  live RIMAPI (port 5101) matches the DTO.
- PS1b: template + resolver tests green; footprint deterministic.
- PS1c: solver test green — one validated option on the happy path; no-fit
  on occupied anchor and on `!CanPlaceAll`.
- PS1d: determinism test green (same inputs → same ranking); trace carries
  units.

---

## 5. HumanTodo capture (append in same commit as this plan)

```
- [ ] placement-solver-ps1 [2026-05-28] #construction #willie #solver Implementation plan for Placement Solver PS1 skeleton: PlacementSpec + group-validate client (PS1a), anchor resolution + TemplateAnchoredGenerator (PS1b), gates/score/validate/assemble (PS1c), determinism tests (PS1d). Freezer/near-kitchen/one-option; euclidean Slice-A scoring. [plan](.plans/placement-solver-ps1.md)
```
