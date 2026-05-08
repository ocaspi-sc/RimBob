# Mayor — Minister Design

> **Living document.** See `CLAUDE.md` for update rules.
> **MVP centerpiece (M1).** The Mayor's Agenda *is* the assisted-gameplay product in M1. Everything else feeds into it.

---

## Role

The Mayor is the player's chief advisor. Once per in-game day, it updates the **Agenda** — a living planning document containing ranked short-term priorities and long-term strategic goals — and writes a brief `update_notes` narrative explaining what changed and why.

The Mayor does not micromanage. It maintains a strategic plan, directs the cabinet via `minister_direction` entries in the Agenda, and (post-M5) acknowledges severity-gated tactical alerts from feeder ministers. It does not issue actions; it issues advice.

In `Suggest` mode (the only MVP mode) the player reads the Agenda and decides what to act on. Future `Auto` graduations are per-feeder-minister and per-advice-type, never to the Mayor itself.

→ See [`design/agenda.md`](../agenda.md) for the full Agenda schema, dashboard layout, and API contract.

---

## Escalation rate target

~95%. Almost everything the Mayor decides is judgment. The thin rules layer handles only mechanical posture-shift moments that can short-circuit the LLM call.

---

## Rules layer (thin)

Mayor rules are primarily **prompt-shaping triggers** — conditions that pre-fill posture context for the LLM call (the LLM still writes the Agenda update).

Known rules:
- `winter_prep_lens`: if `DaysToWinter < 20` → bias toward winter prep framing; ensure a winter bullet is in `short_term`
- `food_crisis_lens`: if `DaysOfFoodRemaining < 7` → force food bullet to position 1 in `short_term`
- `year_two_transition_lens`: if `Year == 2 AND Q == 1` → prompt the Mayor to add an endgame-objective bullet to `long_term`
- `quiet_day_short_notes`: if no Medium-or-higher flags fired in the last 24h → request brief `update_notes` ("all clear" tone)

Everything else escalates to the LLM with the full daily digest and lets it write.

---

## LLM output schema

The Mayor's LLM call at turn-end produces a **complete new `MayorAgenda`** — not a diff. The server stores the previous version in history and broadcasts the new one. Short-term bullets are free-text; the Mayor reuses IDs for carried-forward items and assigns new IDs for genuinely new ones. The LLM also optionally emits tactical-alert `AdviceItem`s when feeder flags warrant them (M5+).

### Agenda update (MVP)

```jsonc
{
  "agenda": {
    "posture": {
      "economic": "consolidation",
      "military": "defensive",
      "summary":  "Hold wealth, shore up food before winter."
    },
    "state_of_the_union": "Six colonists, all healthy; mood steady at 72%. Food covers 18 days against a winter in 20. Wealth ~85k and tracking, not racing. Power comfortable, freezer running, east wall has a known gap. Research mid-microelectronics. Stable with one time-pressure (food vs winter) and one quiet liability (the wall).",
    "update_notes": "Winter arrives in ~20 days. Moved food to top. East wall gap still needs patching.",
    "short_term": [
      { "id": "st_1", "text": "Establish a second growing zone before winter — food covers 18 days, winter in ~20.", "status": "active" },
      { "id": "st_2", "text": "Patch the east-wall gap before the next raid window.", "status": "active" }
    ],
    "long_term": [
      { "id": "lt_1", "text": "Complete winter prep (food, heat, schedules) before day 30.", "status": "active" },
      { "id": "lt_2", "text": "Base expansion deferred to year 2.", "status": "deferred" }
    ],
    "minister_direction": {}   // empty in M1; populated M3+
  },
  "flags": [],
  "notes": "free-form rationale, logged not parsed"
}
```

### Tactical alerts (M5+)

Post-M5, extraordinary severity spikes produce a separate `AdviceItem` with `advice_type: "tactical_alert"`. These appear in the dashboard's **Alerts** tab alongside the Agenda. The Mayor's `advice_type` enum: `tactical_alert | strategic_pivot`.

---

## Inputs (daily digest)

The Mayor receives once per in-game day:
- **Date**: quadrum, day, year.
- **State summary**: colonist count, mood distribution, days of food, wealth, wealth velocity, defense tier.
- **CoS digest**: batched Medium flags from the last 24h, ministers' top advice candidates, recent incidents.
- **Trend windows**: mood, wealth, food, threat-points over 7 in-game days.
- **Recent player feedback**: aggregate Accept/Dismiss/Modify rates from the last 7 memos so the Mayor can adapt tone and granularity.
- **Retrieved guide passages** for current quadrum (M4+).

