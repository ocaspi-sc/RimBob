# Plan: Turn the new-colony-1 reach targets green (the rule-side gaps)

Status: PLAN (design-only). Claude writes the plan + slice map; Codex lands each slice via `bring-out-the-gimp`. Claude never edits source on master.

## Motivation

`.plans/advice-for-new-colony-1.md` froze one real day-1 world (`Src/Tests/NewColony/Fixtures/new-colony-1.colony-state.json`, LFS) and wrote the **target** day-1 advice as assertions. Eight of those assertions are tagged `[Trait("kind", "reach")]` and are RED on purpose — they encode where Chef/Welfare/Willie advice *should* land, not where it is. That suite deliberately changed **no rules**; this plan does the rule-side follow-up: turn the reach reds green, one bounded slice at a time.

The acceptance tests are already written and committed. Each slice's "done" is: its named reach test(s) pass, the slice **removes** the `[Trait("kind", "reach")]` tag and the `// REACH` comment from the test(s) it now satisfies (so the test rejoins the default `kind!=reach` gate and guards against regression), and the full `kind!=reach` gate stays green.

## The eight reach targets, grouped by root cause

Run `dotnet test Src/RimBob.sln --filter "kind=reach"` to see them RED (8 today). They collapse into **6 slices** — two Willie reaches share one root, and freezer rides along with the Chef priority calibration:

| # | Slice | Reach test(s) it turns green | Primary file(s) | Size |
|---|---|---|---|---|
| 1 | Chef food-stockpile rule (NEW) | `Chef_NewColony1_DesignatesFoodStockpile` | `Src/Ministers/Food/Rules.cs` | M |
| 2 | Chef unforbid raw food | `Chef_NewColony1_UnforbidsForbiddenFungus` | `Src/Ministers/Food/Rules.cs` | S |
| 3 | Welfare temperature emits heater | `Welfare_NewColony1_EmitsTemperature` | `Src/Ministers/Welfare/Rules.cs` | S |
| 4 | Chef day-1 priority calibration | `Chef_NewColony1_GrowingCapacityIsNotHigh`, `Chef_NewColony1_DoesNotPushFarHunt` | `Src/Ministers/Food/Rules.cs` | M |
| 5 | Food-buffer: honest edible buffer + latent figure (Option C) | `Snapshot_DerivesTargetFoodBuffer` | `Src/StateStore/Derivations/FoodBriefingDerivation.cs` | M |
| 6 | Willie snapshot-mode anchor/coverage | `Willie_NewColony1_Standalone_FlagsMissingKitchen`, `Willie_NewColony1_PlacesHighPriorityBeds` | `Src/Ministers/Willie/Rules.cs`, Willie briefing derivation | L |

Recommended landing order: **1 → 2 → 3 → 4 → 5 → 6** (rising cost; slice 5 should follow slice 2 because both concern forbidden food; slice 6 is the heaviest and most independent). Slices are otherwise independent and each can land alone; the only shared file is `Food/Rules.cs` (slices 1, 2, 4) — land those in sequence to avoid rebases, or in any order with a clean rebase since they touch different rules.

---

## Slice 1 (detailed): Chef `food_stockpile_missing` rule

**Gap.** On the fixture: 57 meals, `StockpileCells == 0`, meals unpositioned, healthy 10.7-day buffer. `SetStockpileZone` is emitted today only inside `BuildNutritionSignalGap` (`Food/Rules.cs:93`) and `BuildUnknownFoodState` (`:122`), both gated on `briefing.EstimatedDaysOfFood is null` (`MatchesNutritionSignalGap` `:85`, `MatchesUnknownFoodState` `:114`). At a known healthy buffer `EstimatedDaysOfFood` is non-null, so neither fires and no stockpile advice is produced — even though there is food and nowhere reachable to store it. `Tasks.md` explicitly flags "why not tiles marked as storage?".

**Briefing inputs already available** (`Src/Common/Briefings/FoodBriefing.cs`):
- `MealsCount` (`:17`), `RawFoodCount`, `StockpileCells` (`:26`).
- `Storage.UnpositionedFoodUnits` / `PositionedFoodUnits` / `HasFoodPlacementSignal` (`:236–244`).
- `DataCoverage.HasZoneCells` (`:84`).

No derivation change needed — the signal is already in the briefing.

**Change.** Add a new rule to the Chef rule list (`Food/Rules.cs:74–82`):

