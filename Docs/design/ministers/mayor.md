# Mayor - Minister Design

> **Living document.** See `AGENTS.md` for update rules.
> **MVP centerpiece.** The Mayor's Agenda is the assisted-gameplay product in
> M1; later ministers feed into it.

---

## Role

The Mayor is the player's chief advisor. It updates the Agenda: a living plan
containing ranked short-term priorities, long-term goals, posture, state of the
union, and update notes.

The Mayor does not micromanage. It maintains strategic posture, directs the
cabinet through read-only `cabinet_direction`, and synthesizes important flags.
It issues advice, not actions.

In MVP, the player reads the Agenda and decides what to do. Future `Auto`
graduations are per feeder minister and action kind, not Mayor-wide autonomy.

See [`agenda.md`](../agenda.md).

---

## Escalation Posture

Almost everything the Mayor decides is judgment. The rules layer should remain
thin and mostly prompt-shaping: mechanical signals can force attention, but the
LLM writes the Agenda update.

Rules can shape:

- winter prep urgency,
- critical food pressure,
- year/phase transitions,
- quiet-day brevity,
- active flag priorities.

Everything else goes through the LLM with the current briefing, flags, trends,
and guide context. The previous Agenda is not prompt input.

---

## RAG

Mayor RAG runs before the LLM call when enabled and configured. It retrieves
guide passages relevant to the current briefing, agenda directives, and colony
phase. The prompt receives guide context; the final Agenda is server-stamped
with citation metadata for dashboard rendering.

Missing or disabled RAG should degrade cleanly. The Mayor still runs without
guide context.

See [`rag.md`](../rag.md).

---

## Output

The Mayor outputs a complete new Agenda, not a diff. The unified minister
output store owns version stamping, latest-snapshot persistence, reload on
startup, and broadcast via the advice stream. History belongs in the replay
corpus.

Agenda bullets remain free text. The Mayor should use stable semantic ids for
obvious recurring issues, but it no longer receives the prior Agenda as a
continuity input.

Post-M5 tactical alerts may become separate urgent advice items when feeder flag
severity warrants it. That path is not part of the current Mayor MVP.

Exact agenda records and prompt parser contracts live in source and tests.

---

## Inputs

The Mayor may receive:

- Date and phase context.
- Colony-wide briefing.
- Active relevant flags and CoS/Mayor-side arbitration output.
- Trend windows.
- Recent explicit feedback and Pushback summaries.
- Guide context passages.

Exact briefing fields live in `MayorBriefing` and its derivation tests.

---

## Key Constraints

- Do not micromanage individual colonists in bullet text.
- Do not specify exact blueprints, rooms, or research targets unless the design
  later gives Mayor that authority.
- Do not emit routine Critical/High flags; active tactical alert authority
  belongs with feeder/CoS flows.
- Mayor does not arbitrate same-tick conflicts once CoS exists.
- Mayor is the only writer of the Agenda.
- Ministers read Agenda direction; they never mutate it.
- Short-term priorities should be capped tightly enough to force ranking.

---

## Strategic Frame

The Mayor prompt carries only a compact summary. This doc is the canonical
reasoning source. Treat these as defaults that live state can override.

### Year 1 - Survival

Do not die. Build the smallest viable engine. Wealth is the throttle. Establish
stone, food, freezer, shelter, and basic defense before expanding.

### Year 2 - Engine

Specialize. Open trade. Defense must keep pace with wealth velocity. Choose an
endgame trajectory by the end of year 2 rather than drifting.

### Wealth Velocity

Raid pressure scales with wealth and colony age. Watch wealth velocity, not only
absolute wealth. If defense is behind wealth growth, posture should move toward
consolidation or survival until parity improves.

### Wealth-Light Capability

Research progress, faction goodwill, and consumed goods can raise capability
without the same stockpile-driven raid pressure. Prefer these sinks over idle
wealth when appropriate.

### Defense Threshold

Once shooters, stone walls, and a functional defensive setup are in place,
wealth pressure relaxes. Before then, expansion and hoarding should be questioned.

### Posture Shifts

The Mayor should pivot deliberately on major state changes: permanent shelter,
stable food/freezer, viable defense, first caravan/trade opening, wealth
outpacing defense, and endgame selection.

When posture shifts, reflect it in posture summary and update notes.

---

## System Prompt

The Mayor system prompt lives in source. Update it when this doc changes Agenda
constraints, strategic frame, or briefing-reading rules. Do not duplicate the
full prompt here.

---

## Success Metrics

- Player Accept rate on short-term Agenda bullets improves as feedback arrives.
- Explicit Dismiss/Pushback patterns shrink after refinement.
- Pushback patterns produce at least one useful rule, prompt, briefing, or RAG
  improvement by M6.
- `state_of_the_union` reads like a real status briefing: concrete, short, no
  filler.
- `update_notes` stay scannable in one quick read.

---

## Open Questions / TODO

- [ ] Tactical-alert priority/action granularity.
- [ ] Tactical-alert cadence: per flag or batched.
- [ ] DLC-specific Mayor advice boundaries.
- [ ] Cross-session memory of past colonies.
- [ ] CoS digest format once CoS is implemented.
- [ ] Optional Mayor voice/tone settings.
- [ ] Confirm short-term bullet cap before locking it into the prompt.
