# Chief of Staff - Minister Design

> **Living document.** See `AGENTS.md` for update rules.
> CoS is the cabinet arbitration role. This doc records responsibilities and
> boundaries, not final schemas or rule code.

---

## Role

Chief of Staff sits between feeder ministers and the Mayor. Its job is to make
cabinet pressure coherent before it reaches the player.

CoS can:

- Dedupe overlapping flags.
- Choose lead framing when multiple ministers describe the same issue.
- Downgrade inflated flags with a logged reason.
- Decide whether a flag becomes a tactical alert or normal Mayor input.
- Surface strategic tensions to the Mayor.

CoS does not issue labor requests, produce routine player advice, mutate the
Agenda, or call RIMAPI.

---

## Deterministic Solver Model

CoS should be deterministic. Treat it as a solver over structured cabinet issue
inputs, not another chatty advisor. The broader issue-report catalogue lives in
[`deterministic-cos-cabinet-issue-solver.md`](../../../.plans/deterministic-cos-cabinet-issue-solver.md).

The mental model is: ministers publish pressure, the Mayor publishes posture,
and CoS computes the cabinet routing table for the current cycle. It should be
boring, replayable, and inspectable.

Inputs:

- Latest Mayor output: posture, ranked priorities, state-of-the-union category
  summaries, and `cabinet_direction` as read-only guidance.
- Active feeder issue reports, or current-runtime `AgentFlag`s projected from those issues: source minister, priority, domain, summary, detail, expiry, and stable ids.
- Structured requests projected from the issue: each `ResourceRequest` says
  what the source minister needs from another subsystem, with `requested_from`
  filled when the owner is known.
- Current minister snapshot metadata when useful for dedupe, such as whether a
  player-facing advice card already covers the same issue. CoS should not parse
  routine advice prose as input.
- Play-cycle metadata such as trigger reason, game tick/day, and live-state
  freshness when available.

Minister prose is not a solver contract. Ministers output issue-shaped facts:
evidence, urgency, candidate actions, and structured requests. Current runtime
can carry the requests on flags, but those requests describe the need; they do
not command CoS, reserve another minister's resource, allocate pawns, or execute
writes.

Outputs should be equally structured at the design level:

- Deduped issue groups.
- Lead framing / lead source for each issue.
- Tactical-alert candidates versus Mayor-digest inputs.
- Downgraded, suppressed, superseded, or deferred flags with reasons.
- Cross-domain tensions the Mayor should see.

The exact record names and DTO fields belong in source and tests when the
solver is implemented.

### Request Contract

Ministers can and should output structured requests for CoS when a problem in
their domain needs another subsystem's help. The request belongs on the flag
that created the cross-domain pressure.

Examples:

- Chef flags low meals and requests `Labor` with `work_type: Cook`.
- Chef flags spoilage risk and requests `Willie` for a freezer/cooler
  building dependency.
- Defense flags raid danger and requests `Willie` attention for a weak
  wall/door bottleneck.
- Welfare flags mood collapse and requests `Chef` attention if meal quality is
  one of the concrete causes.

Good requests are compact and typed:

- `kind`: what class of thing is needed, such as labor, building, bill, item,
  stockpile space, tile, trade capacity, or attention.
- `request`: short noun phrase, not an essay.
- `reason`: the blocking condition or risk.
- `requested_from`: target owner when known.
- `quantity`: only when a real count is known.
- `work_type` / `skill`: required for labor requests when the game work type is
  known.

Bad requests:

- "Fix this soon" with no owner or kind.
- Labor requests without a work type when the work type is knowable.
- Requests that duplicate the source advice action instead of naming a
  cross-domain dependency.
- Requests that tell CoS to execute, reserve, or assign something.

### Solver Pipeline

The first solver can be a pure sequence of deterministic passes:

1. **Collect.** Read active flags at the selected priority threshold, discard
   expired entries, and keep stale/live source metadata visible for tracing.
2. **Normalize.** Fill missing owner hints from the ownership map when safe,
   normalize request priorities, and mark unresolved owners instead of guessing.
3. **Group.** Build issue groups by stable flag id, source/domain, request
   target, and obvious same-problem keys such as `food/freezer`,
   `food/cooking`, `defense/raid`, or `construction/power`.
