# Minister of Welfare - Minister Design

> **Living document.** See `AGENTS.md` for update rules.
> Welfare is a future feeder advisor. This doc records durable scope and open
> boundaries, not final enum values or implementation rules.

---

## Domain

Welfare keeps the minister name and owns the **Mood & Needs** domain:

- Mood and break risk as explained by needs, thoughts, recreation, comfort,
  beauty, sleep, and schedules.
- Relationship and social-conflict pressure when it creates mood risk.
- Guest/visitor comfort, lodging quality, and hospitality social pressure when
  hosting affects mood, relationship pressure, or break risk.
- Ideology mood pressure until it justifies a split.
- Apparel warmth/comfort as mood-and-needs pressure, with Industry owning
  production.

Medical is now treated as its own subsystem in the cabinet model, though Welfare
and Medical will overlap on mood effects from pain, illness, hospital quality,
and care access. Medical owns the treatment decision; Welfare owns the mood
impact and break-risk framing. Reconcile the final rollout order before
implementation.

Chef owns the nutrition chain. Welfare can flag hunger, bad meal mood, or
nutrient-paste pressure as Mood & Needs evidence, but requests Chef when the
actual fix is meals, crops, hunting, cooking, or storage.

Trade/Economy is no longer a Welfare sub-block in the target cabinet. Economy
owns trade and wealth once scheduled.

Guest/visitor stewardship defaults to Welfare for comfort, lodging quality, and
social pressure. Economy owns guests as trade, caravan, goodwill, or diplomacy
opportunities; Construction owns the physical guest-room work; Defense owns
active threat response.

---

## First Slice Shape

Welfare should begin as rules-first `Suggest`-mode advice.

Likely first advice areas:

- Immediate or elevated mental-break risk.
- The need or thought most responsible for the current mood risk.
- Recreation coverage gaps.
- Schedule problems that are visible and actionable.
- Comfort/beauty/sleep issues that a concrete build or policy can address.
- Apparel warmth risk when live data supports it.
- Social conflict or ideology pressure only when it creates clear advice.

Exact concerns should be defined when Welfare implementation starts.

---

## Available Source Signals

The current source-data slice does not implement Welfare advice, rules, LLM
triggers, or Apply actions. It only makes source data visible through
`GET /api/briefings/welfare/latest` and the dashboard's Welfare > Briefing view.

Available signals:

- Mood distribution from live colonist mood.
- Per-pawn wellbeing need levels: sleep, comfort, beauty, joy, fresh air, and
  drugs desire where the game exposes it.
- Active mood thoughts from the RimBob RIMAPI fork:
  `def_name`, `label`, `mood_offset`, `stage_index`.
- Room evidence from `/api/v1/map/rooms`: role, temperature, cell count,
  prison flag, doorway/open-roof/map-edge signals, contained bed ids, and
  room stats for impressiveness, beauty, cleanliness, space, and wealth.

Snapshot schema changes for these signals use no compat code; wipe-and-regen
on upgrade.

---

## Briefing Direction

Welfare briefing should answer:

- Who is at break risk and why?
- Which need, thought, schedule, room, relationship, or ideology pressure is
  driving the risk?
- Are recreation and sleep needs being met?
- Are schedule settings creating avoidable mood loss?
- Are comfort, beauty, room quality, or apparel causing clear pressure?
- Are relationships/social events creating a current risk?
- Are guests/visitors creating hospitality, room-quality, or social pressure?
- Which requests should go to Construction, Industry, Medical, or Economy?

Exact fields belong in code/tests once Welfare ships.

---

## Escalation Boundaries

Rules should handle obvious mood thresholds and missing basic recreation/sleep
signals. Escalate for:

- Specific pawn intervention choices.
- Complex relationship/social conflicts.
- Apparel program trade-offs.
- Ideology-specific decisions.
- Welfare/Medical boundary cases.

---

## Resource Requests

Welfare may request:

- Chef: meals, meal quality, or nutrition-chain fixes when hunger or food mood
  is the driver.
- Construction: beds, recreation buildings, room improvements, comfort assets.
- Economy: visitor trade, caravan, goodwill, or diplomacy opportunities.
- Industry: apparel or beauty/comfort goods.
- Medical: pain, wounds, disease, or care issues affecting mood.
- Labor/player attention: schedule or policy changes in Suggest mode.

Requests remain advice in MVP; they do not issue RIMAPI writes by themselves.

---

## Success Metrics

- Break-risk warnings are timely and not noisy.
- Mood distribution stays mostly stable or improving.
- Recreation and sleep gaps are surfaced before crises.
- Welfare advice stays concrete rather than generic "improve mood" prose.
- Advice names the current need/thought driver instead of hiding behind
  generic wellbeing language.
- Escalation rate falls as common mood patterns become rules.

---

## Open Questions / TODO

- [ ] Resolve Welfare/Medical rollout order before implementation.
- [ ] Define first Welfare concerns.
- [ ] Decide how schedule advice interacts with deferred Labor/Auto.
- [ ] Define recreation/build priority from live data.
- [ ] Add ideology-specific handling only when DLC scope requires it.
- [ ] Decide whether prisoner living-conditions mood pressure stays Welfare or
      moves later.
