# Plan: Apply-verdict coverage on the landed new-colony scenario (close the `ebfaef5` seam)

> **Status update (2026-06-05):** `advice-for-new-colony-1` **LANDED** (`560f73a test(new-colony): land day-one target suite`). It already built the state-rooted harness this plan depended on, and it already covers `state → briefing → advice` for forage from the real snapshot. The dependency is satisfied and the scope below has **shrunk accordingly** — this is now a small add (one production seam + apply-verdict assertions), not a new corpus.

## Motivation

The `forage-apply-readiness` bug (`ebfaef5`) shipped because no test spanned `state → briefing → advice → apply`. The old `FoodReplayCorpusTests` records the already-derived `briefing` and stops at `advice`, blind to both the `state→briefing` selection (`IsHarvestReady`) and the `advice→apply` validation where the bug lived.

`560f73a` closed the **front half**: `Src/Tests/NewColony/AdviceForNewColony1Tests.cs` loads a real frozen `ColonyStateSnapshot` and runs the real pipeline to advice. In particular `Chef_NewColony1_ForagesNearestBerries` (a plain green `[Fact]`) already asserts the snapshot derives `chef_wild_harvest_available` with a `MarkHarvestAreaApply` whose `TargetIds` are non-empty — proving `state→briefing→advice` for forage off the 25 harvestable-below-0.85 berries/healroot in that world.

**The remaining gap is the back half.** That suite is tests-only at the Rules layer; it **never invokes `AssistedApplyService`**, so the apply-side readiness predicate — *exactly where `ebfaef5` lived* — is still unexercised by any historic-data test. Generation emits the right `TargetIds`; nothing asserts the apply layer would *accept* them. Revert `PlantHarvest.IsReady` on the apply side and the whole landed suite stays green.

This plan adds the apply-verdict step on top of the landed harness.

## Context

Landed harness (`560f73a`), all reusable as-is:

- [Src/Tests/NewColony/AdviceForNewColony1Tests.cs](Src/Tests/NewColony/AdviceForNewColony1Tests.cs) — `LoadScenarioAsync` (a shared `Lazy<Task<NewColonyScenario>>`):
  `ColonyStateSnapshotStore.LoadAsync(path) → store.RestoreInto(state) → new BriefingCache(state) → cache.GetFoodBriefing() → new FoodRules().Evaluate(food) → RuleRun.ProjectFor("Chef","food",…) → ProjectedRuleRun`. `NewColonyScenario` already exposes `State`, `Snapshot`, `Food`, `FoodDecision` (and Welfare/Willie). Fixture is copied to a temp dir before load (so an unsupported-schema delete can't touch the committed file).
- [Src/Tests/NewColony/Fixtures/new-colony-1.colony-state.json](Src/Tests/NewColony/Fixtures/new-colony-1.colony-state.json) — the real frozen world, committed via **Git LFS** (`.gitattributes` rule `Src/Tests/NewColony/Fixtures/*.colony-state.json filter=lfs diff=lfs merge=lfs -text`). Contains **25 forage plants `is_harvestable:true` at `growth < 0.85`** (123 `Plant_Berry`, 138 `Plant_Healroot`, +agave/ambrosia) — the `ebfaef5` condition, from real capture.
- `Chef_NewColony1_ForagesNearestBerries` — already green, already proves the forage advice + non-empty `MarkHarvestAreaApply` emit from `scenario.FoodDecision`. **This plan reuses `scenario.FoodDecision` and `scenario.State`; it does not re-derive anything.**
- [Src/ApiHost/AssistedApplyService.cs:243](Src/ApiHost/AssistedApplyService.cs) — `ApplyHarvestAsync`. The pure readiness verdict (compute `currentTargets`/`readyInRect`/`missing`/`stale` over `state.Plants`, plus the `apply.MapId != state.Map.Value.Id` and cap checks → one of the early-return statuses) is interleaved with RIMAPI HTTP (`RefreshForValidationAsync`, `DesignateAreaAsync`, `RefreshForReadbackAsync`). The verdict is pure over `ColonyState + apply`; the HTTP is not.
- [Src/Common/Aggregates/Snapshots.cs](Src/Common/Aggregates/Snapshots.cs) — `PlantHarvest.IsReady` (from `ebfaef5`), the shared predicate both the verdict and the generator already use.

Note: `AdviceForNewColony1Tests` is in `RimBob.Tests` and references `RimBob.Host` (the suite already uses `RimBob.State`, `RimBob.Core.*`; `AssistedApplyService` lives in `RimBob.Host`, which the test assembly already references via other ApiHost tests). So a pure `AssessHarvest` in `RimBob.Host` is callable from the scenario test.

## Scope

In scope:

1. **Extract a pure apply-verdict seam** from `ApplyHarvestAsync`:
   ```csharp
   // RimBob.Host
   public enum HarvestApplyOutcome { Ready, AlreadySatisfied, StaleMissing, StaleNotReady, WrongMap, TooBroad }
   public readonly record struct HarvestApplyAssessment(HarvestApplyOutcome Outcome, int ReadyCount, int MissingCount, int StaleCount);
   public static HarvestApplyAssessment AssessHarvest(ColonyState state, MarkHarvestAreaApply apply);
   ```
   It folds the cap check, map check, `missing`/`already-satisfied`/`stale` logic — reusing `PlantHarvest.IsReady`, `IsInside`, `AllowedMissing`, `AssistedApplyLimits`. `ApplyHarvestAsync` calls it *after* its live refresh and maps the outcome to the existing response statuses — **behaviour-identical** (same statuses, same precedence: caps → map → missing → already-satisfied → stale → write). This also removes the remaining interleave where a third copy of the predicate could drift.
2. **Apply-verdict assertions on the landed scenario.** Extend `AdviceForNewColony1Tests` (or a sibling `NewColonyApplyTests` sharing the same `Scenario`) so that, for the forage advice already asserted by `Chef_NewColony1_ForagesNearestBerries`:
   - pull the `MarkHarvestAreaApply` from `scenario.FoodDecision`,
   - assert `AssessHarvest(scenario.State, apply).Outcome == HarvestApplyOutcome.Ready`.
   This is the **direct `ebfaef5` regression**: with the fix, `Ready`; revert either `PlantHarvest.IsReady` call site and it flips to `StaleNotReady`/`AlreadySatisfied`. Plain `[Fact]`, green.
3. **(Optional, cheap) generalize** to a small loop: for every emitted `MarkHarvestAreaApply` across `scenario.FoodDecision`, assert `AssessHarvest` is a non-stale terminal (`Ready`/`AlreadySatisfied`). Future scenario snapshots inherit the check.

Out of scope / non-goals:

- **No new corpus format, runner, capture helper, or fixture authoring.** `560f73a` already provides the snapshot, the LFS rule, the `LoadAsync→RestoreInto→BriefingCache→Evaluate` harness, and the `Lazy<NewColonyScenario>`. Reuse, don't rebuild.
- **No live-RIMAPI / HTTP replay harness.** The pure `AssessHarvest` seam is the boundary; the real `DesignateAreaAsync` write + readback stay HTTP and remain covered by `AssistedApplyServiceTests`.
- **Do not retune** the existing `AssistedApplyServiceTests`; they must stay green to prove the extraction is behaviour-identical (the `ebfaef5` forage tests there can optionally be reframed to assert through `AssessHarvest`, but that is not required).
- **Harvest apply only.** Structure `Assess*` so hunt/unforbid can follow, but do not wire them (hunt apply is being reworked — `mark-hunt-apply-per-animal`).
- **No schema/wire/replay-corpus change.** New production helper + tests only.

## Approach

Codex picks exact names/placement; shape only:

1. Extract `AssessHarvest` from `ApplyHarvestAsync`; repoint `ApplyHarvestAsync` to it (behaviour-identical). Add focused `AssessHarvest` unit tests (ready / already-satisfied / stale-missing / stale-not-ready / wrong-map), including forage `is_harvestable:true` below 0.85 → `Ready`.
2. Add the scenario apply-verdict assertion(s) to the `NewColony` suite, reusing `scenario.State` + `scenario.FoodDecision`.
3. Confirm `AssistedApplyServiceTests` + `AdviceForNewColony1Tests` stay green.

## Tests

- `AssessHarvest` unit tests (the verdict matrix).
- `NewColony` forage apply-verdict: `AssessHarvest(scenario.State, forageApply).Outcome == Ready` — the `ebfaef5` regression off real captured state.
- Behaviour-identical guard: existing `AssistedApplyServiceTests` unchanged and green.

## Dependencies & sequencing

- **Dependency satisfied:** `advice-for-new-colony-1` (`560f73a`) is landed. This plan can proceed now.
- **Co-touched file with `hunt-apply-per-animal`:** both edit `Src/ApiHost/AssistedApplyService.cs` — this plan extracts `AssessHarvest` from `ApplyHarvestAsync`; the hunt plan rewrites `ApplyHuntAsync`. Different methods → trivial/no conflict, order-independent. If both run, land either first; the second rebases cleanly.
- **REACH cases:** the landed suite tags target-state tests `[Trait("kind","reach")]` (some intentionally RED pending rule fixes). The forage front-half test this plan builds on (`Chef_NewColony1_ForagesNearestBerries`) is **not** reach-tagged (green). Keep the new apply-verdict tests plain `[Fact]` (green) and ensure the default test run filters `kind!=reach` so a real forage regression here is visible, not masked by an already-red reach set.

## Payoff

One historic-data test now spans `state → briefing → advice → apply-verdict` end to end. Any future drift between plant-selection and apply-validation fails a cheap, deterministic, LLM-free assertion off a real captured world — which is what "an integration test from historic data" was supposed to guarantee, and what `ebfaef5` slipped past.
