# Summed-Area Tables

Source: https://cir.nii.ac.jp/crid/1360855570734236800
Retrieved: 2026-05-22
Authors: Franklin C. Crow
Published: 1984
Source license: ACM rights policy referenced by CiNii; treat as citation-only.
Note: Original RimBob summary. This is not a verbatim copy of the source.

## RimBob Use

Summed-area tables, also known as integral images, are the first primitive to
implement for rectangular Construction placement. They turn repeated rectangle
queries into constant-time table lookups after one linear precompute pass.

For RimBob, this should back cheap hard gates before any pathfinding, material
logic, or RIMAPI write attempt.

## Placement Heuristics

- Build one table per binary or weighted map layer: occupied, forbidden, roofed, unroofed, filth, fire risk, danger, snow, fertility, and path cost.
- Use rectangle sums for room footprints, freezer footprints, wind-turbine exclusion areas, stockpile regions, and construction staging areas.
- Use separate tables per terrain-affordance class when a building needs Heavy, Medium, Light, Bridgeable, Waterproof, or ShallowWater support.
- Use weighted tables for soft penalties, not just yes/no checks: danger, walking distance cost, dirt, beauty penalty, and expansion conflict.

## Candidate Generation Pattern

```pseudo
function rectangle_sum(table, min_x, min_z, max_x, max_z):
    return table[max_x, max_z]
         - table[min_x - 1, max_z]
         - table[max_x, min_z - 1]
         + table[min_x - 1, min_z - 1]

for anchor in candidate_anchors:
    bounds = template_bounds(template, anchor, rotation)

    if rectangle_sum(occupied_sat, bounds) > 0:
        reject("occupied")

    if rectangle_sum(unroofed_sat, bounds) > allowed_unroofed_tiles:
        reject("roof_gap")

    danger_score = rectangle_sum(danger_sat, bounds)
    keep_top_k(anchor, danger_score)
```

## Fit / Limits

Summed-area tables are strongest for rectangular regions. For irregular
blueprints, use them as a fast bounding-box filter, then run exact cell checks
only on survivors. They do not replace RIMAPI placement validation because
RimWorld has def-specific and mod-specific placement rules.

## Dashboard Evidence

- Show per-candidate fast rejection counts by layer.
- Show rectangle overlays for hard gates and soft penalties.
- Include timing for precompute and query counts so bad brute-force loops are visible.