4. **Calibrate priority.** Respect the emitting minister's priority, allow CoS to downgrade with a reason, and keep upgrades as new minister flags rather than silent CoS edits.
5. **Choose lead framing.** Pick the player-facing lead by owner, urgency, and
   Mayor posture. The lead framing should answer "what is the one thing the
   player needs to understand first?"
6. **Route.** Mark each group as tactical alert, Mayor-digest input, deferred,
   suppressed duplicate, or unresolved.
7. **Explain.** Emit traceable reasons for every downgrade, suppression,
   deferral, and lead-framing choice.

The solver should be stable: the same inputs produce the same output and the
dashboard can show the exact reasoning.

### Routing Defaults

Routing should be conservative early:

- `Critical` flags route to tactical alert unless expired, superseded, or
  clearly duplicate an active higher-confidence alert.
- `High` flags route to tactical alert when the player likely needs to act
  before the next Mayor cycle; otherwise they become high-priority digest input.
- `Medium` flags normally feed the Mayor digest unless they combine with another
  flag into a higher-pressure issue group.
- `Low` flags stay deferred/background unless the Mayor posture explicitly makes
  that domain the current focus.

Mayor posture does not override immediate danger, but it can break ties. If the
Mayor posture is "survival food security", a Medium Chef freezer/cooking issue
should lead over a Medium long-term research opportunity.

### Example Resolutions

Food shortage plus no cook coverage:

- Chef emits a High food flag with requests for `Cook` labor and maybe a simple
  meal bill.
- CoS keeps Chef as lead because the player-facing issue is starvation risk.
- The Labor/Bill requests stay attached as dependencies.
- Route is tactical alert if days of food are dangerously low; otherwise Mayor
  digest with Chef as lead.

Freezer risk:

- Chef emits a Medium spoilage/freezer flag requesting `Willie` for a
  cooler-backed room.
- Willie later emits a power/material blocker for the same freezer work.
- CoS groups them under one issue, chooses Willie as the execution owner
  but Chef as the reason if nutrition risk is the player-facing issue.
- Route is digest unless spoilage or heat makes it time-sensitive.

Raid during routine food expansion:

- Defense emits Critical raid danger.
- Chef emits Low/Medium crop expansion or hunting opportunity.
- CoS routes Defense as tactical alert and defers/suppresses routine Chef
  pressure with a reason such as "active threat preempts expansion advice".

Three ministers point at the same root cause:

- Chef wants freezer space, Willie reports power shortage, Economy warns
  steel/components are scarce.
- CoS groups the requests into one "freezer blocked by infrastructure/materials"
  issue instead of three separate alerts.
- The Mayor sees the strategic tension: protect food stores without creating a
  wealth/material spiral.

### Dashboard Shape

When implemented, the dashboard should expose CoS output as inspection data:

- active issue groups,
- lead source and contributing flags,
- route decision,
- requests grouped by target owner,
- downgrade/suppression reasons,
- unresolved owner/request warnings.

This belongs in the cabinet/System inspection surface, not as player-facing
advice prose. Tactical alerts may render separately when the route says they
should.

---

## Implementation Posture

Keep CoS split in the design language, but do not overbuild runtime structure
while feeder volume is low. The first pass may be a Mayor-side deterministic
solver helper. A separate loop/process is justified only when active flag
traffic makes the helper hard to reason about.

---

## Rules And Escalation

Rules should handle mechanical arbitration:

- Critical threat flags preempt normal work.
- Single high-priority flags need little arbitration.
- Low flags defer behind high/critical pressure.
- Same-source duplicate flags should update/supersede rather than stack.
- Requests aimed at another subsystem use the ownership map for routing; the
  requesting minister does not take over that subsystem.

Escalate when multiple serious flags compete, when two ministers should share a
single player-facing framing, or when priority depends on Mayor posture.

---

## Open Questions / TODO

- [ ] Define first CoS solver output records when implementation starts.
- [ ] Decide whether CoS needs its own briefing or only the active flag set plus
      Mayor posture.
- [ ] Define three-way contention handling.
- [ ] Decide whether ignored Low flags age upward.
