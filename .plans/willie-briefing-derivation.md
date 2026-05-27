# Willie Briefing Derivation — Implementation Plan (Track A)

> Implementation plan. Lands **code** in RimBob. No fork work.
>
> Implements the briefing record + per-cycle derivation specified by
> [`willie-briefing-schema.md`](willie-briefing-schema.md) +
> [`willie-briefing-fields.md`](willie-briefing-fields.md). Pulls in the
> RIMAPI signals already exposed (FORK1 `/api/v1/map/construction/backlog`,
> FORK3 `/api/v1/map/reach` / `/api/v1/map/path-cost` / batch) so Willie
> Rules Slice A and the Placement Solver PS1 can read a real briefing.
>
> Sliced by risk — **gimp one slice at a time**, keep build/tests green
> between.

---

## 0. Motivation

The Willie Briefing Schema named every field but did not land a single line of
C#. Two downstream tracks still cannot start:

- **Willie Rules Slice A** writes deterministic rules that read briefing
  fields. Without `WillieBriefing` in `Src/Common/Briefings/`, `Rules.cs`
  has nothing to read.
- **Placement Solver PS1** reads `WillieAnchorInventory` to resolve
  `near:<class>` requests. The anchor record is specified but does not
  exist.

This plan is the smallest cut that closes both gaps. It does **not** wire
the Willie minister itself (registry / DI / dashboard) — that is the
follow-on slice once Willie Rules Slice A has something to emit.

Per-slice motivation:

- **WB1** — `WillieBriefing` record skeleton. Mirrors `FoodBriefing` shape
  so the cabinet / `BriefingCache` / dashboard plumbing already in place
  can swallow it without surgery.
- **WB2** — Construction backlog ingestion. The FORK1 endpoint
  `/api/v1/map/construction/backlog` has shipped; RimBob does not consume
  it yet. Adds a new state-store aggregate + the briefing fields backed by
  it. Unblocks `material_bottleneck` and `stalled_builds` real-now rules.
- **WB3** — `WillieAnchorInventoryDerivation`. Anchor inventory is the
  PS1 entry point. Reads existing `RoomRegistry` + `BuildingRegistry`; no
  new RIMAPI endpoint.
- **WB4** — RimBob FORK3 client methods. Endpoints landed fork-side; no
  C# in RimBob talks to them yet. Required by PS1 Slice-B scoring and by
  the `frame_unreachable` / `walkable_loop_length_long` Slice-A rules.

---

## 1. Slices (gimp independently, in order)

Naming convention (per reviewer): every new public type uses the **Willie**
persona name, not "Construction". `WillieBriefing`, `WillieAnchorInventory`,
`WillieRoomAnchor`, etc. Types live under namespace `RimBob.Core.Briefings`
(briefings) / `RimBob.Core.Aggregates` (state-store records); the `IBriefing`
interface contract is unchanged.

**Test directory.** New Willie test files live under `Src/Tests/Willie/`
(matches the `Willie*` type prefix). State-store derivation tests stay under
`Src/Tests/State/` per existing cross-cutting pattern (e.g. `Src/Tests/State/
WelfareBriefingDerivationTests.cs`). Ingestion client tests stay under
`Src/Tests/Ingestion/`.

**No compat code; wipe-and-regen on upgrade** (AGENTS Coding rule). WB1, WB2
add new persisted state-store aggregates + a new `IBriefing` shape. On any
schema/wire change for these types during or after this plan, wipe persisted
snapshots; do not maintain a read path for the old shape.

**Worktree port.** Port 5000 is reserved for the main `C:\dev\RimBob`
checkout (AGENTS Build rule). When running RimBob from the gimp worktree,
use `.\run-rimbob.ps1 -ListenUrl http://127.0.0.1:5101` and verify
`/api/system/health` + `RimBob.Host.exe` process path before treating the
worktree build as live.

### WB1 — `WillieBriefing` record skeleton  *(SAFE — gimp first)*