Note: in M1, the only feeder is the colony-wide briefing; CoS digest, trend windows, and feedback aggregation come online M2/M3/M5.

The M1 colony-wide briefing schema is implemented as `RimAI.Core.Briefings.MayorBriefing` (see [`Src/Common/Briefings/MayorBriefing.cs`](../../../Src/Common/Briefings/MayorBriefing.cs)). It targets ≤ 800 tokens serialized and is derived from `ColonyState` via `MayorBriefingDerivation`.

---

## Key constraints

- Mayor does not micromanage individual colonists in bullet text — names may appear for context but not as assignments.
- Mayor does not specify blueprints, rooms, or exact research targets — leaves headroom for the player.
- Mayor does not emit Critical or High flags except for extraordinary strategic pivots.
- Mayor does not arbitrate same-tick conflicts (CoS's job).
- One Agenda update per in-game day. Tactical alerts (post-M5) are the only exception to one-output-per-day.
- Short-term priorities are capped at 5. The Mayor is forced to rank and cut rather than surfacing everything.
- Agenda history is retained for 30 in-game days; older versions move to the decision log.
- The Mayor is the only writer of the Agenda. Ministers read it; they never write to it.

---

## Strategic frame

The Mayor's prompt carries a six-line summary; the canonical reasoning lives here. Treat all of this as defaults the briefing can override.

### The two phases

- **Year 1 — Survival.** Don't die. Build the smallest viable engine. Wealth is the throttle. Stone everything by Q2. Stockpile and harden by Q3. Survive winter. End with: ≥4 colonists, stone base, killbox, freezer, multi-analyzer.
- **Year 2 — Engine.** Specialize. Open the trade economy. Cross the chain-shotgun threshold. By end Y2Q3, defense tier must match wealth velocity or you halt expansion. By end Y2Q4, choose an endgame trajectory.

### The wealth-velocity rule

Raid points scale with wealth and colony age. Watch *velocity* (wealth-per-quadrum), not absolute wealth. If defense tier is not at parity with current velocity, set posture to `consolidation` or `survival` and add a "halt expansion" bullet until parity is reached. This is the single most common reason colonies fail in year 2.

### Wealth-free power

Three things raise effective colony capability without raising raid points: research progress, faction goodwill, and consumed goods. Prefer these sinks over stockpiles, decoration, or excess apparel.

### The chain-shotgun threshold

Once every shooter has a chain shotgun or better, stone walls, and a functional killbox, wealth concerns relax sharply. Crossing this threshold is a posture-shift moment — it is the boundary between reactive play (Year 1) and proactive play (Year 2+).

### Posture-shift moments

Pivot deliberately, not gradually, on these triggers:

1. First permanent shelter complete → end "land safely," begin "permanence."
2. Freezer + 60-day food stockpile → end "plant or starve," begin "harden for winter."
3. Chain-shotgun threshold crossed → end "reactive," begin "specialize."
4. First successful caravan → "diplomacy on."
5. Wealth velocity > defense tier → "wealth wall," halt expansion.
6. End of Y2Q4 → "choose endgame."

When a posture-shift moment fires, reflect it in `posture.summary` and in `update_notes`.

---

## System prompt

See [`Src/LlmGateway/prompts/mayor.system.md`](../../../Src/LlmGateway/prompts/mayor.system.md). It carries the schema, briefing-reading rules, and a short summary of the strategic frame above. Update when this doc changes the memo schema or constraints.

---

## Success metrics

- Player Accept rate > 50% on short-term Agenda bullets by M6.
- Player explicit Dismiss with note < 20% (silent ignore is acceptable).
- Modify-then-accept patterns drive at least one rule promotion per quadrum (post-M6).
- `state_of_the_union` reads like a real status briefing — concrete numbers, no filler. ~100–200 words.
- `update_notes` stays scannable in 10–15s — 50–100 words.

---

## Open questions / TODO

- [ ] `advice_type` enum for tactical alerts: just `tactical_alert | strategic_pivot`, or finer?
- [ ] Tactical-alert cadence: per-flag, or batched within a short window? (M5 question.)
- [ ] How does the Mayor handle DLC-specific advice (Royalty, Biotech)?
- [ ] Cross-session memory of past colonies — deferred.
- [ ] Define the CoS digest format precisely (M2/M3).
- [ ] Tone calibration: do we let the player pick a Mayor "voice" (terse / verbose / formal)?
- [ ] Short-term bullet cap: 5 is the hypothesis — confirm before encoding in the system prompt.
