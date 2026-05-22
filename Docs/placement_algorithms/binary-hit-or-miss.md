# Binary Hit-Or-Miss Template Matching

Source: https://docs.scipy.org/doc/scipy/reference/generated/scipy.ndimage.binary_hit_or_miss.html
Retrieved: 2026-05-22
Project: SciPy
Source license: SciPy documentation/source licensing should be rechecked before copying; treat as citation-only here.
Note: Original RimBob summary. This is not a verbatim copy of the source.

## RimBob Use

Binary hit-or-miss matching is useful when Construction needs to find exact
local tile patterns, not broad rectangular space. It is a reference for matching
foreground and background requirements on a grid.

RimBob does not need SciPy in production. The useful concept is the pattern
contract: required occupied/solid cells, required empty cells, and don't-care
cells.

## Placement Heuristics

- Match cooler slots: solid wall cell, clear cold-side cell, clear hot-side exhaust cell, reachable build cell.
- Match door candidates: wall segment with empty cells on both sides and a useful room-to-room transition.
- Match wind-turbine keep-clear masks: turbine footprint plus airflow cells that must avoid tall blockers.
- Match defense lint patterns: enemy cover adjacent to approach lanes, trap corridors without service access, or straight hostile lanes.

## Candidate Generation Pattern

```pseudo
pattern = {
    must_be_wall: [(0, 0)],
    must_be_empty: [(0, -1), (0, 1)],
    must_be_reachable: [(1, 0)],
    dont_care: remaining_offsets
}

for cell in wall_cells:
    for rotation in rotations:
        if pattern_matches(map_layers, pattern, cell, rotation):
            emit_candidate("cooler_slot", cell, rotation)
```

## Fit / Limits

Template matching is strong for small fixed local patterns. It should not be
used as the main room layout engine. Large rooms need empty-rectangle or growth
methods; exact game acceptance still needs RIMAPI validation.

## Dashboard Evidence

- Show matched cells and failed pattern offsets.
- Label failures as `missing_wall`, `blocked_hot_side`, `blocked_cold_side`, or `unreachable_build_cell`.
- Add a pattern id to candidate diagnostics so repeated false positives can be fixed.
