# RIMAPI Building Detail Read — hp / stuff / room / power / fuel / flammability

## Summary

Willie reasons about building *condition* (is the cooler powered? is the wall
damaged? what is it made of?) but the live wire only carries `id`, `def`,
`label`, `position`, `rotation`, `size`, `type`. The richer fields exist on the
RimWorld `Building` object and are already read by the fork's turret/generator
helpers — they are just not exposed on the generic building list. This plan adds
hp/maxhp, stuff, room id, working state, power state, fuel, flickable, and
flammability to the RIMAPI `/api/v1/map/buildings` list (and the by-id detail
read), and ingests them into RimBob's `BuildingRecord` so Willie rules can stop
defaulting them.

Fills the long-standing stub Tasks.md `rimapi-building-detail-read` and unblocks
`source-todo-building-condition-read`.

## Current Evidence

**Live list** (`GET http://localhost:8765/api/v1/map/buildings?map_id=0`) returns
per building only: `id, def, label, position, rotation, size, type`. No
hp/power/working/stuff/room/fuel. Confirmed against the live host (55 buildings,
all bare).

**Fork list source** — `Source/RIMAPI/RimworldRestApi/Helpers/MapHelper.cs:394`
(`GetMapBuildings(int mapId)`) inline-constructs `BuildingDto` per
`map.listerBuildings.allBuildingsColonist`, setting only the 7 bare fields.

**Fork DTO** — `Source/RIMAPI/RimworldRestApi/Models/BuildingDto.cs:23-32`
(`BuildingDto`): `Id, Def, Label, Position, Rotation, Size, Type`. Subclasses
`TurretInfoDto` (Health/MaxHealth/IsWorking/power/fuel) and
`PowerGeneratorInfoDto` (PowerOn/PowerOutput) prove every needed comp is
reachable from a `Building`.

**Fork by-id detail** — `Source/RIMAPI/RimworldRestApi/Services/BuildingService.cs:13-39`
(`GetBuildingInfo`). **Bug:** line 37 unconditionally reassigns
`result = BuildingHelper.BuildingToDto(building)` *after* computing the turret /
generator DTO, discarding the richer subclass. The detail endpoint therefore
already returns only bare fields even for turrets/generators.

**Fork base mapper** — `Source/RIMAPI/RimworldRestApi/Helpers/BuildingHelper.cs:11-26`
(`BuildingToDto`) — the shared bare mapper (also omits Rotation/Size).

**RimBob ingest DTO** — `Src/GameStateSync/Dtos/MapDto.cs:209-215` (`BuildingDto`,
5 fields) with a TODO: "expand when Willie begins or RIMAPI exposes hp/power state".

**RimBob mapper** — `Src/StateStore/Ingestion/MapAggregateMapper.cs:294-302`
(`BuildingRecordFrom`) hardcodes `Hp: 1.0f, PowerOn: null, IsWorking: null`. The
work-table branch (`:280-288`) does the same.

**RimBob record** — `Src/Common/Aggregates/Snapshots.cs:110-118` (`BuildingRecord`)
*already* has `float Hp`, `bool? PowerOn`, `bool? IsWorking` placeholder fields;
they have never carried real values.

## Wire Shape (target)

Extend the `/api/v1/map/buildings` element (snake_case):

```jsonc
{
  "id": 44710, "def": "FueledStove", "label": "fueled stove",
  "position": {...}, "rotation": 0, "size": {...}, "type": "Building_WorkTable",
  // new:
  "hp": 180, "max_hp": 180,
  "stuff": "Steel",            // null when not made-from-stuff
  "room_id": 12,               // RimWorld Room.ID, null if outdoors/none
  "is_working": true,          // building.IsWorking()
  "power": {                   // null when no CompPowerTrader
    "required": true, "on": true, "consumption_w": 100
  },
  "fuel": {                    // null when no CompRefuelable
    "current": 18.5, "capacity": 25.0, "fuel_def": "WoodLog"
  },
  "flickable_on": true,        // CompFlickable.SwitchIsOn, null when none
  "flammability": 1.0          // GetStatValue(StatDefOf.Flammability)
}
```

