# RIMAPI Power Info DTO Migration

## Problem

`PowerInfoDto` at [MapDto.cs:218](Src/GameStateSync/Dtos/MapDto.cs:218) expects 4
floats (`Production`, `Consumption`, `Stored`, `Capacity`) with `production` /
`consumption` / `stored` / `capacity` JSON property names. RIMAPI's
`/api/v1/map/power/info` actually returns 9 fields (six ints, three `List<int>`
building-id lists) and uses PascalCase JSON names per the default RIMAPI
serializer.

Live source of truth: [`MapPowerInfoDto`](C:/dev/RIMAPI-for-RimBob/Source/RIMAPI/RimworldRestApi/Models/Map/MapDto.cs:23)
and [`MapHelper.GetMapPowerInfoInternal`](C:/dev/RIMAPI-for-RimBob/Source/RIMAPI/RimworldRestApi/Helpers/MapHelper.cs:119).

Today the RimBob DTO will silently bind to nothing and surface zeros for every
field. Mayor's power telemetry has been wrong since the RIMAPI swap.

## RIMAPI Field Semantics (verified from MapHelper)

| RIMAPI field            | Type        | Unit | Source                                                                |
| ----------------------- | ----------- | ---- | --------------------------------------------------------------------- |
| `CurrentPower`          | int         | W    | Σ `CompPowerPlant.PowerOutput` (live generator output)                |
| `TotalPossiblePower`    | int         | W    | Σ `Abs(CompPowerPlant.Props.PowerConsumption)` (nameplate generation) |
| `CurrentlyStoredPower`  | int         | Wd   | Σ `CompPowerBattery.StoredEnergy`                                     |
| `TotalPowerStorage`     | int         | Wd   | Σ `CompPowerBattery.Props.storedEnergyMax`                            |
| `TotalConsumption`      | int         | W    | Σ `Props.PowerConsumption` for any trader with declared draw          |
| `ConsumptionPowerOn`    | int         | W    | Σ `Abs(PowerOutput)` for traders currently powered on                 |
| `ProducePowerBuildings` | `List<int>` | —    | `thingIDNumber` of each generator                                     |
| `ConsumePowerBuildings` | `List<int>` | —    | `thingIDNumber` of each trader                                        |
| `StorePowerBuildings`   | `List<int>` | —    | `thingIDNumber` of each battery                                       |

`TotalConsumption` is the nameplate sum (every powered comp, on or off);
`ConsumptionPowerOn` is the actual current grid load. They're meaningfully
different — `ConsumptionPowerOn` is what the network is drawing *right now*,
which is what should drive surplus/deficit reasoning.

JSON property casing: RIMAPI uses ASP.NET's default `JsonSerializerOptions`
(PascalCase property names, no `[JsonPropertyName]` overrides on this DTO). The
new RimBob record needs `[JsonPropertyName]` attributes with PascalCase names.

## Decisions

### 1. Replace `PowerInfoDto` with full 9-field record

New shape mirrors RIMAPI exactly. Ints stay ints (no float coercion at the DTO
layer); convert in the mapper.

```csharp
public record PowerInfoDto(
    [property: JsonPropertyName("CurrentPower")]          int CurrentPower,           // W
    [property: JsonPropertyName("TotalPossiblePower")]    int TotalPossiblePower,     // W
    [property: JsonPropertyName("CurrentlyStoredPower")]  int CurrentlyStoredPower,   // Wd
    [property: JsonPropertyName("TotalPowerStorage")]     int TotalPowerStorage,      // Wd
    [property: JsonPropertyName("TotalConsumption")]      int TotalConsumption,       // W (nameplate)
    [property: JsonPropertyName("ConsumptionPowerOn")]    int ConsumptionPowerOn,     // W (live draw)
    [property: JsonPropertyName("ProducePowerBuildings")] IReadOnlyList<int> ProducePowerBuildings,
    [property: JsonPropertyName("ConsumePowerBuildings")] IReadOnlyList<int> ConsumePowerBuildings,
    [property: JsonPropertyName("StorePowerBuildings")]   IReadOnlyList<int> StorePowerBuildings);
```

