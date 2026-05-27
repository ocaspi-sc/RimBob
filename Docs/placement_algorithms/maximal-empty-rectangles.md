# Dynamic Maximal Empty Rectangles

Source: https://websrv.cecs.uci.edu/~papers/date08/PAPERS/2004/DATE04/PDFFILES/IP3_13.PDF
Retrieved: 2026-05-22
Authors: Manish Handa and Ranga Vemuri
Published: 2004
Source license: IEEE copyright notice in PDF; treat as citation-only.
Note: Original RimBob summary. This is not a verbatim copy of the source.

## RimBob Use

Dynamic maximal empty rectangle methods are relevant when placed objects change
over time and the solver needs to maintain reusable free-space candidates.
RimWorld maps are dynamic: walls, blueprints, stockpiles, chunks, trees, and
terrain prerequisites can change between ticks.

This reference is especially useful for caching free-space regions between
Willie evaluations.

## Placement Heuristics

- Maintain a list of maximal empty rectangles for a search zone.
- Update or invalidate only the regions affected by new buildings, removed
  blockers, mining, bridge prerequisites, or blueprint placement.
- Prefer maximal rectangles over disjoint partitions because overlapping
  candidate rectangles preserve more placement options.
- Use region age and state-version keys so stale cached empty space never drives apply.

## Candidate Generation Pattern

```pseudo
cache_key = (map_id, search_zone, occupancy_version, terrain_version)

if empty_rectangle_cache.has(cache_key):
    rectangles = empty_rectangle_cache.get(cache_key)
else:
    rectangles = compute_maximal_empty_rectangles(occupied_grid, search_zone)
    empty_rectangle_cache.put(cache_key, rectangles)

for rectangle in rectangles:
    for room_template in templates_that_fit(rectangle):
        candidate = align_template_inside(room_template, rectangle)
        keep_top_k(candidate, cheap_score(candidate))
```

## Fit / Limits

The paper's FPGA setting uses rectangular tasks on a grid. RimWorld has
non-rectangular blueprints, terrain prerequisites, pawns, reservations, fog, and
modded placement logic. Treat maximal rectangles as proposal regions, not as
final placement guarantees.

## Dashboard Evidence

- Show cache key, cache hit/miss, and invalidation reason.
- Show maximal rectangles separately from final proposed blueprint footprints.
- Flag candidates derived from stale map evidence as invalid until refreshed.
