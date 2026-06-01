# RimBob - Evaluation and Iteration

> **Living document.** See `AGENTS.md` for update rules.
> This doc defines the Oracle improvement loop and approval gates. Exact log fields,
> replay record schemas, fixture JSON, and helper scripts live in source, tests,
> and repo-local skills.

---

## Core Idea

The Oracle improves minister rules and supporting artifacts from evidence. The loop connects:

- what the minister saw,
- which path it took,
- what advice it emitted,
- how the player responded,
- what happened later,
- and what a proposed change would have done to real prior turns.

The system gets smarter when the Oracle replays and reviews actual behavior, not when ministers self-modify or promote guesses.

---

## Decision Logging

Every rules decision or LLM escalation should write structured audit data.

Design requirements:

- Identify minister, timing, trigger, and path (`rules` or `llm`).
- Preserve or recover the briefing context used for the decision.
- Record rule fired or escalation reason.
- Record emitted advice and flags.
- Record LLM prompt/output metadata when an LLM path is used.
- Attach feedback, pushback, and observed outcome when those exist.
- Keep enough data to explain why advice appeared and to seed replay records.
- Every minister LLM attempt, including retries, validation rejects, provider
  failures, and accepted manual substitutes, should have a replay record.

Exact JSON fields are implementation details. Use the current log/replay writer
and tests as the source of truth.

---

## Outcome Attribution

Outcome attribution is approximate. Colonies are complex and many events
intervene, but broad direction over a reasonable window is still useful signal.

When an outcome window closes, the evaluator should compare relevant before/after
briefing metrics and classify direction as improved, stable, degraded, or
unusable/confounded. Confounded cases should reduce confidence rather than force
a false pass/fail.

---

## Historic Replay Corpus

Refinement must compare proposed changes against historical minister inputs
before promotion. Fixtures are the small curated regression net; replay corpus is
the broader "what would this have done to real prior turns?" check.

Each replayable record should preserve enough data to rerun the minister path
offline:

- minister and trigger/context,
- briefing payload or recoverable reference,
- active flags and agenda context,
- RAG citation ids when used,
- prompt/system-prompt version or hash,
- raw LLM output when the path was LLM,
- normalized advice and flags,
- feedback/pushback,
- observed outcome when available.

If a candidate cannot be replayed because fields are missing, the correct output
is a logging/corpus gap, not a rule promotion.

Before/after reports should include corpus size, target-cluster size, unchanged
non-target count, path changes, concern/priority changes,
resource/action/flag diffs, schema validity, and missing-field confidence gaps.

---

## Refinement Loops

### Deduplication

Before the Oracle surfaces a candidate rule or prompt change, compare it against existing rules and previously rejected proposals. Suppress near-duplicates so the Oracle loop does not keep proposing changes already covered or rejected.

Exact similarity metrics and thresholds should live in refinement tooling and be
calibrated from use.

### Loop 1: Rule Promotion

Trigger: repeated escalations with the same reason that produce consistent,
successful output.

Process:

1. Cluster similar escalations.
2. Draft a rule that would have handled the cluster.
3. Replay current and candidate behavior over historical records.
4. Run fixtures.
5. Surface diff, replay results, fixture results, and rationale for approval.
6. Promote only after approval.

Goal: escalation rate drops for patterns rules can handle safely.

### Loop 2: Rule Regression

Trigger: a rule fires repeatedly and outcomes degrade or pushbacks identify a
recurring failure.

The candidate should improve degraded examples without changing stable prior
outputs unless the diff is intentional and explained.

### Loop 3: Prompt / RAG Iteration

Trigger: LLM escalation produced degraded advice, repeated pushback, or poor use
of retrieved guide context.

Prompt, briefing, and RAG changes use the same replay rule: compare outputs
before and after on historical prompts/briefings, not only on the single
offending prompt.

---

## Fixture Testing

Fixtures prevent regressions and provide known-good expected outputs for common
or important scenarios. They complement replay history with curated edge cases.

Fixture shape and location are test contracts. Do not duplicate their JSON schema
in this doc; check `Src/Tests/` and the relevant minister fixture tests.

A rule change that breaks a fixture is a regression unless the fixture is
intentionally updated with a clear rationale.

---

## Approval Gates

### v1: Human In The Loop

All Oracle refinement loops require human approval before rule, prompt, briefing, fixture, or retrieval-profile changes are written. The Oracle session surfaces:

- The pattern and example evidence.
- The proposed change.
- Replay before/after results.
- Fixture results.
- The Oracle's rationale.

Rejection notes are logged so future runs do not re-propose the same change.

### Post-MVP: Auto Approval

Auto-approval is future, per-minister, and opt-in only after the fixture suite
and replay corpus are mature enough to catch regressions. Thresholds and scope
are not designed up front.

---

## Tooling: Claude Code Escalation

Oracle refinement may hand code-shaped work to Claude Code by writing a prompt file under `.plans/` and invoking it against the repo.

This creates a deliberate two-tier model:

- In-process Gemini handles fast, schema-bound play decisions.
- Claude Code handles slower code-shaped refinement work such as drafting rule diffs, generating fixtures, and reviewing escalation clusters.

The handoff stays simple: prompt file in, repo diff or generated artifact out.
No bespoke agent SDK integration is part of MVP.

Repo-local skills worth keeping:

| Skill | Purpose |
|---|---|
| `minister-refine` | Proposal-first refinement from logs, replay corpus, prompts, rules, briefings, and fixtures |
| `fixture-gen` | Generate candidate fixtures from scenarios or pushbacks |
| `rule-promote` | Draft candidate rule changes from a validated cluster |
| `briefing-check` | Validate a briefing against current code/tests and flag derivation gaps |

---

## Open Questions

- [ ] How is Oracle refinement triggered: manual command, threshold, schedule, or a mix?
- [ ] Where are refinement-session transcripts stored?
- [ ] What retention policy should the durable replay corpus use?
- [ ] Should outcome attribution be per-advice item or per-minister cycle?
- [ ] How should confounded outcomes affect confidence?
