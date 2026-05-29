# Willie Reuse-Footprint — Review Fixes (75c2e1c)

> Implementation plan. Lands **code** in RimBob. No RIMAPI work.
>
> Follow-up to the S3d room-reuse land (`75c2e1c`, `feat(willie): land S3d room reuse`) and the
> partial review-fix pass (`f9e5d4d`, `fix(willie): close review findings`). A review of `75c2e1c`
> surfaced four P2 items + reuse-generator coverage gaps. `f9e5d4d` already closed one (generator
> default templates `FreezerOnly`→`Default`); this slice closes the rest.
>
> **No compat code; wipe-and-regen on upgrade** for any persisted advice/option/trace shape.
> Worktree port: `.\run-rimbob.ps1 -ListenUrl http://127.0.0.1:5101`.

---

## 0. Scope

| # | Finding | Status before this slice | Action |
|---|---------|--------------------------|--------|
| 1 | Per-call work recomputed in inner loops | open | hoist `InteriorOrigins` + shell/asset filter out of loops |
| 2 | `GetRoomsAsync` hardcodes all detail flags | open (mostly WAI) | parameterize detail so the heavy fetch is explicit + opt-out-able; default preserves behavior |
| 3 | Default-ctor generator was a no-op (`FreezerOnly`) | **closed by `f9e5d4d`** | none — verify only |
| 4 | Anchor loop emits blueprint-dup drafts that consume `MaxDrafts` before dedupe | open | dedupe by cell-set **inside** the generator so dups don't starve later footprints |
| T | Reuse-generator coverage gaps | open | add tests: freezer exclusion, `MaxDrafts` cap, occupied-cell skip, multi-anchor dedup, footprint starvation |

Out of scope: wall replacement / freezer-cooler retrofit (still deferred), RegionId/Bounds plumbing cleanup
(owned by `willie-anchor-detection-closeout`), point-approx occupancy (disclosed assumption, needs RIMAPI footprint occupancy).

---

## 1. Slice RF1 — Generator perf hoist (#1)

File: [`ReuseExistingFootprintGenerator.cs`](../Src/Ministers/Willie/Generators/ReuseExistingFootprintGenerator.cs)

