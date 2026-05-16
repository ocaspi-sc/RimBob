# RimAI - Minister Design

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
- Decision and replay logging for audit and refinement.
- A refinement mode that proposes rule, prompt, briefing, fixture, or RAG
  changes from real prior behavior.

What differs by minister is the domain, escalation rate, and advice vocabulary.

---

## Play Mode

Play mode is live and suggest-only. A typed play-cycle context wakes a minister;
the minister reads its briefing, evaluates deterministic rules first, and either
emits advice/flags or escalates to the LLM.

The durable trigger vocabulary is:

- `StartupBootstrap`
- `CabinetRefresh`
- `ManualTrigger`
- `FlagFired`
- `Heartbeat`
- `ScheduledWakeupFired`

Exact C# contracts live in `Src/Common/Ministers/`. Current runtime uses startup
bootstrap, cabinet refresh, and manual dashboard triggers; the other trigger
names are reserved extension points.

### First Live Cycle Bootstrap

Every feeder minister gets one special case on its first live cycle after Host
startup, or the first cycle after that minister is newly introduced into a save:
bootstrap via escalation first, then return to normal rules-first behavior.

Rationale:

- The first memo establishes the minister's initial read of the colony.
- Early rules are intentionally coarse and may only cover steady-state triage.
- A grounded first-pass plan is more useful than a generic "domain is weak"
  card.

Constraints:

- Bootstrap is one-time behavior, not a standing exception to rules-first.
- If the briefing cannot support concrete advice, the minister should say that
  rather than fabricate precision.
- Mayor remains separate: the Mayor already writes an LLM-backed agenda on wake.

Scheduled wakeups may be registered by rules or escalation output. A fired
wakeup still runs the normal rules-first evaluation cycle; its payload is an
opaque note to the minister, not system-parsed control data.

---

## Refinement Mode

Refinement is async, between sessions or on demand. The minister reviews its own
pushbacks, decision history, replay corpus, prompt traces, and fixtures; then it
proposes changes for human approval.

Each minister owns its own pushback list. Pushbacks are scoped: the Mayor does
not see Food's pushbacks, and Food does not see Defense's. Pushbacks can inform
the issuing minister's next prompt and later provide the refinement corpus.
Implicit state-diff feedback is not part of MVP; see [`advice.md`](advice.md).

Refinement is the same minister in a different mode, not a separate product
actor. Code-shaped refinement work may use dev-agent tooling, but human approval
is required before rule, prompt, briefing, fixture, or RAG changes are promoted.

Any applied minister-logic change must close with a before/after advice diff on
the same input corpus. Prefer historic replay records; if they are missing or
not replayable, use the focused fixture/regression input and label the result
fixture-only. The diff should show the path taken, advice type, priority, title,
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
- Output remains `AdviceItem`s and flags in MVP, never RIMAPI writes.

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
important few items and avoid exhaustive menus. Critical or unusually complex
states can produce more, but each item must still have one clear `priority`.

Briefings should provide compact opportunity summaries rather than raw dumps.
Spatial data is useful when it becomes actionable: distance/proximity buckets,
nearest clusters, tile counts, and bottleneck signals are preferred over lists
of every coordinate.

When an LLM is called, it must produce the same execution-facing fields the
runtime accepts: advice type, priority, title/body/rationale, ordered steps,
optional flags, optional scheduled wakeup, and trace notes.
See [`advice.md`](advice.md) for the advice schema and feedback lifecycle.

LLM rules:

- Advice types are closed per minister.
- `priority` is required on every advice item.
- Feeder ministers emit concrete operational advice, not grand strategy menus.
- `steps` are the single player-facing action path on advice.
- `AgentFlag.Requests` describe cross-minister needs; they do not allocate pawns
  or reserve another minister's resource in MVP.
- Trace notes are for logging/refinement, not player-facing advice.

---

## Rules Layer

The rules layer is pure, deterministic C# with no I/O. It handles the common
case cheaply; the LLM earns its cost only when judgment is needed.

Authoring rules:

- Put the highest-value, highest-frequency rule first.
- Return on the first match unless the minister intentionally produces a
  multi-item snapshot.
- Give every rule a stable name so logs and replay records can attribute
  behavior.
- Treat recurring LLM output as a candidate for rule promotion, not as a reason
  to keep escalating forever.

---

## Cabinet Domain Ownership

Each minister owns either a production chain or a well-defined subsystem:

