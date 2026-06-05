# Plan: Fix "Mark forage" apply — share harvest-readiness predicate

## Motivation

User report (verbatim): *"Chef: Apply Mark Forage doesnt work. it says 'Too many harvest targets are no longer ready in the original area.' instead."*

The Chef minister emits a **Mark forage** advice action (kind `mark_harvest_area`) listing exact wild-plant `TargetIds`. Pressing **Apply** always fails with `stale_advice` → "Too many harvest targets are no longer ready in the original area." Forage apply is effectively dead.

## Root cause

The advice **generator** and the assisted-apply **validator** use two different readiness predicates, and they disagree for forageable wild plants.

- Generator readiness — [Src/StateStore/Derivations/FoodBriefingDerivation.cs:491](Src/StateStore/Derivations/FoodBriefingDerivation.cs):
  ```csharp
  private static bool IsHarvestReady(PlantRecord plant) =>
      plant.IsHarvestable ?? plant.Growth >= 0.85f;
  ```
  Forage targets are selected through this (`ReadyFoodForagePlants` → `DeriveHarvestTargets`), so a wild berry/agave/healroot whose live `IsHarvestable == true` is included even at growth **below** 0.85. Those plant ids become the advice `TargetIds`.

- Apply validator — [Src/ApiHost/AssistedApplyService.cs:271](Src/ApiHost/AssistedApplyService.cs):
  ```csharp
  IReadOnlyList<PlantRecord> readyInRect = currentTargets
      .Where(plant => plant.Position is not null &&
                      plant.Growth >= 0.85f &&
                      IsInside(apply.Rect, plant.Position))
      .ToList();
  ...
  int stale = apply.TargetIds.Count - readyInRect.Count;
  if (stale > AllowedMissing(apply.TargetIds.Count))
      return Response("stale_advice", "Too many harvest targets are no longer ready in the original area.", ...);
  ```
  The validator gates on **`plant.Growth >= 0.85f` only** — it ignores `IsHarvestable`. Every forage plant harvestable below 0.85 growth is counted not-ready → `stale` ≈ full target count → exceeds `AllowedMissing` → `stale_advice`.

`PlantRecord` carries the flag — [Src/Common/Aggregates/Snapshots.cs:206](Src/Common/Aggregates/Snapshots.cs) `IsHarvestable` (`bool?`), populated from RIMAPI `LiveIsHarvestable` ([Src/GameStateSync/Dtos/MapDto.cs:179](Src/GameStateSync/Dtos/MapDto.cs), mapped at [Src/StateStore/Ingestion/MapAggregateMapper.cs:46](Src/StateStore/Ingestion/MapAggregateMapper.cs)). Both sides consume the **same** `PlantRecord`; only the predicate differs.

Crop "Mark harvest" mostly escapes because mature crops sit at growth ≈ 1.0 ≥ 0.85, but it is subject to the same latent drift (a crop harvestable below 0.85 would also be wrongly rejected).

## Context

Files in play:

- [Src/ApiHost/AssistedApplyService.cs](Src/ApiHost/AssistedApplyService.cs) — `ApplyHarvestAsync`, the failing validator (lines ~268–285). Already `using RimBob.Core.Aggregates;`.
- [Src/StateStore/Derivations/FoodBriefingDerivation.cs](Src/StateStore/Derivations/FoodBriefingDerivation.cs) — `IsHarvestReady` (491), `ReadyFoodForagePlants` (484), `DeriveHarvestTargets` (240+). Generator side of the predicate.
- [Src/Common/Aggregates/Snapshots.cs](Src/Common/Aggregates/Snapshots.cs) — `PlantRecord` (`IsHarvestable`), namespace `RimBob.Core.Aggregates`.
- [Src/Ministers/Food/Rules.cs:927](Src/Ministers/Food/Rules.cs) — `HarvestApply` builds the `MarkHarvestAreaApply` payload (`TargetIds = target.PlantIds`).
- [Src/Tests/ApiHost/AssistedApplyServiceTests.cs](Src/Tests/ApiHost/AssistedApplyServiceTests.cs) — existing harvest-apply tests (currently crop-only, growth = 1.0).

## Scope

In scope:

1. **Extract one shared readiness predicate** so generator and validator can't drift again. Add a small static helper in `RimBob.Core.Aggregates` (next to `PlantRecord` in `Snapshots.cs`, or a sibling file Codex chooses), e.g.:
   ```csharp
   public static class PlantHarvest
   {
       public const float DefaultHarvestMinGrowth = 0.85f;
       public static bool IsReady(PlantRecord plant) =>
           plant.IsHarvestable ?? plant.Growth >= DefaultHarvestMinGrowth;
   }
   ```
2. **Validator uses it** — in `ApplyHarvestAsync`, replace the inline `plant.Growth >= 0.85f` in the `readyInRect` predicate with `PlantHarvest.IsReady(plant)` (keep the `Position is not null` and `IsInside(apply.Rect, ...)` guards).
3. **Generator delegates to it** — `FoodBriefingDerivation.IsHarvestReady` becomes (or calls) `PlantHarvest.IsReady`, retiring the duplicated literal. Behavior-identical for generation.
4. Tests (below).

Out of scope / non-goals:

