# RIMAPI Blueprint Placement Endpoint(s) — Plan

> Agent-created plan. **All work lands in the RIMAPI fork**
> (`C:\dev\RIMAPI-for-RimBob`, repo `ocaspi-sc/RIMAPI-for-RimBob`), not this
> repo. RimBob integrates over HTTP only (no linking; GPL-3.0 posture per
> `AGENTS.md` / `RimApiClient` header).

---

## 1. Scope

Add three HTTP endpoints to the RIMAPI fork that let an external caller
**validate**, **place**, and **read** construction blueprints, with the cell
coordinates supplied by the caller. No `CanPlaceBlueprintAt`-skipping writes,
no destructive obstacle clearing, no implicit area paste.

Out of scope (explicit, §8): RimBob-side `RimApiClient` methods, Assisted Apply
integration, advice apply-handle wiring, dashboard cell-pick UI, server-side
cell resolution, multi-asset / area placement. Those land in a separate slice
once the endpoints exist.

## 2. Why these three (not just `place`)

`GenConstruct.PlaceBlueprintForBuild` silently no-ops on invalid placement, and
the fork's existing `POST /api/v1/builder/blueprint`
(`Source/RIMAPI/RimworldRestApi/Services/BuilderService.cs:169-235`) returns a
bare `ApiResult.Ok()` regardless. Without a paired **validate** (dry-run via
`CanPlaceBlueprintAt`) and **read** (pending blueprints/frames), a caller has
no way to know whether a write took effect or to be idempotent across retries.
The triplet is the minimum safe unit.

## 3. Endpoints

All three follow the fork's conventions:
attribute-routed `*Controller` (`BaseControllers/BuilderController.cs:9-41`),
`ReadBodyAsync<T>` snake_case DTO (`Core/HttpContext/HttpListenerRequestExtensions.cs`),
`ApiResult<T>` envelope (`Core/HttpContext/ResponceBuilder.cs`),
`MapHelper.GetMapByID`. Queued → main-thread execution by the existing
`RIMAPI_GameComponent.ProcessServerQueues()` pump — service code may call Verse
APIs directly with no extra marshaling.

### 3.1 `POST /api/v1/builder/blueprint/validate` — dry-run

Request DTO (`BlueprintValidateRequestDto`):

| field | type | notes |
|---|---|---|
| `map_id` | int | required |
| `def_name` | string | `ThingDef.defName` or `TerrainDef.defName` |
| `stuff_def_name` | string? | null for stuffless defs and terrain |
| `cell` | `{x:int, z:int}` | absolute map cell |
| `rotation` | int | 0–3, `Rot4.AsInt` |

Implementation (`BuilderService.ValidateBlueprint`):

- Resolve `BuildableDef def` via `DefDatabase<ThingDef>.GetNamedSilentFail` and,
  on null, `DefDatabase<TerrainDef>.GetNamedSilentFail`. 404 reason if neither
  resolves.
- Resolve `ThingDef stuff` only if `stuff_def_name` is non-empty.
- Build `IntVec3 center = new IntVec3(cell.x, 0, cell.z)`; bounds-check via
  `center.InBounds(map)`.
- Call `AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(def, center,
  new Rot4(rotation), map, godMode:false, thingToIgnore:null, thing:null,
  stuffDef:stuff)`.
- Compute occupied cells via `GenAdj.OccupiedRect(center, new Rot4(rotation),
  def.Size)`.
- Compute cost via `def.CostListAdjusted(stuff)` (each entry → `{def_name,
  count}`).
- Check existing things at `center` for already-blueprinted / already-built
  same-def matches (see §3.2 idempotency rules).

Response data (`BlueprintValidateResultDto`):

```jsonc
{
  "can_place": true,
  "reason": "",                       // AcceptanceReport.Reason when !can_place
  "occupies_cells": [{"x":12,"z":34}, ...],
  "cost": [{"def_name":"Steel","count":35}, ...],
  "already_blueprinted": false,
  "already_built": false
}
```

**No mutation under any code path.**

### 3.2 `POST /api/v1/builder/blueprint/place` — single safe placement

Request DTO (`BlueprintPlaceRequestDto`): same fields as §3.1. Critically a
**dedicated DTO**, not the shared `PasteAreaRequestDto` (whose `ClearObstacles`
default is destructive — see §4).

Implementation (`BuilderService.PlaceBlueprint`):

1. Resolve def/stuff/center exactly as §3.1.
2. Re-run the validity check (do not trust the caller). On `!report.Accepted`
   return `ApiResult<BlueprintPlaceResultDto>` with
   `status:"rejected"`, `placed:false`, `reason:report.Reason`, and a populated
   `validate` block — surface code maps non-`Ok` to `400 BadRequest`.
3. Idempotency check at `center`:
   - Pending `Blueprint_Build` of same `def` + `stuff` + `rotation` →
     `status:"already_present"`, `placed:false`, `thing_id:<existing>`.
   - In-progress `Frame` of same `def` + `stuff` → ditto.
   - Completed building of same `def` → ditto.
   - Otherwise: `Thing blueprint = GenConstruct.PlaceBlueprintForBuild(def,
     center, map, new Rot4(rotation), Faction.OfPlayer, stuff);`
     → `status:"placed"`, `placed:true`, `thing_id:blueprint.thingIDNumber`.
4. **Never** call `Destroy` on cell contents. There is no obstacle-clearing
   option.

Response data (`BlueprintPlaceResultDto`):

```jsonc
{
  "status": "placed",                  // "placed" | "already_present" | "rejected"
  "placed": true,
  "thing_id": 12345,                   // present when placed or already_present
  "reason": "",                        // present when rejected
  "validate": { /* §3.1 result */ }
}
```

### 3.3 `GET /api/v1/map/blueprints?map_id=…` — pending build read

