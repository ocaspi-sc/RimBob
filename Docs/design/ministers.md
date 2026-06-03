# RimBob - Minister Design

> **Living document.** See `AGENTS.md` for update rules.
> This doc records durable minister behavior and domain boundaries. Exact C#
> signatures, enum members, fixture JSON shape, and rule helper names live in
> source and tests.

---

## Universal Minister Shape

Every minister has the same broad shape:

- A focused briefing computed by the state store.
- A deterministic rules layer for common cases.
- LLM escalation for judgment calls.
- Decision and replay logging for audit and Oracle refinement.
- Replay, trace, fixture, and Pushback evidence that lets the Oracle propose rule, prompt, briefing, fixture, or RAG changes from real prior behavior.

What differs by minister is the domain, escalation rate, and advice vocabulary.

---

## Play Mode

Play mode is live and `Suggest` mode by default. A typed play-cycle context
wakes a minister; the minister reads its briefing, evaluates deterministic rules
first, and either emits advice/flags or escalates to the LLM.

The durable trigger vocabulary is:

- `StartupBootstrap`
- `CabinetRefresh`
- `ManualTrigger`
- `FlagFired`
- `Heartbeat`
- `ScheduledWakeupFired`

Exact C# contracts live in `Src/Common/Ministers/`. Current runtime uses cabinet
refresh and manual dashboard triggers; `StartupBootstrap` remains a vocabulary
value for explicitly labeled first-run/bootstrap flows, not a Host-rebuild
cabinet wake.

Manual dashboard minister triggers also carry a run mode: `Run Rules` means deterministic rules only and must stop before provider work, while `Run LLM` means a forced LLM path and is only enabled for ministers with a wired LLM implementation.

Flag-carried requests can also wake an owning rules-only minister. In particular, a newly published Willie-owned `building_request` immediately runs Willie in `FlagFired` / rules-only mode so the deterministic Placement Solver handles the request without an extra manual Willie trigger.

### First Live Cycle

