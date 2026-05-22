# Jump Point Search

Source: https://ojs.aaai.org/index.php/AAAI/article/view/7994
Retrieved: 2026-05-22
Authors: Daniel Harabor and Alban Grastien
Published: 2011
Source license: AAAI page does not grant a reuse license; treat as citation-only.
Note: Original RimBob summary. This is not a verbatim copy of the source.

## RimBob Use

Jump Point Search is a grid-pathfinding acceleration reference. It is useful
after candidate generation, when RimBob needs to answer reachability questions:
can pawns build it, can a freezer be serviced, will a wall sequence seal off a
room, and is there still a safe route through the base?

## Placement Heuristics

- Use normal flood-fill for small local connected-component checks.
- Use accelerated grid pathfinding for repeated route checks across the map.
- Path over buildable/reachable cells, not merely empty cells.
- Run after cheap placement filters. Pathfinding should not be inside the broad
  candidate enumeration loop.

## Candidate Generation Pattern

```pseudo
for candidate in top_k_after_fast_gates:
    build_cells = required_builder_stand_cells(candidate)

    if not any_reachable(colony_area, build_cells):
        reject("unreachable_build_cells")

    simulated_map = add_impassable_finished_buildings(candidate)

    if not reachable(colony_core, food_storage, simulated_map):
        reject("blocks_food_route")

    if seals_pawn_or_blueprint(simulated_map):
        reject("build_order_trap")

    emit(candidate)
```

## Fit / Limits

JPS assumes grid structure and works best on relatively uniform movement costs.
RimWorld pathing has doors, danger, region logic, reservations, pawn capability,
and modded behavior. Use it as a fast approximation or internal validator, then
surface uncertainty when live RIMAPI/RimWorld data is missing.

## Dashboard Evidence

- Show which route was validated: builder access, service access, evacuation,
  hostile path, or food path.
- Show failed start/end cells and the blocker set.
- Include route-check timing separately from candidate generation timing.
