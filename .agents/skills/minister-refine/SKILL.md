---
name: minister-refine
description: Refine a RimAI minister's quality by reviewing decision logs, historic replay corpus records, future minister-owned Pushbacks, rules, system prompts, briefings, and fixtures. Use when asked to refine or improve a minister, review minister quality, inspect decision logs, compare before/after outputs, promote repeated patterns into rules, improve a minister prompt, or check whether a briefing supports better advice.
---

# Minister Refine

Refine one RimAI minister from evidence. Default to an evidence-backed proposal; edit `Rules.cs`, prompts, briefing code, fixtures, or docs only when the user explicitly asks to apply the proposal.

## Workflow

1. Ground in repo truth.
   - Read `Docs/DESIGN.md`, `HumanTodo.md`, `Docs/design/ministers.md`, `Docs/design/evaluation.md`, and the target minister doc under `Docs/design/ministers/`.
   - Resolve current artifacts: `Src/Ministers/<Minister>/`, `Src/LlmGateway/prompts/<minister>.system.md`, briefing record in `Src/Common/Briefings/`, derivation in `Src/StateStore/Derivations/`, tests and fixtures under `Src/Tests/<Minister>/`.
   - Also check future pushbacks at `Src/Cabinet/<Minister>/Pushbacks/*.jsonl`. If absent, continue from logs and say Pushbacks are not wired yet.

2. Summarize logs.
   - Run the helper:
     ```powershell
     py .agents\skills\minister-refine\scripts\summarize_decision_log.py --repo . --minister Food --days 7
     ```
   - If `py` is unavailable, use the working Python launcher for the machine.
   - Inspect raw `logs/decisions-*.jsonl` lines when the summary points to an important cluster or parsing uncertainty.
   - Check whether records are replayable: briefing payload or ref, trigger/context, active flags/agenda context, prompt/raw LLM output when relevant, normalized output, feedback/Pushback, and outcome. If fields are missing, classify that as a `Logging gap`.

3. Build a replay set.
   - Prefer a historic corpus sampled from decision logs and Pushbacks that covers the repeated pattern plus nearby non-target cases.
   - Fall back to fixtures only when historic records are not replayable yet, and say that the comparison is fixture-only.
   - Keep the same input corpus for before/after. Do not judge an improvement from a single hand-picked example.

4. Classify each finding.
   - `Rules`: repeated deterministic state that existing briefing fields can decide.
   - `System prompt`: LLM had enough facts but violated schema, scope, tone, action kinds, resource request shape, or suggest-only constraints.
   - `Briefing`: advice is blocked by missing, ambiguous, stale, or over-raw state. Prefer compact derived facts over raw dumps.
   - `Fixtures/tests`: a repeated case lacks a regression fixture or expected assertion.
   - `Logging gap`: evidence is too thin to reproduce a decision, trace, prompt, briefing ref, or outcome.

5. Choose the smallest useful refinement.
   - Promote to rules only when the briefing already carries every fact needed by the condition.
   - Prefer prompt changes when the model is making a contract mistake despite adequate context.
   - Prefer briefing changes when the minister cannot know the right answer from current fields.
   - Treat quota, DNS, socket, and sandbox/network failures as environment or logging findings, not minister-quality failures.
   - Avoid broad redesign. One minister, one or two high-confidence improvements per pass.

6. Compare before/after outputs.
   - For rule or prompt proposals, replay the before and after behavior on the same historic corpus when possible.
   - Compare escalation-vs-rule path, advice type, priority, resource requests, suggested actions, flags, schema validity, and unchanged non-target cases.
   - A good proposal should improve the target cluster without making stable historic cases noisier or less concrete.

7. Report in proposal-first format.
   - Evidence: concrete log counts, traces, pushback clusters, fixture names, and relevant files.
   - Diagnosis: classify by `Rules`, `System prompt`, `Briefing`, `Fixtures/tests`, or `Logging gap`.
   - Proposal: exact intended behavior change and why it is justified.
   - Replay result: historic corpus size, before/after output changes, regressions, and any fields missing from the corpus.
   - Files to change if applied: name only the needed files.
   - Verification: targeted fixture/tests first; full `dotnet test Src/RimAI.sln` if code is edited.

## Apply Mode

Only apply changes when the user explicitly asks to implement, apply, fix, or patch the proposal.

When applying:
- Keep edits scoped to the target minister and directly required shared contracts.
- Update design docs in the same turn if the design changes.
- Add or update fixtures before or alongside rule changes.
- Run before/after replay on the historic corpus when records are replayable; otherwise add the logging/corpus gap to the result.
- Run the target test slice, then broader tests when shared behavior changed.
- Do not promote rules from Pushbacks without human approval in v1.