```
new("food_stockpile_missing", MatchesFoodStockpileMissing, FoodStockpileMissingReason,
    briefing => BuildFoodStockpileMissing(briefing, now)),
```

- `MatchesFoodStockpileMissing(briefing)` = there is food to store and nowhere to store it, at a known buffer:
  `briefing.EstimatedDaysOfFood is not null` (don't overlap the nutrition-gap/unknown paths)
  `&& (briefing.MealsCount + briefing.RawFoodCount) > 0`
  `&& briefing.StockpileCells == 0` (confirm against `Storage.UnpositionedFoodUnits > 0` at impl; pick the field that most precisely means "food exists but no reachable stockpile cell").
- `BuildFoodStockpileMissing` emits one `SetStockpileZone` action (owner Willie) **plus** a `BuildingClass.Stockpile` request routed to Willie, reusing `StockpileVisibilityRequest` (`Food/Rules.cs:710`). Priority **Medium–High** (high-value day-1 basic; pick Medium unless the buffer is also thin).
- Body/reason: name that meals are unpositioned with zero stockpile cells; instruct designating a reachable food stockpile near the meals.

**Guard against double-emit.** `food_stockpile_missing` must be mutually exclusive with `nutrition_signal_gap`/`unknown_food_state` (those own the null-buffer case; this owns the healthy-buffer case). The `EstimatedDaysOfFood is not null` clause already does this — verify no other rule emits `SetStockpileZone` when the buffer is known.

**Acceptance.** `Chef_NewColony1_DesignatesFoodStockpile` (`AdviceForNewColony1Tests.cs:64`) asserts a `SetStockpileZone` action with `Owner == "Willie"` and a `BuildingClass.Stockpile` request from Willie. On land, **remove** that test's `[Trait("kind", "reach")]` + `// REACH` line. Add a focused unit test in `Src/Tests/Food/` for the new rule (fires at healthy buffer + 0 stockpile cells; does **not** fire when `StockpileCells > 0` or buffer is null).

**Out of slice 1.** No change to the nutrition-gap/unknown-food paths; no derivation change; no new briefing field.

---

## Slice 2 (stub): Chef unforbid raw food

**Gap.** `ForbiddenMealUnforbidTargets` (`Food/Rules.cs:1199`) filters `briefing.UnforbidTargets` to `Kind == "meal"` only, so the 49 forbidden `RawFungus` (derived with `Kind == "raw_food"`, see `FoodBriefingDerivation.DeriveUnforbidTargets:196`) are dropped — free food stays excluded. The derivation already surfaces them (`Snapshot_Loads_And_DerivesThreeBriefings` asserts `UnforbidTargets` contains `RawFungus`/`raw_food`, and is green).

**Change.** Widen the predicate at `:1201` to accept edible forbidden food (`"meal"` **or** `"raw_food"`). Audit the `ForbiddenMeal*` naming cluster (`:1196–1260`, `ForbiddenMealItemRequest`, labels) — either generalize the names or keep them and just widen the filter; prefer a rename to `ForbiddenEdibleUnforbidTargets` for honesty since it no longer means meals only. Ensure the `ItemRequest` path (`ForbiddenMealItemRequest`) also covers raw food so the flag carries the fungus quantity.

**Acceptance.** `Chef_NewColony1_UnforbidsForbiddenFungus` (`:90`) — an `Unforbid`/`UnforbidThingsApply` action targeting `RawFungus` and a flag `ItemRequest` for `RawFungus` qty 49. Untag on green.

**Note.** Conceptually paired with slice 5 (both about forbidden food). This slice changes only the **advice** (unforbid the fungus); slice 5 changes the **buffer accounting**.

---

## Slice 3 (stub): Welfare `temperature_comfort` emits a heater on this fixture

**Gap.** The rule exists and *should* match: `MatchesTemperatureComfort` (`Welfare/Rules.cs:252`) fires when the Temperature thought group's `WorstOffset <= -3`, and the fixture's group is `-4` with `PawnCount == 2` (asserted green in `Snapshot_Loads…:41`). Yet `Welfare_NewColony1_EmitsTemperature` (`:193`) is RED — it wants trace `rules:shelter_floor+temperature_comfort`, a `welfare_temperature_comfort` advice, and a **Heater** building request. Most likely cause: `SniffTemperatureDirection` (`:331`) does not classify the fixture's example label ("Chilly" / `EnvironmentCold`) as `Cold`, so direction is `Ambiguous` (`:280`) → advice emitted but **no Heater request**. Confirm at impl by inspecting the derived `ExampleLabel`.

**Change.** Make the cold/heat keyword sniff recognize the real RimWorld labels present in the fixture ("Chilly", `EnvironmentCold`) as `Cold`. Keep it a keyword widening, not a special-case for this fixture. If the true cause turns out to be the trace string or the group not being derived, fix that instead — the test pins the observable, not the mechanism.

**Acceptance.** `Welfare_NewColony1_EmitsTemperature` green (trace + advice + Heater→Willie). Untag on green. Add/extend a Welfare unit test that "Chilly"/`EnvironmentCold` → `Cold` → Heater.

---

## Slice 4 (stub): Chef day-1 priority calibration (growing + hunt + freezer)

**Gap.** With a healthy 10.7-day buffer and 44 days to winter, day-1 doctrine wants long-game items demoted and far/no-payoff items suppressed:
- `expand_growing_capacity` is emitted **High** with an "emergency rice" framing (`BuildExpandGrowingCapacity`, `Food/Rules.cs:300`); target ≤ **Medium** and drop the emergency framing at a healthy buffer.
- `hunt_low_risk_animals` (`:259`) fires Medium and emits butcher+campfire builds for a 119–139-cell hunt; target **Low or suppressed**, and **no** premature `Butcher` production-bench request when targets are far and the buffer is healthy.
- `freezer_missing` (`:351`) at Medium is premature when the only food is 57 non-spoiling packaged meals; target **Low/deferred**. (No reach test exists for freezer yet — either add `Chef_NewColony1_FreezerNotHigh` or fold the demotion in untested and note it.)

**Change.** Tie these priorities to buffer health + target distance (inputs already in the briefing: days-of-food, hunt-target distances). Likely a shared "day-1 / healthy-buffer" gate that demotes the long-game rules and the far-hunt build. Keep each rule's *match* intact where the advice is still wanted (growing, hunt-as-info); change *priority* and the **build requests** they emit.

**Acceptance.** `Chef_NewColony1_GrowingCapacityIsNotHigh` (`:151`, priority ≤ Medium) and `Chef_NewColony1_DoesNotPushFarHunt` (`:162`, hunt Low if present + no Butcher bench request). Untag both on green. Guard against regressing the existing higher-pressure food tests (a thin buffer must still escalate growing/hunt).

---

## Slice 5 (stub): food-buffer — honest edible buffer + latent figure (Option C, chosen 2026-06-06)

**Gap.** The fixture's 57 `MealSurvivalPack` stacks are all **forbidden**. `FoodBriefingDerivation` excludes forbidden items from the nutrition count (`:165`, `.Where(item => !item.IsForbidden …)`), so the buffer derives as ~0.51 food-days (`NutritionSource: reported`). The original `Snapshot_DerivesTargetFoodBuffer` (`:51`) was written for "count them" (≈10.7 days, `item_def_catalog`, 57 meals, 0 raw).

**Decision — Option C.** Don't conflate the two truths. Keep `EstimatedDaysOfFood` **honest = currently-edible food only** (forbidden stays excluded → ~0.5 days here), and add a **separate latent figure** for forbidden-but-edible nutrition (~10.2 days sitting behind the forbidden meals). Both surface: the headline buffer never lies about what colonists can eat right now, and the latent figure tells the player — and the rules — how much food is one unforbid-click away. Rejected: (A) counting forbidden food into the headline buffer (the colony "feels safe" on food it can't eat), (B) reporting only the low buffer with no latent signal (hides the ~10 days that physically exist).