## Scope

### Part A — RIMAPI fork: enrich the building read

- **`Models/BuildingDto.cs`** — add the new fields to the base `BuildingDto`
  (nullable where the comp may be absent): `Hp`, `MaxHp`, `Stuff`, `RoomId`,
  `IsWorking`, a `PowerInfo` sub-object (`Required`/`On`/`ConsumptionW`), a
  `FuelInfo` sub-object (`Current`/`Capacity`/`FuelDef`), `FlickableOn`,
  `Flammability`. Keep existing `TurretInfoDto`/`PowerGeneratorInfoDto` working
  (they inherit the new optional fields harmlessly).

- **`Helpers/BuildingHelper.cs` (`BuildingToDto`)** — populate the new fields from
  the live `Building`:
  - `Hp = building.HitPoints`, `MaxHp = building.MaxHitPoints`
  - `Stuff = building.Stuff?.defName`
  - `RoomId = building.GetRoom()?.ID` (guard null/outdoors)
  - `IsWorking = building.IsWorking()`
  - power: `CompPowerTrader` → `Required = comp != null`, `On = comp?.PowerOn`,
    `ConsumptionW = comp?.Props.PowerConsumption`
  - fuel: `CompRefuelable` → `Current = comp?.Fuel`, `Capacity =
    comp?.Props.fuelCapacity`, `FuelDef = comp?.Props.fuelFilter?.AnyAllowedDef?.defName`
  - `FlickableOn = building.TryGetComp<CompFlickable>()?.SwitchIsOn`
  - `Flammability = building.GetStatValue(StatDefOf.Flammability)`
  Wrap each comp read defensively (the helpers already use the `?.`/`?? default`
  pattern) so a missing comp yields null, never a throw.

- **`Helpers/MapHelper.cs` (`GetMapBuildings`, :394)** — replace the inline
  `new BuildingDto { ...7 fields... }` with a call to the enriched
  `BuildingHelper.BuildingToDto(building)` (which now also sets Rotation/Size), so
  the list and the by-id detail share one mapper and one field set.

- **`Services/BuildingService.cs` (`GetBuildingInfo`, :37)** — fix the overwrite
  bug: only fall back to `BuildingToDto` when no specialized DTO was produced
  (`result ??= BuildingHelper.BuildingToDto(building);`), so turret/generator
  detail survives. With the base DTO now enriched, every building returns the
  condition fields regardless.

- **CHANGELOG / bruno** — bump the fork CHANGELOG and update
  `tests/bruno_api_collection/Map/Buildings.yml` expected fields.

### Part B — RimBob: ingest the new fields

- **`Src/GameStateSync/Dtos/MapDto.cs` (`BuildingDto`, :209-215)** — add matching
  nullable properties (`Hp`, `MaxHp`, `Stuff`, `RoomId`, `IsWorking`, `Power`,
  `Fuel`, `FlickableOn`, `Flammability`) with `JsonPropertyName` snake_case; add
  the two small sub-DTOs (`BuildingPowerDto`, `BuildingFuelDto`). Drop the "expand
  when RIMAPI exposes" TODO.

- **`Src/Common/Aggregates/Snapshots.cs` (`BuildingRecord`, :110-118)** — add the
  new fields after the existing `Hp/PowerOn/IsWorking`: `float? MaxHp`,
  `string? Stuff`, `int? RoomId`, `BuildingPower? Power`, `BuildingFuel? Fuel`,
  `bool? FlickableOn`, `float? Flammability`. Keep them optional so existing
  fixtures/construction sites compile unchanged. (Consider replacing the loose
  `PowerOn` with the `Power` sub-record, or keep both — decide in implementation;
  the simplest is to keep `PowerOn` mirroring `Power.On`.)

- **`Src/StateStore/Ingestion/MapAggregateMapper.cs` (`BuildingRecordFrom`,
  :294-302)** — map real values from the DTO instead of `Hp: 1.0f / null / null`.
  Update the work-table fallback branch (`:280-288`) to leave the now-real fields
  null (work tables come from a different endpoint without condition data) — or
  populate `IsWorking` from bills if cheap; default null is acceptable.

