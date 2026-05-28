# Complete RIMAPI Blueprint Lifecycle Slice

> Agent-created plan. Fork-side work lands in the local RIMAPI fork
> (`C:\dev\RIMAPI-for-RimBob`, repo `ocaspi-sc/RIMAPI-for-RimBob`), not in the
> RimBob Host. RimBob integrates over HTTP only.

## Summary

Expand the RIMAPI fork blueprint support to cover the full pending-build
lifecycle:

- validate one caller-supplied blueprint placement
- place one validated blueprint
- read pending blueprints and frames
- allow/disallow pending blueprints and frames
- cancel/delete pending blueprints and frames by explicit id
- summarize pending blueprint/frame backlog for Willie

No RimBob Host, minister, dashboard, apply-handle, or cell-picker wiring in this
slice. No room, stockpile, power-net, building-detail, or buildability-layer
endpoints in this slice; those are separate Willie follow-ups in `Tasks.md`.

No compatibility code; this adds new fork-only endpoints and no persisted state.
If a later wire/persistence format changes, wipe-and-regen on upgrade.

## Endpoint Contract

### `POST /api/v1/builder/blueprint/validate`

Dry-run one placement. No mutation.

Request:

```json
{
  "map_id": 0,
  "def_name": "Wall",
  "stuff_def_name": "WoodLog",
  "cell": { "x": 80, "z": 80 },
  "rotation": 0
}
```

Response data:

```json
{
  "can_place": true,
  "reason": "",
  "def_type": "thing",
  "occupies_cells": [{ "x": 80, "z": 80 }],
  "cost": [{ "def_name": "WoodLog", "count": 5 }],
  "work_to_build": 135,
  "already_blueprinted": false,
  "already_built": false
}
```

Implementation notes:

- Resolve `def_name` as `ThingDef` first, then `TerrainDef`.
- Resolve `stuff_def_name` only for thing blueprints.
- Reject missing map, missing def, invalid stuff, invalid rotation, and
  out-of-bounds cell.
- Use `GenConstruct.CanPlaceBlueprintAt(...)`.
- Compute occupied cells with `GenAdj.OccupiedRect(...)`.
- Compute cost with `BuildableDef.CostListAdjusted(stuff)`.
- Compute work with `GetStatValueAbstract(StatDefOf.WorkToBuild, stuff)`.

### `POST /api/v1/builder/blueprint/place`

Place one safe blueprint after server-side validation.

Request adds optional `allowed`:

```json
{
  "map_id": 0,
  "def_name": "Wall",
  "stuff_def_name": "WoodLog",
  "cell": { "x": 80, "z": 80 },
  "rotation": 0,
  "allowed": false
}
```

Response data:

```json
{
  "status": "placed",
  "placed": true,
  "thing_id": 12345,
  "reason": "",
  "validate": { "can_place": true }
}
```

Rules:

- Re-run validation. Do not trust caller pre-validation.
- Return `status:"rejected"` when invalid.
- Return `status:"already_present"` when the same pending blueprint/frame or
  completed building already exists.
- If `allowed:false`, call `SetForbidden(true, false)` on the newly placed
  blueprint.
- Never destroy obstacles or expose a clear-obstacles option.

Pseudo-code:

```csharp
target = ResolveTarget(request);
validation = BuildValidationResult(target);
existing = FindExistingBuild(target);

if (existing.IsPending || existing.IsBuilt)
    return AlreadyPresent(existing, validation);

if (!validation.CanPlace)
    return Rejected(validation);

Thing placed = GenConstruct.PlaceBlueprintForBuild(...);
if (request.Allowed == false)
    placed.SetForbidden(true, false);

return Placed(placed, validation);
```

### `GET /api/v1/map/blueprints?map_id=...`

Read all pending build work on the requested map.

Rows include:

- `id`
- `kind:"blueprint"|"frame"`
- `def_name`
- `def_type`
- `stuff_def_name`
- `cell`
- `rotation`
- `allowed`
- `is_forbidden`
- `work_left`
- `hp`
- `cost[]`

Rules:

- Include `Blueprint_Build` and `Frame`.
- Exclude completed buildings.
- For blueprints, `work_left` is the full build work; for frames, it is
  `Frame.WorkLeft`.

### `POST /api/v1/builder/blueprint/allowed-state`

Pause/resume pending blueprints and frames by explicit ids.

Request:

```json
{
  "map_id": 0,
  "thing_ids": ["12345"],
  "allowed": false
}
```

Rules:

- Only `Blueprint_Build` and `Frame`.
- `allowed:false` -> `SetForbidden(true, false)`.
- `allowed:true` -> `SetForbidden(false, false)`.
- Reject completed buildings, items, pawns, wrong-map ids, malformed ids,
  missing ids, and empty/oversized batches.
- No rect-wide mode.

Response data includes `requested`, `matched`, `changed`, `already_in_state`,
`cancelled`, `already_gone`, `missing`, `non_map_targets`, and
`non_pending_build_targets`.

### `POST /api/v1/builder/blueprint/cancel`

Cancel pending blueprints and frames by explicit ids.

Request:

```json
{
  "map_id": 0,
  "thing_ids": ["12345"]
}
```

Rules:

- Only `Blueprint_Build` and `Frame`.
- Use RimWorld-native pending-build cancel behavior.
- Reject completed buildings, items, pawns, wrong-map ids, malformed ids,
  missing ids, and empty/oversized batches.
- No rect-wide cancel.
- No "cancel all".