**Change.**
- Derivation: add a latent field to `FoodBriefing` (e.g. `ForbiddenEdibleFoodDays` / `LatentFoodDays`, name TBD at impl) computed from forbidden-but-edible items; leave `EstimatedDaysOfFood` excluding forbidden.
- Dashboard: surface the latent figure beside days-of-food (e.g. "0.5 days edible · +10.2 behind forbidden") — required by the dashboard-visibility rule.
- Couples to slice 2: the latent figure is exactly what slice 2's unforbid advice converts into real buffer. Land slice 2 first.

**Acceptance.** **Rewrite** `Snapshot_DerivesTargetFoodBuffer` to assert the honest split: `EstimatedDaysOfFood` low (currently-edible, forbidden excluded) **and** the new latent field ≈ the forbidden-meal nutrition (~10.2 days / 57 meals). Untag on green. Add a derivation unit test for the edible/latent split (a forbidden edible stack counts latent, not edible; unforbidding it moves it to edible).

---

## Slice 6 (stub): Willie snapshot-mode anchor / buildable-region coverage

**Gap (one root, two reaches).** In snapshot mode the Willie briefing has `hasLiveState:false`, `hasAnchorInventory:false`, `functionalRooms:{}`, `anchors:[]`.
- `Willie_NewColony1_Standalone_FlagsMissingKitchen` (`:215`) is RED because `kitchen_missing` requires coverage — `MatchesMissingRoom` is guarded by `HasCoverage = DataCoverage.HasAnchorInventory || FunctionalRooms.RoomCountsByClass.Count > 0` (`Willie/Rules.cs:450`); both are empty in snapshot mode, so the rule can't fire.
- `Willie_NewColony1_PlacesHighPriorityBeds` (`:227`) is RED because, given an inbound High beds-barracks request, `building_request_active` selects but the `PlacementSolver` returns no-fit: with empty `AnchorInventory.Anchors` it has nothing to place against.

