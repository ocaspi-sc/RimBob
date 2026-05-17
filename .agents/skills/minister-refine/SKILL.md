---
name: minister-refine
description: Refine a RimBob minister's quality by reviewing decision logs, historic replay corpus records, future minister-owned Pushbacks, rules, system prompts, briefings, and fixtures. Use when asked to refine or improve a minister, review minister quality, inspect decision logs, compare before/after outputs, promote repeated patterns into rules, improve a minister prompt, or check whether a briefing supports better advice.
---

# Minister Refine

Refine one RimBob minister from evidence. Default to an evidence-backed proposal; edit `Rules.cs`, prompts, briefing code, fixtures, or docs only when the user explicitly asks to apply the proposal.

## Workflow

1. Ground in repo truth.
   - Read `Docs/DESIGN.md`, `HumanTodo.md`, `Docs/design/ministers.md`, `Docs/design/evaluation.md`, and the target minister doc under `Docs/design/ministers/`.
   - Resolve current artifacts: `Src/Ministers/<Minister>/`, `Src/LlmGateway/prompts/<minister>.system.md`, briefing record in `Src/Common/Briefings/`, derivation in `Src/StateStore/Derivations/`, tests and fixtures under `Src/Tests/<Minister>/`.
   - Also check future pushbacks at `Src/Cabinet/<Minister>/Pushbacks/*.jsonl`. If absent, continue from logs and say Pushbacks are not wired yet.

2. Summarize logs.
   - Check durable replay records first at `logs/replay/<minister>-*.jsonl`; these are the preferred input for before/after comparisons.
   - Run the helper:
     ```powershell
     py .agents\skills\minister-refine\scripts\summarize_decision_log.py --repo . --minister Food --days 7
     ```
   - If `py` is unavailable, use the working Python launcher for the machine.
   - Inspect raw `logs/decisions-*.jsonl` lines when the summary points to an important cluster or parsing uncertainty.
   - Check whether records are replayable: briefing payload or ref, trigger/context, active flags/agenda context, prompt/raw LLM output when relevant, normalized output, feedback/Pushback, and outcome. If fields are missing, classify that as a `Logging gap`.

3. Build a replay set.
   - Prefer a historic corpus sampled from `logs/replay/<minister>-*.jsonl`, then enrich it from decision logs and Pushbacks when useful.
   - Fall back to decision logs when replay records are absent or incomplete. Fall back to fixtures only when historic records are not replayable yet, and say that the comparison is fixture-only.
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
   - When a minister logic change is applied, the final report must include a compact before/after advice diff. If historic replay records are not available or not replayable, use the focused fixture/regression input and label the diff `fixture-only`.

7. Run a Codex-vs-Gemini calibration pass when requested.
   - Use this path when the user explicitly asks for Codex/subagent comparison, manual Codex generation, or a provider-quality calibration pass.
   - Sample replayable historic LLM records from `logs/replay/<minister>-*.jsonl`; preserve the exact system prompt if captured, user prompt or briefing JSON, play-cycle context, flags, agenda/RAG context, recorded Gemini raw output, and recorded normalized output.
   - Trigger the `run-minister-using-subagent` workflow against the historic inputs: pass the exact captured prompt/context to the subagent, require JSON only, and keep the live minister prompt as the schema/style source of truth when the replay record lacks a prompt snapshot.
   - Do not overwrite live Raw LLM output or POST to `/api/ministers/{minister}/llm-output/manual` during historic calibration unless the user explicitly asks for live manual ingestion. Historic calibration should produce comparison artifacts first.
   - Compare Codex output to the recorded Gemini output on schema validity, suggest-only scope, use of briefing facts, step/action concreteness, priority, flags, compact structured strings, style warnings, and whether non-target concerns stayed quiet.
   - If Codex is clearly better, treat that as evidence about the minister harness, not as a provider-swap conclusion. Try to bring Gemini/the normal harness up to par by tightening the system prompt, improving briefing derived facts, adding deterministic rules, strengthening parser/style validation, or adding focused fixtures/tests.
   - Report the calibration as a small matrix: replay id/input source, Gemini issue, Codex behavior, likely harness gap, proposed harness change.

8. Report in proposal-first format.
   - Evidence: concrete log counts, traces, pushback clusters, fixture names, and relevant files.
   - Diagnosis: classify by `Rules`, `System prompt`, `Briefing`, `Fixtures/tests`, or `Logging gap`.
   - Proposal: exact intended behavior change and why it is justified.
   - Replay result: historic corpus size, before/after output changes, regressions, and any fields missing from the corpus.
   - Files to change if applied: name only the needed files.
   - Verification: targeted fixture/tests first; full `dotnet test Src/RimBob.sln` if code is edited.

## Apply Mode

Only apply changes when the user explicitly asks to implement, apply, fix, or patch the proposal.

When applying:
- Keep edits scoped to the target minister and directly required shared contracts.
- Update design docs in the same turn if the design changes.
- Add or update fixtures before or alongside rule changes.
- Run before/after replay on the historic corpus when records are replayable; otherwise use the focused fixture/regression input, label it `fixture-only`, and add the logging/corpus gap to the result.
- Always show the before/after advice diff after changing minister logic. Include the input source, path taken (rule/escalation), advice type, priority, title, resource requests, suggested actions, flags, and whether non-target cases stayed unchanged.
- Run the target test slice, then broader tests when shared behavior changed.
- Do not promote rules from Pushbacks without human approval in v1.
