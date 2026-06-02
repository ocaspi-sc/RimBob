# Chef Kitchen Prerequisite Request

## Goal

Chef should request starter kitchen/cooking-station construction when a selected food path creates or depends on food that cannot become meals because no cooking building is visible. Willie should place that prerequisite before chasing dependent freezer-near-kitchen requests.

## Scope

- Add Chef rule support for a Willie `building_request` with `room_class: kitchen` and `target_class: production_bench` on harvest, forage, growing, cooking, and hunting paths when no cooking building exists.
- Keep Willie as the placement/feasibility owner, not the food-chain owner.
- Make Willie prefer an explicit kitchen request, or its own missing-kitchen fallback, when a selected inbound request structurally depends on a missing kitchen anchor.
- Update Food and Willie tests plus the relevant minister design docs.
