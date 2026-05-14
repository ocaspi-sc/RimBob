# Minister of Welfare - Minister Design

> **Living document.** See `AGENTS.md` for update rules.
> Welfare is a future feeder advisor. This doc records durable scope and open
> boundaries, not final enum values or implementation rules.

---

## Domain

Welfare owns pawn wellbeing:

- Mood, break risk, recreation, comfort, beauty, sleep, and schedules.
- Relationship and social-conflict pressure.
- Ideology mood pressure until it justifies a split.
- Apparel warmth/comfort as wellbeing pressure, with Industry owning production.

Medical is now treated as its own subsystem in the cabinet model, though Welfare
and Medical will overlap on mood effects from pain, illness, hospital quality,
and care access. Reconcile the final rollout order before implementation.

Trade/Economy is no longer a Welfare sub-block in the target cabinet. Economy
owns trade and wealth once scheduled.

---

## First Slice Shape

Welfare should begin as rules-first, suggest-only advice.

Likely first advice areas:

- Immediate or elevated mental-break risk.
- Recreation coverage gaps.
- Schedule problems that are visible and actionable.
- Comfort/beauty/sleep issues that a concrete build or policy can address.
- Apparel warmth risk when live data supports it.
- Social conflict or ideology pressure only when it creates clear advice.

Exact advice types should be defined when Welfare implementation starts.

---

## Briefing Direction

Welfare briefing should answer:

- Who is at break risk and why?
- Are recreation and sleep needs being met?
- Are schedule settings creating avoidable mood loss?
- Are comfort, beauty, room quality, or apparel causing clear pressure?
- Are relationships/social events creating a current risk?
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

- Construction: beds, recreation buildings, room improvements, comfort assets.
- Industry: apparel or beauty/comfort goods.
- Medical: pain, wounds, disease, or care issues affecting mood.
- Labor/player attention: schedule or policy changes in Suggest mode.

Requests remain advice in MVP and do not issue RIMAPI writes.

---

## Success Metrics

- Break-risk warnings are timely and not noisy.
- Mood distribution stays mostly stable or improving.
- Recreation and sleep gaps are surfaced before crises.
- Welfare advice stays concrete rather than generic "improve mood" prose.
- Escalation rate falls as common mood patterns become rules.

---

## Open Questions / TODO

- [ ] Reconcile Welfare/Medical implementation order and boundary.
- [ ] Define first Welfare advice types.
- [ ] Decide how schedule advice interacts with deferred Labor/Auto.
- [ ] Define recreation/build priority from live data.
- [ ] Add ideology-specific handling only when DLC scope requires it.
- [ ] Decide whether prisoner wellbeing stays Welfare or moves later.