No derived/convenience properties on the DTO. DTOs stay wire-faithful; semantic
mapping lives in `MapAggregateMapper`.

### 2. Keep `PowerNetwork` shape unchanged for this slice

[`PowerNetwork`](Src/Common/Aggregates/Snapshots.cs:126) today is
`PowerNetwork(float ProductionW, float ConsumptionW, float StoredWd, float CapacityWd)`.
The cleanest minimum fix: keep that shape, update the mapper:

```csharp
public static PowerNetwork FromPower(PowerInfoDto p) =>
    new(p.CurrentPower, p.ConsumptionPowerOn, p.CurrentlyStoredPower, p.TotalPowerStorage);
```

Rationale for the mapping choices:

- `ProductionW` ← `CurrentPower` — live generator output, matches old intent.
- `ConsumptionW` ← `ConsumptionPowerOn` — live draw, not nameplate. This is the
  number Mayor needs for surplus/deficit. `TotalConsumption` (nameplate) would
  over-count anything off.
- `StoredWd` ← `CurrentlyStoredPower`.
- `CapacityWd` ← `TotalPowerStorage`.

The ints convert to floats implicitly. No new units, no new downstream changes.

**Deferred (separate slice if needed):** expanding `PowerNetwork` to surface
`TotalPossiblePowerW`, nameplate `ConsumptionW`, and building-id lists. None of
the current consumers ([`MayorBriefingDerivation.cs:227`](Src/StateStore/Derivations/MayorBriefingDerivation.cs:227),
the `PowerSnapshot` derivation) reads those fields, and Construction's
building-condition work ([`source-todo-building-condition-read`](../HumanTodo.md))
is the better home for per-building power state. Keep this slice focused on
"stop returning zeros."

### 3. Caller audit

Grep confirms exactly three RimBob touch points:

- [`Src/GameStateSync/RimApiClient.cs:274`](Src/GameStateSync/RimApiClient.cs:274) —
  `GetPowerInfoAsync` just passes `PowerInfoDto` through. No change needed
  beyond the new DTO compiling.
- [`Src/StateStore/Ingestion/MapAggregateMapper.cs:282`](Src/StateStore/Ingestion/MapAggregateMapper.cs:282) —
  reads `.Production`, `.Consumption`, `.Stored`, `.Capacity`. Update field
  names per the mapping above.
- [`Src/Tests/State/IngestionDispatcherTests.cs:732`](Src/Tests/State/IngestionDispatcherTests.cs:732) —
  `new PowerInfoDto(2000f, 1500f, 100f, 500f)`. Rewrite to the new
  9-argument constructor, e.g.
  `new PowerInfoDto(2000, 4000, 100, 500, 2200, 1500, [], [], [])`. Adjust
  expectations if any test asserts on `PowerNetwork` values — the mapper now
  pulls `ConsumptionPowerOn` (the 6th arg) into `ConsumptionW`.

No other call sites. `PowerNetwork` itself has many consumers
([snapshot store](Src/StateStore/ColonyState.cs:27), Mayor/Chef briefing tests,
food briefing test) but none change because the record shape is unchanged.

### 4. Wipe persisted latest snapshots

Any persisted `ColonyStateSnapshot` JSON containing the all-zero broken
`PowerNetwork` should be wiped on the same merge, per the project pattern from
[`normalized-game-time`](normalized-game-time.md). Not strictly required (next
ingestion overwrites in-memory state), but persisted latest files should not
ship a known-broken zero baseline.

## Implementation Slice

1. Rewrite the `PowerInfoDto` record in
   [`Src/GameStateSync/Dtos/MapDto.cs:218`](Src/GameStateSync/Dtos/MapDto.cs:218)
   with the 9-field PascalCase-attributed shape above. Add a leading comment
   noting RIMAPI's PascalCase serializer and the W vs Wd unit split.