Response data includes `requested`, `matched`, `cancelled`, `already_gone`,
`missing`, `non_map_targets`, and `non_pending_build_targets`.

### `GET /api/v1/map/construction/backlog?map_id=...`

Blueprint/frame backlog summary for Willie.

Group key:

- `kind`
- `def_name`
- `stuff_def_name`
- `allowed`

Each group includes:

- `count`
- `thing_ids[]`
- `sample_cells[]`
- `total_work_left`
- `cost[]`
- `materials_available[]`
- `materials_missing[]`
- `blocked_count`
- `disallowed_count`

Material availability counts non-forbidden haulable items on the same map. This
is a construction backlog signal, not a full stockpile/storage-flow model.

## Files In The Fork

| Area | File |
|---|---|
| Builder routes | `Source/RIMAPI/RimworldRestApi/BaseControllers/BuilderController.cs` |
| Map read routes | `Source/RIMAPI/RimworldRestApi/BaseControllers/MapController.cs` |
| DTOs | `Source/RIMAPI/RimworldRestApi/Models/BuilderDtos.cs` |
| Builder service | `Source/RIMAPI/RimworldRestApi/Services/BuilderService.cs` |
| Map service glue | `Source/RIMAPI/RimworldRestApi/Services/MapService.cs` |
| Interfaces | `Source/RIMAPI/RimworldRestApi/Services/Interfaces/IBuilderService.cs`, `Source/RIMAPI/RimworldRestApi/Services/Interfaces/IMapService.cs` |
| API macro docs | `Docs/_api_macroses/controllers/BuilderController.yml`, `Docs/_api_macroses/controllers/MapController.yml` |

## Verification

Build both fork configs:

```powershell
dotnet build C:\dev\RIMAPI-for-RimBob\Source\RIMAPI\RimApi.csproj -c Release-1.5
dotnet build C:\dev\RIMAPI-for-RimBob\Source\RIMAPI\RimApi.csproj -c Release-1.6
```

Live RimWorld checks after installing/restarting the fork build:

- `/api/v1/dev/endpoints` lists all new blueprint endpoints.
- Validate a clear cell and a blocked cell.
- Place one allowed blueprint.
- Place one disallowed blueprint.
- Read pending list and verify `allowed` / `is_forbidden`.
- Flip allowed-state false/true and verify in-game.
- Cancel an explicit blueprint id and verify it disappears.
- Cancel an explicit frame id and verify it disappears.
- Attempt cancel/allowed-state on a completed building and verify rejection.
- Verify backlog groups pending blueprints/frames with cost and material gap info.

## Follow-Up Boundaries

Keep these outside this slice:

- `rimapi-building-detail-read`
- `rimapi-stockpile-detail-read`
- `rimapi-room-detail-read`
- `rimapi-power-net-read`
- `rimapi-buildability-layers-read`

Those endpoints are general Willie evidence, not pending
blueprint/frame lifecycle.

---

## Summary (landed 2026-05-27)

**Motivation.** Willie needs a complete fork-side blueprint
lifecycle so the minister can validate, place, list, allow/disallow, cancel,
and summarize pending pending-build work without ever touching the RimBob
Host. This slice closes the last contract gap in that lifecycle.

**Context.** Most of the six lifecycle endpoints already shipped in fork
master commit `1a3126e [rimapi][construction] Add blueprint lifecycle
endpoints` (validate, place, allowed-state, cancel, `/api/v1/map/blueprints`,
`/api/v1/map/construction/backlog`). What was missing was the plan's
"invalid stuff" rejection in `validate`: the prior code only rejected
unknown stuff names, not stuff supplied for non-stuff buildables or stuff
missing for must-stuff buildables.

**Scope.** Tightened one-blueprint validation in
`Source/RIMAPI/RimworldRestApi/Services/BuilderService.cs` so thing
blueprints reject:

- stuff supplied to a non-stuff buildable
- stuff missing for a must-stuff buildable
- a stuff name outside the buildable's allowed stuffs

before placement is attempted. No changes to the other five endpoints; the
existing implementations already satisfied the plan's contract.

**How to verify (human).**

- Dashboard: none in this slice — fork-only change, no Host wiring.
- Live RimWorld checks against the rebuilt fork (still pending; tracked as
  the `rimapi-blueprint-live-verify` capture in `Tasks.md`):
  - `/api/v1/dev/endpoints` lists all blueprint lifecycle endpoints.
  - Validate a clear cell and a blocked cell.
  - Validate a stuff-required buildable (e.g. Wall) with and without
    `stuff_def_name` and confirm the missing case is rejected with a clear
    reason.
  - Validate a non-stuff buildable with a `stuff_def_name` set and confirm
    rejection.
  - Place allowed and disallowed blueprints; flip allowed-state; cancel
    blueprint and frame by id; pull the backlog summary and confirm
    grouping/material gaps.
- Commands the Codex worktree ran cleanly:
  - `dotnet build .\Source\RIMAPI\RimApi.csproj -c Release-1.5`
  - `dotnet build .\Source\RIMAPI\RimApi.csproj -c Release-1.6`
- Files to glance at:
  - `Source/RIMAPI/RimworldRestApi/Services/BuilderService.cs` (new
    `ValidateThingStuff` and its call site).

**Codex run:** `20260526-235714-rimapi-blueprint-placement-endpoint` ·
branch `codex/prompt-20260526-235714-rimapi-blueprint-placement-endpoint` ·
landed commit `3eb1d84` on `ocaspi-sc/RIMAPI-for-RimBob` master (not pushed
to origin; fork master is now ahead 5)
