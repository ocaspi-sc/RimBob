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
`WillieRoomAnchor`, etc. Existing project conventions (lowercase namespace
`Src/Common/Briefings/`, `IBriefing` interface) are preserved.

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
  `Food` / `Mayor` / `Welfare` patterns at lines 49–56).

Tests (new):

- `Src/Tests/Willie/WillieBriefingShapeTests.cs`
  - Willie briefing with all defaults round-trips through
    `System.Text.Json.JsonSerializer` (mirror
    `FoodBriefingSerializationTests` if one exists).
  - `WillieAnchorInventory.Anchors` defaults to empty list.
  - `WillieRoomAnchor.EntryCells` / `RegionId` are optional and serialize
    as empty list / null without throwing.

Risk: **low.** Pure type addition. No service consumes it yet.

### WB2 — Construction backlog ingestion  *(FOUNDATIONAL)*

Adds the missing state-store aggregate + briefing wiring for
`/api/v1/map/construction/backlog`. Unblocks `material_bottleneck` +
`stalled_builds` rule families.

Files (new):

- `Src/Ingestion/Dtos/ConstructionBacklogDto.cs`
  - `ConstructionBacklogGroupDto` matching the fork DTO shape
    (`kind`, `def_name`, `stuff_def_name`, `allowed`, `count`,
    `thing_ids[]`, `sample_cells[]`, `total_work_left`, `cost[]`,
    `materials_available[]`, `materials_missing[]`, `blocked_count`,
    `disallowed_count`) — verify against
    `C:\dev\RIMAPI-for-RimBob\Source\RIMAPI\RimworldRestApi\Models\BuilderDtos.cs`
    at touch-time.
  - Nested DTO records for `MaterialCount`, `SampleCell`, etc.
- `Src/Common/Aggregates/WillieBacklog.cs` (or extend `Snapshots.cs`)
  - `WillieConstructionBacklog(IReadOnlyList<WillieBacklogGroup> Groups)`.
  - `WillieBacklogGroup(string Kind, string DefName, string?
    StuffDefName, bool Allowed, int Count, IReadOnlyList<string>
    ThingIds, IReadOnlyList<MapPosition> SampleCells, int
    TotalWorkLeft, IReadOnlyList<MaterialCount> Cost,
    IReadOnlyList<MaterialCount> MaterialsAvailable,
    IReadOnlyList<MaterialCount> MaterialsMissing, int BlockedCount,
    int DisallowedCount)`.
  - `MaterialCount(string DefName, int Count)`.

Files (modified):

- `Src/GameStateSync/RimApiClient.cs` — add:
  ```csharp
  public Task<IReadOnlyList<ConstructionBacklogGroupDto>>
      GetConstructionBacklogAsync(int mapId, CancellationToken ct) =>
      GetEnvelopedAsync<IReadOnlyList<ConstructionBacklogGroupDto>>(
          $"api/v1/map/construction/backlog?map_id={mapId}", ct);
  ```
- `Src/StateStore/Ingestion/MapAggregateMapper.cs` (or new
  `WillieBacklogMapper.cs` if cleaner) — `FromConstructionBacklog(dto)
  → WillieConstructionBacklog`.
- `Src/StateStore/ColonyState.cs` — new `Versioned<WillieConstructionBacklog>
  WillieBacklog` aggregate; default in `AggregateDefaults`.
- `Src/StateStore/Ingestion/IngestionDispatcher.cs` — schedule the new
  call on the live-refresh cycle.
- `Src/StateStore/ColonyState.cs` `WillieBriefingAggregateNames` constant
  picks up `"WillieBacklog"`.

Briefing wiring:

- `Src/StateStore/Derivations/WillieBriefingDerivation.cs` (new) —
  start with a stub that copies `WillieConstructionBacklog` straight into
  `WillieMaterialBottleneckSummary` / `WillieStalledBuildsSummary`.
  No rules yet; just data plumbing.

Tests (new):

- `Src/Tests/State/WillieBacklogMapperTests.cs` — DTO → aggregate mapping.
- `Src/Tests/State/WillieBriefingDerivationTests.cs` — given a
  fixture `ColonyState` with one backlog group, the briefing surfaces
  the group on both `MaterialBottleneck` and `StalledBuilds` summaries.

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
  `IsKitchenLike(BuildingRecord)` / `IsHospitalLike(BuildingRecord)` /
  `IsWorkshopLike(BuildingRecord)` predicates for the fallback room
  inference. Mirror existing `IsCooker` / `IsCooler` style.
