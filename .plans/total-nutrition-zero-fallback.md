# Plan: Documented fallback for `total_nutrition == 0`

## Motivation

RIMAPI sometimes returns `total_nutrition == 0` even when `food_total > 0` and there are clearly meals + raw food on the map. Today, the Food briefing silently reflects that zero into `EstimatedDaysOfFood`, which collapses to "no usable nutrition signal" in the player-facing summary and makes Food advice misfire. The upstream fix in RIMAPI is undecided and out of our control timeline; we should add a documented RimBob-side fallback so the briefing degrades gracefully without lying about authority.

User framing (verbatim from HumanTodo line 66): *"Investigate and fix/patch `total_nutrition == 0`. Decide whether to patch RIMAPI upstream or compute a documented RimBob fallback from stored meal/raw-food counts."* — we are taking the RimBob-side fallback path.

## Context

Relevant files (grep confirmed):

- [Src/StateStore/Ingestion/ResourceAggregateMapper.cs:15](Src/StateStore/Ingestion/ResourceAggregateMapper.cs) — RIMAPI `TotalNutrition` lands in the snapshot here as `food?.TotalNutrition ?? 0f`.
- [Src/StateStore/Derivations/FoodBriefingDerivation.cs](Src/StateStore/Derivations/FoodBriefingDerivation.cs) — composes the briefing from snapshots; this is where derived values (like `EstimatedDaysOfFood`) live.
- [Src/Common/Briefings/FoodBriefing.cs](Src/Common/Briefings/FoodBriefing.cs) — the briefing DTO. Has `MealsCount`, `RawFoodCount`, `EstimatedDaysOfFood`, `MissingBriefingSignals`, `DataCoverage`.
- [Src/Common/Briefings/FoodStateSummary.cs](Src/Common/Briefings/FoodStateSummary.cs) — `BuildStoredFoodLine` currently emits "days-of-food cannot be estimated because no usable nutrition signal is available." when `EstimatedDaysOfFood is null`. This is the player-facing fallout.
- [Src/Tests/Food/Fixtures/nutrition-signal-gap.json](Src/Tests/Food/Fixtures/nutrition-signal-gap.json) — an existing fixture for exactly this case: `"RIMAPI food_total exists but total_nutrition is missing or zero"`. Currently expects `manage_food_stockpile` advice; the fallback should not weaken that signal.

Design boundary: ingestion mappers preserve RIMAPI truth (raw `TotalNutrition` stays 0 in the snapshot). The fallback is a **derivation-layer concern** — it's a computed view of the data, not new ground truth.

## Scope

In scope:

1. Add a derivation-layer fallback for `total_nutrition` in `FoodBriefingDerivation` when (and only when) the RIMAPI signal is unusable: `snapshot total_nutrition <= 0` AND (`MealsCount > 0` OR `RawFoodCount > 0`).
2. Compute the fallback using the formula from the user's todo: `meals_count * 0.9 + raw_food_count * 0.05`. (0.9 nutrition per simple meal, 0.05 per raw-food unit — order-of-magnitude estimate; tighter constants can come later if needed.)
3. Use the fallback to populate `EstimatedDaysOfFood` so the briefing's days-of-food line works even when RIMAPI nutrition is broken.
4. Add an entry to `MissingBriefingSignals` (or `UnimplementedBriefingSignals`, whichever already carries "nutrition signal degraded") naming this gap, so the data-gap line surfaces it and the dashboard's confidence-gap UI reflects it. **Codex picks the exact signal string and documents it in the diff.**
5. Update `FoodStateSummary.BuildStoredFoodLine` so the days-of-food output, when computed from the fallback, carries a short qualifier like "(estimate; RIMAPI nutrition signal missing)" so the human reader knows the number is derived. No change to the existing happy path.
6. Add unit-test coverage:
   - A fixture/case where `TotalNutrition == 0`, `MealsCount > 0`, `RawFoodCount > 0` → `EstimatedDaysOfFood` is non-null and matches the formula.
   - A case where `TotalNutrition == 0` AND `MealsCount == 0` AND `RawFoodCount == 0` → no fallback applied (the briefing still reports no usable signal; we don't fabricate days out of nothing).
   - A case where `TotalNutrition > 0` (happy path) → fallback NOT applied, RIMAPI value used directly.
   - The data-gap line includes the new signal entry when the fallback fires.

Out of scope / explicit non-goals:

- **Do not patch RIMAPI upstream** in this slice. Fallback only.
- **Do not change the ingestion mapper.** Raw snapshot still reflects RIMAPI truth.
- **Do not retune the constants** beyond the formula in the todo. If 0.9/0.05 turn out wrong, that's a follow-up plan with empirical data.
- **Do not touch hunting target nutrition** (`TotalNutrition` in `FoodBriefingDerivation` around lines 506/571/650 is a different concept — per-target wild-animal nutrition, not stored-food nutrition). Leave it alone.
- **Do not change `manage_food_stockpile` advice gating** — the existing fixture's expectation must still pass.

## Approach

Codex chooses the exact method names/placement; the points below are the shape, not a literal recipe.

1. In `FoodBriefingDerivation`, locate where `EstimatedDaysOfFood` is computed from `TotalNutrition`. Refactor that into something like `ResolveNutritionForBriefing(snapshot)` that returns either `(nutrition, isFallback: false)` from the RIMAPI value, or `(fallbackNutrition, isFallback: true)` when the guard above triggers.
2. Add the fallback formula as a private const-driven helper (e.g. `MealsNutritionEstimate = 0.9f`, `RawFoodNutritionEstimate = 0.05f`) so the constants are reviewable in one spot. Add a comment naming them as order-of-magnitude estimates with a follow-up pointer.
3. When `isFallback`, append the appropriate signal string to the briefing's missing/unimplemented signals list. Keep the string stable for future grep-ability.
4. In `FoodStateSummary.BuildStoredFoodLine`, detect the "fallback was used" condition (cheapest path: have the derivation set a new bool on the briefing, or piggyback on a known signal in `MissingBriefingSignals`) and append the qualifier. **Pick whichever is cleaner; if a new bool is added, mark it nullable-or-defaulted so legacy fixtures load.**
5. Add tests under `Src/Tests/State/` (briefing derivation tests) and/or `Src/Tests/Food/` (state summary tests) per the cases listed in Scope. If there's already a parameterized derivation test class, extend it; don't create a new file unless natural.
6. Ensure the existing `nutrition-signal-gap.json` fixture still passes its current expectations.

## Verification

Run from the worktree (after `git merge master` if behind):

```powershell
dotnet build C:\<worktree>\Src\RimBob.sln --no-restore
dotnet test C:\<worktree>\Src\Tests\RimBob.Tests.csproj --no-restore --no-build
```

Pass criteria:

- Build is green.
- All existing tests pass; 120/120 was the May 15 refactor-baseline count, but the count may have moved, so pass means no regressions.
- New tests for the three fallback cases pass.
- The existing `nutrition-signal-gap.json` fixture-driven test still passes with its current expected advice.

If `RimBob.Host.exe` is locked (MSB3021/MSB3027), stop the running Host process and rebuild — that's an environment issue, not a code regression. Note in the final message but don't treat as failure.

## Where to see it (dashboard)

Food panel → snapshot section → "Stores" line. With this slice landed:

- Pre-fix (RIMAPI broken): `"Stores: <meals> and <raw>; days-of-food cannot be estimated because no usable nutrition signal is available."`
- Post-fix (fallback active): `"Stores: <meals> and <raw>; about X.X days for N colonists; buffer <posture> (estimate; RIMAPI nutrition signal missing)."`
- Post-fix (RIMAPI healthy): unchanged from today.

The "Confidence gaps" line should also surface the new signal entry when the fallback fires.

---

## Summary (landed 2026-05-21)

**Motivation.** RIMAPI sometimes returns `total_nutrition == 0` with food present, collapsing Food advice. This adds a documented RimBob-side fallback so the briefing degrades gracefully.

**Context.** Fallback formula (`meals * 0.9 + raw * 0.05`) and `EstimatedDaysOfFood` wiring were pre-existing on master. This slice adds: the signal entry, the summary qualifier, and full test coverage.

**Scope.**
- `UsesFallbackNutrition` computed property on `FoodBriefing`
- `rimapi_total_nutrition_missing` signal in `MissingBriefingSignals` when fallback fires
- `"(estimate; RIMAPI nutrition signal missing)"` qualifier in `FoodStateSummary.BuildStoredFoodLine`
- 4 new test cases in `FoodBriefingDerivationTests` + `FoodStateSummaryTests`
- Doc-comment update on `Snapshots.cs`; string constant refactor in `FoodBriefing.cs`

**How to verify (human).**
- Dashboard: Food panel → Stores line shows qualifier when RIMAPI nutrition is 0 but food items exist
- Commands: `dotnet test Src/Tests/RimBob.Tests.csproj` — 306/306 pass
- Files: `FoodBriefing.cs`, `FoodStateSummary.cs`, `FoodBriefingDerivationTests.cs`, `FoodStateSummaryTests.cs`

**Codex run:** 20260521-181911-total-nutrition-zero-fallback · branch `codex/prompt-20260521-181911-total-nutrition-zero-fallback` · landed commit `3069908f`

## Open questions

Codex should escalate, not guess at:

- **Signal name.** Should the new entry land in `MissingBriefingSignals` (RIMAPI is sending the field, just wrong) or `UnimplementedBriefingSignals` (we don't compute it ourselves)? Lean `MissingBriefingSignals` since the upstream signal *is* the gap, but if the conventions in the codebase point the other way, follow the conventions.
- **Bool vs piggybacked signal for the summary qualifier.** A new bool on `FoodBriefing` is explicit but adds a field; signal-string sniffing in `FoodStateSummary` is clutter but no DTO change. Codex picks; both are reasonable.
