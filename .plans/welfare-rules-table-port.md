# Welfare rules — port onto the shared base (slice 3)

**Status:** PLANNED. Slice 3 of [`minister-rules-table-refactor`](minister-rules-table-refactor.md).
**Owner:** Welfare.
**Depends on:** slice 2 (`minister-rules-shared-base`, landed `2d02576`) — the base + Chef port are the template.

---

## Motivation

Welfare is **already all-hits** (slice B): `RuleEmissions()` collects four independent
concerns (`break_risk`, `shelter_floor`, `recreation_gap`, `comfort_beauty`), `needs_stable`
fallback. But it still carries the triple copy — imperative `RuleEmissions` + parallel
`RuleMatches` + static `AllRuleEvaluations` — plus its own private `RuleEmission`,
`DecisionForEmissions`, `WithRuleEmissions`, `CompositeTrace`. This slice ports Welfare onto
the shared base from slice 2, deleting all of that. **Mirror Chef's port** (`Src/Ministers/Food/Rules.cs`
at `2d02576`) — it already solved table + fallback-descriptor + `BuildTrace` usage.

## Context

- Base: `Src/Common/Ministers/MinisterRule.cs` (`RuleEmission`, `MinisterRule<TB>`,
  `MinisterRuleTraceDescriptor<TB>`, `MinisterRuleTableResult`) +
  `MinisterRuleTableEvaluator.cs` (`EvaluateAllHits`, `Aggregate`, public `BuildTrace`,
  dedup).
- Chef template: `Src/Ministers/Food/Rules.cs` — `RuleTable(now)` array, `EvaluateAllHits`,
  fallback descriptors for terminal pseudo-rules, `DiagnosticsForFallback` calling
  `BuildTrace` with the chosen selected rule.
- Welfare today: `Src/Ministers/Welfare/Rules.cs`. Four `Should*`/`*Emission` pairs;
  `needs_stable` terminal; private `RuleEmission` (shadows Common); ordering tiebreak =
  **insertion index** (`DecisionForEmissions` uses `.ThenBy(index)`).

## Scope

**In:**
- Convert the four `RuleEmissions` branches into a `MinisterRule<WelfareSourceBriefing>[]`
  table: `break_risk`, `shelter_floor`, `recreation_gap`, `comfort_beauty`. Each rule's
  `Build` reuses the existing `*Emission` body; `Matches` reuses the `Should*` predicates;
  `Reason` reuses the strings from `RuleMatches`.
- Use `MinisterRuleTableEvaluator.EvaluateAllHits`. On no match, return the `needs_stable`
  `Decision` with diagnostics from `BuildTrace(..., selectedRule: "needs_stable", ...)` —
  mirroring Chef's `DiagnosticsForFallback`.
- Model `needs_stable` as a `MinisterRuleTraceDescriptor` (reason "no deterministic Welfare
  rule matched"; outcome "selected" when selected else "not_matched") so `AllRules` keeps all
  5 entries.
- Use Common's `RuleEmission`; **delete** Welfare's private `RuleEmission`, `RuleMatches`,
  `AllRuleEvaluations`, `RuleEvaluation`, `Match`, `DecisionForEmissions`, `WithRuleEmissions`,
  `CompositeTrace`.
- Keep every advice/flag builder helper (`BreakRiskEmission`, `ShelterFloorEmission`,
  `BreakRiskDriverFlag`, `BuildFlag`, taxonomy helpers, …) unchanged.
- **Drop the redundant `ColonistCount > 0` conjunct from `break_risk`'s `Matches`** (keep it
  on `shelter_floor`, where `BedDeficit`/`UnroofedBedroomCount` can be non-zero with zero
  colonists). `BreakRiskCount > 0` already implies colonists exist. **Verify**: if any fixture
  has `BreakRiskCount > 0` with `ColonistCount == 0`, keep the guard and flag it.

**Out (non-goals):** no base changes; no Chef/Willie changes; no cap; no new advice/flags; no
coordination/dashboard change.

## Behavior contract

- **Neutral:** Welfare advice, flags, and the matched/selected rule set are identical per
  fixture. Welfare gains the base's request-dedup, but its four concerns request distinct
  builds (Bed / Roof / Recreation / Table) so nothing collapses — confirm no flag loses a
  request.
