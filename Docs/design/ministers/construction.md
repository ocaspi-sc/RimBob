# Willie - Minister Design

> **Living document.** See `AGENTS.md` for update rules.
> Willie is a rules-only feeder advisor. This doc records domain boundaries
> and first-slice intent, not a code-level rule catalogue.
> Willie owns the construction domain; schema/code names use `Willie*`, not `Construction*`.

---

## Research / Planning Links

Use these as the current routing links before starting Willie/construction-domain work.

Direct plans:

- [Base / Willie / Layout Agent Plan](../../../.plans/base-construction-layout-agent.md) - implementation slice plan for the first Willie minister.
- [Base Layout / Willie Tips](../../../.plans/base-layout-construction-tips.md) - community layout heuristics translated into Willie spatial lint.
- [RIMAPI Blueprint Placement Endpoint](../../../.plans/rimapi-blueprint-placement-endpoint.md) - fork-side pending blueprint/frame lifecycle for future Willie apply and backlog reads.
- [Deterministic CoS Cabinet Issue Solver](../../../.plans/deterministic-cos-cabinet-issue-solver.md) - issue-report routing, including Willie-owned issue classes and cross-minister requests.

Reference corpora:

- [Placement Algorithm References](../../placement_algorithms/README.md) - algorithmic building blocks for efficient Willie candidate generation, pruning, scoring, and validation.

Direct todo entries in [Tasks.md](../../../Tasks.md):

- `source-todo-building-condition-read` - add building hitpoint/power/working-state reads before Willie relies on condition evidence.
- `construction-minister` - add Willie after minimal CoS handling.
- `construction-placement-layout-strategy-base` - decide placement/layout strategy and whether Base Layout ever splits out.
- `base-layout-construction-tips` - fold community base-building heuristics into spatial lint and dashboard evidence.
- `add-rimapi-fork-endpoints-blueprint` - add blueprint validate/place/read, allow/disallow, cancel, and backlog endpoints in the RIMAPI fork.
- `rimapi-building-detail-read` - add detailed building condition, working-state, and flickable/fuel/power evidence.
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
- Pens, barns, animal beds, and animal shelter assets when requested by Welfare or another owning minister.
- Layout efficiency as an extension of room/base planning.

Willie owns build feasibility and placement cost. Other ministers own why a build matters. For example, Chef owns freezer need and Welfare owns tame-animal living-condition need; Willie owns the cooler, wall, power, fence, barn, or bed work required to satisfy it.

No separate Base Layout minister is planned for the first pass. Split only if Willie's layout reasoning becomes noisy enough to justify it.

---

## First Slice Shape

Willie began as rules-first `Suggest`-mode advice. The current Willie rules path is still deterministic and has no LLM escalation; placement-solver options may now carry player-click `place_blueprint_group` Apply payloads when Willie has exact validated blueprint groups.

First deterministic advice areas:

- Resolve power deficit or low backup.
- Surface material/component bottlenecks that block current plans.
- Surface blocked current blueprints/frames.
- Build missing functional rooms from populated room anchors.
- Address missing freezer/power assets requested by Chef.

Richer storage-distance, fire-risk, frame-age, and base-layout rules wait on
briefing derivation extensions or fork reads. Wire/persistence changes in this
area follow the project default: no compat code; wipe-and-regen on upgrade.

### Concerns (defined)

The canonical concern set lives in
[`willie-advice-types.md`](../../../.plans/willie-advice-types.md). That plan is
the source of truth for the current eight Willie concerns and the rules-vs-LLM
split. `basic_shelter` moved to Welfare; survival-floor bedroom/barracks asks
reach Willie as `functional_rooms` build requests.
`Src/Common/Advice/WillieConcern.cs` now carries the closed eight-member enum on
the code side.

Decisions: `base_topology` is folded into `base_layout` (a dashboard /
briefing grouping, not its own type); `functional_rooms` and
`storage_placement` stay separate (room existence/purpose vs material-flow
placement - different blast radius, must graduate independently). No generic
`build_structure` concern:
"build" is an **action** (`place_blueprint`), not a category, because
`concern` is the autonomy-dial unit and stays granular.

### Placement & Authorship

Build placement is computed by the **Placement Solver** — a deterministic
component, not the LLM. The minister LLM (or rules) emits judgment plus, for
build advice, a compact semantic intent (target class, room class, capacity,
near-anchors, constraints) with **no coordinates**. The Placement Solver reads
the live map, generates 1–3 candidate layouts, validates them, and assembles the
pickable `options[]`. Request-driven builds are deterministic: the Solver
consumes `building_request`s directly (already structured), so no LLM step is
needed. See [`placement-solver.md`](../../../.plans/placement-solver.md).

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

The executable Apply (place a chosen layout) renders in **Willie's own
dashboard scope** for options where Willie emits an explicit action apply
payload.
A requesting minister (e.g. Chef asking for a freezer) shows only its outbound
request — never another minister's build Apply.

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

The first Willie briefing record now feeds `MinisterOfWillie` in a rules-only
slice. It includes backlog summaries from `/api/v1/map/construction/backlog`,
room-anchor inventory for `RoomClass` lookup, and coverage flags. The dashboard
renders the Willie scope with Briefing, Build Queue, Rules, and Advice views;
Prompt/RAG/Raw LLM remain intentionally absent until a later LLM slice.

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

- [x] Willie concerns defined — see **Concerns (defined)** above and `.plans/willie-advice-types.md`.
- [ ] Define room-program derivation in the state store.
- [ ] Decide how Willie consumes Chef/Welfare/Defense/Medical build requests.
- [ ] Decide when Research should split from Willie.
- [ ] Define practical layout heuristics without overbuilding a planner.