**Change (data coverage, the hard one).** Give snapshot-mode Willie enough placement evidence to (a) treat an empty room inventory as "rooms missing" rather than "unknown", and (b) offer ≥1 placement option from buildable terrain/region data already in the `ColonyState` snapshot (52,020 growable cells, terrain present) when no anchor inventory exists — a buildable-region fallback for the solver. This is a Willie-briefing-derivation + solver-input change, not just a rule tweak; scope it carefully and consider splitting (6a: kitchen_missing coverage signal; 6b: solver buildable-region fallback).

**Acceptance.** Both Willie reach tests green; untag on green.

**Accurate framing (corrects the earlier "butcher beat beds / one placement attempt" note).** The solver already runs **per request**: `MinisterOfWillie.RunPlayCycle` solves the driving request, every missing-room request, then **every** remaining inbound request on the board via `foreach (… in solveBoard)` (`MinisterOfWillie.cs:123–142`), each recorded to `solverStore`. The only single-select is the headline `building_request_active` advice card, chosen by `SelectPlacementRequest` (priority-descending, `Rules.cs:479`) — beds (High) headline over a butcher (Medium) whenever both are inbound. So this slice's real gaps are: (a) **snapshot-mode no-fit** — empty `AnchorInventory.Anchors` means the solver can't place *any* request, beds included (this is the rule/data fix); and (b) **whether Welfare's bed request reaches Willie in the same cabinet cycle** — routing/ordering in `CabinetCycle`, a **separate task**, not fixed here. Neither is "Willie gets one placement attempt."

---

## Cross-cutting rules for every slice

- **Untag on green.** When a slice makes a reach test pass, delete that test's `[Trait("kind", "reach")]` attribute and its `// REACH …` comment so it joins the default gate. When the last reach test is untagged, remove/adjust the reach-set note in `AGENTS.md` (`Repo Conventions`) and in `.plans/advice-for-new-colony-1.md`.
- **Keep the gate honest.** `dotnet test Src/RimBob.sln --filter "kind!=reach"` must stay green after each slice, and the reach count (`--filter "kind=reach"`) must drop by exactly the number of tests that slice targets.
- **No wire/persistence/compat change; no snapshot recapture.** All six slices are rule/derivation logic over the existing schema-6 fixture. No `.gitattributes`/LFS/corpus touch.
- **Per-slice unit coverage.** Each slice adds or extends a minister-level unit test (synthetic briefing) for the new behaviour, so the fix is pinned independently of the one scenario fixture (the scenario test proves it end-to-end; the unit test proves the logic).

## Verify (per slice, at land, via gimp)

1. `dotnet test Src/RimBob.sln --filter "FullyQualifiedName~<TargetTest>"` → green.
2. `dotnet test Src/RimBob.sln --filter "kind!=reach"` → still green (now +N tests as the untagged ones join).
3. `dotnet test Src/RimBob.sln --filter "kind=reach"` → count dropped by N.
4. Launch RimBob from the worktree on a non-5000 port and confirm the HOME day-1 advice shifted as intended (stockpile/unforbid/warmth surface; far-hunt/freezer recede) per the worktree-launch rule.

## Dashboard

No new panel. Each landed slice should visibly change the new-colony advice on HOME; verify in the dashboard from the worktree build before merge.

## Tasks.md

Add one umbrella entry linking this plan, with the six slices as checkable sub-items so they can be landed and ticked independently.