There is no first-cycle special case. A newly introduced feeder runs the **same
rules-first evaluation on its first live cycle as on every other**: evaluate
deterministic rules, emit advice/flags on a match, and reach the LLM only through
the normal escalation triggers (chiefly "no rule matches the current briefing
state"). A genuinely ambiguous first cycle therefore still escalates — by the
ordinary path, not a forced bootstrap.

Rationale:

- It keeps the rules-first invariant whole. Forcing an LLM call on cycle 1 was an
  exception to "rules handle common cases first; the LLM earns its cost only when
  judgment is needed," and it spent a guaranteed call even when a rule already
  answered — e.g. the deterministic shelter rule answers a new colony's day-one
  bed/shelter gap with no LLM.
- The factual current-state summary already gives the player a grounded initial
  read of the colony without an LLM. Coarse rules are fixed by better rules, not
  by a standing first-cycle escalation.
- A deterministic first cycle is replayable and fixture-testable (e.g. a captured
  new-colony fixture); a forced-LLM first cycle is not.
- It drops the fragile "have I bootstrapped yet?" persistence and the first-run
  vs. Host-rebuild ambiguity entirely.

`StartupBootstrap` stays in the trigger vocabulary as a wake **reason** (the
minister woke because it was newly introduced / on first run); it no longer
implies forced escalation. Mayor remains separate: a labeled bootstrap Agenda is
created only when no persisted minister output exists at all — that is a Mayor
synthesis mechanism, not a feeder rules-first exception, and is unaffected here.

Scheduled wakeups may be registered by rules or escalation output. A fired
wakeup still runs the normal rules-first evaluation cycle; its payload is an
opaque note to the minister, not system-parsed control data.

---

## Oracle Refinement

Refinement is async, between sessions or on demand. The Oracle reviews a minister's pushbacks, decision history, replay corpus, prompt traces, and fixtures; then it proposes changes for human approval.

Each minister owns its own pushback list. Pushbacks are scoped: the Mayor does
not see Chef's pushbacks, and Chef does not see Defense's. Pushbacks can inform
the issuing minister's next prompt and later provide the refinement corpus.
Implicit state-diff feedback is not part of MVP; see [`advice.md`](advice.md).

The Oracle is a developer/refinement role, not a play-mode minister and not another in-game advisor. Ministers do not inspect their own logs, rewrite their own rules, or self-promote behavior changes. Code-shaped refinement work may use dev-agent tooling, but human approval is required before rule, prompt, briefing, fixture, or RAG changes are promoted.

Any applied minister-logic change must close with a before/after advice diff on
the same input corpus. Prefer historic replay records; if they are missing or
not replayable, use the focused fixture/regression input and label the result
fixture-only. The diff should show the path taken, rule/LLM trace, priority, title,
resource requests, suggested actions, flags, and whether non-target cases stayed
unchanged.

Every minister LLM attempt must be replayable. The minister layer records the
briefing, trigger/context, guide citations, normalized output, accepted manual
substitute output, and failure details under the replay corpus. Provider raw
output capture is useful metadata, but it is not a substitute for a minister
replay record because it lacks the decision context and normalized output.

---

## Shared Runtime Contract

All ministers implement the same behavioral contract:

- Entry point receives execution metadata: why the minister woke and any
  relevant flag or wakeup payload.
- Rules evaluate briefing-derived domain facts.
- Rules may return advice/flags, schedule a future wakeup, or escalate.
- Escalation returns normalized advice/flags through the same public surfaces.
- Output remains `AdviceItem`s and flags in MVP. Ministers never call RIMAPI;
  player-confirmed Assisted Apply, if exposed, is handled by a separate
  allowlisted Host execution path.

The exact interfaces and result types are code contracts. Check
`Src/Common/Ministers/`, `Src/Common/Advice/`, and the relevant test fixtures
before editing implementation.

---

## Advice Quality Contract

All ministers should emit advice that is near-term, actionable, and possible in
the current or short-term game state. The Mayor may reason about strategy, but
even Mayor agenda items should prioritize concrete next moves over broad wish
lists.

Minister output should be sparse. A normal cycle should surface the most
important few items and avoid exhaustive menus. Under all-hits this sparsity
comes from priority-sorting matched rules and capping the tail — not from
suppressing matched rules, which would also drop their cross-minister flags.
Critical or unusually complex states can produce more, but each item must still
have one clear `priority`.

Briefings should provide compact opportunity summaries rather than raw dumps.
Spatial data is useful when it becomes actionable: distance/proximity buckets,
nearest clusters, tile counts, and bottleneck signals are preferred over lists
of every coordinate.

When an LLM is called, it must produce the same execution-facing fields the
runtime accepts: priority, title/body/rationale, concrete actions,
optional flags, optional scheduled wakeup, and trace notes.
See [`advice.md`](advice.md) for the advice schema and feedback lifecycle.

Future CoS work should treat each minister output as an issue-shaped record
before it becomes advice or a flag: evidence, urgency, ownership, candidate
actions, and cross-domain requests. The design-level catalogue lives in
[`deterministic-cos-cabinet-issue-solver.md`](../../.plans/deterministic-cos-cabinet-issue-solver.md).

LLM rules:

- Stable ids and trace notes should identify the rule or LLM path that produced the advice.
- `priority` is required on every advice item.
- Feeder ministers emit concrete operational advice, not grand strategy menus.
- `actions` are the single player-facing action list on advice.
- `AgentFlag.Requests` describe cross-minister needs; they do not allocate pawns
  or reserve another minister's resource in MVP.
- Trace notes are for logging and Oracle refinement, not player-facing advice.

---

## Rules Layer

The rules layer is pure, deterministic C# with no I/O. It handles the common
case cheaply; the LLM earns its cost only when judgment is needed.

### Rules are data, evaluated once

A minister's rules are a single ordered list of rule records, not a cascade of
hand-written `if` blocks. Each rule declares, in one place:

- a stable id / `trace` (used in logs, traces, replay attribution, and supersession);
- a match predicate over briefing-derived facts;
- a live `reason` selector — a short human string computed from the briefing (e.g. `break_risk_count=3`), evaluated for matched and non-matched rules alike;
- a builder that produces the `Decision` (or `Escalate`) when it fires.

Everything that consumes rules derives from that one list: live evaluation, the
matched/suppressed signal trace, and the "all rules" diagnostics catalogue. There
is **no static condition or output string** beside the predicate to drift: the
catalogue derives each rule's explanation from its live `reason` (run for matched
and non-matched rules) and from the advice/flags the builder actually emits. The
superseded shape wrote each rule three times (executable guards, a parallel
predicate re-evaluation for the trace, and a static string catalogue); keeping
the three in sync was manual and silently lossy.