Current `Generate` triple-loops `footprint → anchor → origin`, and:
- `InteriorOrigins(footprint, interior)` ([:39](../Src/Ministers/Willie/Generators/ReuseExistingFootprintGenerator.cs#L39)) depends only on `footprint`+`interior` but is recomputed once **per anchor**.
- `TryBuildDraft` builds `RoomShell shell = template.BuildShell(interior, North)` ([:63](../Src/Ministers/Willie/Generators/ReuseExistingFootprintGenerator.cs#L63)) and re-filters `shell.Assets` via `IsReusableInteriorAsset` ([:146-149](../Src/Ministers/Willie/Generators/ReuseExistingFootprintGenerator.cs#L146)) on **every** `(footprint,anchor,origin)`, though shell + reusable-asset set depend only on `template`+`interior`.

Change (pure perf, **no output/ordering change** — all hoisted values are loop-invariant and deterministic):
1. Build `RoomShell shell` once at top of `Generate` after the `interior` null-check.
2. Precompute `IReadOnlyList<TemplateAsset> reusableTemplateAssets = shell.Assets.Where(a => IsReusableInteriorAsset(a, interior)).ToList()` once.
3. Hoist `InteriorOrigins(footprint, interior)` to just inside the `footprint` loop (above the `anchor` loop).
4. `TryBuildDraft` / `ReuseAssets` take the precomputed `reusableTemplateAssets` + `origin`; `ReuseAssets` drops its own filter call and only does `TranslateInterior` + `footprint.Cells.Contains` + `InBounds` + `IsOccupied`.

Acceptance: existing two tests still green (single anchor, identical drafts/cells); no public-API change.

---

## 2. Slice RF2 — In-generator cell-set dedupe (#4)

File: [`ReuseExistingFootprintGenerator.cs`](../Src/Ministers/Willie/Generators/ReuseExistingFootprintGenerator.cs)

Problem: middle `anchor` loop re-emits drafts whose **assets/cells are identical** across anchors (cells depend on
`footprint`+`origin`, not anchor). `DraftDedupe.ByCellsShapeAnchor`'s anchor-independent `CellKey`
([`DraftDedupe.cs:28`](../Src/Ministers/Willie/Placement/DraftDedupe.cs#L28)) drops them downstream — but the generator
counts each toward `budget.MaxDrafts` ([:46](../Src/Ministers/Willie/Generators/ReuseExistingFootprintGenerator.cs#L46))
**before** dedupe runs. With `footprint` as the outer loop, a 1-origin footprint × N anchors can fill the budget with
soon-to-be-dropped dups and **starve later footprints**, yielding fewer distinct options than the budget allows.

Change: maintain a local `HashSet<string> seenCellKeys` in `Generate`; compute a built draft's cell-key (same
normalization as `DraftDedupe.CellKey` — sorted distinct `x,z`), and **skip without incrementing the budget count** if
already seen. First anchor (deterministic `evidence.Anchors` order) wins per cell-set, matching downstream dedupe.

Rationale: product already collapses to one option per cell-set (`CellKey`), so per-anchor variants of an identical
physical build are redundant; this just stops them from consuming generator budget. Determinism preserved (footprint
order, origin order, anchor order all unchanged).

Acceptance: RF-T multi-anchor + starvation tests below.

---

## 3. Slice RF3 — Explicit room-detail fetch (#2)

File: [`RimApiClient.cs:311-317`](../Src/GameStateSync/RimApiClient.cs#L311)

`GetRoomsAsync` hardcodes `&include_cells&include_entry_cells&include_contained_buildings&include_region=true`. Single
call site — [`IngestionDispatcher.cs:43`](../Src/StateStore/IngestionDispatcher.cs#L43) — feeds the **shared**
`RoomRegistry` consumed by both Welfare and Willie. Willie reuse genuinely needs the footprint detail, so ingestion must
keep requesting it; this is **mostly WAI**. Fix = make the cost **explicit and opt-out-able** rather than a magic URL.

Change (no behavior change):
- `GetRoomsAsync(int mapId, bool includeFootprint = true, CancellationToken ct = default)`; build the
  `include_*` query segment only when `includeFootprint` is true.
- `IngestionDispatcher` calls `GetRoomsAsync(home.Id, includeFootprint: true, ct)` explicitly (documents why the heavy
  read is on the periodic path).
- Note in xmldoc: fork omits detail fields for oversized rooms, bounding worst-case payload.

(If review prefers to treat #2 as accepted-WAI and skip the param, RF3 can be dropped without affecting RF1/RF2/RF-T.)

---

## 4. Slice RF-T — Reuse-generator coverage

File: [`ReuseExistingFootprintGeneratorTests.cs`](../Src/Tests/Willie/ReuseExistingFootprintGeneratorTests.cs)

Add (reusing existing `Templates()`/`CellsForRect()`/`ResolvedAnchor()` helpers):

1. **Freezer excluded** — `RoomClass.Freezer` spec + freezer footprint ⇒ `Generate` empty (guards the `RoomClass.Freezer` early-return).
2. **`MaxDrafts` cap** — footprint large enough for ≥2 interior origins, `MaxDrafts: 1` ⇒ exactly 1 draft.
3. **Occupied interior cell skipped** — place a `BuildingRecord` on an interior cell; assert no asset lands on it, draft still valid (or empty if only floors remain).
4. **Multi-anchor dedupe** — 2 same-class `near` anchors, one footprint ⇒ exactly 1 draft, `SourceAnchor` = first anchor (proves RF2 cell-set dedupe).
5. **Footprint starvation fixed** — 2 same-class footprints, 2 anchors, `MaxDrafts: 2` ⇒ 2 drafts covering **both** rooms (pre-RF2 this returned drafts for only the first footprint after dedupe).

---

## 5. Verification

- `dotnet test` (Willie + Ingestion + State suites) green.
- `dotnet build` clean (no new warnings).
- Manual diff check: RF1 produces byte-identical solver output to pre-change on the existing fixtures (perf-only).

---

## Summary (landed 2026-05-30)

**Motivation.** A review of `75c2e1c` (`feat(willie): land S3d room reuse`) surfaced four P2 items plus
reuse-generator coverage gaps. A partial pass (`f9e5d4d`) had already closed finding #3 (generator default
templates `FreezerOnly`→`Default`). User asked to fix the rest. This slice closes #1, #2, #4 and the test gaps.

**Context.** Builds on the S3d land (`75c2e1c`) and review-fix pass (`f9e5d4d`). Related: `placement-solver-3.md`,
`willie-anchor-detection-closeout.md` (owns the RegionId/Bounds plumbing cleanup, deliberately left untouched here).
Downstream dedupe context: `DraftDedupe.ByCellsShapeAnchor` / `CellKey` — RF2's in-generator key matches its
normalization exactly.

**Scope.**
- **RF1 (perf):** `ReuseExistingFootprintGenerator.Generate` hoists loop-invariant work — `RoomShell` + the
  reusable-interior-asset filter built once per call, `InteriorOrigins` once per footprint (out of the anchor loop).
  Pure perf; output and ordering byte-identical.
- **RF2 (#4):** in-generator `seenCellKeys` dedupe drops blueprint-identical drafts (same cells, different anchor)
  *before* they count against `budget.MaxDrafts`, so anchor-dups no longer starve later footprints. First anchor wins
  per cell-set, matching downstream `DraftDedupe`.
- **RF3 (#2):** `GetRoomsAsync(int mapId, bool includeFootprint = true, …)` makes the heavy detail fetch explicit and
  opt-out-able; `IngestionDispatcher` passes `includeFootprint: true` (Willie needs it via shared `RoomRegistry`).
  No behavior change.
- **RF-T:** 5 reuse-generator tests (freezer exclusion, `MaxDrafts` cap, occupied-cell skip, multi-anchor dedupe,
  footprint starvation) + 2 `GetRoomsAsync` param tests.
- Finding #3 already closed by `f9e5d4d` — not re-touched.

**How to verify (human).**
  - Dashboard: no direct surface — internal solver/perf change. Indirectly, the Willie "Build Queue" tab shows
    `reuse_existing_footprint` options when a same-class room with live `cells[]` exists; behavior unchanged by this slice.
  - Commands: `dotnet build Src\RimBob.sln`; `dotnet test Src\Tests\RimBob.Tests.csproj --filter FullyQualifiedName~RimBob.Tests.Willie` (88) / `~RimBob.Tests.Ingestion` (41) / `~RimBob.Tests.State` (62).
  - Files to glance at: `Src/Ministers/Willie/Generators/ReuseExistingFootprintGenerator.cs`, `Src/GameStateSync/RimApiClient.cs`, `Src/StateStore/IngestionDispatcher.cs`.

**Codex run:** 20260530-015609-willie-reuse-footprint-review-fixes · branch `codex/prompt-20260530-015609-willie-reuse-footprint-review-fixes` · landed commit `9e527e0`