- **Allowed changes (mirror Chef slice 2 + slice 1):**
  1. `AllRules` `Conditions`/`OutputAction` text now derives from `Reason` + emitted actions.
  2. Equal-priority **tiebreak** changes from insertion-order to rule-name (`.ThenBy(Rule,
     Ordinal)` in the base). This can reorder the composite trace when two equal-priority
     Welfare concerns co-emit (e.g. the two Low concerns recreation_gap/comfort_beauty).
     Update order-dependent assertions; regenerate the Welfare replay corpus **only if** the
     tiebreak shifts it (and say so).
- `AllRules` must still contain all 5 rules (4 table + `needs_stable`). Do not shrink it
  (this was the slice-2 regression — do not repeat it).

## Approach

1. Read Chef's ported `Rules.cs` as the structural template.
2. Build the Welfare table + `needs_stable` descriptor; route `Evaluate` through
   `EvaluateAllHits` + a `needs_stable` fallback via `BuildTrace`.
3. Delete the triple-copy + shadow `RuleEmission` + bespoke aggregation/trace helpers.
4. Drop the redundant `break_risk` `ColonistCount` guard (verify invariant).
5. Run tests; adjust only `AllRules` condition-text and any equal-priority order assertions.

## Verification

- `dotnet build Src/RimBob.sln` — 0 errors.
- `dotnet test Src/RimBob.sln --filter "FullyQualifiedName~Welfare"` — green.
- `dotnet test Src/RimBob.sln` — full suite green.
- Grep: `Src/Ministers/Welfare/Rules.cs` no longer defines `RuleMatches` or
  `AllRuleEvaluations`, and no `private sealed record RuleEmission`.
- `AllRules` count for Welfare stays 5.

## Where to see it

Pure refactor — no runtime/dashboard change. Welfare SYSTEM rules-trace shows the same
selected concerns + emitted advice/flags; `AllRules` derived.

## Open questions (escalate, don't guess)

- If dropping the `break_risk` `ColonistCount` guard changes any fixture outcome, keep the
  guard and report (the invariant `BreakRiskCount <= ColonistCount` may not hold in a fixture).
- If you cannot keep `AllRules` at 5 rules without a hand-written string catalogue, STOP and
  surface it — do not shrink `AllRules` and do not add static condition strings (this is the
  exact slice-2 regression).
- If porting changes Welfare advice/flags/selected concerns (not just AllRules text + equal-
  priority order), STOP and report — the port is behavior-neutral.

---

## Summary (landed 2026-06-03)

**Motivation.** Welfare was already all-hits (slice B) but still carried the triple copy
(`RuleMatches` + `AllRuleEvaluations`) and a private `RuleEmission` shadowing the Common one.
Ported onto the shared base (slice 2), mirroring Chef.

**Scope (shipped).** Welfare's four concerns (`break_risk`, `shelter_floor`, `recreation_gap`,
`comfort_beauty`) → `MinisterRule<WelfareSourceBriefing>[]` table; `needs_stable` → a
`MinisterRuleTraceDescriptor` (AllRules stays 5). Deleted `RuleMatches`, `AllRuleEvaluations`,
`RuleEvaluation`, `Match`, `DecisionForEmissions`, `WithRuleEmissions`, `CompositeTrace`, and
the private `RuleEmission` (−211 lines). Dropped the redundant `ColonistCount > 0` conjunct
from `break_risk`'s `Matches` (kept on `shelter_floor`). Builders untouched.

**How to verify (human).**
- Commands: `dotnet test Src/RimBob.sln --filter "FullyQualifiedName~Welfare"` → 24/24;
  full suite → 548/548.
- Behavior-neutral: advice/flags/selected concerns identical; replay corpus unchanged (the
  equal-priority tiebreak did not shift it). `AllRules` = 5, derived from `Reason`/emissions.
- Files: `Src/Ministers/Welfare/Rules.cs`.

**Codex run:** 20260603-204736-welfare-rules-table-port · branch
`codex/prompt-20260603-204736-welfare-rules-table-port` · landed commit `b8189f3`.
