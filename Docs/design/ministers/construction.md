# Willie - Minister Design

> **Living document.** See `AGENTS.md` for update rules.
> Willie is a rules-only feeder advisor. This doc records domain boundaries
> and first-slice intent, not a code-level rule catalogue.
> Willie owns the construction domain; schema/code names use `Willie*`, not `Construction*`.

---

## Research / Planning Links

Use these as the current routing links before starting Willie/construction-domain work.

Direct plans:

- [Base / Willie / Layout Plan](../../../.plans/base-construction-layout-agent.md) - implementation slice plan for the first Willie minister.
- [Base Layout / Willie Tips](../../../.plans/base-layout-construction-tips.md) - community layout heuristics translated into Willie spatial lint.
- [RIMAPI Blueprint Placement Endpoint](../../../.plans/rimapi-blueprint-placement-endpoint.md) - fork-side pending blueprint/frame lifecycle for future Willie apply and backlog reads.
- [Willie Home-Area Buildable-Region Anchor](../../../.plans/willie-home-area-buildable-region-anchor.md) - fallback anchor from the player's painted Home area when no room anchor exists.
- [Deterministic CoS Cabinet Issue Solver](../../../.plans/deterministic-cos-cabinet-issue-solver.md) - issue-report routing, including Willie-owned issue classes and cross-minister requests.

Reference corpora:

- [Placement Algorithm References](../../placement_algorithms/README.md) - algorithmic building blocks for efficient Willie candidate generation, pruning, scoring, and validation.

Direct todo entries in [Tasks.md](../../../Tasks.md):

- `source-todo-building-condition-read` - add building hitpoint/power/working-state reads before Willie relies on condition evidence.
- `construction-minister` - add Willie after minimal CoS handling.
- `construction-placement-layout-strategy-base` - decide placement/layout strategy and whether Base Layout ever splits out.
- `base-layout-construction-tips` - fold community base-building heuristics into spatial lint and dashboard evidence.
- `add-rimapi-fork-endpoints-blueprint` - add blueprint validate/place/read, allow/disallow, cancel, and backlog endpoints in the RIMAPI fork.
- `rimapi-building-detail-read` - added detailed building condition, working-state, and flickable/fuel/power evidence.
- `rimapi-stockpile-detail-read` - add stockpile detail reads for storage pressure and material flow.
- `rimapi-room-detail-read` - add room detail reads for welfare-sensitive construction planning.
- `rimapi-power-net-read` - add power-net detail reads for energy bottleneck and disconnected-asset evidence.
- `rimapi-buildability-layers-read` - add bounded buildability-layer reads for placement scoring.

Supporting research todo entries in [Tasks.md](../../../Tasks.md):

- `investigate-player-approved-construction-proposal` - learn from RimMind proposal approval patterns.
- `investigate-construction-minister-algorithms-rimmind` - mine RimMind construction/layout algorithms.
- `rimmind-construction-backlog` - inspect blueprint/frame/material-gap grouping for the Willie briefing.
- `rimmind-storage-saturation` - inspect stockpile/storage utilization as a Willie/Chef/Mayor signal.
- `investigate-spatial-evidence-tools-location` - inspect location-backed spatial evidence for actionable advice.
- `rimmind-defense-posture-coverage` - preserve Defense posture context for fortification/build requests.
- `food-freezer-briefing` - add freezer temperature/spoilage evidence that can drive Willie freezer requests.
- `source-todo-room-quality-read` - adjacent Welfare room evidence that may also inform Willie room-program advice.
- `action-ownership-catalogue` - keep Willie ownership aligned with the broader action/endpoint catalogue.

---

## Domain

Willie owns built infrastructure:

- Rooms, walls, doors, floors, roofs, furniture, and base expansion.
- Power generation, conduits, batteries, switches, and load margin.
- Temperature systems as buildable assets.
- Build queue feasibility, material readiness, and construction bottlenecks.
- Material/component stockpile zones.
- Growing-zone placement when another minister owns the crop/size/urgency and routes a `zone_request` to Willie.
- Pens, barns, animal beds, and animal shelter assets when requested by Welfare or another owning minister.
- Layout efficiency as an extension of room/base planning.

Willie owns build feasibility and placement cost. Other ministers own why a build matters. For example, Chef owns freezer need and Welfare owns tame-animal living-condition need; Willie owns the cooler, wall, power, fence, barn, or bed work required to satisfy it.

No separate Base Layout minister is planned for the first pass. Split only if Willie's layout reasoning becomes noisy enough to justify it.

---

## First Slice Shape

Willie began as rules-first `Suggest`-mode advice. The current Willie rules path is still deterministic and has no LLM escalation; placement-solver options may now carry player-click `place_blueprint_group` Apply payloads when Willie has exact validated blueprint groups, and grow-zone options may carry player-click `create_growing_zone` Apply payloads when Willie has a complete rectangular zone option.

First deterministic advice areas:

- Resolve power deficit or low backup.
- Surface material/component bottlenecks that block current plans.
- Surface blocked current blueprints/frames.
- Build missing functional rooms from populated room anchors.
- Address missing freezer/power assets requested by Chef.

Richer storage-distance, fire-risk, frame-age, and base-layout rules wait on
briefing derivation extensions or fork reads. Wire/persistence changes in this
area follow the project default: no compat code; wipe-and-regen on upgrade.

### Issue Classes

The historical issue-class discussion lives in [`willie-advice-types.md`](../../../.plans/willie-advice-types.md). It remains useful for rule/LLM split and dashboard grouping, but there is no closed Willie advice-category enum or `AdviceItem` field. `basic_shelter` moved to Welfare; survival-floor bedroom/barracks asks reach Willie as `functional_rooms` build requests.

Decisions: `base_topology` is folded into `base_layout` (a dashboard / briefing grouping, not its own type); `functional_rooms` and `storage_placement` stay separate (room existence/purpose vs material-flow placement - different blast radius, different validation and Auto trust gates). No generic `build_structure` advice category: "build" is an **action** (`place_blueprint`), and future build Auto grouping keys on `BuildingClass`.

### Placement & Authorship

Build placement is computed by the **Placement Solver** — a deterministic
component, not the LLM. The minister LLM (or rules) emits judgment plus, for
build advice, a compact semantic intent (target class, room class, capacity,
near-anchors, constraints) with **no coordinates**. The Placement Solver reads
the live map, generates 1–3 candidate layouts, validates them, and assembles the
pickable `options[]`. Request-driven builds are deterministic: the Solver
consumes `building_request`s directly (already structured), so no LLM step is
needed. See [`placement-solver.md`](../../../.plans/placement-solver.md).

Inbound `building_request`s for any Willie-owned build class usually preempt generic missing-room advice after hard build blockers such as power deficits, material gaps, and blocked frames. If a request structurally depends on a missing room anchor, such as a freezer with `near kitchen` adjacency while no kitchen exists, Willie places the prerequisite first: an explicit inbound kitchen request wins when present, otherwise Willie's missing-kitchen fallback synthesizes the same request shape. Missing kitchen, hospital, and storage rules use the generic `building_request_active`/missing-room solver path and attach validated options to the emitted advice item instead of keeping non-freezer rooms prose-only.

When another minister publishes a flag with a Willie `building_request`, the Host immediately wakes Willie in rules-only `FlagFired` mode and focuses that request for the solver run. The operator should not need to click Willie `Run Rules` just to turn a fresh Chef/Welfare/Medical build request into placement options.

When another minister publishes a flag with a Willie `zone_request`, the Host also wakes Willie in rules-only `FlagFired` mode, but zone placement is a separate path from building placement. The grow-zone solver reads coordinate terrain, existing growing zones, stockpiles, room/building occupancy, and Home/buildable-region evidence, then records options or a no-fit reason in the zone board. For grow zones, Home/buildable-region evidence is an anchor and scoring locus, not a placement boundary: the solver may search and place outside Home, with oversized maps using a budgeted search window centered on the anchor. It does not run the building Placement Solver and does not produce `place_blueprint_group`. When a selected zone option is a complete rectangle, Willie emits a `create_growing_zone` Assisted Apply action; `AssistedApplyService` refreshes live state, validates map, plant def, terrain growability, and unoccupied/unzoned cells, calls RIMAPI's growing-zone endpoint, and reads back zones before recording the result.

Here, "1-3 candidate layouts" means the final emitted `options[]`, not the
internal search space. The solver may run several bounded candidate generators
(templates, empty rectangles, pattern matches, reuse-existing-footprint, and
future WFC/CP-SAT experiments), but all drafts flow through one shared hard-gate,
score, dedupe/diversity, and RIMAPI-validation pipeline. Generators propose;
the solver decides.

Candidate score traces should keep measured raw values with explicit units
(`tiles`, `watts`, `celsius`, `nutrition`, `stacks`, `silver_value`, etc.) beside
the normalized 0-1 values used for ranking. Status facts such as `draftable`,
`placement_valid`, `materials_ready`, `apply_ready`, and RIMAPI validation state
stay booleans/enums rather than fake unit-bearing metrics.

Material shortage is advisory for blueprint placement: `materials_ready` can be blocked while `apply_ready` stays ready when the footprint is validated, because pawns haul materials after the blueprint group exists.
Any Willie dashboard surface that shows a material or building-ingredient count should render the resource icon with the quantity. Solver traces and raw payload inspectors keep exact source fields, but Build Queue, Requests, Briefing HUD chips, and Advice/flag request rows use icon-led material quantities.

The executable Apply (place a chosen layout) renders in **Willie's own
dashboard scope** for options where Willie emits an explicit action apply
payload.
A requesting minister (e.g. Chef asking for a freezer) shows only its outbound
request — never another minister's build Apply.

