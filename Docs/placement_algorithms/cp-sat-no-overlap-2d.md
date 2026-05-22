# CP-SAT NoOverlap2D

Source: https://or-tools.github.io/docs/pdoc/ortools/sat/python/cp_model
Retrieved: 2026-05-22
Project: Google OR-Tools
Source license: OR-Tools documentation/source licensing should be rechecked before copying; treat as citation-only here.
Note: Original RimBob summary. This is not a verbatim copy of the source.

## RimBob Use

CP-SAT `NoOverlap2D` is a reference for exact rectangle placement after RimBob
has already narrowed the problem. It models axis-aligned rectangles with X and Y
intervals and ensures selected rectangles do not overlap.

This should be a local exact solver for small candidate sets, not the default
whole-map placement engine.

## Placement Heuristics

- Use for small room-program choices: "fit freezer, kitchen, dining, and
  stockpile inside this expansion band."
- Encode rectangles with optional presence variables so the solver can choose
  among alternatives.
- Add adjacency/separation constraints as extra booleans or penalties.
- Keep terrain and RimWorld-specific validity outside the generic solver; feed
  it only prefiltered legal zones.

## Candidate Generation Pattern

```pseudo
model = CpSatModel()

for room in requested_rooms:
    x_interval[room] = interval_var(room.x, room.width)
    z_interval[room] = interval_var(room.z, room.height)

model.add_no_overlap_2d(x_interval.values, z_interval.values)

for room in requested_rooms:
    model.add(room.x >= search_bounds.min_x)
    model.add(room.z >= search_bounds.min_z)
    model.add(room.x + room.width <= search_bounds.max_x)
    model.add(room.z + room.height <= search_bounds.max_z)

model.minimize(weighted_distance_and_adjacency_costs)
solutions = solve_with_short_time_limit(model)
```

## Fit / Limits

CP-SAT can become expensive or awkward when the grid has many irregular
obstacles, non-rectangular shapes, or modded placement rules. Use it after
summed-area tables, distance fields, and empty rectangles have shrunk the search
space to a handful of plausible zones.

## Dashboard Evidence

- Show solver inputs: selected rooms, bounds, fixed obstacles, and objective weights.
- Show solve status and time limit.
- Show whether a rejected exact layout failed geometry, terrain, pathing, or RIMAPI validation.