Exact record fields, the shared base/helpers, and predicate bodies are code
contracts — see `Src/Common/Ministers/`. Design docs do not mirror them.

### All-hits evaluation

A cycle can match several rules at once. Every minister uses **all-hits**: each
matching rule emits its advice and flags, and the results aggregate into one
snapshot — priority-sorted, capped, and same-`trace` deduped. Escalation is
considered only when no rule matched. There is no first-match policy: suppressing
a matched rule would also drop its cross-minister flag and hide coexisting
problems from the inspection dashboard. Mutually-exclusive predicates self-limit,
so a rule that should win alone simply has a predicate no other rule overlaps.

All-hits needs three things, which live in the shared base:

- **priority sort + cap** — rank matched rules and keep the most important few;
  this is how output stays sparse, not by suppressing matches;
- **same-`trace` dedup/consolidation** — fold rules that map to one issue into a
  single card instead of near-duplicates;
- **explicit dominance predicates** — where one rule should suppress another,
  encode it in the predicate (e.g. defer kitchen-dependent placement until a
  kitchen exists), never in list order.

### Authoring rules

- Order rules highest-value first; under all-hits this sets trace/catalogue order
  and the priority-sort tiebreak.
- Give every rule a stable id so logs and replay records can attribute behavior.
- Keep escalation as a fallback after deterministic rules, not a parallel path.
- Treat recurring LLM output as a candidate for a new rule, not a reason to keep
  escalating forever.

---

## Cabinet Domain Ownership

Each minister owns either a production chain or a well-defined subsystem:

| Minister | Boundary |
|---|---|
| Chef | Nutrition chain: forage/edible plant harvest, crop production, hunting-for-food, butchering, cooking, meals, food stockpiles, freezer integrity |
| Defense | Threat response: raids, drafted combat, fortifications as defensive intent, weapons/ammo readiness |
| Willie | Built infrastructure: rooms, power, temperature systems, base layout, non-defense blueprints |
| Industry | Non-food production chain: stonecutting, tailoring, smithing, machining, fabrication, drug production, production stockpiles |
| Welfare | Mood & Needs: mood risk from pawn/guest needs, thoughts, recreation, schedules, relationships, comfort, beauty, ideology pressure, tame-animal living-condition requests |
| Medical | Health subsystem: wounds, disease, surgery, triage, medicine stock, hospital readiness |
| Research | Technology path: research queue, unlock dependencies, capability planning |
| Economy | Wealth and trade chain: trade goods, caravans, buying scarce resources, selling surplus, wealth pressure |
| Mayor | Colony-wide strategy and posture; owns no routine operational action |
| Chief of Staff | Flag triage and conflict arbitration; owns no direct production chain |
| Labor | Deferred Auto-epic work-system **policy recommender** (priorities/zones/schedules/policies). RimWorld's job-giver allocates pawns. Re-engages at Auto. |

