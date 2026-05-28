# Placement Solver Solver1 — Implementation Plan

> Implementation plan. Lands **code** in RimBob. No fork work.
>
> Implements the **Solver1 skeleton** slice of the design in
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
`building_request` into 1–3 validated, pickable layout options for Willie. Solver1
is the smallest end-to-end cut: **one room class (freezer), one anchor (near
kitchen), one generator, one candidate, one validated option** — proving the
pipeline shape before competition/diversity (Solver2+) is added.

Solver1 is now unblocked: `WillieBriefing.AnchorInventory` ships from the
derivation (`3235902`), the FORK2 group-`validate` endpoint exists, schema S1
already landed the dormant `BlueprintGroup` / `AdviceOption` types
(`Src/Common/Advice/AdviceOption.cs`, namespace `RimBob.Core.Advice`), and the
FORK3 client wrappers exist for the Slice-B scoring upgrade.

**Refresh deltas vs the `placement-solver.md` design (written pre-landing):**

- Anchor source is the concrete `WillieAnchorInventory` record
  (`Src/Common/Briefings/WillieRoomAnchor.cs`, namespace
  `RimBob.Core.Briefings`), **not** an abstract anchor. Solver1 reads
  `briefing.AnchorInventory.Anchors.Where(a => a.Class == RoomClass.Kitchen)`
  (`WillieRoomAnchor.Class` is `RoomClass`). `Centroid` is a `MapPosition?`
  (3-D `X/Y/Z`, `Src/Common/Aggregates/Snapshots.cs`) and is populated;
  `EntryCells` / `RegionId` are empty/null stubs (their fork endpoints have
  not landed) → Solver1 scores by **Manhattan-from-centroid** on the `X/Z`
  plane, reusing the landed `MapDistance.Manhattan` helper
  (`Src/StateStore/Derivations/Common/MapDistance.cs`, namespace
  `RimBob.State.Derivations.Common`) — the cheap Slice-A fallback the locked
  decision allows (Slice-B swaps in FORK3 walkable path-cost).
- The FORK2 group-`validate` endpoint exists fork-side, but **RimBob has no
  client method for it yet** (`3235902` added only the FORK3 reach/path-cost
  wrappers `PostPathCostAsync` / `PostPathCostBatchAsync`). Solver1 must add
  `PostBlueprintGroupValidateAsync` + its DTOs.
