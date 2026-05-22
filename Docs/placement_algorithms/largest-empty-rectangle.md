# Largest Empty Rectangle

Source: https://digitalcommons.dartmouth.edu/facoa/2070/
Retrieved: 2026-05-22
Authors: B. Chazelle, R. L. Drysdale, and D. T. Lee
Published: 1986
Source license: Dartmouth page does not grant a reuse license; treat as citation-only.
Note: Original RimBob summary. This is not a verbatim copy of the source.

## RimBob Use

Largest-empty-rectangle algorithms are useful for finding meaningful room-sized
candidate regions instead of trying every width, height, and anchor. For
RimWorld, the immediate use is generating rectangular candidates for bedrooms,
freezers, stockpiles, workshops, hospitals, and battery rooms.

## Placement Heuristics

- Convert blocked tiles into obstacle points or blocked cells.
- Generate large empty rectangles inside a requested search zone.
- Filter rectangles by room-specific minimum dimensions, terrain affordance,
roofability, and adjacency needs.
- Rank rectangles by soft score before exact validation.

## Candidate Generation Pattern

```pseudo
search_zone = bounds_near(request.anchor_entities)
empty_rectangles = find_large_empty_rectangles(occupied_grid, search_zone)

for rectangle in empty_rectangles:
    if rectangle.area < request.min_area:
        continue

    if not supports_required_affordance(rectangle, request.buildables):
        continue

    candidate = room_candidate(rectangle, request.room_kind)
    score = adjacency_score(candidate) + expansion_score(candidate)
    keep_top_k(candidate, score)
```

## Fit / Limits

The original problem focuses on axis-aligned rectangles. That maps well to many
RimWorld rooms, but not every useful base shape is rectangular. Use this as a
generator for good first candidates, then let templates or constrained growth
handle irregular layouts.

## Dashboard Evidence

- Show the empty rectangles considered and the selected rectangle.
- Include the blocking tiles that caused a rectangle to be rejected or shrunk.
- Report whether the region came from free-space generation or manual anchor expansion.