No behavior change for any live minister. Adds dormant types in
`Src/Common/Briefings/`. Mirrors `FoodBriefing` shape.

Files (new):

- `Src/Common/Briefings/WillieBriefing.cs`
  - `WillieBriefing` sealed record implementing `IBriefing`.
  - Constructor positional fields = the "always-present" set (versioning,
    date, tick, map id, colonist count, plus the per-concern summary
    objects defined below).
  - Init-only fields for the larger nested registries (anchors, backlog,
    apply-eligible target lists) so the record matches `FoodBriefing`'s
    pattern.
- `Src/Common/Briefings/WillieRoomAnchor.cs` (or grouped in the same file)
  - `WillieAnchorInventory(IReadOnlyList<WillieRoomAnchor> Anchors)`.
  - `WillieRoomAnchor(string RoomId, RoomClass Class, string RoleLabel,
    int CellsCount, IReadOnlyList<MapPosition> EntryCells, int? RegionId,
    MapPosition? Centroid, IReadOnlyList<string> ContainedBuildingIds)`.
  - `RoomClass` is the existing enum from
    [`willie-request-taxonomy.md`](willie-request-taxonomy.md) §1a; reused
    so request anchors and briefing anchors share the type.
- `Src/Common/Briefings/WillieConcernSummaries.cs`
  - One sealed record per concern's briefing slice (the 8 canonical
    concerns; `basic_shelter` is Welfare per `willie-advice-types.md`
    §4.5). Fields populated incrementally across WB2–WB4; WB1 stubs the
    types with default empties.
  - `WilliePowerStabilitySummary`, `WillieThermalControlSummary`,
    `WillieFunctionalRoomsSummary`, `WillieStoragePlacementSummary`,
    `WillieMaterialBottleneckSummary`, `WillieFireRiskSummary`,
    `WillieStalledBuildsSummary`, `WillieBaseLayoutSummary`.
- `Src/Common/Briefings/WillieDataCoverage.cs`
  - Mirrors `FoodDataCoverage`. Boolean availability hints: `HasLiveState`,
    `HasRooms`, `HasConstructionBacklog`, `HasAnchorInventory`,
    `HasReachability` (FORK3 client wired), etc. Lets the dashboard show
    why a rule did not fire.

Files (modified):

- `Src/Common/Briefings/IBriefing.cs` — no change expected; verify
  `WillieBriefing` slots in.
- `Src/StateStore/ColonyState.cs` — add `WillieBriefingAggregateNames`
  constant + matching `GetVersionsForWillieBriefing()` accessor (mirror
  `Food` / `Mayor` / `Welfare` patterns at lines 49–56). Aggregate-name
  set populated incrementally across WB2 (`WillieBacklog`) + WB3 (no new
  aggregate, just derivation off `Rooms` + `Buildings`).

Tests (new):

- `Src/Tests/Willie/WillieBriefingShapeTests.cs`
  - `WillieBriefing` constructed with all defaults round-trips through
    `System.Text.Json.JsonSerializer` (mirror
    `FoodBriefingSerializationTests` if one exists).
  - `WillieAnchorInventory.Anchors` defaults to empty list.
  - `WillieRoomAnchor.EntryCells` / `RegionId` are optional and serialize
    as empty list / null without throwing.

Docs to touch (same commit, AGENTS Documentation Discipline):

- `Docs/design/state-store.md` — add `WillieBriefing` to the briefing
  inventory; note the new `IBriefing` impl and per-cycle cadence.
- `Docs/design/ministers/construction.md` — replace the stale concern
  list with a pointer to `willie-advice-types.md` (8 concerns; the
  briefing record now exists). Promote when the minister wires
  (phase 7); this slice only adds the record reference.

`// TODO:` comments to land in source (AGENTS Coding rule):

- `WillieRoomAnchor.EntryCells` default `[]` —
  `// TODO: populate after rimapi-room-entry-cells lands`
