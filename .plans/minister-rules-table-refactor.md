# Minister rules: table-driven rule-as-data + all-hits hit-policy

**Status:** PLANNED (not started). Design decisions locked this session — see
`Docs/DESIGN.md` rows "Rules layer is table-driven (rule-as-data)…" and "All-hits
is the default rules hit-policy…", and `Docs/design/ministers.md` → Rules Layer.
**Owner:** cross-cutting — `Src/Common/Ministers` (shared base) + Chef/Welfare/Willie `Rules.cs`.
**Depends on:** lands **after** [`delete-concern`](delete-concern.md). Both rewrite
the same Food/Welfare/Willie emission factories and `IMinisterRules.cs`; removing
`concern` first means the rule record carries no concern field and each `Rules.cs`
is rewritten once, not twice.

---

## Problem

Each minister writes every rule three times, hand-synced:

1. `Evaluate` — executable guards → `Decision`/`Escalate`.
2. `RuleMatches` — the same predicates re-evaluated to build the match trace.
3. `AllRuleEvaluations` — a static catalogue restating each rule's conditions and
   output as strings.

…plus the stable trace id repeated across all three (and again in Willie's
`TryGetPlacementRequest` / `TryMissingRoomClass` trace switches). Changing one rule
means editing 3–5 places, and the diagnostics catalogue can drift silently from
behavior. Willie and Welfare also first-match, so a matched-but-suppressed rule
hides a coexisting problem *and* drops its cross-minister flag.

---

## Target model

Rule-as-data: one ordered rule table per minister. Each rule record carries a
stable id/trace, condition + output descriptions (for diagnostics), a match
predicate, and a builder producing `Decision` or `Escalate`. A shared base
evaluates the table under a per-minister **hit-policy** and derives the match
trace and all-rules catalogue from the same list. No `concern` field (removed by
`delete-concern`).

### Hit-policy

- **All-hits (default)** — `rules.Where(Matches).Select(Build)` → `Aggregate`
  (priority sort, cap, same-`trace` dedup/consolidation). Escalate only when no
  rule matched (alongside any explicit Escalate rule that fired).
- **First-match** — `rules.FirstOrDefault(Matches)?.Build` else terminal
  fallback. Interim sparsity debt only; equivalent to all-hits + a forced stop
  after rule one.

### Shared base (`Src/Common/Ministers`)

- A `MinisterRule<TBriefing>` record: id, conditions, output, `Matches`
  predicate, `Reason` selector, and a `Build` returning `RulesResult`.
- A table evaluator: `Evaluate(policy)`, `BuildTrace` → `RuleTraceDetails`
  (`MatchedSignals` / `SuppressedCandidates` / `AllRules`), and `Aggregate` for
  all-hits (sort / cap / dedup / consolidate).
- Generalize Chef's existing `DecisionForConcerns` aggregation into `Aggregate`.

Exact record fields and helper names are code contracts — design docs do not
mirror them.

---

## Hard problems

- **Escalation as data.** Food has Escalate rules (`winter_food_tradeoff`,
  `hunt_targets_blocked_by_risk`, `unresolved_food_gap`) plus an
  escalate-when-none fallback. The `Build` returns `RulesResult` (`Decision` |
  `Escalate`); the base supports a terminal fallback rule.
- **Dominance.** Willie's `ShouldDeferToMissingKitchen` (implicit list order)
  becomes an explicit predicate on the placement rule
  (`… && !(kitchenMissing && dependsOnKitchen)`).
- **Willie trace switches.** `TryGetPlacementRequest` / `TryMissingRoomClass`
  map trace → request; derive them from the table (the rule owns its request
  factory) instead of a parallel switch.
- **Diagnostics parity.** `RuleTraceDetails` (matched/suppressed, `AllRules`,
  emitted advice/action/flag traces) must stay stable where behavior is
  unchanged; the all-rules catalogue now derives from predicates, not a hand
  list. This is the main regression surface.

---

## Sequencing

1. **Chef dedup/cap slice** — build priority-cap + same-`trace`
   dedup/consolidation for Chef's already-landed all-hits (the deferred debt in
   [`food-rules-independent-concerns`](food-rules-independent-concerns.md)). Build
   the hard part where the need already exists.
2. **Hoist into the shared base** — generalize the rule record, evaluator,
   `BuildTrace`, and `Aggregate`; port Chef onto it behavior-neutral.
3. **Flip Welfare** — 2 near-independent rules; smallest proof of the base on a
   second minister. all-hits + priority sort.
4. **Flip Willie** — 9 rules; convert kitchen-defer to an explicit dominance
   predicate, fold the trace switches into the table, then flip to all-hits.

Each step de-risks the next: build the hard part where it is already required,
prove the shared path on the easy minister, tackle Willie's dominance last.

---

## Verification (JSON-first)

Before/after on the replay corpus + fixtures: same path/advice/flags/trace for
unchanged cases; assert the derived all-rules catalogue equals the predicate set;
a Welfare new-colony tick now emits **both** `break_risk` and `shelter_floor`
(with the shelter `BuildingRequest` flag to Willie) where first-match previously
hid one. `dotnet build` + `dotnet test`, dashboard `tsc`/`vite build`, then a live
cabinet-run JSON check.

---

## Non-goals

- Autonomy-dial implementation (M7+). This only relies on the dial's unit being
  action-kind (`delete-concern`), not concern.
- No external rule engine (NRules/RETE, Microsoft.RulesEngine) — rejected in
  `DESIGN.md`.
- No briefing/schema changes.
