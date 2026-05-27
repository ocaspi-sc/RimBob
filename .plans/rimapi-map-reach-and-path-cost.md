# RIMAPI Map Reachability + Path Cost Endpoints

> Agent-created plan. Fork-side work lands in the local RIMAPI fork
> (`C:\dev\RIMAPI-for-RimBob`, repo `ocaspi-sc/RIMAPI-for-RimBob`), not in the
> RimBob Host. RimBob integrates over HTTP only.
>
> Motivation: the Willie Placement Solver needs walkable distance + reachability
> to score `near:<class>` requests against candidate cells. Today RIMAPI exposes
> only the worldmap caravan path and a rooms dump; no in-map reachability or
> pathfinding. Solver falls back to euclidean-from-centroid, which ranks through
> walls and into sealed rooms. See
> [`willie-briefing-schema.md`](willie-briefing-schema.md) S3.

## Summary

Add three fork-side endpoints for in-map spatial scoring:

- single-cell reachability check (`CanReach` wrapper)
- single-pair path cost (region-BFS coarse, optional A* exact)
- batch path cost for the solver's K-candidate × M-anchor ranking step

No RimBob Host wiring in this slice. No solver, minister, dashboard, or
state-store changes. RimBob integrates over HTTP only.

No persisted state; no compatibility code. If a later wire format changes,
wipe-and-regen on upgrade.

## Endpoint Contract

### `GET /api/v1/map/reach`

Single cell-to-cell reachability check. Wraps `Map.reachability.CanReach`.

Query:

```
map_id=0
from_x=80&from_z=80
to_x=120&to_z=95
mode=pass_doors          # optional, default pass_doors
peMode=on_cell           # optional, default on_cell
```

Response data:

```json
{
  "can_reach": true,
  "from": { "x": 80, "z": 80 },
  "to":   { "x": 120, "z": 95 },
  "mode": "pass_doors",
  "pe_mode": "on_cell"
}
```

Rules:

- Reject missing map, out-of-bounds cells, unknown `mode`, unknown `peMode`.
- `mode` values: `pass_doors` (default), `no_pass_closed_doors`,
  `pass_all_destroyable_things`. Map to `TraverseMode` enum.
- `peMode` values: `on_cell` (default), `touch`, `closest_touch`. Map to
  `PathEndMode` enum.
- Traverse parms built with `TraverseParms.For(TraverseMode.<mode>)`, no
  pawn-specific overrides.
- O(1) amortized; backed by region cache. Safe to call per-cycle.

### `POST /api/v1/map/path-cost`

Single-pair walkable path cost. Two tiers:

- `tier:"region"` — region BFS hop count. Cheap. Returns coarse rank.
- `tier:"astar"` — full `Map.pathFinder.FindPath`. Returns exact integer cost.

Request:

```json
{
  "map_id": 0,
  "from": { "x": 80, "z": 80 },
  "to":   { "x": 120, "z": 95 },
  "tier": "region",
  "mode": "pass_doors",
  "pe_mode": "on_cell",
  "max_cost": 100000
}
```

Response data:

```json
{
  "reachable": true,
  "cost": 47,
  "tier": "region",
  "from": { "x": 80, "z": 80 },
  "to":   { "x": 120, "z": 95 }
}
```

Rules:

- `tier:"region"`: count regions returned by
  `RegionTraverser.BreadthFirstTraverse(...)` until the target region is hit,
  capped at `max_cost` regions. Returns `-1` and `reachable:false` on no path.
- `tier:"astar"`: call `Map.pathFinder.FindPath(from, to, traverseParms,
  peMode)`. Return `path.TotalCost` as integer. Dispose `PawnPath`. Returns
  `-1` and `reachable:false` on `PawnPath.NotFound`.
- `max_cost` optional. For `region`, caps region hop count (default 1000).
  For `astar`, ignored — pathfinder runs to completion. Document the asymmetry.
- Reject missing map, out-of-bounds cells, unknown `tier`, unknown `mode`,
  unknown `peMode`, negative `max_cost`.

### `POST /api/v1/map/path-cost/batch`

Batch path cost for the solver's K-candidate × M-anchor ranking step.
Single request, one map.

Request:

```json
{
  "map_id": 0,
  "tier": "region",
  "mode": "pass_doors",
  "pe_mode": "on_cell",
  "max_cost": 1000,
  "pairs": [
    { "from": { "x": 80, "z": 80 }, "to": { "x": 120, "z": 95 } },
    { "from": { "x": 81, "z": 80 }, "to": { "x": 120, "z": 95 } }
  ]
}
```

Response data:

```json
{
  "results": [
    { "reachable": true,  "cost": 47, "from": {"x":80,"z":80}, "to": {"x":120,"z":95} },
    { "reachable": false, "cost": -1, "from": {"x":81,"z":80}, "to": {"x":120,"z":95} }
  ]
}
```

Rules:

- Cap `pairs.Length` at 4096. Reject larger batches with a clear reason.
- One shared `traverseParms` per batch (mode/peMode at the top level).
- Reuse one `RegionEntryPredicate` / pathfinder per request to amortize setup.
- For `tier:"astar"`, log a warning if `pairs.Length > 64` — A* is expensive.
  Solver should use `region` tier for bulk and `astar` only for top-K
  tiebreak.
- Reject missing map, unknown `tier`/`mode`/`peMode`, out-of-bounds cells.
  Reject the entire batch on any malformed pair; do not partially process.

## Files In The Fork

| Area | File |
|---|---|
| Map read routes | `Source/RIMAPI/RimworldRestApi/BaseControllers/MapController.cs` |
| Map service | `Source/RIMAPI/RimworldRestApi/Services/MapService.cs` |
| Map service interface | `Source/RIMAPI/RimworldRestApi/Services/Interfaces/IMapService.cs` |
| Map helper | `Source/RIMAPI/RimworldRestApi/Helpers/MapHelper.cs` |
| DTOs | `Source/RIMAPI/RimworldRestApi/Models/Map/MapDto.cs` (or new `MapPathDto.cs`) |
| API macro docs | `Docs/_api_macroses/controllers/MapController.yml` |

## Implementation Notes

- `TraverseMode` mapping table belongs in `MapHelper` as a small switch; share
  with any future endpoint that takes traverse parms.
- Same for `PathEndMode`.
- Region BFS:

  ```csharp
  int regionsTraversed = 0;
  Region startReg = map.regionGrid.GetValidRegionAt_NoRebuild(from);
  Region endReg   = map.regionGrid.GetValidRegionAt_NoRebuild(to);
  if (startReg == null || endReg == null) return Unreachable();
  if (startReg == endReg) return new Result(0, true);

  bool found = false;
  RegionTraverser.BreadthFirstTraverse(
      startReg,
      (from, to) => to.Allows(traverseParms, false),
      reg => {
          regionsTraversed++;
          if (reg == endReg) { found = true; return true; }
          return regionsTraversed >= maxCost;
      },
      maxRegions: maxCost);

  return found ? new Result(regionsTraversed, true) : Unreachable();
  ```

- A*:

  ```csharp
  using var path = map.pathFinder.FindPath(from, to, traverseParms, peMode);
  if (path == PawnPath.NotFound) return Unreachable();
  return new Result((int)path.TotalCost, true);
  ```

- `using` matters — `PawnPath` is pooled; not disposing leaks pool slots.

## Verification

Build both fork configs:

```powershell
dotnet build C:\dev\RIMAPI-for-RimBob\Source\RIMAPI\RimApi.csproj -c Release-1.5
dotnet build C:\dev\RIMAPI-for-RimBob\Source\RIMAPI\RimApi.csproj -c Release-1.6
```

Live RimWorld checks after installing/restarting the fork build:

- `/api/v1/dev/endpoints` lists `/api/v1/map/reach`, `/api/v1/map/path-cost`,
  `/api/v1/map/path-cost/batch`.
- `reach` returns `true` for two cells in the same open room.
- `reach` returns `false` for a cell sealed by walls.
- `reach` returns `false` when `mode=no_pass_closed_doors` and the only route
  is through a closed door.
- `path-cost` `tier:"region"` returns small cost between two adjacent rooms,
  larger cost across the base, `-1` for sealed.
- `path-cost` `tier:"astar"` returns a finite cost matching in-game pawn
  travel for the same path.
- `path-cost/batch` returns one result per pair, in input order.
- `path-cost/batch` with `pairs.Length > 4096` is rejected.
- `path-cost/batch` with one malformed pair rejects the whole batch (no
  partial results).
- Stress: 1000-pair `region`-tier batch returns in < 100 ms on a mid-game
  save. A* batch of 64 returns in < 500 ms. (Numbers indicative; record
  actuals in the landed Summary.)

## Follow-Up Boundaries

Out of scope:

- Per-pawn reachability (would need pawn id and pawn-specific traverse parms).
  Solver only needs default colonist traversal.
- Path geometry — return cost only, not the cell list. If solver later wants
  the path for visualization, add `/api/v1/map/path` as a separate slice.
- Region id exposure as a primitive (`GET /api/v1/map/region-at`). Useful but
  not required by Placement Solver; defer until a consumer asks.
- `outage-prone net IDs` from `willie-briefing-schema.md` — separate
  `rimapi-power-net-read` slice already in HumanTodo.
- RimBob Host wiring (DTOs, client method, solver consumption) — separate
  follow-up after these endpoints land.

## Consumers (post-land)

- Willie Placement Solver `near:<class>` scoring (primary).
- Willie Construction briefing for `stalled_builds` reachability checks
  ("frame X is unreachable by colonists, no wonder it's stalled").
- Future Defense minister for choke-point / corridor scoring.
