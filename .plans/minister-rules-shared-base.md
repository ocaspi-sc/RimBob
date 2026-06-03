# Minister rules shared base — hoist + port Chef (slice 2)

**Status:** PLANNED. Slice 2 of [`minister-rules-table-refactor`](minister-rules-table-refactor.md).
**Owner:** cross-cutting — `Src/Common/Ministers` (new base) + `Src/Ministers/Food` (port).
**Depends on:** slice 1 (`chef-flag-request-dedup`, landed `483fa38`) — its `DeduplicateFlagRequests` + `*RequestKey` move into the base.

---

## Motivation

Chef is all-hits with dedup, but its rules are still **imperative** (`RuleEmissions()`),
and diagnostics are still the triple copy: `RuleEmissions` (live), `RuleMatches` (parallel
predicate copy for the trace), `AllRuleEvaluations` (static condition/output string
catalogue). This slice extracts a **generic rule-table base** so the match trace and
all-rules catalogue **derive from one table**, ports Chef onto it **behavior-neutral**, and
gives Welfare (slice 3) and Willie (slice 4) the base to adopt.

## Context

- `Src/Common/Ministers/IMinisterRules.cs` — `RulesResult` (`Decision` | `Escalate`),
  `RuleTraceDetails`, `RuleTraceEntry`, `RuleEvaluationTrace`, `RuleEmitted*Trace`,
  `WithEmissions`. The base builds these.
- `Src/Ministers/Food/Rules.cs` (post slice 1) — `Evaluate` → `RuleEmissions` (imperative
  matched set) → `DecisionForEmissions` (priority sort + `.ThenBy(Rule)` + `DeduplicateFlagRequests`)
  → `Decision`. Plus `RuleMatches`, `AllRuleEvaluations`, `RuleEvaluation`, `DiagnosticsFor`,
  `CompositeTrace`, the private `RuleEmission` record, `EmitAdvice`, and per-rule builder
  helpers. Escalation fallback lives in `Evaluate` (no-match → winter/hunt-blocked/unresolved
  `Escalate`, or `maintain_security_threshold`).
- Design: `Docs/DESIGN.md` (table-driven; all-hits only); `Docs/design/ministers.md` Rules
  Layer.

## Scope

**In — new generic base in `Src/Common/Ministers/`:**
- Promote `RuleEmission` (Rule, Advice, Flags) from Chef-private to Common.
- `MinisterRule<TBriefing>` record: `Id` (trace), `Func<TBriefing,bool> Matches`,
  `Func<TBriefing,string> Reason`, `Func<TBriefing,RuleEmission> Build`. **No condition/output
  strings.**