- `WillieRoomAnchor.RegionId` default `null` —
  `// TODO: populate after rimapi-map-region-at lands`
- `WillieDataCoverage.HasReachability` default `false` —
  `// TODO: flip true once WB4 client method is wired into a consumer`

Risk: **low.** Pure type addition. No service consumes it yet.

### WB2 — Construction backlog ingestion  *(FOUNDATIONAL)*

Adds the missing state-store aggregate + briefing wiring for
`/api/v1/map/construction/backlog`. Unblocks `material_bottleneck` +
`stalled_builds` rule families.

**No compat code; wipe-and-regen on upgrade.** New persisted aggregate
shape (`WillieConstructionBacklog`). On any subsequent schema change to
this aggregate or its `WillieBriefing` consumer, wipe persisted
snapshots and regenerate — do not maintain a read path for an older
shape.

Files (new):

- `Src/Ingestion/Dtos/ConstructionBacklogDto.cs`
  - `ConstructionBacklogGroupDto` matching the fork DTO shape
    (`kind`, `def_name`, `stuff_def_name`, `allowed`, `count`,
    `thing_ids[]`, `sample_cells[]`, `total_work_left`, `cost[]`,
    `materials_available[]`, `materials_missing[]`, `blocked_count`,
    `disallowed_count`) — verify against
    `C:\dev\RIMAPI-for-RimBob\Source\RIMAPI\RimworldRestApi\Models\BuilderDtos.cs`
    at touch-time.
  - Nested DTO records for `MaterialCountDto`, `SampleCellDto`. Use the
    same `JsonNamingPolicy` (snake_case) as sibling DTOs in the folder;
    add explicit `[JsonPropertyName]` only if the surrounding pattern
    requires it.
- `Src/Common/Aggregates/Snapshots.cs` — extend (do not split into a new
  file; mirrors `StockpileLedger` / `BuildingRegistry` cohesion):
  - `WillieConstructionBacklog(IReadOnlyList<WillieBacklogGroup> Groups)`.
  - `WillieBacklogGroup(string Kind, string DefName, string?
    StuffDefName, bool Allowed, int Count, IReadOnlyList<string>
    ThingIds, IReadOnlyList<MapPosition> SampleCells, int
    TotalWorkLeft, IReadOnlyList<MaterialCount> Cost,
    IReadOnlyList<MaterialCount> MaterialsAvailable,
    IReadOnlyList<MaterialCount> MaterialsMissing, int BlockedCount,
    int DisallowedCount)`.
  - `MaterialCount(string DefName, int Count)`.
- `Src/StateStore/Ingestion/WillieBacklogMapper.cs` — new file (mappers
  are per-aggregate; mirror existing `MapAggregateMapper.cs` /
  `PawnAggregateMapper.cs` split):
  `FromConstructionBacklog(IReadOnlyList<ConstructionBacklogGroupDto>)
  → WillieConstructionBacklog`.
- `Src/StateStore/Derivations/WillieBriefingDerivation.cs` (new) —
  start with a stub that copies `WillieConstructionBacklog` straight into
  `WillieMaterialBottleneckSummary` / `WillieStalledBuildsSummary`.
  No rules yet; just data plumbing. WB3 extends this same file.

Files (modified):

- `Src/GameStateSync/RimApiClient.cs` — add:
  ```csharp
  public Task<IReadOnlyList<ConstructionBacklogGroupDto>>
      GetConstructionBacklogAsync(int mapId, CancellationToken ct) =>
      GetEnvelopedAsync<IReadOnlyList<ConstructionBacklogGroupDto>>(
          $"api/v1/map/construction/backlog?map_id={mapId}", ct);
  ```
- `Src/StateStore/ColonyState.cs` — two edits in this slice (single
  file, both edits same commit):
  1. Add `Versioned<WillieConstructionBacklog> WillieBacklog` aggregate
     + `AggregateDefaults.WillieBacklog = new([])` default.
  2. Append `"WillieBacklog"` to the `WillieBriefingAggregateNames`
     constant introduced in WB1.