- `Src/StateStore/Derivations/WillieBriefingDerivation.cs` — populate
  `WillieBriefing.AnchorInventory` from the derivation.

Tests (new):

- `Src/Tests/State/WillieAnchorInventoryDerivationTests.cs`
  - Fixture: 1 kitchen room (RoleLabel="Kitchen") + 1 unmapped room +
    1 bedroom (via contained-bed-ids fallback).
  - Asserts: 2 anchors emitted (kitchen + bedroom), unmapped room
    skipped, `EntryCells = []`, `RegionId = null`, `Centroid` populated
    for the bedroom, null for kitchen if no buildings inside.

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
    fork plan §"Endpoint Contract".

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
  All POST methods follow the existing `GetEnvelopedAsync` envelope
  pattern (`api/v1/map/reach` is GET; the two `path-cost` variants are
  POST per the fork plan).

Tests (new):

- `Src/Tests/Ingestion/RimApiClientFork3Tests.cs` — mock HTTP fixture
  per existing `RimApiClientTests` pattern. Covers:
  - `GetReachAsync` returns `MapReachResponseDto` for a happy-path 200.
  - `PostPathCostBatchAsync` rejects > 4096 pairs (client-side guard
    mirrors fork plan §"Endpoint Contract" cap).
  - Envelope `success: false` throws `RimApiException` (existing helper).

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

| Slice | New | Modified |
|---|---|---|
| WB1 | `Src/Common/Briefings/WillieBriefing.cs`, `WillieRoomAnchor.cs`, `WillieConcernSummaries.cs`, `WillieDataCoverage.cs`, `Src/Tests/Willie/WillieBriefingShapeTests.cs` | `Src/StateStore/ColonyState.cs` (aggregate-name constant) |
| WB2 | `Src/Ingestion/Dtos/ConstructionBacklogDto.cs`, `Src/Common/Aggregates/WillieBacklog.cs`, `Src/StateStore/Derivations/WillieBriefingDerivation.cs`, `Src/Tests/State/WillieBacklogMapperTests.cs`, `Src/Tests/State/WillieBriefingDerivationTests.cs` | `Src/GameStateSync/RimApiClient.cs`, `Src/StateStore/Ingestion/MapAggregateMapper.cs`, `Src/StateStore/Ingestion/IngestionDispatcher.cs`, `Src/StateStore/ColonyState.cs` |
| WB3 | `Src/StateStore/Derivations/WillieAnchorInventoryDerivation.cs`, `Src/StateStore/Derivations/Common/RoomClassMapper.cs`, `Src/Tests/State/WillieAnchorInventoryDerivationTests.cs` | `Src/StateStore/Derivations/Common/BuildingClassifier.cs`, `Src/StateStore/Derivations/WillieBriefingDerivation.cs` |
| WB4 | `Src/Ingestion/Dtos/MapReachDto.cs`, `Src/Tests/Ingestion/RimApiClientFork3Tests.cs` | `Src/GameStateSync/RimApiClient.cs` |

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

- Slice WB1: shape tests pass; JSON round-trip stable.
- Slice WB2: live RIMAPI call returns a populated backlog; aggregate
  version bumps once per ingest cycle; briefing surfaces the same data
  back through `WillieMaterialBottleneckSummary`.
- Slice WB3: derivation tests pass; cycle-cost stays below the
  per-cycle ingest budget (mirror Food's derivation benchmark if one
  exists; otherwise log timing and document).
- Slice WB4: client unit tests pass; manual `curl` against a live
  RIMAPI confirms wire shape matches DTO (cross-check against
  `rimapi-map-reach-and-path-cost.md` §"Verification" expected payloads).

---

## 7. HumanTodo capture text (to append in same commit as this plan)

```
- [ ] willie-briefing-derivation [2026-05-28] #construction #briefing #willie #state Implementation plan for Willie briefing record + state-store derivation + FORK3 client (Track A of the willie-briefing-schema follow-on). Sliced WB1-WB4: WillieBriefing skeleton; construction-backlog ingestion; WillieAnchorInventoryDerivation; RimApiClient FORK3 wrappers. Unblocks Willie Rules Slice A + Placement Solver PS1. [plan](.plans/willie-briefing-derivation.md)
```