2. Update
   [`Src/StateStore/Ingestion/MapAggregateMapper.cs:282`](Src/StateStore/Ingestion/MapAggregateMapper.cs:282)
   `FromPower` to read `CurrentPower` / `ConsumptionPowerOn` /
   `CurrentlyStoredPower` / `TotalPowerStorage`.
3. Update the fixture in
   [`Src/Tests/State/IngestionDispatcherTests.cs:732`](Src/Tests/State/IngestionDispatcherTests.cs:732)
   to the new constructor; pick values such that `ConsumptionPowerOn` matches
   what the existing assertions (if any) expect for `ConsumptionW`.
4. Add a tiny regression: serialize a sample RIMAPI payload string (PascalCase
   names, lists present) and assert it deserializes into the new DTO with
   non-zero values across all 9 fields. Confirms casing + list binding.
5. Run `dotnet test` for the GameStateSync / StateStore / Tests projects.
6. Delete any committed `latest-*.json` snapshot files that carry the broken
   zero `PowerNetwork`. (Optional — confirm with user before deleting.)

## Verification

Codex must run all of these from the worktree root and they must all pass
before declaring done:

1. `dotnet build` on the solution — the new DTO + mapper + test fixture must
   compile cleanly. No new warnings.
2. `dotnet test Src/Tests/Tests.csproj --filter "FullyQualifiedName~IngestionDispatcher|FullyQualifiedName~MapAggregate|FullyQualifiedName~PowerInfo"`
   — covers the rewritten fixture and the new deserialization regression.
3. `dotnet test Src/Tests/Tests.csproj` — full suite. PowerNetwork's float
   record shape is unchanged so downstream Mayor/Chef/Food/Briefing tests
   should pass without edits. If anything breaks, it indicates an unrelated
   regression introduced by the change — investigate, do not paper over.
4. Deserialization regression test (added in step 4 of the Implementation
   Slice): feeds a PascalCase JSON payload through the same
   `System.Text.Json` options the production `GetEnvelopedAsync` uses and
   asserts every field binds to its expected non-zero value, including all
   three `List<int>` lists with at least one element each.

Pass/fail: all four green. Codex must paste the build line and the test
summary (`Passed:`, `Failed:`, `Skipped:`) into the final message.

## Where to See It (Dashboard)

End-to-end surface for a human sanity check:

- **Snapshot store** — `state.Power.Value` on
  [`ColonyStateSnapshot.cs:61`](Src/StateStore/ColonyStateSnapshot.cs:61)
  should have non-zero `ProductionW` / `ConsumptionW` / `StoredWd` /
  `CapacityWd` once an ingest tick runs against a live RIMAPI with any
  generator on the map.
- **Mayor briefing** — `PowerSnapshot` at
  [`MayorBriefingDerivation.cs:227`](Src/StateStore/Derivations/MayorBriefingDerivation.cs:227)
  derives `Net = ProductionW - ConsumptionW`; this is what Mayor advice keys
  off and what the dashboard renders.
- **Dashboard** — `PowerSnapshot` mirror at
  [`Dashboard/src/types/colony.ts:71`](Dashboard/src/types/colony.ts:71),
  surfaced in `ColonySidebar.tsx` and `InfoOverview.tsx`. No TS changes
  required; the existing fields just start carrying real numbers.

No new dashboard panel is in scope. The check is "the existing power readout
stops showing zeros".

## Out of Scope

- Expanding `PowerNetwork` with `TotalPossiblePowerW`, nameplate
  `TotalConsumptionW`, or building-id lists.
- Mayor / dashboard surfacing of per-building power state (belongs with
  [`source-todo-building-condition-read`](../HumanTodo.md)).
- Any RIMAPI-side change. The fork already returns the 9 fields correctly.
- Deleting persisted `latest-*.json` snapshot files. (Mentioned in the
  earlier draft. The next ingest tick overwrites in-memory state; the
  on-disk file is rewritten on the next snapshot save. Not worth tangling
  this slice with file deletion.)