- `Src/StateStore/Ingestion/IngestionDispatcher.cs` — schedule the new
  call on the live-refresh cycle; per-aggregate exception isolation
  must already swallow per-call failures so a backlog 500 cannot kill
  the whole cycle (verify at touch-time; add try/catch if missing).

Tests (new):

- `Src/Tests/State/WillieBacklogMapperTests.cs` — DTO → aggregate mapping
  (mirror `Src/Tests/State/AggregateMapperTests.cs` style).
- `Src/Tests/State/WillieBriefingDerivationTests.cs` — given a
  fixture `ColonyState` with one backlog group, the briefing surfaces
  the group on both `MaterialBottleneck` and `StalledBuilds` summaries.

Docs to touch (same commit):

- `Docs/design/state-store.md` — register `WillieBacklog` aggregate +
  its source endpoint.
- `Docs/design/RimAPI.md` — note RimBob now consumes
  `/api/v1/map/construction/backlog` (FORK1).

`// TODO:` comments to land in source:

- `WillieBacklogGroup` per-frame age fields — `// TODO: compute
  frame_age_exceeded via state-store snapshot diff in a follow-on slice`
- `IngestionDispatcher` schedule entry — `// TODO: revisit cadence if
  the backlog grows past N groups (per-cycle cost watch)`

Risk: **medium.** New state-store aggregate + new ingestion path.
Mitigation: backlog endpoint has no writes; failure mode = empty list.

### WB3 — `WillieAnchorInventoryDerivation`  *(STATE-STORE ONLY)*

Pure derivation from `RoomRegistry` + `BuildingRegistry`. No new RIMAPI
endpoint. Slice-A only — `EntryCells = []`, `RegionId = null` until
`rimapi-room-entry-cells` / `rimapi-map-region-at` land (see
`willie-briefing-schema.md` S5).

Files (new):

- `Src/StateStore/Derivations/WillieAnchorInventoryDerivation.cs`
  - `Derive(ColonyState s) → WillieAnchorInventory`. Per
    `willie-briefing-schema.md` S3:
    1. For every `RoomRecord`, map `RoleLabel` → `RoomClass` via a
       deterministic table; fall back to `BuildingClassifier` on
       contained-bed-ids; skip if still unmapped.
    2. Compute `Centroid` as mean of contained `BuildingRecord.Position`s
       (today: bed positions only; broader after
       `rimapi-room-detail-read`). Null if no contained positions known.
    3. `EntryCells = []`, `RegionId = null`.
- `Src/StateStore/Derivations/Common/RoomClassMapper.cs`
  - Deterministic `RoleLabel → RoomClass` table; shared by anchor
    inventory + future `functional_rooms` rules.

Files (modified):

- `Src/StateStore/Derivations/Common/BuildingClassifier.cs` — add
  `IsHospitalBed(BuildingRecord)` / `IsResearchBench(BuildingRecord)` /
  `IsWorkshopBench(BuildingRecord)` predicates for fallback room
  inference (drop "Like" suffix — matches existing `IsCooler` /
  `IsCookingBuilding` / `IsButcherTable` style already in this file).
- `Src/StateStore/Derivations/WillieBriefingDerivation.cs` — extend the
  WB2 stub to populate `WillieBriefing.AnchorInventory` from the
  derivation.

Tests (new):

- `Src/Tests/State/WillieAnchorInventoryDerivationTests.cs`
  - Fixture: 1 kitchen room (RoleLabel="Kitchen") + 1 unmapped room +
    1 bedroom (via contained-bed-ids fallback).
  - Asserts: 2 anchors emitted (kitchen + bedroom), unmapped room
    skipped, `EntryCells = []`, `RegionId = null`, `Centroid` populated
    for the bedroom, null for kitchen if no buildings inside, solver
    fallback path tolerates null-centroid anchors without throwing.