Welfare's domain label is **Mood & Needs**. It watches needs and thoughts as they affect mood and break risk, then asks the owning domain for the smallest concrete fix. Guest/visitor comfort, lodging quality, and hospitality social pressure default to Welfare; Economy owns trade, caravans, goodwill, and diplomacy purpose; Willie owns physical guest-room work; Defense owns active threat response. Tame animal living-condition pressure also defaults to Welfare when the issue is pens, barns, animal beds, temperature-safe shelter, cleanliness-sensitive rest areas, or similar comfort/suffering evidence; Willie owns the physical assets, Chef owns feed/slaughter pressure, Economy owns herd economics, Defense owns threat/combat animals, and Medical owns treatment. Hunger routes to Chef when the fix is nutrition; pain, disease, and wounds route to Medical when the fix is treatment. Welfare keeps the mood impact and break-risk framing.

### Resource Requests

Ministers may request resources needed to satisfy their domain: tiles,
work-type-qualified labor, items, buildings, bills, stockpile space, or
attention from another subsystem. In MVP those requests are advisory only and
travel on flags. A request does not grant ownership of the target resource and
does not execute anything.

For CoS, these requests are solver inputs projected from an issue. They should
describe cross-domain dependencies in a compact typed form, not imperative
tasks. The source minister keeps ownership of the problem it is flagging;
`requested_from` names the owner of the dependency when known.

Labor requests must be specific enough for a player or future Labor minister to
act on. They should name the relevant RimWorld work-tab type when possible, and
the skill signal when a skill threshold matters. Routine hauling and cleaning
should not become labor requests unless they are urgently blocking the domain.

The executor is the game, not RimBob. In MVP a labor request is player advice.
At Auto it becomes an input to Labor, which recommends the smallest
work-system *policy* change (a work-tab priority, zone membership, schedule
block) and lets RimWorld's job system do the allocation. No minister — Labor
included — ever writes per-pawn jobs. This is why requests name the work-tab
type: that name *is* the knob. See [`DESIGN.md`](../DESIGN.md) decision "Auto
execution delegates to the game's native automation" and
[`ministers/labor.md`](ministers/labor.md).

Canonical work-type names are a code contract. Do not maintain a duplicate enum
list in this doc; use `Src/Common/Advice/WorkType.cs` and expand it from live
defs/modded work types when ingestion supports that.

---

## Action Ownership Map

Every game action eventually gets one primary owner. Other ministers may be
requesters when the action serves their chain. In MVP, requester/owner language
is advisory by itself; it does not execute writes or allocate pawns. Assisted
Apply may use the same ownership map to decide which minister is allowed to
surface an apply handle for a narrow action.

### Chef And Survival

| Action family | Owner | Common requesters | Notes |
|---|---|---|---|
| Sow food crops, choose food crop, expand food growing zone | Chef | Mayor, Economy | Chef owns nutrition timing and crop choice |
| Harvest crops, wild berries/agave, ambrosia-for-food | Chef | Welfare, Economy | Nutrition pressure belongs to Chef |
| Hunt for food | Chef | Defense, Economy | Chef owns need/target recommendation; Defense may veto dangerous hunts |
| Butcher animals/corpses for meat | Chef | Economy | Human/insect corpse policy may involve Welfare |
| Cook meals, choose meal type, set cook/butcher bill targets | Chef | Welfare, Medical | Welfare can request fine meals; Medical can request safe food |
| Manage freezer, food stockpile, spoilage response | Chef | Willie | Chef owns the need; Willie owns requested assets |

### Base And Infrastructure

