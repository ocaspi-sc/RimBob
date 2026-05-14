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
  "minister": "Food",
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
    "advice": [{ "advice_type": "expand_growing_capacity", "priority": "medium" }],
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

Attribution is approximate — a colony is a complex system and many things happen simultaneously. But gross direction over a reasonable window (e.g., did food go up or down after Food decided to expand zones?) is signal enough for refinement.

---

## Historic replay corpus

Refinement must compare proposed changes against a corpus of historical minister inputs before promotion. Fixtures are the small curated regression net; the historic corpus is the broader "what would this change have done to real prior turns?" check.

Each replayable record should preserve enough data to rerun the minister path offline: minister, trigger/context, briefing payload or recoverable briefing ref, active flags and Agenda context, RAG citation ids when used, prompt/system-prompt version or hash, raw LLM output when the path was LLM, normalized advice/flags, feedback/Pushback, and observed outcome when available.

Current local runs already write `logs/decisions-YYYYMMDD.jsonl`, and some entries include full briefing JSON. That is useful seed material, but the durable target is a structured replay corpus rather than scraping dashboard state or in-memory raw-output stores. If a refinement cannot replay a candidate because fields are missing, the correct output is a logging/corpus gap, not a rule promotion.

Before/after comparison should report at least: corpus size, target-cluster size, unchanged non-target count, escalation-vs-rule path changes, advice type changes, priority changes, resource request/action diffs, flag diffs, schema validity, and any missing fields that reduced confidence.

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
4. Replay before/after outputs on the historic corpus for that minister.
5. Run fixture suite.
6. Surface for approval (human in v1).
7. On approval: update `Rules.cs`, log promotion event.

**Goal:** over time, escalation rate for covered patterns drops to near zero.

### Loop 2: Rule regression

**Trigger:** a rule fires repeatedly and `observed_outcome` is `degraded`.

Before any regression edit is approved, replay the current and candidate rule over the historical corpus. The candidate should improve degraded examples without changing stable prior outputs unless the diff is intentional and explained.

Minister groups decisions by `rule_fired` × outcome and surfaces rules with a high degraded rate as candidates to modify, delete, or convert to escalation. Approval gate → `Rules.cs` change.

### Loop 3: Prompt / RAG iteration

**Trigger:** LLM escalation produced a decision that was later `degraded`.

Prompt/RAG changes use the same corpus rule: compare outputs before and after on historical prompts/briefings, not only on the single offending prompt.

Replay the offending prompt with variations (system prompt edit, different RAG retrieval); if outputs improve, surface for approval. Approval gate → prompt file change.

---

## Fixture testing

### Purpose

- Prevent regressions when rules change.
- Provide known-good expected outputs for common scenarios.
- Used by refinement to validate proposed changes.
- Complement the historic corpus with hand-curated edge cases and expected outcomes.

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
- Historic replay results (before/after output diffs on real prior records)
- The minister's rationale

Human approves or rejects with a note. Rejection note is written to the refinement-session log so future improve runs don't re-propose the same change.

### Post-MVP (auto-approve)

Auto-approve is a future, per-minister opt-in once the fixture suite is mature enough to catch regressions. Specifics (thresholds, scope) are decided when the first minister is a candidate; not designed up front.

---

## Tooling: Claude Code escalation

Refinement (and any minister-side dev automation) can hand work off to **Claude Code** by writing a prompt to a file and invoking it as a subprocess. This is a deliberate second tier above the in-process LLM call:

- The in-process LLM (Gemini) handles fast, schema-bound judgment during play.
- Claude Code handles harder code-shaped work — drafting `Rules.cs` diffs, generating fixtures, reviewing escalation clusters — with a stronger model and full tool access (file read/write, test runs).

The handoff is intentionally simple: write the prompt to `.plans/<minister>-<task>.md`, run Claude Code against the repo, read back the diff or generated artefact. No bespoke agent SDK integration in MVP.

**Where this matters most:**
- **Bootstrapping initial automations.** First-pass `Rules.cs`, fixture seeds, and prompt templates for a new minister are easier to draft via Claude Code than to hand-write or in-process-LLM.
- **Refinement loops.** Promoting an escalation cluster into a rule is a code-edit task — exactly Claude Code's strength.
- **Dev workflows in general.** Any minister-authored tooling (skills below) can be a Claude Code prompt rather than a bespoke C# pipeline.

Skills worth building (each is a repo-local workflow skill, optionally with thin helper scripts):

| Skill | Purpose |
|---|---|
| `minister-refine` | Run proposal-first refinement for a minister: read logs and pushbacks, classify quality gaps, and surface rule, prompt, briefing, or fixture candidates |
| `fixture-gen` | Given a scenario description, generate a fixture JSON |
| `rule-promote` | Given a log cluster, generate a candidate Rules.cs change |
| `briefing-check` | Validate a briefing against its schema; flag derivation errors |

---

## Open questions

- [ ] How is refinement triggered? Manual slash command? After N escalations threshold? Nightly cron?
- [ ] Where are refinement-session transcripts stored? (Separate log file per session?)
- [ ] What retention policy and on-disk layout should the durable historic replay corpus use?
- [ ] Should outcome attribution be per-goal or per-minister? (Did the specific goal succeed, or did the minister have a good day overall?)
- [ ] How do we handle decisions where outcome is confounded by external events (e.g., a random disease outbreak masks a good food decision)?
