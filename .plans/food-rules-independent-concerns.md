# Food Rules — independent concerns (kill the first-match cascade)

**Status:** LANDED on master — commit `9357831` "feat(food): emit independent rule concerns" (2026-06-02).
**Owner:** Chef (Food minister)
**Scope:** `Src/Ministers/Food/Rules.cs`, `Src/Tests/Food/*`, Food design docs. Coordination **code** untouched; coordination **behavior** changes (multiple solver runs/cycle — see "Flag axis").

**Locked decisions (this session):** no dedup, no cap, no folding — for now. Ship the simplest full independence first; throttles are a later slice.

---

## Goal

Replace `Rules.Evaluate()`'s priority-ordered first-match cascade (every `if` ends in `return`, so exactly one rule emits per tick) with **independent concern evaluation**: every matched deterministic rule contributes its own advice item **and its own flag**. A new-colony tick raises several concerns at once (harvest + cook + forage + freezer + grow), ordered by the bus's existing priority sort, instead of one-per-tick deferral.

User intent: *"each rule independent… it makes sense that the first colony raises a lot of concerns."* Accepted: emit everything matched.

---

## Why this is safe on the advice axis (investigation findings)

Single-emission today is **purely a convention of `Evaluate()` returning early.** Everything downstream already handles a list:

| Layer | Evidence | Multi-advice ready? |
| --- | --- | --- |
| `Decision` record | `Decision(IReadOnlyList<AdviceItem> Advice, IReadOnlyList<AgentFlag> Flags, …)` — [IMinisterRules.cs:18](../Src/Common/Ministers/IMinisterRules.cs#L18) | Yes, already lists |
| Chef publish | `PublishSnapshot(decision.Advice, decision.Flags, …)` → `bus.ReplaceMinisterAdvice` + `foreach flag … flags.Publish` — [Chef.cs:203](../Src/Ministers/Food/Chef.cs#L203) | Yes, publishes the whole list |
| AdviceBus store + sort | `SortAdvice` = `OrderByDescending(Priority).ThenByDescending(IssuedAt)` — [AdviceBus.cs:254](../Src/Coordination/AdviceBus.cs#L254) | Yes — **dashboard already priority-sorts** |
| Advice chain model | `FoodChainActionTargets.From(advice)` iterates **all** items — [FoodChainModelBuilder.cs:281](../Src/Common/Briefings/FoodChainModelBuilder.cs#L281) | Yes — works *better* with more concerns |

Multiple advice items per minister is free. The dashboard already priority-sorts (no change needed).

## Flag axis — accepted cost: multiple solver runs

With **no consolidation / no dedup**, each matched concern emits its own flag (`food:<trace>`, as today). Consequences, accepted "for now":

- `FlagChannel` is keyed by `flag.Id` — N concerns = N distinct active flags ([FlagChannel.cs:8](../Src/Coordination/FlagChannel.cs#L8)).
- `CabinetCycle.RunWillieBuildingRequestFollowUpsAsync` runs Willie **once per distinct Food flag id that carries a Willie building request** ([CabinetCycle.cs:328](../Src/Coordination/CabinetCycle.cs#L328)).
- Each Willie run fires **≤1 placement solve** (`TryGetPlacementRequest` → single request → `TrySolvePlacementAsync`) ([MinisterOfWillie.cs:42](../Src/Ministers/Willie/MinisterOfWillie.cs#L42)).
- ⇒ **N concern-flags-with-builds → N solver runs per cycle.**
- **No dedup** ⇒ a build wanted by two concerns (campfire from emergency + hunt; freezer from emergency + freezer_missing) is **solved more than once**, and each Willie run's `ReplaceMinisterAdvice` overwrites the prior → **last flag's solve wins the Willie snapshot**.

No coordination code changes — the per-flag follow-up loop already exists. The behavior change (multiple solver runs, last-write-wins Willie advice) is the explicit tradeoff of shipping independence without dedup. Revisit if solver cost or snapshot churn bites (see "Deferred").

---

## Design

### D1 — Independent deterministic concerns (no folding)
Rewrite `Evaluate()` to evaluate each deterministic concern predicate independently; each match contributes its own `AdviceItem` + its own `AgentFlag`. The predicate set already exists, mirrored in `RuleMatches()` ([Rules.cs:491](../Src/Ministers/Food/Rules.cs#L491)) — promote it into the live emission path so matching logic lives once.

Concerns, each its own advice + flag:
`nutrition_signal_gap`, `unknown_food_state`, `emergency_food_flag`, `harvest_mature_crops`, `meals_understocked`, `wild_harvest_available`, `hunt_low_risk_animals`, `expand_growing_capacity`, `freezer_missing`.

**No folding:** `freezer_missing` and `meals_understocked` (cook bill) are their own concerns — not folded as support actions onto other concerns. Drop the per-branch `ActionsWithFreezerSupport` / `RequestsWithFreezerSupport` wrapping ([Rules.cs:241](../Src/Ministers/Food/Rules.cs#L241)) and the ~135-line `EmergencyRequests`/`EmergencyActions` superset re-aggregation ([Rules.cs:634](../Src/Ministers/Food/Rules.cs#L634)) — both existed only to compensate for first-match suppression. Each concern now emits only its own requests. **This is the main code win.**

Also fixes the **`expand_growing_capacity` starvation** (today permanently preempted by forage/hunt under 20 days) — now evaluated independently.

### D2 — No gate, no cap (throttles deferred)
Emit **all** matched deterministic concerns. No hard cap on count, no priority-threshold gate. Ordering is the bus's existing priority sort only. Accept the higher concern volume per the user's "raise a lot of concerns" intent. (If the old "too many things generated" note — [Tasks.md:189](../Tasks.md#L189) — resurfaces in practice, add a gate/cap as a later slice.)

### D3 — No flag consolidation, no dedup
One flag per concern, id `food:<trace>` (unchanged shape). No merged "cabinet" flag, no request dedup. Coordination loops per flag as today (→ the multiple-solver-runs behavior above). Simplest path; revisit dedup later if duplicate solves matter.

### D4 — Escalation stays a terminal fallback (structural, keep)
Chef's pipeline switches on `Decision | Escalate` as mutually exclusive whole-cycle paths ([Chef.cs:67](../Src/Ministers/Food/Chef.cs#L67)) — cannot emit deterministic advice *and* escalate in one result. So the escalate predicates (`hunt_targets_blocked_by_risk`, `winter_food_tradeoff`, `unresolved_food_gap`) are **not** independent peers:

> Build the deterministic concern set first. If **non-empty**, return `Decision` with those concerns + their flags. Only if **empty** may escalation predicates return `Escalate`; else fall through to `maintain_security_threshold` / empty.

This is a structural constraint of the pipeline, independent of the throttle decisions. Document it.

### D5 — Trace
`Decision.Trace` is one string, consumed by Chef logging + replay `RuleTrace` + diagnostics. Willie keys placement off **its own** trace, not Food's, so Food's trace can become a **composite** (e.g. `concerns:emergency_food_flag+harvest_mature_crops` or `lead=<top concern>`). No Willie impact. Replay corpus asserts a single `rule_trace` but is only 2 cases (regen).

---

## Blast radius

**Rewrite**
- `Src/Ministers/Food/Rules.cs` — `Evaluate()` becomes: build every matched concern's (advice, flag) → return them all. Keep the per-concern request/action builder helpers (`HarvestAction`, `HuntingRequests`, `FreezerSupportRequests`, `CookBillActions`, growing-zone, forbidden-meal). Delete the cascade ordering, the `...WithFreezerSupport` wrappers, the `EmergencyRequests`/`EmergencyActions` supersets.
- `Src/Tests/Food/FoodRulesTests.cs` — heavy. ~39 trace-coupled assertions; `decision.Advice.Should().ContainSingle()` / `decision.Flags.Should().ContainSingle()` / single-`Trace` / order-based `SuppressedCandidates` all become multi-concern assertions ([FoodRulesTests.cs:29](../Src/Tests/Food/FoodRulesTests.cs#L29)). With no gate/cap, tests assert the **full** matched set per briefing.

**Regenerate**
- `Src/Tests/Food/Fixtures/replay-corpus/food-rules-history.jsonl` — **2 cases**, regen via the existing recorder.
- `Src/Tests/Food/FoodReplayCorpusTests.cs` — single-trace + advice/flag equality → multi-concern shape ([FoodReplayCorpusTests.cs:29](../Src/Tests/Food/FoodReplayCorpusTests.cs#L29)).
- `Src/Tests/Food/FoodChainModelBuilderTests.cs` — verify green with multi-advice input.

**Docs**
- `Docs/design/ministers.md`, `Docs/design/food.md` — independent-concern model, no-throttle/no-dedup decision + the deferred follow-ups, escalation carve-out.

**Diagnostics:** `RuleTraceDetails.SuppressedCandidates` loses meaning (nothing order-suppressed). Either always empty or repurpose to gate-suppressed (empty for now since no gate). `AllRules` stays.

**Coordination:** no code change. Behavior change = multiple solver runs + last-write-wins Willie snapshot per cycle (accepted).

---

## Step-by-step

1. Extract a `ConcernCandidate` (concern, priority, advice factory, flag factory) and build the candidate list from the existing predicates (unify with `RuleMatches`).
2. Rewrite `Evaluate()`: build all matched candidates → if non-empty return `Decision(allAdvice, allFlags, compositeTrace, diagnostics)`; else escalation fallback (D4) → else maintain/empty.
3. Update diagnostics (`SuppressedCandidates` empty; `AllRules` kept; composite selected-rule).
4. Rewrite `FoodRulesTests` to the full matched set per briefing; regen replay corpus (2 cases); fix corpus + chain-model tests.
5. Update design docs.
6. `dotnet test`; then live verification.

---

## Verification (JSON-first, per standing guidance)

Confirm via cabinet run + replay JSON, not the dashboard screenshot:
- New-colony save, run a cabinet cycle; read Chef's replay record / advice snapshot JSON.
- Assert: **multiple** advice items, priority-sorted; **multiple** Food flags (one per matched concern); `CabinetRunLog` shows **multiple Willie follow-up runs / solver runs** (expected, not a bug now).
- Confirm `expand_growing_capacity` now surfaces alongside forage/hunt on a `10 < days < 20` tick (starvation fixed).
- Note duplicate solves where two concerns request the same build (documents the deferred-dedup cost).

---

## Deferred (revisit if it bites)

- **Dedup / consolidated flag** — collapse overlapping building requests so a build solves once per cycle. Biggest lever against duplicate solver runs + Willie snapshot churn.
- **Priority gate / cap** — if concern volume becomes noise ([Tasks.md:189](../Tasks.md#L189)).
- **Folding** freezer/cook-bill back to support actions — only if independent freezer/cook concerns prove too noisy.

---

## Human-facing summary

Chef's food rules no longer pick one winner per tick. `Evaluate()` now collects **every** matched deterministic concern and emits them all in one snapshot — each concern gets its own advice card and its own flag, ordered by the dashboard's existing priority sort. A struggling colony surfaces harvest + cook + forage + hunt + grow + freezer together instead of one-per-tick.

What landed, matching the locked decisions (no dedup, no cap, no folding):
- **Collect-all:** `DeterministicConcerns()` builds the full matched set; `DecisionForConcerns()` returns them all. The old priority-ordered `if`/`return` cascade is gone.
- **No folding:** `EmergencyActions`/`EmergencyRequests` were slimmed from a ~135-line superset (which used to re-aggregate harvest/forage/hunt/cook/grow) down to emergency-only actions (unforbid, stockpile visibility, trade fallback) plus a pointer to "the separate Food concern cards." `freezer_missing` and `meals_understocked` are their own concerns. No double-emit.
- **`expand_growing_capacity` starvation fixed** — it is evaluated independently now, so it surfaces alongside forage/hunt under 20 days instead of being permanently preempted.
- **Composite trace** (`concerns:a+b`) for diagnostics/replay; Willie unaffected (keys off its own trace).
- **Escalation stays terminal** — escalate to the LLM only when zero deterministic concerns match (structural: Chef treats `Decision`/`Escalate` as mutually exclusive).

Accepted tradeoffs (deferred, see "Deferred"): no request dedup, so a build wanted by two concerns can be solved more than once that cycle, and the last flag's Willie solve wins the Willie snapshot. No gate/cap on concern volume yet.

Touched: `Rules.cs` (489 lines), `FoodRulesTests.cs` (193), `FoodChainModelBuilderTests.cs`, `FoodMinisterTests.cs`, replay corpus (2 cases), `Docs/design/ministers.md` + `ministers/food.md`. Coordination code untouched; behavior change = multiple solver runs/cycle.
