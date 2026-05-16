# Defense Minister - Minister Design

> **Living document.** See `AGENTS.md` for update rules.
> Defense is a future feeder advisor. This doc records durable scope and
> escalation boundaries, not prebuilt enums, HTN steps, or write commands.

---

## Domain

Defense owns threat response:

- Active raids, sieges, infestations, fires threatening key structures, and
  other emergencies.
- Combat posture, draft/retreat/hold recommendations, and tactical warnings.
- Fortification intent: killbox need, defensive wall intent, turret/trap need.
- Weapon and armor readiness as combat requirements.
- Post-threat repair/triage pressure routed to the relevant owner.

Construction owns building the fortifications. Industry owns producing weapons
or armor. Medical owns casualty treatment. Defense owns why those assets matter
for threat readiness.

---

## First Slice Shape

Defense should begin as rules-first `Suggest`-mode advice with tactical-alert
authority for truly urgent states.

Likely first advice areas:

- Active raid or infestation response.
- Breach or fire threatening critical structures.
- Missing basic defensive posture for colony size/wealth.
- Weapon readiness below the current threat level.
- Post-raid repair or medical pressure flagged to owners.

Exact advice types should be defined when the Defense implementation slice
starts.

---

## Briefing Direction

Defense briefing should answer:

- Is there an active threat?
- What is the threat type, approach, and known composition?
- Which colonists are combat-capable right now?
- What weapons, armor, turrets, traps, or chokepoints exist?
- Are walls/perimeter/killbox assets intact?
- What recent threats changed readiness expectations?
- What build or production requests should be sent to Construction/Industry?

Exact briefing fields belong in code/tests once Defense ships.

---

## Critical Flag Authority

Defense is the natural owner of Critical threat flags, but `Critical` should be
reserved for states that can immediately cost colonists or the colony:

- Active raid or violent threat.
- Active fire spreading toward key structures.
- Active infestation or breach inside the base.

All other Defense concerns should normally emit High or lower.

---

## Escalation Boundaries

Rules should handle obvious active-threat, breach, and readiness threshold
states. Escalate for:

- Unusual raid compositions.
- Mech clusters, sieges, and special threats.
- Tactical response trade-offs.
- Killbox/layout decisions.
- Weapon upgrade trade-offs.

---

## Resource Requests

Defense may request:

- Construction: walls, doors, barricades, traps, turret infrastructure.
- Industry: weapons, armor, ammo/modded combat supplies.
- Medical: treatment capacity after combat.
- Labor/player attention: urgent draft or positioning decisions in Suggest mode.

Requests remain advice in MVP; they do not issue RIMAPI writes by themselves.

---

## Success Metrics

- No avoidable colonist deaths from threats the colony was prepared to handle.
- Defensive posture is established before major threat escalation.
- Weapon/readiness gaps are visible before raids exploit them.
- Critical alerts are rare and trustworthy.
- Escalation rate falls as common threat patterns become rules.

---

## Open Questions / TODO

- [ ] Define first Defense advice types.
- [ ] Define weapon/readiness scoring from live data.
- [ ] Define how Defense requests fortification work from Construction.
- [ ] Decide retreat-vs-hold thresholds.
- [ ] Add DLC/modded threat handling only when scope requires it.
