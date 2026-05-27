# WFC Variant Generator For Willie

## Goal

Evaluate Wave Function Collapse as a bounded **candidate generator** for
Willie placement proposals, not as the primary placement solver.

## Current Decision

The Placement Solver owns the full decision pipeline:

1. Resolve anchors such as kitchen, freezer, stockpiles, fields, power, or walls.
2. Precompute shared map evidence.
3. Run several bounded candidate generators against the same `PlacementSpec`.
4. Apply shared hard validation for terrain, rooms, power, occupancy, materials,
   and build-order reachability.
5. Score candidates by one shared score vector: request fit, path distance,
   adjacency, thermal risk, defense exposure, expansion space, material readiness,
   build-order safety, generator confidence, and diversity.

WFC is one possible generator in that registry. It may be useful after hard
boundaries are known, where it can produce alternate internal layouts inside a
constrained rectangle. It does not get private validation, private scoring, or a
shortcut around RIMAPI validation.

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
- Do not let WFC compete by inventing a different score model; it emits drafts
  into the normal Placement Solver pipeline.

## Generator Contract

```pseudo
generate(spec, evidence, budget):
    if not supports(spec):
        return []

    rectangle = choose_bounded_region(spec, evidence)
    seed = deterministic_seed(spec.id, evidence.snapshot_id, rectangle)
    variants = run_wfc(grammar_for(spec), rectangle, seed, budget.max_drafts)

    return variants.map(variant => DraftCandidate(
        generator_id = "wfc",
        blueprint_group = variant.to_blueprint_group(),
        source_anchors = rectangle.anchors,
        assumptions = variant.constraints,
        reason = "alternate internal layout inside bounded candidate region",
        seed = seed))
```

The shared Placement Solver then dedupes, gates, scores, validates, and ranks the
WFC drafts beside template, rectangle, pattern-match, reuse-footprint, and future
local exact-solver drafts.

## Implementation Slice

1. Define a small grammar for one bounded use case, likely freezer support:
   `wall`, `door`, `cooler`, `shelf`, `walkway`, `empty`, and fixed anchors.
2. Seed WFC with deterministic inputs and store the seed in the proposal trace.
3. Generate a small number of drafts, then run the normal solver validator/scorer.
4. Render rejected and accepted WFC drafts in the Willie dashboard proposal
   view with reason chips and score components.
5. Compare WFC drafts against hand-authored templates in fixtures.

## Validation

- Same request, snapshot, region, and seed produces identical variants.
- WFC never runs until a bounded region and fixed anchors exist.
- Invalid WFC outputs are rejected by the existing placement validator.
- Top candidates explain their score components.
- Dashboard can show why WFC was used and why variants were accepted/rejected.
- Fixtures prove WFC beats or usefully diversifies simple templates often enough
  to justify keeping it enabled.

If WFC adds persisted proposal/trace fields, use **no compat code; wipe-and-regen
on upgrade.**

## Open Questions

- Which first grammar is worth the cost: freezer, hospital, barracks, or power?
- Should WFC live only in dashboard experimentation until Willie is live?
- What fixture corpus proves WFC beats simple templates often enough to keep it?
- What per-generator draft budget keeps WFC from crowding out simpler generators?