Docs to touch (same commit):

- `Docs/design/state-store.md` — note `WillieAnchorInventoryDerivation`
  as a per-cycle derivation alongside the food derivations.

`// TODO:` comments to land in source:

- `WillieAnchorInventoryDerivation` contained-buildings inference —
  `// TODO: use rimapi-room-detail-read general contained-building
  list once available; today limited to ContainedBedIds`
- `RoomClassMapper` fallback miss path — `// TODO: re-evaluate
  fallback BuildingClassifier coverage when functional_rooms rule
  ships`
- `WillieRoomAnchor.Centroid` null branch — `// TODO: solver Slice-A
  fallback must skip null-centroid anchors until FORK3 client wires
  EntryCells path`

Risk: **low–medium.** Pure derivation; no live mutation; failure mode =
empty anchor list (solver fallback per `placement-solver.md`).

### WB4 — FORK3 RimApiClient methods  *(NETWORK PRIMITIVES)*

Adds RimBob client methods for the three FORK3 endpoints. **No
ingestion** in this slice — the methods stand alone until PS1 calls them.
This keeps the slice tiny and lets PS1 land independently.

Files (new):

- `Src/Ingestion/Dtos/MapReachDto.cs`
  - `MapReachResponseDto` + `MapPathCostRequestDto` /
    `MapPathCostResponseDto` + `MapPathCostBatchRequestDto` /
    `MapPathCostBatchResponseDto` matching
    [`rimapi-map-reach-and-path-cost.md`](rimapi-map-reach-and-path-cost.md).
  - `MapCellDto(int X, int Z)` — note `{x, z}` shape (no `y`), per the
    fork plan §"Endpoint Contract". Wire keys are lowercase `x` / `z`;
    match the sibling DTO casing convention (`JsonNamingPolicy.SnakeCaseLower`
    or `[JsonPropertyName]` per the surrounding files — verify at
    touch-time and use whichever pattern already covers this folder).

Files (modified):

- `Src/GameStateSync/RimApiClient.cs` — add three methods:
  ```csharp
  public Task<MapReachResponseDto> GetReachAsync(
      int mapId, int fromX, int fromZ, int toX, int toZ,
      string mode = "pass_doors", string peMode = "on_cell",
      CancellationToken ct = default);

  public Task<MapPathCostResponseDto> PostPathCostAsync(
      MapPathCostRequestDto request, CancellationToken ct = default);

  public Task<MapPathCostBatchResponseDto> PostPathCostBatchAsync(
      MapPathCostBatchRequestDto request, CancellationToken ct = default);
  ```
  GET via `GetEnvelopedAsync<T>`; POST methods follow the existing POST
  envelope helper (`PostEnvelopedAsync<T>` or equivalent — verify at
  touch-time; if absent, add a sibling helper in the same file rather
  than duplicating envelope-unwrap logic per method).

  **Client-side cap:** `PostPathCostBatchAsync` throws
  `ArgumentException` when `request.Pairs.Count > 4096` before the
  HTTP call, matching the fork plan §"Endpoint Contract" cap. This is
  robust validation, not compat code.

Tests (new):

- `Src/Tests/Ingestion/RimApiClientFork3Tests.cs` — mock HTTP fixture
  per existing `RimApiClientTests` pattern. Covers:
  - `GetReachAsync` returns `MapReachResponseDto` for a happy-path 200.
  - `PostPathCostBatchAsync` throws `ArgumentException` when
    `Pairs.Count > 4096` (client-side guard mirrors fork plan cap).
  - Envelope `success: false` throws `RimApiException` (existing helper).

Docs to touch (same commit):

- `Docs/design/RimAPI.md` — register the three new RimBob-side wrappers
  for the FORK3 endpoints if the doc catalogues client coverage; if it
  only catalogues fork endpoints, no edit needed (verify section at
  touch-time).