| Action family | Owner | Common requesters | Notes |
|---|---|---|---|
| Build rooms, walls, doors, floors, roofs, furniture | Willie | All ministers | Willie owns build feasibility, placement, materials, and layout cost |
| Build pens, barns, animal beds, animal shelter assets | Willie | Welfare, Chef, Economy, Defense, Medical | Welfare owns tame-animal living-condition need; Willie owns build feasibility and placement |
| Build power generation, batteries, conduits, switches | Willie | Chef, Industry, Defense, Medical | Requester owns why power matters |
| Build temperature systems | Willie | Chef, Welfare, Medical, Industry | Freezer need is Chef; asset build is Willie |
| Build production benches | Willie | Industry, Chef, Medical, Research | Requester owns production need |
| Manage material/component stockpiles | Willie | Industry, Defense | Willie owns base material availability |
| Dumping zones, stone chunk flow, cleanup infrastructure | Willie | Industry, Welfare | Infrastructure and zone purpose, not cleaning labor |

### Industry And Goods

| Action family | Owner | Common requesters | Notes |
|---|---|---|---|
| Stonecutting, smelting, machining, fabrication | Industry | Willie, Defense, Economy | Requesters define need; Industry makes goods |
| Tailoring, apparel quality, textile processing | Industry | Welfare, Defense, Economy | Welfare requests clothing; Defense requests armor |
| Smithing, weapons, armor, shield belts | Industry | Defense, Economy | Defense owns combat requirement |
| Drug production, chemfuel, refinery outputs | Industry | Medical, Welfare, Economy, Defense | Policy can cross domains |
| Art, statues, quality furniture production | Industry | Welfare, Economy, Willie | Welfare requests beauty; Economy requests sale value |

### Defense And Emergencies

| Action family | Owner | Common requesters | Notes |
|---|---|---|---|
| Draft/undraft, combat positioning, retreat/hold advice | Defense | Mayor, Medical | Pawn allocation remains player/Labor in Suggest mode |
| Weapon readiness, loadout recommendations, armor readiness | Defense | Industry | Defense owns what is needed |
| Killbox, traps, turrets, defensive wall intent | Defense | Willie, Industry | Willie owns build feasibility |
| Firefighting, breach response, infestation response | Defense | Willie, Medical | Treat as emergency response |
| Prisoner combat risk and escape response | Defense | Welfare, Economy | Care/trade is not Defense unless threat is active |

### Welfare And Health

| Action family | Owner | Common requesters | Notes |
|---|---|---|---|
| Recreation, comfort, beauty, sleep quality, room impressiveness | Welfare | Willie, Industry | Welfare owns the Mood & Needs reason |
| Schedules, joy/work/sleep balance, mental-break prevention | Welfare | Medical, Defense | Suggest-only until Labor/Auto |
| Relationships, social fights, ideology mood pressure | Welfare | Mayor | Split later only if complexity justifies it |
| Guest lodging, comfort, and hospitality social pressure | Welfare | Economy, Willie | Economy owns visitor trade/diplomacy purpose; Willie owns room work; Defense owns threats |
| Tame animal living-condition pressure | Welfare | Chef, Economy, Defense, Medical | Requests Willie for pens, barns, beds, shelter, or rest-area fixes; does not own feed, herd economics, combat, or treatment |
| Triage, tending, disease monitoring, surgery, hospital readiness | Medical | Welfare, Willie, Industry | Split from Welfare because cadence/severity differ |
| Medicine stock, hospital beds, sterile room, vitals risk | Medical | Willie, Industry, Economy | Medical owns readiness |

### Strategy, Research, And Economy

| Action family | Owner | Common requesters | Notes |
|---|---|---|---|
| Research queue and tech path | Research | Mayor, Defense, Chef, Industry, Medical | Mayor sets posture; Research owns queue mechanics |
| Trade offers, buying scarce resources, selling surplus | Economy | Chef, Medical, Defense, Industry | Requester owns need; Economy owns trade decision |
| Visitor trade, caravans, goodwill, and diplomacy purpose | Economy | Welfare, Mayor | Welfare owns guest comfort and social pressure; Defense owns threat response |
| Caravan formation/provisioning purpose | Economy | Chef, Defense, Medical | Chef/Defense/Medical own sufficiency and risk inputs |
| Wealth pressure, stockpile liquidation, trade-good strategy | Economy | Mayor, Industry | Mayor sets posture; Economy manages wealth |
| Colony-wide goals and priority ordering | Mayor | All ministers | Mayor owns strategy, not routine operation |
| Conflicting flags and same-tick priority conflicts | Chief of Staff | All ministers | CoS arbitrates framing/priority |

