# Minister of Construction - Minister Design

> **Living document.** See `AGENTS.md` for update rules.
> Construction is a future feeder advisor. This doc records domain boundaries
> and first-slice intent, not a code-level rule catalogue.
> Persona / UI name: **Willie**. Cabinet label stays **Construction** / **Minister of Construction**.

---

## Research / Planning Links

Use these as the current routing links before starting Construction work.

Direct plans:

- [Base / Construction / Layout Agent Plan](../../../.plans/base-construction-layout-agent.md) - implementation slice plan for the first Construction minister.
- [Base Layout / Construction Tips](../../../.plans/base-layout-construction-tips.md) - community layout heuristics translated into Construction spatial lint.
- [RIMAPI Blueprint Placement Endpoint](../../../.plans/rimapi-blueprint-placement-endpoint.md) - fork-side pending blueprint/frame lifecycle for future Construction apply and backlog reads.
- [Deterministic CoS Cabinet Issue Solver](../../../.plans/deterministic-cos-cabinet-issue-solver.md) - issue-report routing, including Construction-owned issue classes and cross-minister requests.

Reference corpora:

- [Placement Algorithm References](../../placement_algorithms/README.md) - algorithmic building blocks for efficient Construction candidate generation, pruning, scoring, and validation.

Direct todo entries in [HumanTodo.md](../../../HumanTodo.md):

- `source-todo-building-condition-read` - add building hitpoint/power/working-state reads before Construction relies on condition evidence.
- `construction-minister` - add Construction after minimal CoS handling.
- `construction-placement-layout-strategy-base` - decide placement/layout strategy and whether Base Layout ever splits out.
- `base-layout-construction-tips` - fold community base-building heuristics into spatial lint and dashboard evidence.
- `add-rimapi-fork-endpoints-blueprint` - add blueprint validate/place/read, allow/disallow, cancel, and backlog endpoints in the RIMAPI fork.
- `rimapi-building-detail-read` - add detailed building condition, working-state, and flickable/fuel/power evidence.
- `rimapi-stockpile-detail-read` - add stockpile detail reads for storage pressure and material flow.
- `rimapi-room-detail-read` - add room detail reads for welfare-sensitive construction planning.
- `rimapi-power-net-read` - add power-net detail reads for energy bottleneck and disconnected-asset evidence.
- `rimapi-buildability-layers-read` - add bounded buildability-layer reads for placement scoring.

Supporting research todo entries in [HumanTodo.md](../../../HumanTodo.md):

- `investigate-player-approved-construction-proposal` - learn from RimMind proposal approval patterns.
- `investigate-construction-minister-algorithms-rimmind` - mine RimMind construction/layout algorithms.
- `rimmind-construction-backlog` - inspect blueprint/frame/material-gap grouping for the Construction briefing.
- `rimmind-storage-saturation` - inspect stockpile/storage utilization as a Construction/Chef/Mayor signal.
- `investigate-spatial-evidence-tools-location` - inspect location-backed spatial evidence for actionable advice.
- `rimmind-defense-posture-coverage` - preserve Defense posture context for fortification/build requests.
- `food-freezer-briefing` - add freezer temperature/spoilage evidence that can drive Construction freezer requests.
- `source-todo-room-quality-read` - adjacent Welfare room evidence that may also inform Construction room-program advice.
- `action-ownership-catalogue` - keep Construction ownership aligned with the broader action/endpoint catalogue.

---

## Domain

Construction owns built infrastructure:

- Rooms, walls, doors, floors, roofs, furniture, and base expansion.
- Power generation, conduits, batteries, switches, and load margin.
- Temperature systems as buildable assets.
- Build queue feasibility, material readiness, and construction bottlenecks.
- Material/component stockpile zones.
- Layout efficiency as an extension of room/base planning.

Construction owns build feasibility and placement cost. Other ministers own why
a build matters. For example, Chef owns freezer need; Construction owns the
cooler/wall/power work required to satisfy it.

No separate Base Layout minister is planned for the first pass. Split only if
Construction's layout reasoning becomes noisy enough to justify it.

---

## First Slice Shape

Construction should begin as rules-first `Suggest`-mode advice.

Likely first advice areas:

- Repair breaches or damaged critical infrastructure.
- Resolve power deficit or low backup.
- Build missing beds/basic rooms.
- Address missing freezer/power assets requested by Chef.
- Warn about wood structures in critical rooms.
- Surface material/component bottlenecks that block current plans.

### Concerns (defined)

Nine canonical concerns, kept granular so each is an independent autonomy-dial unit:
`power_stability`, `freezer_infrastructure`, `basic_shelter`, `room_program`,
`storage_adjacency`, `material_bottleneck`, `fire_risk`, `build_queue_blocked`,
`layout_efficiency`. Definitions + the first-slice rules-vs-LLM split live in
[`willie-advice-types.md`](../../../.plans/willie-advice-types.md).

Decisions: `base_topology` is folded into `layout_efficiency` (a dashboard /
briefing grouping, not its own type); `room_program` and `storage_adjacency`
stay separate (room existence/purpose vs material-flow placement — different
blast radius, must graduate independently). No generic `build_structure` concern:
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

The executable Apply (place a chosen layout) renders in **Construction's own
dashboard scope** (Willie's tab), since Construction emits the placement action.
A requesting minister (e.g. Chef asking for a freezer) shows only its outbound
request — never another minister's build Apply.

---

## Briefing Direction

Construction briefing should answer:

- What is currently queued or blocked?
- Which materials/components are bottlenecks?
- Is power generation/storage sufficient?
- Which rooms/assets are missing for current colony size and Mayor posture?
- Are there structural or fire risks?
- Which requests from other ministers require build work?

Exact briefing fields should be defined in code/tests when Construction ships.

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

Construction may request:

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

- [x] Construction concerns defined — see **Concerns (defined)** above and `.plans/willie-advice-types.md`.
- [ ] Define room-program derivation in the state store.
- [ ] Decide how Construction consumes Chef/Defense/Medical build requests.
- [ ] Decide when Research should split from Construction.
- [ ] Define practical layout heuristics without overbuilding a planner.