`// TODO:` comments to land in source:

- `RimApiClient.PostPathCostBatchAsync` cap constant —
  `// TODO: source the 4096 cap from a shared constant once a second
  consumer needs it; today only PS1 reads`
- All three methods — `// TODO: first consumer is PS1
  (placement-solver.md); methods unused until then`

Risk: **low.** Pure additive HTTP wrappers. No state-store change.
Mitigation: methods unused outside tests until PS1 ships.

---

## 2. Keep-green (every slice)

- Sync worktree with `master` before any verification build (AGENTS GIT
  rule).
- `dotnet build Src/RimBob.sln`
- `dotnet test Src/Tests/RimBob.Tests.csproj`
- `npm.cmd run build` from `Dashboard` (no dashboard wiring this plan —
  but Willie's dashboard tab is the next slice's home; keep build green).
- `.\run-rimbob.ps1` then smoke-check Host `/api/system/health`.

---

## 3. Recommended gimp order

1. **WB1** — safe, mechanical; gets the record skeleton in. Test:
   `WillieBriefingShapeTests` passes.
2. **WB2** — adds the state-store aggregate + live ingestion of
   `/api/v1/map/construction/backlog`. Smoke: launch RimWorld with
   blueprints placed, hit `GET /api/briefings/willie/latest` (endpoint
   added in next plan) or inspect via existing
   `MinisterEndpoints.GetLatestBriefing` once registered.
3. **WB3** — derivation. Pure code; falls under the unit-test fence.
4. **WB4** — FORK3 client wrappers. No live consumer; tests use mock
   HTTP fixtures.

WB2 + WB3 are independent and may be ordered either way.

---

## 4. Files touched (summary)

| Slice | New | Modified | Docs |
|---|---|---|---|
| WB1 | `Src/Common/Briefings/WillieBriefing.cs`, `WillieRoomAnchor.cs`, `WillieConcernSummaries.cs`, `WillieDataCoverage.cs`, `Src/Tests/Willie/WillieBriefingShapeTests.cs` | `Src/StateStore/ColonyState.cs` (aggregate-name constant + accessor) | `Docs/design/state-store.md`, `Docs/design/ministers/construction.md` |
| WB2 | `Src/Ingestion/Dtos/ConstructionBacklogDto.cs`, `Src/StateStore/Ingestion/WillieBacklogMapper.cs`, `Src/StateStore/Derivations/WillieBriefingDerivation.cs`, `Src/Tests/State/WillieBacklogMapperTests.cs`, `Src/Tests/State/WillieBriefingDerivationTests.cs` | `Src/Common/Aggregates/Snapshots.cs` (extend with `WillieConstructionBacklog` + `WillieBacklogGroup` + `MaterialCount`), `Src/GameStateSync/RimApiClient.cs`, `Src/StateStore/Ingestion/IngestionDispatcher.cs`, `Src/StateStore/ColonyState.cs` (aggregate slot + name-list append) | `Docs/design/state-store.md`, `Docs/design/RimAPI.md` |
| WB3 | `Src/StateStore/Derivations/WillieAnchorInventoryDerivation.cs`, `Src/StateStore/Derivations/Common/RoomClassMapper.cs`, `Src/Tests/State/WillieAnchorInventoryDerivationTests.cs` | `Src/StateStore/Derivations/Common/BuildingClassifier.cs`, `Src/StateStore/Derivations/WillieBriefingDerivation.cs` | `Docs/design/state-store.md` |
| WB4 | `Src/Ingestion/Dtos/MapReachDto.cs`, `Src/Tests/Ingestion/RimApiClientFork3Tests.cs` | `Src/GameStateSync/RimApiClient.cs` | `Docs/design/RimAPI.md` (verify scope) |

### Dashboard implications (note for the follow-on minister slice)

This plan does not wire the dashboard, but the `WillieBriefing` it lands
should surface on a future **Willie > Briefing** tab once the
`MinisterOfWillie` registers (separate slice). Likely panels:

- `Anchor Inventory` — table of `WillieRoomAnchor` rows (class,
  room id, cells count, centroid, entry-cell count, region id).
  Useful for debugging `near:<class>` resolution.
- `Construction Backlog` — table of `WillieBacklogGroup` rows
  (def, count, allowed flag, total work left, missing materials,
  blocked count). Mirrors RimMind's `ConstructionBacklogPart` view.
- `Data Coverage` — boolean strip from `WillieDataCoverage` showing
  which signals the current cycle has (rooms / backlog / anchors /
  reachability). Lets the player see why a rule did not fire.

No code in this plan; logged here so the follow-on slice can spec the
panels against a stable briefing.

---

## 5. Out of scope (separate plans)

- **`MinisterOfWillie` registry / DI wiring** — covered by
  `base-construction-layout-agent.md` Slices A/B/C.
- **Willie Rules `Rules.cs`** — depends on this plan but lands in the
  follow-on Willie minister slice. The Slice-A rule cut is specified in
  `willie-briefing-schema.md` §S4.
- **Placement Solver PS1** — separate plan
  ([`placement-solver.md`](placement-solver.md) §5). Consumes
  `WillieAnchorInventory` + FORK3 client methods from this plan.
- **Dashboard `Willie` tab** — needs the minister to register first.
- **FORK2 (blueprint groups)** — fork-side
  ([`rimapi-blueprint-groups-and-planning-overlay.md`](rimapi-blueprint-groups-and-planning-overlay.md)).
- **`rimapi-room-detail-read` / `rimapi-stockpile-detail-read` /
  `rimapi-building-detail-read` / `rimapi-power-net-read` /
  `rimapi-map-region-at` / `rimapi-room-entry-cells` /
  `rimapi-buildability-layers-read`** — each is its own HumanTodo
  capture. The briefing record exposes the fields these endpoints will
  fill; until they land the relevant `WillieBriefing` slots stay empty
  per their `need-fork` rows in
  [`willie-briefing-fields.md`](willie-briefing-fields.md).
- **`WillieBriefing` ingestion into the dashboard
  `GET /api/briefings/willie/latest` endpoint** — small follow-on; not
  part of this slice since `MinisterEndpoints` registration depends on
  the minister existing.

---

## 6. Verification (per slice, before commit)

- **Slice WB1.** Shape tests pass; JSON round-trip stable. Build green
  on `dotnet build Src/RimBob.sln`; test green on
  `dotnet test Src/Tests/RimBob.Tests.csproj`.
- **Slice WB2.** Two smokes: (a) RimWorld stopped → ingest cycle emits
  an empty `WillieConstructionBacklog` without throwing; (b) RimWorld
  running with ≥1 placed blueprint → live RIMAPI call returns a
  populated backlog; aggregate version bumps once per ingest cycle;
  briefing surfaces the same data via `WillieMaterialBottleneckSummary`.
  Use worktree port `http://127.0.0.1:5101` for the live run.
- **Slice WB3.** Derivation tests pass (including the null-centroid
  branch — anchor with `Centroid = null` must serialize and round-trip
  without throwing; solver Slice-A fallback skips it). Cycle-cost stays
  below the per-cycle ingest budget (mirror Food's derivation benchmark
  if one exists; otherwise log timing in the commit summary).
- **Slice WB4.** Client unit tests pass; manual `curl` against a live
  RIMAPI confirms wire shape matches DTO (cross-check against
  `rimapi-map-reach-and-path-cost.md` §"Verification" expected payloads).
  Worktree port still `5101`; main checkout `5000` must not be touched.

---

## 7. HumanTodo capture

**Already landed** in commit `e0d4ebd` (2026-05-28). See the
`willie-briefing-derivation` entry under `Captured by /todo` in
[`HumanTodo.md`](../HumanTodo.md).