## Locked Decisions

These were "open questions" in the first draft. Locking in so Codex doesn't
deliberate:

- **`ConsumptionW` ← `ConsumptionPowerOn`**, not `TotalConsumption`. Mayor's
  power advice should reflect live grid draw, not the nameplate budget.
  `TotalConsumption` (nameplate) would over-count anything turned off and
  produce false deficits.
- **Defer `TotalPossiblePower` and the three building-id lists.** Construction
  is the natural consumer and that work is still scoped under the Willie
  meta-plan. Surfacing these now means touching `PowerNetwork`, `PowerSnapshot`,
  Mayor briefing, dashboard TS — all out of scope for "stop returning zeros".

---

## Summary (landed 2026-05-27)

**Motivation.** `PowerInfoDto` was bound to the old RIMAPI shape (four
lowercase floats: `production`/`consumption`/`stored`/`capacity`). Live RIMAPI
returns nine PascalCase fields (six ints + three `List<int>` building-id
lists), so every field silently bound to zero. Mayor's power telemetry had
been wrong since the RIMAPI swap. Goal: stop returning zeros.

**Context.** RIMAPI source of truth at
[`MapPowerInfoDto`](C:/dev/RIMAPI-for-RimBob/Source/RIMAPI/RimworldRestApi/Models/Map/MapDto.cs:23)
and [`MapHelper.GetMapPowerInfoInternal`](C:/dev/RIMAPI-for-RimBob/Source/RIMAPI/RimworldRestApi/Helpers/MapHelper.cs:119)
confirmed field semantics + units (W vs Wd). `PowerNetwork`'s float shape kept
intact because no consumer needed the new fields; expanding it would have
touched Mayor briefing, dashboard TS, and the Willie Construction work.

**Scope.**
- Rewrote `PowerInfoDto` to the 9-field PascalCase record with explicit
  `[JsonPropertyName]` attrs (RIMAPI uses default ASP.NET PascalCase serializer).
- Updated `MapAggregateMapper.FromPower` to map `CurrentPower` →
  `ProductionW`, `ConsumptionPowerOn` → `ConsumptionW`, `CurrentlyStoredPower`
  → `StoredWd`, `TotalPowerStorage` → `CapacityWd`. `PowerNetwork` shape
  unchanged.
- Added deserialization regression in
  [`Src/Tests/Ingestion/RimApiClientTests.cs`](Src/Tests/Ingestion/RimApiClientTests.cs)
  that feeds a PascalCase payload through the production envelope/options and
  asserts every field — including all three building-id lists — binds non-zero.
- Updated `IngestionDispatcherTests.cs:732` fixture to the new 9-argument
  constructor.

**How to verify (human).**
- Dashboard: `ColonySidebar` / `InfoOverview` power readout (driven by
  `PowerSnapshot` mirror at
  [`Dashboard/src/types/colony.ts:71`](Dashboard/src/types/colony.ts:71)) —
  power numbers stop showing zeros once a live ingest tick runs.
- Snapshot: `state.Power.Value` (`ProductionW`/`ConsumptionW`/`StoredWd`/
  `CapacityWd`) carries real numbers after the next ingest against a live
  RIMAPI.
- Commands: `dotnet build Src\RimBob.sln` and
  `dotnet test Src\Tests\RimBob.Tests.csproj` (328 passed / 0 failed on
  landing).
- Files to glance at: [MapDto.cs](Src/GameStateSync/Dtos/MapDto.cs:218),
  [MapAggregateMapper.cs](Src/StateStore/Ingestion/MapAggregateMapper.cs:282),
  [RimApiClientTests.cs](Src/Tests/Ingestion/RimApiClientTests.cs).

**Codex run:** `20260527-180147-rimapi-power-info-dto` · branch
`codex/prompt-20260527-180147-rimapi-power-info-dto` · landed commit
`0b10ede`
