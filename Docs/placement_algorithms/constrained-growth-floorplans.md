# Constrained Growth Floor Plans

Source: https://publications.tno.nl/publication/104066/Yar9HQ/lopes-2010-constrained.pdf
Retrieved: 2026-05-22
Authors: Ricardo Lopes, Tim Tutenel, Ruben M. Smelik, Klaas Jan de Kraker, and Rafael Bidarra
Published: 2010
Source license: Source PDF does not grant a clear reuse license; treat as citation-only.
Note: Original RimBob summary. This is not a verbatim copy of the source.

## RimBob Use

Constrained growth is the best reference in this set for multi-room base layout.
It starts from user-defined topology and area constraints, places seeds on a
grid, then grows rooms while preserving adjacency and connectivity.

For RimBob, this belongs after the simple candidate solver. It is useful when a
request is no longer "place one freezer" but "propose a kitchen/freezer/dining
cluster" or "expand the base with bedrooms and a hospital wing."

## Placement Heuristics

- Represent the room program as zones and rooms: public/work, food, storage,
  medical, prison, bedrooms, power, and defense support.
- Define constraints as adjacency, separation, area ratio, required exits,
  connectivity, and forbidden-neighbor rules.
- Seed rooms near their strongest anchors: freezer near fields/kitchen, dining
  near traffic, batteries near conduit but away from fire risk.
- Grow rooms stepwise into free cells, scoring each expansion against
  constraints and keeping multiple alternatives.

## Candidate Generation Pattern

```pseudo
program = {
    rooms: [freezer, kitchen, dining, stockpile],
    adjacency: [(freezer, kitchen), (kitchen, dining)],
    separation: [(butcher_table, stove)],
    area_targets: { freezer: 30, kitchen: 16, dining: 24 }
}

seeds = choose_seed_cells(program, distance_fields, empty_regions)
layouts = initialize_layouts(seeds)

while not all_rooms_reach_target_area(layouts):
    next_layouts = []

    for layout in layouts:
        for room in expandable_rooms(layout):
            for cell in frontier_cells(room):
                expanded = grow(layout, room, cell)
                if constraints_still_possible(expanded, program):
                    next_layouts.add(expanded)

    layouts = keep_top_k(next_layouts, layout_score)

for layout in layouts:
    if all_rooms_reachable(layout):
        emit_layout_candidate(layout)
```

## Fit / Limits

This is more complex than RimBob needs for the first Construction slice. Do not
make it the initial implementation. Use it as the upgrade path after the single
placement solver can explain candidates, reject bad terrain, and validate with
RIMAPI.

## Dashboard Evidence

- Show room-program constraints as structured chips.
- Show seed cells, growth frontier, and final room cells.
- Explain constraint pressure: `needs_adjacency`, `too_small`, `connection_missing`, `separation_violation`.