Willie's dashboard scope includes a latest-only Solver view for placement diagnostics. The view reads the most recent solver outcome from the Host, shows the driving build request, selected rule, no-fit stage, and readiness ladder, and leaves option picking/apply controls in Build Queue.

Willie's dashboard scope also includes a Requests view backed by live per-request solver memory. Every Willie cycle records the current inbound building-request board, keyed by request target class, target def, room class, and request text, and joins each row to its latest solver outcome when one exists. The same live store reuses a prior building or zone solver outcome only when the request identity and placement-input fingerprint still match; the fingerprint covers the solver-owned map, anchor, occupancy, terrain, crop, material, and construction-backlog inputs, and transient offline/error outcomes are never reused. The board carries full `BuildingRequest` fields; solver options are held only in the live store and `/api/ministers/willie/solver/requests` response, not in `PlacementSolverReplayOutput` or the replay corpus.

The same Requests view also surfaces inbound `zone_requests` through `/api/ministers/willie/zone-requests`. Zone rows carry the full `ZoneRequest` fields plus the latest zone-solver outcome: awaiting solve, no-fit, error, or zone options. Apply for a zone belongs only on Willie-authored `create_growing_zone` advice actions after the solver emits a concrete rectangular option, never on the requesting minister's outbound row.

When a solver run returns zero `options[]` or errors before options can be attached, the driving Willie `AdviceItem` must say in its body that no layout options were suggested and include the concrete reason. The rationale and Solver view may carry the more technical no-fit/error trace, but the Advice card itself cannot look like plain prose advice when the solver failed to produce placements.

Solver reuse of existing rooms is evidence-gated: `ReuseExistingFootprintGenerator` may propose interior fixtures/floors inside a same-class existing room only when the fork supplied real bounded room `cells[]`; it does not infer room polygons from point-approx building positions, and it does not replace walls for freezer coolers yet.

---

## Briefing Direction

Willie briefing should answer:

- What is currently queued or blocked?
- Which materials/components are bottlenecks?
- Is power generation/storage sufficient?
- Which rooms/assets are missing for current colony size and Mayor posture?
- Are there structural or fire risks?
- Which requests from other ministers require build work?

The first Willie briefing record now feeds `MinisterOfWillie` in a rules-only slice. It includes backlog summaries from `/api/v1/map/construction/backlog`, room-anchor inventory for `RoomClass` lookup, and coverage flags. The dashboard renders the Willie scope with Briefing, Build Queue, Solver, Requests, Rules, and Advice views; Prompt/RAG/Raw LLM remain intentionally absent until a later LLM slice.

Room-anchor inventory treats work tables as building evidence. RimBob ingests `/api/v1/map/work-tables` into the shared building registry, then surfaces one anchor per detected room function: a multi-purpose barracks with a stove keeps its Barracks primary anchor and also exposes a Kitchen anchor at the stove cell for placement requests such as "near kitchen."

When no room anchor resolves, Willie may fall back to a Home-area `BuildableRegion` anchor derived from `/map/zones` `data.areas[]`. This is a low-priority fallback only: it never competes with real room anchors, and the solver uses it only after normal `near:<room>` resolution returns zero anchors. Building-placement fallback remains clamped to the Home/buildable-region bounds when geometry is available; grow-zone placement treats Home as a locus and may search outside it. When the fork supplies no Home geometry, including `cells_count:0`, Willie still treats the Home area row as a valid fallback anchor and uses map bounds plus a bounds-center target as an explicitly approximate locus. Exact non-rectangular cell-mask placement remains deferred to buildability-layer evidence.

---

## Escalation Boundaries

Rules should handle obvious threshold and missing-asset cases. Escalate for:

- Room program trade-offs.
- Power architecture and placement decisions.
- Expansion layout.
- Material substitution trade-offs.
- Research/build-order trade-offs if Research is not yet split.

---

## Resource Requests

Willie may request:

- Labor: Construct, Mine, Deconstruct, Haul when directly blocking build work.
- Items: steel, wood, stone blocks, components, power materials.
- Tiles/space: room footprints, stockpile expansion, safe build corridors.
- Bills/settings: stonecutting or fabrication needs that support construction.

In MVP these requests remain advice and flags; they never execute writes by themselves.

---

## Success Metrics

- Power deficit does not persist for long except during unavoidable events.
- All colonists get beds promptly.
- Critical rooms avoid high fire-risk construction once materials allow.
- Chef/freezer build requests are resolved or clearly blocked.
- Build queue blockers are visible and actionable.

---

## Open Questions / TODO

- [x] Willie issue classes documented - see **Issue Classes** above and `.plans/willie-advice-types.md`.
- [ ] Define room-program derivation in the state store.
- [ ] Decide how Willie consumes Chef/Welfare/Defense/Medical build requests.
- [ ] Decide when Research should split from Willie.
- [ ] Define practical layout heuristics without overbuilding a planner.