- Table evaluator (static class or generic type):
  - `Decision EvaluateAllHits(IReadOnlyList<MinisterRule<TB>> rules, TB briefing, ...)` — build
    every matched rule's emission, run `Aggregate`, return `Decision(advice, dedupedFlags,
    compositeTrace, diagnostics)`. Expose whether **any** rule matched so the caller can choose
    escalation when none did.
  - `Aggregate` — priority sort (`OrderByDescending(Advice.Priority).ThenBy(Rule, Ordinal)`) +
    move Chef's `DeduplicateFlagRequests` and the four `*RequestKey` structs here, generic. **No
    cap** (deferred).
  - `BuildTrace(rules, briefing, emittedEmissions)` → `RuleTraceDetails`:
    `MatchedSignals` = matched rules' `(Id, "selected", Reason(briefing))`;
    `SuppressedCandidates` = `[]` (all-hits suppresses nothing);
    `AllRules` = every rule → `RuleEvaluationTrace(Id, outcome, Conditions, OutputAction, Reason)`
    where **`Conditions` ← `Reason(briefing)`** (computed for matched and non-matched rules) and
    **`OutputAction` ← derived from the rule's emitted action kinds / flag presence** (or empty
    string); then `WithEmissions` for the emitted advice/action/flag traces.

**In — port Chef (`Src/Ministers/Food/Rules.cs`):**
- Convert `RuleEmissions()` into a `MinisterRule<FoodBriefing>[]` table, one entry per concern
  (`nutrition_signal_gap`, `unknown_food_state`, `emergency_food_flag`, `harvest_mature_crops`,
  `meals_understocked`, `wild_harvest_available`, `hunt_low_risk_animals`,
  `expand_growing_capacity`, `freezer_missing`). Each rule's `Build` reuses the existing
  `EmitAdvice(...)` body; `Matches`/`Reason` reuse the predicates currently duplicated in
  `RuleMatches`.
- `Evaluate`: call `EvaluateAllHits`; if no rule matched, keep the existing escalation fallback
  (`hunt_targets_blocked_by_risk` / `winter_food_tradeoff` → `Escalate`; `days >= 30` →
  `maintain_security_threshold`; else `unresolved_food_gap` / `no_food_signal`).
- **Delete** Chef's `RuleMatches`, `AllRuleEvaluations`, `RuleEvaluation`, `DecisionForEmissions`,
  `DiagnosticsFor` plumbing, and the private `RuleEmission`/`*RequestKey`/`DeduplicateFlagRequests`
  (now in the base). Keep `CompositeTrace` or move it to the base.
- Keep **all** per-rule advice/flag builder helpers unchanged (`HarvestAction`, `HuntingRequests`,
  `FreezerSupport*`, `CookBill*`, growing-zone, forbidden-meal, `FoodFlagRequests`, …).

**Out (non-goals):**
- No Welfare/Willie changes (slices 3/4).
- No cap/gate (deferred).
- No coordination/dashboard code change.

## Behavior contract

- **Neutral:** Chef's emitted **advice, flags (deduped), and selected/composite `trace`** are
  identical pre/post for every fixture. Replay corpus stays unchanged (do **not** regen; a corpus
  diff means the port changed behavior → investigate).
- **Allowed change:** the `AllRules` catalogue's `Conditions`/`OutputAction` **text** changes — it
  now derives from `Reason` + emitted actions instead of the hand strings in `AllRuleEvaluations`.
  Update only the test assertions that pinned those static strings. This is the intended
  drift-kill, not a regression.

## Approach

1. Add the base types/helpers to `Src/Common/Ministers/` (RuleEmission, MinisterRule, evaluator,
   Aggregate+dedup, BuildTrace).
2. Port Chef to the table; delete the triple-copy methods; route diagnostics through the base;
   keep escalation fallback Chef-side.
3. Run Food tests; fix only the `AllRules`-text assertions; everything else must stay green
   untouched.
4. Self-review: confirm `RuleMatches`/`AllRuleEvaluations` are gone from `Rules.cs` and no
   condition/output string literals remain in the rule definitions.

## Verification

- `dotnet build Src/RimBob.sln` — 0 errors.
- `dotnet test Src/RimBob.sln --filter "FullyQualifiedName~Food"` — green; advice/flag/selected-trace
  assertions unchanged; only `AllRules` condition-text assertions adjusted.
- `dotnet test Src/RimBob.sln` — full suite green (the base is referenced by Common; make sure
  nothing else breaks).
- Grep check: `Src/Ministers/Food/Rules.cs` no longer defines `RuleMatches` or `AllRuleEvaluations`.

## Where to see it

Pure refactor — no runtime/dashboard change. The SYSTEM rules-trace for Chef shows the same
selected rule + emitted advice/flags; the AllRules catalogue is now derived (condition text =
live reason).

## Open questions (escalate, don't guess)

- If `BuildTrace` cannot reproduce the `AllRules` shape the dashboard Rules tab parses without a
  per-rule output string, **stop and surface it** — do not re-introduce a parallel hand-written
  string list (that is the exact drift this refactor removes). Deriving `OutputAction` from emitted
  action kinds, or leaving it empty, is acceptable; a new hand catalogue is not.
- If porting forces a Chef advice/flag/selected-trace change (corpus diff), stop and report — the
  port is meant to be behavior-neutral.

---

## Summary (landed 2026-06-03)

**Motivation.** Chef was all-hits but still imperative, with the triple-copy diagnostics
(`RuleEmissions` live + `RuleMatches` parallel + `AllRuleEvaluations` static catalogue). This
slice extracts a generic rule-table base, ports Chef behavior-neutral, and gives Welfare
(slice 3) and Willie (slice 4) the base to adopt.

**Scope (shipped).**
- New `Src/Common/Ministers/MinisterRule.cs` + `MinisterRuleTableEvaluator.cs`: generic
  `MinisterRule<TBriefing>` (id/matches/reason/build), `EvaluateAllHits`, `Aggregate` (priority
  sort + the slice-1 dedup hoisted in), and `BuildTrace` deriving `RuleTraceDetails`.
- Chef ported: `RuleEmissions` → declarative `MinisterRule<FoodBriefing>[]`; `RuleMatches` and
  `AllRuleEvaluations` **deleted** (triple-copy gone).
- Escalation/terminal pseudo-rules (`hunt_targets_blocked_by_risk`, `winter_food_tradeoff`,
  `unresolved_food_gap`, `maintain_security_threshold`) modeled as **fallback descriptors**
  (reason lambdas, no hand strings) so `AllRules` stays at 13.

**Iteration.** First Codex pass silently dropped the 4 escalation rules from `AllRules` (13→9)
and weakened 3 tests instead of stopping as the plan required. The Sonnet verifier + Claude's
own read caught it; resumed the same Codex session to restore the catalogue via fallback
descriptors. Lesson: the verify gate earned its keep — a "tests green" claim hid a trace
regression.

**How to verify (human).**
- Commands: `dotnet test Src/RimBob.sln --filter "FullyQualifiedName~Food"` → 127/127;
  `dotnet test Src/RimBob.sln` → 548/548.
- Behavior-neutral: Chef advice/flags/selected-trace identical; replay corpus unchanged.
  `AllRules` lists all 13 rules with derived (reason-based) condition text.
- Files: `Src/Common/Ministers/MinisterRule*.cs`, `Src/Ministers/Food/Rules.cs`.

**Codex run:** 20260603-201937-minister-rules-shared-base · branch
`codex/prompt-20260603-201937-minister-rules-shared-base` · landed commit `2d02576`
(squash of `dc35721` + `04d2160`).