| Minister | Boundary |
|---|---|
| Food | Nutrition chain: wild harvest, crop production, hunting-for-food, butchering, cooking, meals, food stockpiles, freezer integrity |
| Defense | Threat response: raids, drafted combat, fortifications as defensive intent, weapons/ammo readiness |
| Construction | Built infrastructure: rooms, power, temperature systems, base layout, non-defense blueprints |
| Industry | Non-food production chain: stonecutting, tailoring, smithing, machining, fabrication, drug production, production stockpiles |
| Welfare | Pawn wellbeing: mood, recreation, schedules, relationships, comfort, beauty, ideology mood pressure |
| Medical | Health subsystem: wounds, disease, surgery, triage, medicine stock, hospital readiness |
| Research | Technology path: research queue, unlock dependencies, capability planning |
| Economy | Wealth and trade chain: trade goods, caravans, buying scarce resources, selling surplus, wealth pressure |
| Mayor | Colony-wide strategy and posture; owns no routine operational action |
| Chief of Staff | Flag triage and conflict arbitration; owns no direct production chain |
| Labor | Deferred Auto-epic assignment solver; owns pawn allocation only after Auto re-engages |

### Resource Requests

Ministers may request resources needed to satisfy their domain: tiles,
work-type-qualified labor, items, buildings, bills, stockpile space, or
attention from another subsystem. In MVP those requests are advisory only and
travel on flags. A request does not grant ownership of the target resource and
does not execute anything.

Labor requests must be specific enough for a player or future Labor minister to
act on. They should name the relevant RimWorld work-tab type when possible, and
the skill signal when a skill threshold matters. Routine hauling and cleaning
should not become labor requests unless they are urgently blocking the domain.

Canonical work-type names are a code contract. Do not maintain a duplicate enum
list in this doc; use `Src/Common/Advice/WorkType.cs` and expand it from live
defs/modded work types when ingestion supports that.

---

## Action Ownership Map

Every game action eventually gets one primary owner. Other ministers may be
requesters when the action serves their chain. In MVP this is advisory only;
requester/owner language does not execute writes or allocate pawns.

### Food And Survival

| Action family | Owner | Common requesters | Notes |
|---|---|---|---|
| Sow food crops, choose food crop, expand food growing zone | Food | Mayor, Economy | Food owns nutrition timing and crop choice |
| Harvest crops, wild berries/agave, ambrosia-for-food | Food | Welfare, Economy | Nutrition pressure belongs to Food |
| Hunt for food | Food | Defense, Economy | Food owns need/target recommendation; Defense may veto dangerous hunts |
| Butcher animals/corpses for meat | Food | Economy | Human/insect corpse policy may involve Welfare |
| Cook meals, choose meal type, set cook/butcher bill targets | Food | Welfare, Medical | Welfare can request fine meals; Medical can request safe food |
| Manage freezer, food stockpile, spoilage response | Food | Construction | Food owns the need; Construction owns requested assets |

### Base And Infrastructure

| Action family | Owner | Common requesters | Notes |
|---|---|---|---|
| Build rooms, walls, doors, floors, roofs, furniture | Construction | All ministers | Construction owns build feasibility, placement, materials, and layout cost |
| Build power generation, batteries, conduits, switches | Construction | Food, Industry, Defense, Medical | Requester owns why power matters |
| Build temperature systems | Construction | Food, Welfare, Medical, Industry | Freezer need is Food; asset build is Construction |
| Build production benches | Construction | Industry, Food, Medical, Research | Requester owns production need |
| Manage material/component stockpiles | Construction | Industry, Defense | Construction owns base material availability |
| Dumping zones, stone chunk flow, cleanup infrastructure | Construction | Industry, Welfare | Infrastructure and zone purpose, not cleaning labor |

### Industry And Goods

| Action family | Owner | Common requesters | Notes |
|---|---|---|---|
| Stonecutting, smelting, machining, fabrication | Industry | Construction, Defense, Economy | Requesters define need; Industry makes goods |
| Tailoring, apparel quality, textile processing | Industry | Welfare, Defense, Economy | Welfare requests clothing; Defense requests armor |
| Smithing, weapons, armor, shield belts | Industry | Defense, Economy | Defense owns combat requirement |
| Drug production, chemfuel, refinery outputs | Industry | Medical, Welfare, Economy, Defense | Policy can cross domains |
| Art, statues, quality furniture production | Industry | Welfare, Economy, Construction | Welfare requests beauty; Economy requests sale value |

