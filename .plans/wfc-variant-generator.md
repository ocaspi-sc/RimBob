# WFC Variant Generator For Construction

## Goal

Evaluate Wave Function Collapse as a bounded variant generator for Construction
placement proposals, not as the primary placement solver.

## Current Decision

The first Construction solver should stay deterministic and explainable:

1. Find anchors such as kitchen, freezer, stockpiles, fields, power, or walls.
2. Generate simple templates and fixed room/utility shapes.
3. Apply hard validation for terrain, rooms, power, occupancy, materials, and
   build-order reachability.
4. Score candidates by path distance, adjacency, thermal risk, defense exposure,
   expansion space, and material readiness.

WFC may be useful after those boundaries are known, where it can produce
alternate internal layouts inside a constrained rectangle.

## Candidate Uses

- Freezer/kitchen/dining micro-layout variants.
- Airlock, cooler, shelf, and walking-lane alternatives.
- Barracks, hospital, and workshop internal layouts.
- Defensive wall/corridor shape alternatives after Defense constraints exist.
- Dashboard compare-mode drafts, where the player can inspect multiple variants.

## Non-Goals

- Do not use WFC as the core solver spine.
- Do not let WFC bypass hard live-state validation.
- Do not emit unexplainable coordinates without source-backed reasons.
- Do not use WFC for autonomous blueprint placement.

## Implementation Slice

1. Define a small grammar for one bounded use case, likely freezer support:
   `wall`, `door`, `cooler`, `shelf`, `walkway`, `empty`, and fixed anchors.
2. Seed WFC with deterministic inputs and store the seed in the proposal trace.
3. Generate a small number of variants, then run the normal validator/scorer.
4. Render rejected and accepted variants in the Construction dashboard proposal
   view with reason chips.
5. Compare WFC variants against hand-authored templates in fixtures.

## Validation

- Same request and seed produces identical variants.
- Invalid WFC outputs are rejected by the existing placement validator.
- Top candidates explain their score components.
- Dashboard can show why WFC was used and why variants were accepted/rejected.

## Open Questions

- Which first grammar is worth the cost: freezer, hospital, barracks, or power?
- Should WFC live only in dashboard experimentation until Construction is live?
- What fixture corpus proves WFC beats simple templates often enough to keep it?