- The solver introduces a new minister directory `Src/Ministers/Willie/`
  (namespace `RimBob.Ministers.Willie`, project `RimBob.Ministers.csproj`).
  The schema-level `Construction → Willie` rename (`8f21699`) already landed,
  but **no `Src/Ministers/Willie/` directory exists yet** — Solver1 creates it
  (AGENTS: "every new minister gets its own directory under
  `Src/Ministers/<Name>/`"). The older design text's
  `Src/Ministers/Construction/` is stale.

Per-slice motivation:

- **Solver1a** — Contract + I/O boundary. Without `PlacementSpec` + the
  group-validate client method, there is nothing for a generator to target
  and no way to validate a draft.
- **Solver1b** — Anchor resolution + the one generator. The `near:kitchen` →
  map-location resolution is the hard sub-problem; Solver1 does the simplest
  correct version against the landed anchor inventory.
- **Solver1c** — Gates + score + validate + assemble. Produces the single
  `AdviceOption` the dashboard can render, proving the full path.
- **Solver1d** — Determinism tests. Same spec + same snapshot + same seed →
  same draft, per the design's test discipline.

---

## 1. Slices (gimp independently, in order)

Naming: solver types are persona-named (`Willie*`) where they are
Willie-owned; the generic packing helpers stay neutral. Minister namespace
`RimBob.Ministers.Willie`.

### Solver1a — `PlacementSpec` + group-validate client  *(CONTRACT — gimp first)*

Files (new):

- `Src/Ministers/Willie/PlacementSpec.cs` (namespace `RimBob.Ministers.Willie`)
  - `PlacementSpec` record — the normalized solver input
    ([`placement-solver.md`](placement-solver.md) §2), mapped from the landed
    `BuildingRequest` (`Src/Common/Advice/FlagRequests.cs`): `TargetClass`
    (`BuildingClass`), `RoomClass?`, `CapacityNeed?`
    (`{ CapacityMeasure Measure, double? Amount, string? Unit }`),
    `IReadOnlyList<AdjacencyHint>? Adjacency` (each
    `{ AdjacencyRelation Relation, string Target }`), `PowerNeed? Power`,
    `TempNeed? Temperature`, `MaterialsOnHand` (`IReadOnlyList<MaterialHint>?`),
    `Deadline?`, `AdvicePriority? Priority`, `string? Source`.
    - `// TODO: BuildingRequest has no Constraints[] field today; derive
      constraints (double_wall, fire_safe_material) from RoomClass/intent when
      self-intent feeds the solver (placement-solver.md §2). Solver1 leaves it
      empty.`
    - `Source` maps from `BuildingRequest.RequestedFrom` (used for cross-link +
      `concern` mapping); there is no `source_minister` flag on the request.
  - `static PlacementSpec FromBuildingRequest(BuildingRequest req)`.
- `Src/GameStateSync/Dtos/BlueprintGroupDto.cs` (namespace
  `RimBob.Ingestion.Dtos`, matching `MapReachDto.cs`; use
  `[property: JsonPropertyName("snake_case")]` on every field like the other
  DTOs in that folder). Field names below are the **confirmed** fork shapes
  from `C:\dev\RIMAPI-for-RimBob\Source\RIMAPI\RimworldRestApi\Models\BuilderDtos.cs`
  (`BlueprintGroupValidateRequestDto`, `BlueprintGroupItemDto`,
  `BlueprintGroupValidateResultDto`, `BlueprintGroupValidateItemResultDto`,
  `BlueprintCostDto`, `BlueprintGroupOverlapConflictDto`):
  - Request:
    - `BlueprintGroupValidateRequestDto { int MapId, IReadOnlyList<BlueprintGroupItemDto> Items }`.
    - `BlueprintGroupItemDto { string? Role, string DefName, string? StuffDefName, MapCellDto Cell, int Rotation }`
      — note the fork item carries `role`; reuse `MapCellDto` (`{ int X, int Z }`,
      `x`/`z`) from `Src/GameStateSync/Dtos/MapReachDto.cs`.
  - Response (envelope `data`):
    - `BlueprintGroupValidateResponseDto { bool CanPlaceAll, IReadOnlyList<BlueprintGroupValidateItemResultDto> Items, IReadOnlyList<BlueprintCostDto> Cost, IReadOnlyList<BlueprintGroupOverlapConflictDto> OverlapConflicts }`.
    - `BlueprintGroupValidateItemResultDto { int Index, BlueprintGroupItemDto Item, bool CanPlace, string? Reason, string? DefType, IReadOnlyList<MapCellDto> OccupiesCells, IReadOnlyList<BlueprintCostDto> Cost, float WorkToBuild, bool AlreadyBlueprinted, bool AlreadyBuilt }`.
    - `BlueprintCostDto { string DefName, int Count }` — the fork uses
      `Count` (not `qty`); aggregate `Cost` maps to `AdviceOption`'s
      `IReadOnlyList<MaterialEstimate>` (`{ def_name, count }`) at assemble.
    - `BlueprintGroupOverlapConflictDto { MapCellDto Cell, int FirstItemIndex, int SecondItemIndex }`
      — the fork returns conflict triples here, **not** a flat
      `OverlapItemIndices` int list.
    - `// TODO: the place response (BlueprintGroupPlaceResultDto) and its
      require_all/placement_order request fields are out of scope until the
      Apply path lands.`

Files (modified):

- `Src/GameStateSync/RimApiClient.cs` (class `RimApiClient`, namespace
  `RimBob.Ingestion`) — add
  `public Task<BlueprintGroupValidateResponseDto> PostBlueprintGroupValidateAsync(BlueprintGroupValidateRequestDto request, CancellationToken ct = default)`
  delegating to the existing
  `private async Task<TResponse> PostEnvelopedAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct)`
  helper, exactly like the landed FORK3 `PostPathCostAsync`. POST path
  `api/v1/builder/blueprint-group/validate` (FORK2 A.1). Place it next to the
  reach/path-cost reads under the "Map / Colony state" region.
  - `// TODO: add PostBlueprintGroupPlaceAsync when the Apply path lands
    (group place is the player-click write, separate from Solver1 validate).`

Tests (new):

- `Src/Tests/Ingestion/RimApiClientBlueprintGroupTests.cs` (namespace
  `RimBob.Tests.Ingestion`) — copy the mock-HTTP harness from the landed
  `Src/Tests/Ingestion/RimApiClientFork3Tests.cs` (`PathRouter` /
  `CaptureHandler` / `Envelope<T>` helpers): happy-path validate deserializes
  the response DTO, asserts snake_case request body, and envelope
  `success:false` throws `RimApiException`.

Docs to touch: `Docs/design/RimAPI.md` — register the new group-validate
client wrapper.

Risk: **low.** Additive contract + one HTTP wrapper.

### Solver1b — anchor resolution + `TemplateAnchoredGenerator`  *(CORE)*

Files (new) — all under namespace `RimBob.Ministers.Willie` (or a
`.Placement` / `.Generators` sub-namespace):

- `Src/Ministers/Willie/Placement/AnchorResolver.cs`
  - `IReadOnlyList<WillieRoomAnchor> ResolveNear(PlacementSpec spec, WillieBriefing briefing)`
    — for each `AdjacencyHint` with `Relation == AdjacencyRelation.Near`, parse
    its free-string `Target` (e.g. `"kitchen"`) into a `RoomClass` and filter
    `briefing.AnchorInventory.Anchors` by `anchor.Class` (Solver1: `near
    kitchen` → `RoomClass.Kitchen`). Returns 0..N anchors.
  - `// TODO: AdjacencyHint.Target is a free string; Solver1 does a simple
    case-insensitive RoomClass parse. Replace with a shared alias map when
    more room classes land (Solver3).`
  - `// TODO: WillieRoomAnchor.EntryCells/RegionId are empty/null until
    rimapi-room-entry-cells / rimapi-map-region-at land; Solver1 anchors on
    Centroid only.`
- `Src/Ministers/Willie/Placement/FreezerTemplate.cs`
  - Pure footprint math: map a `CapacityNeed` with
    `Measure == CapacityMeasure.FoodUnits` and `Amount` (200) to a rect size
    (200 → 5×5), enumerate shell cells (floor + walls + door) + 1 cooler cell
    on a wall. Emit cells as `MapCell` (`x`/`z`). No I/O — unit-testable in
    isolation.
- `Src/Ministers/Willie/Generators/IPlacementGenerator.cs`
  - `IReadOnlyList<PlacementDraft> Generate(PlacementSpec spec, PlacementEvidence evidence, int budget)`.
- `Src/Ministers/Willie/Generators/TemplateAnchoredGenerator.cs`
  - Solver1: emit **one** `PlacementDraft` (a `BlueprintGroup` from
    `RimBob.Core.Advice` — `{ Label, MapId, Assets }` with `BlueprintAsset`
    `{ Role, DefName, StuffDefName?, Cell, Rotation }` — plus source anchor +
    reason summary) per resolved kitchen anchor, placing the freezer template
    adjacent to the anchor `Centroid` (`MapPosition?`, use `X`/`Z`).
- `Src/Ministers/Willie/PlacementEvidence.cs`
  - Solver1-minimal precompute: map bounds parsed from
    `MapInfoSnapshot.Size` (a `string?` like `"250x250"`, **not** int
    width/height fields), occupied cells from `BuildingRegistry.Buildings`
    `BuildingRecord.Position` (`MapPosition?`), and the resolved anchor list.
    - `// TODO: BuildingRecord.Position is a single cell, not a footprint;
      occupancy is approximate until per-building footprints land.`
    - `// TODO: terrain affordance + reachability evidence when
      rimapi-buildability-layers-read / FORK3 ingestion land.`

Tests (new), under `Src/Tests/Willie/` (namespace `RimBob.Tests.Willie`):

- `Src/Tests/Willie/FreezerTemplateTests.cs` — 200 food →
  5×5 shell with correct cell enumeration; deterministic.
- `Src/Tests/Willie/AnchorResolverTests.cs` — fixture
  briefing with one kitchen anchor → one resolved anchor; no kitchen → empty.

Risk: **medium.** The anchor/footprint logic is the substance of Solver1.

### Solver1c — gates + score + validate + assemble  *(PIPELINE)*

Files (new) — namespace `RimBob.Ministers.Willie`:

- `Src/Ministers/Willie/PlacementSolver.cs`
  - `public async Task<PlacementResult> SolveAsync(PlacementSpec spec, WillieBriefing briefing, ColonyState colonyState, CancellationToken ct = default)`.
    (`ColonyState` is the live aggregate root in `RimBob.State`; read
    `colonyState.Buildings.Value.Buildings` for occupancy. The Ministers
    project already references State — see `Chef.cs`.)
  - Pipeline (Solver1 subset of [`placement-solver.md`](placement-solver.md) §3):
    1. `AnchorResolver.ResolveNear`.
    2. `TemplateAnchoredGenerator.Generate` → drafts.
    3. **Hard gates:** out-of-bounds (vs `MapInfoSnapshot.Size` bounds),
       occupied-cell overlap (from `PlacementEvidence`). Reject before any
       fork call.
    4. **Cheap score:** `freezer_to_kitchen_distance` =
       `MapDistance.Manhattan(draftCentroid, anchorCentroid)` (the landed
       `RimBob.State.Derivations.Common.MapDistance`; both args are
       `MapPosition`, so null-check `WillieRoomAnchor.Centroid` first). Pick
       best 1.
    5. **Validate:** `PostBlueprintGroupValidateAsync` on the single
       survivor; drop on `!CanPlaceAll` or any `OverlapConflicts`.
    6. **Assemble:** aggregate the response `Cost` (`BlueprintCostDto`
       `{ DefName, Count }`) into `IReadOnlyList<MaterialEstimate>`
       (`{ def_name, count }`); build one `AdviceOption`
       (`{ Id, Label, Summary, BlueprintGroup, EstimatedMaterials, TradeoffNote? }`)
       and attach it to an `AdviceItem` (`Src/Common/Advice/AdviceItem.cs`,
       `RimBob.Core.Advice`) via its `Options` list, with
       `Concern = "thermal_control"`.
    7. **No-fit fallback:** return a `PlacementResult` flagged no-fit so the
       caller emits `material_bottleneck` / `stalled_builds` / prose
       instead (Rules owns that branch; Solver1 just signals it).
- `Src/Ministers/Willie/PlacementResult.cs`
  - `{ IReadOnlyList<AdviceOption> Options, PlacementTrace Trace, NoFitReason? NoFit }`.
    Keep `Draftable` / `PlacementValid` / `MaterialsReady` /
    `ApplyReady` as typed status fields per the design §3.2.

Tests (new):

- `Src/Tests/Willie/PlacementSolverTests.cs` (namespace
  `RimBob.Tests.Willie`) — fixture map + spec → one validated option (mock the
  group-validate call); occupied-anchor → no-fit; `!CanPlaceAll` → no-fit.

Docs to touch: none beyond Solver1a (design stays in `placement-solver.md`).

Risk: **medium.** Orchestration + the one fork call.

### Solver1d — determinism + trace tests  *(VERIFICATION)*

Files (new), namespace `RimBob.Tests.Willie`:

- `Src/Tests/Willie/PlacementDeterminismTests.cs`
  - Same spec + same snapshot + same seed → identical draft set + identical
    option ranking (design §6).
  - Score trace carries `raw_value` + `unit` (`tiles`) +
    `normalized` for `freezer_to_kitchen_distance` (design §3.3).
- `Src/Tests/Willie/Fixtures/` — canned map-grid + spec
  JSON (AGENTS: fixtures live under `Src/Tests/<MinisterName>/Fixtures/`).

Risk: **low.** Pure test addition.

---

## 2. Keep-green (every slice)

- Sync worktree with `master` before any verification build.
- `dotnet build Src/RimBob.sln`; `dotnet test Src/Tests/RimBob.Tests.csproj`.
- `.\run-rimbob.ps1 -ListenUrl http://127.0.0.1:5101` for any live smoke;
  verify `/api/system/health` + the `RimBob.Host.exe` process path.

---

## 3. Out of scope (separate plans / later Solver slices)

- **Willie Rules wiring** that *calls* the solver on an active
  `building_request` — owned by [`willie-rules-slice-a.md`](willie-rules-slice-a.md);
  Solver1 exposes `SolveAsync`, Rules wires it (the freezer rule's solver path
  is a TODO in Rules Slice A until Solver1 lands).
  - **AGENTS minister-dir convention:** "`Rules.cs` is the first file in every
    minister directory; it must compile and have tests before the LLM is
    wired." Solver1 creates `Src/Ministers/Willie/` but is a deterministic,
    non-LLM component and does **not** add `Rules.cs` — `willie-rules-slice-a.md`
    owns `Src/Ministers/Willie/Rules.cs` and is the slice that satisfies that
    convention. If Solver1 lands first, that is an accepted, temporary gap the
    Rules slice closes; do not scaffold a stub `Rules.cs` here.
- **Group `place` (the Apply write)** — `place_blueprint_group` executor;
  bundles with the Assisted Apply path, not Solver1.
- **Solver2 competition / diversity / multiple options** — design §5.
- **Slice-B walkable scoring** — swap Manhattan for FORK3
  `PostPathCostBatchAsync` over `EntryCells`; needs `rimapi-room-entry-cells`
  for entry cells + the region-at endpoint. No solver-contract change.
- **Solver3 room classes beyond freezer**, reuse-existing-footprint, richer
  evidence (terrain/reachability ingestion).
- **Dashboard solver-trace panel** — needs the minister registered
  (`willie-rules-slice-a.md` WR3); Solver1 only produces the trace.

---

## 4. Verification (per slice, before commit)

- Solver1a: client unit test green; manual `curl` of group-validate against a
  live RIMAPI (port 5101) matches the DTO.
- Solver1b: template + resolver tests green; footprint deterministic.
- Solver1c: solver test green — one validated option on the happy path; no-fit
  on occupied anchor and on `!CanPlaceAll`.
- Solver1d: determinism test green (same inputs → same ranking); trace carries
  units.

---

## 5. HumanTodo capture (append in same commit as this plan)

```
- [ ] placement-solver-1 [2026-05-28] #construction #willie #solver Implementation plan for Placement Solver Solver1 skeleton: PlacementSpec + group-validate client (Solver1a), anchor resolution + TemplateAnchoredGenerator (Solver1b), gates/score/validate/assemble (Solver1c), determinism tests (Solver1d). Freezer/near-kitchen/one-option; Manhattan Slice-A scoring. [plan](.plans/placement-solver-1.md)
```
