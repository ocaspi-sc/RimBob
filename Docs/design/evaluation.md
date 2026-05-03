# RimAI — Evaluation and Iteration

> **Living document.** See `CLAUDE.md` for update rules.

---

## Core idea

Each minister improves its own rules. Not a separate process, not a developer reviewing logs — the minister itself, in refinement, closing the loop between "what I decided" and "what happened."

The system gets smarter the more it plays.

---

## Decision logging

Every decision — whether from the rules layer or LLM escalation — writes one record to a structured log.

```json
{
  "minister": "Agriculture",
  "tick": "Y1Q3D7H14",
  "tick_sequence": 4820,
  "trigger": "briefing_change | flag | heartbeat",
  "briefing_version": 142,
  "briefing_hash": "sha256:...",
  "briefing_summary": {
    "daysOfFood": 22,
    "season": "Fall",
    "matureCropTiles": 0,
    "activeThreats": false
  },
  "path": "rules | llm",
  "rule_fired": "expand_zone_when_food_low",
  "escalation_reason": null,
  "decision": {
    "advice": [{ "advice_type": "expand_growing_capacity", "severity": "Medium" }],
    "flags": []
  },
  "rationale": "DaysOfFoodRemaining(22) < threshold(30), no mature crops, season allows planting",
  "llm_prompt_hash": null,
  "outcome_window_ticks": 2880,
  "observed_outcome": null
}
```

`observed_outcome` is filled by a background evaluator after `outcome_window_ticks` have elapsed:

```json
"observed_outcome": {
  "metric": "DaysOfFoodRemaining",
  "before": 22,
  "after": 31,
  "direction": "improved",
  "colonist_losses": 0
}
```

---

## Post-hoc outcome attribution

A background process runs every in-game day. For each decision where `outcome_window_ticks` has elapsed and `observed_outcome` is null:

1. Look up the relevant briefing metric(s) at the decision's outcome window.
2. Compute direction: improved / stable / degraded.
3. Write `observed_outcome` into the log.

Attribution is approximate — a colony is a complex system and many things happen simultaneously. But gross direction over a reasonable window (e.g., did food go up or down after Agriculture decided to expand zones?) is signal enough for refinement.

---

## Refinement — the three loops

### Deduplication — suppressing redundant proposals

Before surfacing any candidate rule or prompt change for approval, compute the **edit distance** (Levenshtein or token-overlap) between the candidate and every rule already in `Rules.cs` and every previously-rejected proposal in the refinement log. Suppress if similarity exceeds a threshold (e.g., >80% token overlap). This prevents the improve loop from re-proposing near-identical changes that were already rejected or are already covered.

---

### Loop 1: Rule promotion

**Trigger:** N escalations with the same `escalation_reason` that produced consistent LLM output.

**Process:**
1. Minister reads escalation log, groups by reason.
2. For clusters of ≥5 with consistent goal output: draft a rule that would have matched.
3. Write candidate `Rules.cs` change.
4. Run fixture suite.
5. Surface for approval (human in v1).
6. On approval: update `Rules.cs`, log promotion event.

**Goal:** over time, escalation rate for covered patterns drops to near zero.

### Loop 2: Rule regression

**Trigger:** a rule fires repeatedly and `observed_outcome` is `degraded`.

**Process:**
1. Minister reads decision log, groups by `rule_fired` × outcome.
2. For rules with degraded outcome rate > 20% over 10+ firings: surface as a regression candidate.
3. Minister proposes either: modify the rule's precondition, delete the rule, or replace with escalation.
4. Approval gate → `Rules.cs` change.

### Loop 3: Prompt / RAG iteration

**Trigger:** LLM escalation produced a decision that was later `degraded`.

**Process:**
1. Identify the prompt (hash lookup) and the briefing context.
2. Replay with a modified prompt or different RAG retrieval.
3. Compare outputs.
4. If better: update the system prompt or retrieval profile.
5. Approval gate → prompt file change.

---

## Fixture testing

### Purpose

- Prevent regressions when rules change.
- Provide known-good expected outputs for common scenarios.
- Used by refinement to validate proposed changes.

### Format

```json
{
  "id": "food-shortage-fall-day40",
  "description": "Food shortage on day 40, late fall, no mature crops, trader 3 days away",
  "briefing": {
    "DaysOfFoodRemaining": 14,
    "Season": "Fall",
    "DaysToWinter": 8,
    "MatureCropTiles": 0,
    "TraderApproachingInDays": 3,
    "ActiveThreats": false
  },
  "expected": {
    "should_escalate": false,
    "top_goal": "food_security",
    "flag_severity_min": "High",
    "notes": "Rules should handle this without LLM. Emergency trade flag is secondary."
  }
}
```

### Location

```
Tests/
└── <MinisterName>/
    └── Fixtures/
        ├── scenario-name.json
        └── ...
```

### CI integration

Fixture suite runs on every push. A rule change that causes any fixture to fail is a regression — the PR does not merge.

---

## Approval gates

### v1 (human in the loop)

All three loops require human approval before any `Rules.cs`, prompt, or retrieval-profile change is written. The improve-mode session surfaces:

- The pattern (N examples from log)
- The proposed change (diff)
- Fixture results (before/after pass rate)
- The minister's rationale

Human approves or rejects with a note. Rejection note is written to the refinement-session log so future improve runs don't re-propose the same change.

### Post-MVP (auto-approve)

Auto-approve is available per-minister when:
- Fixture suite covers ≥50 scenarios for that minister.
- Proposed change improves or maintains fixture pass rate.
- Change is in the rules layer only (not prompt or RAG changes — those stay human-gated longer).
- No fixture that was previously passing is now failing.

Requires explicit opt-in per minister. Not the default.

---

## Claude skills to build

These skills would make refinement sessions practical:

| Skill | Purpose |
|---|---|
| `minister-review` | Run refinement for a minister: read log, identify patterns, surface candidates |
| `fixture-gen` | Given a scenario description, generate a fixture JSON |
| `rule-promote` | Given a log cluster, generate a candidate Rules.cs change |
| `briefing-check` | Validate a briefing against its schema; flag derivation errors |

---

## Open questions

- [ ] How is refinement triggered? Manual slash command? After N escalations threshold? Nightly cron?
- [ ] Where are refinement-session transcripts stored? (Separate log file per session?)
- [ ] Should outcome attribution be per-goal or per-minister? (Did the specific goal succeed, or did the minister have a good day overall?)
- [ ] How do we handle decisions where outcome is confounded by external events (e.g., a random disease outbreak masks a good food decision)?