Returns active `Blueprint_Build` + `Frame` things on the map. Enables
idempotency checks above, downstream readback, and an honest
"what is queued" signal for callers.

Implementation: iterate
`map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)` and
`ThingRequestGroup.BuildingFrame`. For each:

- `Blueprint_Build`: `def_name = def.entityDefToBuild.defName`,
  `stuff_def_name = EntityToBuildStuff()?.defName`, `kind:"blueprint"`.
- `Frame`: `def_name = def.entityDefToBuild.defName`,
  `stuff_def_name = Stuff?.defName`, `kind:"frame"`,
  `work_left = frame.WorkLeft`, `hp = frame.HitPoints`.

Response data: list of `PendingBuildDto`:

```jsonc
{
  "id": 67890,
  "kind": "blueprint",                 // "blueprint" | "frame"
  "def_name": "Cooler",
  "stuff_def_name": "Steel",
  "cell": {"x":12,"z":34},
  "rotation": 0,
  "work_left": 1200.0,                 // null for blueprints
  "hp": 100                            // null for blueprints
}
```

Place this in an appropriate reader controller (likely a new
`BuildController` under `BaseControllers/`; defer the
extend-vs-new-controller call to the implementer based on where map reads
currently cluster — see §9).

## 4. Existing code: refactor notes (suggested, not required)

While writing this plan I read the surrounding code; flagging tech debt per
the CLAUDE.md "suggest refactoring" rule. None of these are blockers for the
new endpoints — keep them as separate cleanup if desired.

- **DTO sharing footgun.** `BuilderController.PlaceBlueprints` reuses
  `PasteAreaRequestDto` (`Models/BuilderDtos.cs:14-20`), whose
  `ClearObstacles=true` default destroys buildings/items/plants in
  `BuilderService.PasteArea` (`Services/BuilderService.cs:90-167`).
  `PlaceBlueprints` ignores the field, but the shared shape on a "safe" path
  is dangerous. Recommend splitting into per-endpoint DTOs.
- **`PlaceBlueprints` silent skips.** It computes `count` and never returns it;
  it skips out-of-bounds cells and unresolved defs without reporting which.
  Comment claims `GenConstruct` checks placement validity; it does not (it
  silently no-ops on invalid cells). Recommend either (a) deprecating it
  long-term in favor of repeated calls to the new §3.2 endpoint, or
  (b) reshaping its response to `{placed, skipped, rejected, per_item:[...]}`.
- **Bulk `BlueprintDto` couples floors + buildings.** §3.2 is intentionally
  per-asset; the existing bulk shape stays unchanged for `paste`/`copy`/legacy
  `blueprint` callers.

## 5. Files touched in the fork

| Area | File(s) |
|---|---|
| Controller | `BaseControllers/BuilderController.cs` (validate + place), new `BaseControllers/BuildController.cs` or extend existing reads controller (pending list) |
| Service | `Services/BuilderService.cs` + `Services/Interfaces/IBuilderService.cs` (validate + place); new/extended reads service for §3.3 |
| DTOs | `Models/BuilderDtos.cs` — add `BlueprintValidateRequestDto`, `BlueprintValidateResultDto`, `BlueprintPlaceRequestDto`, `BlueprintPlaceResultDto`, `PendingBuildDto` |
| Logging | reuse `LogApi.Error` on exception paths |

No DI wiring changes — `AutoRouteRegistry` discovers `*Controller` methods by
reflection at startup.

## 6. Threading

Already correct by the fork's queued-request model: HTTP requests enqueue on a
background thread; `RIMAPI_GameComponent.ProcessServerQueues()` drains the
queue on the main game thread each Unity frame, so service methods invoke
`GenConstruct` / `DefDatabase` / `Map` directly. No `LongEventHandler` needed
for these endpoints. Implication for callers: writes/reads land at frame
cadence, not synchronously with HTTP send.

## 7. Verification

- **Manual in-game (primary).** Load a colony, exercise via `curl` / Postman:
  - validate clear cell → `can_place:true`;
  - validate blocked cell → `can_place:false` with `reason`;
  - validate already-built cell → `already_built:true`;
  - place once → `status:"placed"`, blueprint visible in-game;
  - re-POST same → `status:"already_present"`, no duplicate;
  - GET pending list → new blueprint present; construct it; GET → it leaves
    pending list and reappears as a frame, then disappears when built.
- **Both RimWorld refs.** Repeat on 1.5 and 1.6 builds (see §8). `GenConstruct.
  CanPlaceBlueprintAt` and `PlaceBlueprintForBuild` signatures are stable across
  these versions per the Krafs ref releases — confirm at implementation time.

## 8. Build / deploy

`Source/RIMAPI/RimApi.csproj` is .NET Framework 4.7.2 with conditional
`RIMWORLD_1_5` / `RIMWORLD_1_6` defines and per-version
`Krafs.Rimworld.Ref` (1.5.4104 / 1.6.4518). Release builds drop into
`1.5/Assemblies/` and `1.6/Assemblies/`. Build both configurations; RimWorld
must restart (or mod-reload) to pick up the new assembly. RimBob has no
compile-time dependency.

## 9. Open questions

- [ ] `GET /map/blueprints` — new `BuildController` or extend an existing reads
      controller? Pick at implementation time based on where map reads cluster
      in the fork today.
- [ ] Confirm `Blueprint_Build`'s `entityDefToBuild` accessor name on both 1.5
      and 1.6 refs (Krafs occasionally renames). Verify with a quick decompile
      before coding.
- [ ] Should the validate response include the **work amount** for the build
      (cost in work-ticks)? Useful for downstream feasibility scoring; trivial
      to add (`def.GetStatValueAbstract(StatDefOf.WorkToBuild, stuff)`).