### Part C — surface (optional, thin)

- No Willie *rule* change in this slice — this is a read/ingest slice. Once the
  fields land, `cooler_missing` / `frame_blocked_by_material` and future
  condition rules can read `IsWorking`/`Power`/`Hp` instead of inferring. That
  rule rewrite is a separate follow-up (note it; do not do it here).

## Test Plan

- **Fork** — manual/bruno: `GET /api/v1/map/buildings?map_id=0` returns the new
  fields; a powered cooler shows `power.on=true`; a fueled stove shows
  `fuel.current>0`; a stone wall shows `stuff="BlocksGranite"`, `flammability≈0`;
  `GET /api/v1/map/building/{turretId}` now returns turret detail (regression on
  the :37 fix).

- **`Src/Tests/State/AggregateMapperTests.cs`** — extend the `FromBuildings`
  fixture so a DTO carrying `hp/max_hp/stuff/room_id/is_working/power/fuel/
  flickable_on/flammability` maps into a `BuildingRecord` with those values
  (replaces the current assertion that they are placeholder defaults).

- **`Src/Tests/State/IngestionDispatcherTests.cs`** — confirm the enriched DTO
  round-trips through ingestion without breaking existing building-registry
  assertions.

- **Live** — after fork rebuild + RimWorld restart + RimBob host restart:
  `GET /api/ministers/willie/snapshot` building evidence (or a raw state dump)
  shows non-placeholder `hp`/`power`/`stuff` for at least the kitchen/freezer
  buildings.

## Out of Scope

- Willie rule logic that *consumes* condition fields (separate follow-up; see
  `source-todo-building-condition-read`).
- `rimapi-stockpile-detail-read` and `rimapi-power-net-read` — sibling reads,
  separate slices.
- Writing building state (the existing `SetBuildingPower` flick endpoint stays
  as-is; no new writes).
- Non-colonist buildings — keep the existing `allBuildingsColonist` scope.

## Assumptions

- `building.GetRoom()`, `IsWorking()`, `HitPoints`, `MaxHitPoints`, `Stuff`,
  `CompPowerTrader`, `CompRefuelable`, `CompFlickable`, and
  `StatDefOf.Flammability` are all available in the fork's RimWorld/Verse
  reference set (the turret/generator helpers already use the comps).
- This slice is **independent** of the two Willie-orchestrator slices
  (`willie-freezer-apply-readiness`, non-freezer solver wiring): it touches the
  fork + `MapDto`/`MapAggregateMapper`/`BuildingRecord` only, none of which those
  slices edit. It can run in parallel.

## Summary — Landed (2026-05-30), two repos

Shipped as planned in both halves; ran independent of the Willie slices.

- **RIMAPI fork `34d92d0`** `feat(map): expose building details`. `BuildingToDto`
  now sets `Hp/MaxHp/Stuff/RoomId/IsWorking/Flammability` + power/fuel.
  `BuildingService.cs:37` overwrite bug **fixed** — fallback now guarded by
  `if (result == null)`, so turret/generator detail survives.
- **RimBob `b4206d4`** `feat(state): land building detail read`. `MapDto.BuildingDto`
  gains the fields; `MapAggregateMapper.BuildingRecordFrom` maps real values into
  `BuildingRecord` (the placeholder `Hp/PowerOn/IsWorking` now carry live data).
- **Drift beyond plan (sensible):** also touched `MayorBriefingDerivation` (new
  consumer of building condition) and bumped `ColonyStateSnapshot` schema for
  wipe-and-regen — not in plan scope but reasonable.

Unblocks `source-todo-building-condition-read`. The Willie *rule* rewrite that
consumes these fields is still the deferred follow-up (Part C, not done here).

Not re-verified live this closeout — confirm enriched `hp`/`power`/`stuff` on a
raw state dump after fork rebuild + RimWorld restart.
