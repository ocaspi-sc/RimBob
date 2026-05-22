# Distance Transforms

Source: https://theoryofcomputing.org/articles/v008a019/
Retrieved: 2026-05-22
Authors: Pedro F. Felzenszwalb and Daniel P. Huttenlocher
Published: 2012
Source license: CC-BY, according to the Theory of Computing article page.
Note: Original RimBob summary. This is not a verbatim copy of the source.

## RimBob Use

Distance transforms turn anchor selection and adjacency scoring into map fields.
Instead of asking "how far is this candidate from every kitchen/freezer/field",
RimBob can precompute one grid per target set and read scores directly.

This is the main scoring primitive after hard placement gates.

## Placement Heuristics

- Compute distance-to-freezer, distance-to-kitchen, distance-to-dining, distance-to-fields, distance-to-stockpile, and distance-to-conduit fields.
- Compute negative fields too: distance-from-map-edge-threat, distance-from-hostile-approach, distance-from-fire-risk, and distance-from-high-traffic dirt sources.
- Combine fields with request-specific weights. A freezer candidate should value kitchen/field proximity; a battery room should value conduit reach and low fire risk.
- Convert distance fields into anchor lists before template expansion, so the solver never enumerates the full map unnecessarily.

## Candidate Generation Pattern

```pseudo
weights_for_freezer = {
    distance_to_kitchen: -3,
    distance_to_fields: -2,
    distance_to_conduit: -1,
    distance_to_map_edge_threat: +2
}

anchor_score[cell] = weighted_sum(distance_fields, weights_for_freezer, cell)
anchors = top_k_lowest(anchor_score, 200)

for anchor in anchors:
    for template in freezer_templates:
        candidate = place(template, anchor)
        if passes_fast_gates(candidate):
            score = average_field_score(candidate.cells, weights_for_freezer)
            keep_top_k(candidate, score)
```

## Fit / Limits

Distance fields provide excellent cheap ranking but they are not final
validation. A candidate can be near the right things and still fail because of
terrain, blocked cooler exhaust, roof rules, door topology, or RimWorld
placement rules.

## Dashboard Evidence

- Render heatmaps for the fields used in the candidate score.
- Show each candidate's score breakdown by field.
- Let debug views compare alternate weight profiles for the same request.