### Hard Cases

| Hard case | Provisional handling |
|---|---|
| Psychoid/smokeleaf/beer | Industry owns production; Welfare owns drug policy; Economy owns sale strategy |
| Devilstrand | Chef comments on growing opportunity cost; Industry owns textile use; Economy owns sale value |
| Animals | Welfare owns tame-animal living-condition requests; Chef owns feed/starvation/slaughter pressure; Economy owns sale/trade/herd economics; Defense owns combat or dangerous animals; Medical owns treatment; future Animals minister possible |
| Guests/visitors | Welfare is default host for comfort, lodging, and social pressure; Economy owns trade/caravan/goodwill/diplomacy purpose; Willie owns guest-room builds; Defense owns active threat response |
| Prisoners | Welfare owns mood/needs pressure from living conditions; Medical owns health; Economy owns ransom/slavery/trade; Defense owns escape/riot risk |
| Ideology/rituals | Welfare owns mood pressure until rules/prompts become noisy |
| Multi-map/caravans | Economy owns purpose; Defense owns threat; Chef/Medical own provisioning sufficiency; full multi-map support deferred |

---

## Escalation Triggers

Rules should escalate when:

- No rule matches the current briefing state.
- Competing goals are close enough that context should break the tie.
- An unusual event type appears.
- Guide knowledge is likely to change the decision.

Rules should not escalate for:

- Simple threshold checks with known responses.
- Routine maintenance decisions.
- Patterns that fixtures and replay history already prove rules handle well.

---

## Fixture Testing

Each minister has fixtures: canned briefings with expected outcomes. Fixture
paths and JSON shape are test contracts, not design-doc contracts. Use the test
project as the source of truth.

Player Accept events and Pushback entries on shipped advice are raw material for
new fixtures. A rule change that breaks a fixture is a regression unless the
fixture is intentionally updated with a clear rationale.

---

## Zone Ownership

Zones are owned by the minister whose domain they serve. There is no dedicated
Minister of Zoning because zones are means to other ministers' ends.

| Zone type | Owning minister | Notes |
|---|---|---|
| Food stockpile | Chef | Co-located with freezer; Chef knows food quantities and spoilage risk |
| Material / component stockpile | Willie | Co-located with workshops; Willie knows material flow and build queue |
| Ammo / weapon stockpile | Defense | Near killbox or armoury |
| Medicine stockpile | Medical | Near hospital; Medical tracks medical supply chain |
| Growing zone | Chef | Placement, size, crop assignment |
| Animal pen / barn area | Welfare | Living-condition purpose; Willie owns fences, doors, barns, and beds; Chef/Economy/Defense can request constraints |
| Dumping zone | Willie | Rock chunks, corpses, waste; base hygiene |
| Home zone | Mayor / CoS | Colony-wide; no minister claims it |
| Allowed zone | Mayor / CoS | Colony-wide; no minister claims it |

In Suggest mode, conflicting zone advice from two ministers surfaces as two
cards and the player resolves it. Assisted Apply should not apply contested zone
writes. At Auto graduation, CoS must arbitrate contested writes before RIMAPI
commands are issued.

Layout efficiency belongs to Willie as an extension of its room/base
program, not a new minister.

---

## Open Questions

- [ ] How is Oracle refinement triggered: manual command, threshold, schedule, or a mix?
- [ ] Where are refinement session transcripts logged for audit?
- [ ] Should fixture generation be automated by a repo-local skill?
- [ ] Define the confidence threshold for any post-MVP auto-approve path.
