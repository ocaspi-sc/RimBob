# Placement Algorithm References

Retrieved: 2026-05-22

Note: Original RimBob summaries of external algorithm references. These are not
verbatim copies of the source material.

## RimBob Use

These references support the future Willie placement solver. The practical
shape is a staged solver: cheap grid precomputation first, bounded candidate
generation second, expensive validation last. RimBob should keep this in
`Suggest` mode until RIMAPI can validate/place/read back blueprints.

```mermaid
flowchart LR
    Request["Build request"]
    Precompute["Precompute grid evidence"]
    Generate["Generate bounded candidates"]
    Prune["Cheap prune and score"]
    Validate["Live validation"]
    Emit["Ranked proposal"]

    Request --> Precompute
    Precompute --> Generate
    Generate --> Prune
    Prune --> Validate
    Validate --> Emit
```

## Recommended Implementation Order

1. [Summed-area tables](summed-area-tables.md) - O(1) rectangular checks for blocked tiles, roof, filth, danger, allowed terrain, and room footprint feasibility.
2. [Distance transforms](distance-transforms.md) - distance fields for adjacency scoring against kitchens, freezers, fields, conduits, walls, threats, and map edges.
3. [Template matching](binary-hit-or-miss.md) - fast matching for fixed shape patterns such as cooler wall slots, door-side rules, wind-turbine clearance masks, and defensive lint patterns.
4. [Largest empty rectangle](largest-empty-rectangle.md) and [dynamic maximal empty rectangles](maximal-empty-rectangles.md) - candidate space generation for rectangular rooms and expansion areas.
5. [Constrained growth floor plans](constrained-growth-floorplans.md) - multi-room layout generation when a single room placement becomes a base-program problem.
6. [CP-SAT NoOverlap2D](cp-sat-no-overlap-2d.md) - exact local solving for small candidate sets after pruning, not whole-map brute force.
7. [Jump Point Search](jump-point-search.md) - faster grid pathfinding for reachability/build-order checks after a candidate survives cheaper filters.

## Shared Solver Pipeline

```pseudo
precompute:
    occupancy_sat = summed_area_table(occupied_tiles)
    roof_sat = summed_area_table(unroofed_tiles)
    danger_sat = summed_area_table(danger_tiles)
    affordance_masks = terrain_masks_by_buildable_def()
    distance_fields = distance_transform(kitchen, freezer, fields, conduits, threats)

generate:
    anchors = top_k_cells(distance_fields, request.intent)
    templates = templates_for(request.kind)
    candidate_stream = anchors x templates x rotations

for candidate in candidate_stream:
    if rectangle_sum(occupancy_sat, candidate.bounds) > 0:
        reject("occupied")

    if not terrain_masks_allow(candidate.cells, candidate.defs):
        reject("terrain_affordance")

    cheap_score = distance_score(candidate) + adjacency_score(candidate)
    keep_top_k(candidate, cheap_score)

for candidate in top_k:
    validate_exact_cells(candidate)
    validate_reachability(candidate)
    validate_materials(candidate)
    validate_rimapi_blueprint(candidate)
    emit_candidate_with_reasons(candidate)
```

## Transfer Rules

- Generate from anchors, templates, and empty-space regions. Avoid scanning every cell with every possible room shape.
- Separate draft validity from execution readiness. Material shortage should block apply, not hide a good layout candidate.
- Keep RIMAPI as final truth for placement. Offline tables are proposal accelerators, not authority.
- Expose rejection reasons in the dashboard: `occupied`, `terrain_affordance`, `blocked_path`, `material_shortage`, `rimapi_rejected`.

## Source List

- [Summed-area tables for texture mapping](https://cir.nii.ac.jp/crid/1360855570734236800)
- [Distance Transforms of Sampled Functions](https://theoryofcomputing.org/articles/v008a019/)
- [Computing the Largest Empty Rectangle](https://digitalcommons.dartmouth.edu/facoa/2070/)
- [A Fast Algorithm for Finding Maximal Empty Rectangles for Dynamic FPGA Placement](https://websrv.cecs.uci.edu/~papers/date08/PAPERS/2004/DATE04/PDFFILES/IP3_13.PDF)
- [A Constrained Growth Method for Procedural Floor Plan Generation](https://publications.tno.nl/publication/104066/Yar9HQ/lopes-2010-constrained.pdf)
- [OR-Tools CP-SAT `add_no_overlap_2d`](https://or-tools.github.io/docs/pdoc/ortools/sat/python/cp_model)
- [Online Graph Pruning for Pathfinding On Grid Maps](https://ojs.aaai.org/index.php/AAAI/article/view/7994)
- [SciPy `binary_hit_or_miss`](https://docs.scipy.org/doc/scipy/reference/generated/scipy.ndimage.binary_hit_or_miss.html)
