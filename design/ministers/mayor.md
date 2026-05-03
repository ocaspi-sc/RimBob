# Mayor — Minister Design

> **Living document.** See `CLAUDE.md` for update rules.
> **MVP centerpiece (M1).** The Mayor's daily memo *is* the assisted-gameplay product in M1. Everything else feeds into it.

---

## Role

The Mayor is the player's chief advisor. Once per in-game day, it synthesises a **daily memo** from the colony's full briefing — what's happening, what's important, and what the player should consider doing next.

The Mayor does not micromanage. It writes one well-justified memo per day, plus (post-M5) acknowledges severity-gated tactical alerts from feeder ministers. It does not issue actions; it issues advice.

In `Suggest` mode (the only MVP mode) the player reads the memo and decides what to do. Future `Auto` graduations are per-feeder-minister and per-advice-type, never to the Mayor itself.

---

## Escalation rate target

~95%. Almost everything the Mayor decides is judgment. The thin rules layer handles only mechanical posture-shift moments that can short-circuit the LLM call.

---

## Rules layer (thin)

Mayor rules are primarily **prompt-shaping triggers** — conditions that pre-fill posture context for the LLM call (the LLM still writes the memo).

Known rules:
- `winter_prep_lens`: if `DaysToWinter < 20` → bias the memo toward winter prep framing
- `food_crisis_lens`: if `DaysOfFoodRemaining < 7` → force memo lead with food security at Critical severity
- `year_two_transition_lens`: if `Year == 2 AND Q == 1` → include endgame-objective-selection prompt in the memo body
- `quiet_day_short_memo`: if no Medium-or-higher flags fired in the last 24h → request a deliberately short "all clear" memo

Everything else escalates to the LLM with the full daily digest and lets it write.

---

## LLM output schema

The Mayor's LLM call produces exactly one `AdviceItem` per day (the daily memo), plus optionally additional severity-tagged tactical alerts when feeder flags warrant them (M5+).

```jsonc
{
  "advice": [
    {
      "advice_type": "daily_digest",       // or "tactical_alert" post-M5
      "severity":    "Medium",
      "title":       "End of Day 12 — Food window closing, defense slack",
      "body":        "Markdown body, 2-6 short paragraphs. Lead with the most consequential observation. Group by domain only when it helps the reader.",
      "rationale":   "Why these were the most important items today.",
      "suggested_actions": [
        { "kind": "designate_zone",  "what": "growing zone, ~8x8, fertile soil south of kitchen" },
        { "kind": "build",           "what": "two more sandbag sections covering the east approach" },
        { "kind": "set_priority",    "what": "raise Plants priority on Hannah and Ben" }
      ],
      "citations": ["strategic-plan-y1-y2.md#fall-checklist"],
      "expires_in_in_game_hours": 24
    }
  ],
  "flags": [],
  "notes": "free-form rationale, logged not parsed"
}
```

`advice_type` is a closed enum for the Mayor: `daily_digest | tactical_alert | strategic_pivot`.

`suggested_actions[].kind` is a closed enum across all ministers — see [`advice.md`](advice.md) for the catalogue. The Mayor mostly emits human-readable suggestions; future `Auto` graduations will wire each `kind` to an HTN primitive.

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

---

## Key constraints

- Mayor does not name colonists in `suggested_actions` (`"what"` text is allowed to mention names; structured kinds use roles).
- Mayor does not specify blueprints, rooms, or exact research targets — leaves headroom for the player.
- Mayor does not emit Critical or High flags except for extraordinary strategic pivots.
- Mayor does not arbitrate same-tick conflicts (CoS's job).
- One `daily_digest` advice item per in-game day. Tactical alerts (post-M5) are the only exception.
- Daily digest expires at the next in-game day's tick — superseded, not deleted (history visible in dashboard).

---

## System prompt

See [`RimAI.LLM/prompts/mayor.system.md`](../../LLM/prompts/mayor.system.md). Update when this doc changes the memo schema or constraints.

---

## Success metrics

- Player Accept rate > 50% on `daily_digest` advice items by M6.
- Player explicit Dismiss with note < 20% (silent ignore is acceptable).
- Modify-then-accept patterns drive at least one rule promotion per quadrum (post-M6).
- Memo length stays in the "scannable in 30s" range — body word count median 80–250.

---

## Open questions / TODO

- [ ] Define `advice_type` enum exactly: just `daily_digest | tactical_alert | strategic_pivot`, or finer?
- [ ] Tactical-alert cadence: per-flag, or batched within a short window? (M5 question.)
- [ ] How does the Mayor handle DLC-specific advice (Royalty, Biotech)?
- [ ] Cross-session memory of past colonies — deferred.
- [ ] Define the CoS digest format precisely (M2/M3).
- [ ] Tone calibration: do we let the player pick a Mayor "voice" (terse / verbose / formal)?