### Defense And Emergencies

| Action family | Owner | Common requesters | Notes |
|---|---|---|---|
| Draft/undraft, combat positioning, retreat/hold advice | Defense | Mayor, Medical | Pawn allocation remains player/Labor in Suggest mode |
| Weapon readiness, loadout recommendations, armor readiness | Defense | Industry | Defense owns what is needed |
| Killbox, traps, turrets, defensive wall intent | Defense | Construction, Industry | Construction owns build feasibility |
| Firefighting, breach response, infestation response | Defense | Construction, Medical | Treat as emergency response |
| Prisoner combat risk and escape response | Defense | Welfare, Economy | Care/trade is not Defense unless threat is active |

### Welfare And Health

| Action family | Owner | Common requesters | Notes |
|---|---|---|---|
| Recreation, comfort, beauty, sleep quality, room impressiveness | Welfare | Construction, Industry | Welfare owns the pawn-need reason |
| Schedules, joy/work/sleep balance, mental-break prevention | Welfare | Medical, Defense | Suggest-only until Labor/Auto |
| Relationships, social fights, ideology mood pressure | Welfare | Mayor | Split later only if complexity justifies it |
| Triage, tending, disease monitoring, surgery, hospital readiness | Medical | Welfare, Construction, Industry | Split from Welfare because cadence/severity differ |
| Medicine stock, hospital beds, sterile room, vitals risk | Medical | Construction, Industry, Economy | Medical owns readiness |

### Strategy, Research, And Economy

| Action family | Owner | Common requesters | Notes |
|---|---|---|---|
| Research queue and tech path | Research | Mayor, Defense, Food, Industry, Medical | Mayor sets posture; Research owns queue mechanics |
| Trade offers, buying scarce resources, selling surplus | Economy | Food, Medical, Defense, Industry | Requester owns need; Economy owns trade decision |
| Caravan formation/provisioning purpose | Economy | Food, Defense, Medical | Food/Defense/Medical own sufficiency and risk inputs |
| Wealth pressure, stockpile liquidation, trade-good strategy | Economy | Mayor, Industry | Mayor sets posture; Economy manages wealth |
| Colony-wide goals and priority ordering | Mayor | All ministers | Mayor owns strategy, not routine operation |
| Conflicting flags and same-tick priority conflicts | Chief of Staff | All ministers | CoS arbitrates framing/priority |

### Hard Cases

| Hard case | Provisional handling |
|---|---|
| Psychoid/smokeleaf/beer | Industry owns production; Welfare owns drug policy; Economy owns sale strategy |
| Devilstrand | Food comments on growing opportunity cost; Industry owns textile use; Economy owns sale value |
| Animals | Food owns slaughter pressure; Economy owns sale/trade; Defense owns combat animals; future Animals minister possible |
| Prisoners | Welfare owns living conditions; Medical owns health; Economy owns ransom/slavery/trade; Defense owns escape/riot risk |
| Ideology/rituals | Welfare owns mood pressure until rules/prompts become noisy |
| Multi-map/caravans | Economy owns purpose; Defense owns threat; Food/Medical own provisioning sufficiency; full multi-map support deferred |

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
| Food stockpile | Food | Co-located with freezer; Food knows food quantities and spoilage risk |
| Material / component stockpile | Construction | Co-located with workshops; Construction knows material flow and build queue |
| Ammo / weapon stockpile | Defense | Near killbox or armoury |
| Medicine stockpile | Medical | Near hospital; Medical tracks medical supply chain |
| Growing zone | Food | Placement, size, crop assignment |
| Dumping zone | Construction | Rock chunks, corpses, waste; base hygiene |
| Home zone | Mayor / CoS | Colony-wide; no minister claims it |
| Allowed zone | Mayor / CoS | Colony-wide; no minister claims it |

In Suggest mode, conflicting zone advice from two ministers surfaces as two
cards and the player resolves it. At Auto graduation, CoS must arbitrate
contested writes before RIMAPI commands are issued.

Layout efficiency belongs to Construction as an extension of its room/base
program, not a new minister.

---

## Open Questions

- [ ] How is refinement triggered: manual command, threshold, schedule, or a mix?
- [ ] Where are refinement session transcripts logged for audit?
- [ ] Should fixture generation be automated by a repo-local skill?
- [ ] Define the confidence threshold for any post-MVP auto-approve path.