- **Do not change the `missing` check** ([AssistedApplyService.cs:276](Src/ApiHost/AssistedApplyService.cs)) — that counts plants entirely gone from the snapshot (`TargetIds.Count - currentTargets.Count`), which is correct and unrelated.
- **Do not touch `MaxHarvestTargets` / `MaxHarvestRectArea` / `AllowedMissing`** tolerances — the bug is the predicate, not the thresholds.
- **Do not change the hunt path** (`ApplyHuntAsync`) — different domain (animals), no growth predicate.
- **No RIMAPI / ingestion / wire / payload change.** `IsHarvestable` already flows through; `TargetIds`/`Rect` unchanged. No persisted-snapshot or replay-corpus regen.
- **Do not retune `0.85f`** — keep it as the documented fallback for when `IsHarvestable` is null (older snapshots / probe gaps).

## Approach

Codex picks exact names/placement; shape only:

1. Add `PlantHarvest.IsReady` (+ `DefaultHarvestMinGrowth` const) in `RimBob.Core.Aggregates`. One spot, reviewable, the single source of truth for "is this plant harvest-ready right now."
2. Repoint `FoodBriefingDerivation.IsHarvestReady` to the shared helper (keep the private wrapper if it reads better, but its body must be the shared call — no second literal).
3. In `ApplyHarvestAsync`, swap the `readyInRect` growth literal for `PlantHarvest.IsReady(plant)`. Nothing else in that method moves.
4. Confirm no other site hardcodes a harvest-readiness `Growth >= 0.85f` for the same purpose; if `DeriveHarvestTargets`/`ReadyFoodForagePlants` route through `IsHarvestReady` already (they do), they inherit the fix.

## Tests

In [Src/Tests/ApiHost/AssistedApplyServiceTests.cs](Src/Tests/ApiHost/AssistedApplyServiceTests.cs):

1. **Forage apply succeeds below 0.85 growth (regression for this bug).** Publish a `mark_harvest_area` advice whose `TargetIds` are wild plants with `IsHarvestable: true, Growth: 0.4f`, positions inside `Rect`. Expect status `applied` (not `stale_advice`); assert `target_count` equals the ready count and `DesignateAreaAsync` was invoked.
2. **Still stale when genuinely not ready.** Targets with `IsHarvestable: false` (or null) and `Growth: 0.4f` → `stale_advice` "Too many harvest targets are no longer ready in the original area." (predicate still rejects truly-unready plants).
3. **Crop happy path unchanged.** Existing growth = 1.0 crop test still returns `applied`.
4. (Optional) **Null `IsHarvestable` falls back to 0.85.** `IsHarvestable: null, Growth: 0.9f` → ready; `Growth: 0.4f` → not ready.

Add/confirm a `FoodBriefingDerivation` test that a forage plant with `IsHarvestable: true, Growth < 0.85` is still selected as a `HarvestTarget` (guards the generator side post-refactor) — existing fixtures around [FoodBriefingDerivationTests.cs:291](Src/Tests/Food/FoodBriefingDerivationTests.cs) already construct `PlantRecord(..., IsHarvestable: true)`, so extend rather than invent.

## Risk

Low. One predicate unified; the change only **widens** apply acceptance to match what the generator already promised, plus retires a duplicated literal. No data-shape change. The only behavioral delta is the intended one: forage (and any sub-0.85 harvestable crop) now applies instead of falsely reporting stale.

## Summary (landed 2026-06-05, direct edit — no gimp)

Implemented directly on master main checkout (user said "do it no gimp"), not via Codex. Uncommitted in the working tree pending the user's live dashboard verification.

**What changed (4 files):**

- [Src/Common/Aggregates/Snapshots.cs](Src/Common/Aggregates/Snapshots.cs) — new `PlantHarvest.IsReady(plant)` + `PlantHarvest.DefaultHarvestMinGrowth = 0.85f` in `RimBob.Core.Aggregates`: the single source of truth for "is this plant harvest-ready now" (`plant.IsHarvestable ?? plant.Growth >= 0.85f`).
- [Src/StateStore/Derivations/FoodBriefingDerivation.cs](Src/StateStore/Derivations/FoodBriefingDerivation.cs) — `IsHarvestReady` now delegates to `PlantHarvest.IsReady` (behaviour-identical; kills the duplicated literal).
- [Src/ApiHost/AssistedApplyService.cs](Src/ApiHost/AssistedApplyService.cs) — `ApplyHarvestAsync` `readyInRect` predicate swapped from `plant.Growth >= 0.85f` to `PlantHarvest.IsReady(plant)`. **This is the fix:** forage plants reporting `is_harvestable=true` below 0.85 growth are no longer counted stale.
- [Src/Tests/ApiHost/AssistedApplyServiceTests.cs](Src/Tests/ApiHost/AssistedApplyServiceTests.cs) — made `MinimalRefreshHandler.MapPlantsJson` configurable; added two tests: forage harvestable-below-threshold → `applied` (faithful repro: 1 of 4 ≥0.85, old code would `stale_advice`), and forage `is_harvestable:false` → no designation (predicate still gates).

**Verification:** `dotnet build Src/RimBob.sln` clean; full suite **577 passed / 0 failed**. Host rebuilt and restarted on port 5000, `rimapi_reachable:true`.

**Not done:** not committed (left for live verify + user go-ahead); no RIMAPI/wire/replay-corpus change, as planned.
