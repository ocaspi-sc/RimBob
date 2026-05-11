# Minister of Food - Minister Design

> **Living document.** See `AGENTS.md` for update rules.
> Slice: M3 - first feeder advisor for the Mayor's daily memo. Replaces the old Agriculture minister.
> Implementation status: first runtime slice is `FoodBriefing` + rules-first `MinisterOfFood` with Gemini escalation, `AdviceItem` output, and flags into the Mayor.

---

## Domain

Food security across the full nutrition chain:

- Acquisition: crops, wild harvest, hunting-for-food, emergency trade flags.
- Processing: butchering, cooking, meal mix, food-preserving bills.
- Storage: food stockpiles, freezer capacity, freezer temperature, spoilage risk.
- Recovery: blight, cold snap, food poisoning, lost freezer, caravan drain, post-raid interruptions.

Food is broader than Agriculture. Farming is only one method inside the food chain; the minister is accountable for whether the colony can keep eating.

Food does not own pawn allocation. It may request labor capacity, e.g. cooks, growers, hunters, haulers, but in Suggest mode that request is advice to the player. The deferred Labor minister owns actual pawn assignment once Auto exists.

M3 runtime path:

1. `CabinetCycle` refreshes ingestion, runs Food, then runs the Mayor.
2. Food reads `FoodBriefing` from `BriefingCache`, evaluates `Rules.cs`, and emits `AdviceItem`s plus optional `AgentFlag`s.
3. If rules return `Escalate`, Food calls Gemini with `food.system.md`, `FoodBriefing`, `MinisterBriefingContext`, and food-focused `guide_context`.
4. The Mayor reads active Medium+ flags and reflects relevant Food pressure in `state_of_the_union.food`, `update_notes`, and short-term priorities.

---

## Advice types

Closed enum draft for M3:

```csharp
public enum FoodAdviceType
{
    FoodSecurity,
    ExpandGrowingCapacity,
    HarvestNow,
    WildHarvest,
    HuntForFood,
    ManageCookBills,
    ManageButcherBills,
    ManageFreezer,
    ManageFoodStockpile,
    TradeForFood,
    RecoverFromFoodEvent
}
```

Adding an advice type is a design decision because it becomes a future autonomy-dial unit.

---

## Briefing

See [`design/state-store.md`](../state-store.md#food) for the full field list.

M3 implemented facts:

- `EstimatedDaysOfFood`: reported nutrition when available, otherwise conservative meal/raw-food fallback.
- `NutritionSource`: `reported`, `fallback_meal_raw_counts`, or `unknown`.
- `FoodUnits`, `MealsCount`, `RawFoodCount`, `ReadyToHarvest`, crop breakdown.
- `WildHarvestCandidates` and `WildAnimalCount` as first-pass opportunity counts.
- Plants/Cooking skill coverage, stockpile cells, cooler count, net power, active threat, recent food incidents.

Deferred from the richer target briefing: exact zone-level yield, distance/risk scoring, kitchen/butchery bills, room temperature, item spoilage, and caravan provisioning.

---

## Rules layer (target ~80% coverage)

Known first-pass rules:

| Rule | Condition | Output |
|---|---|---|
| `maintain_security_threshold` | DaysOfFood >= 30 AND no urgent spoilage/harvest issue | No advice |
| `nutrition_signal_gap` | Food units exist but nutrition is unknown/fallback-derived | ManageFoodStockpile, stockpile audit |
| `emergency_food_flag` | DaysOfFood < 7 | FoodSecurity High, flag Mayor |
| `low_food_flag` | DaysOfFood < 15 | FoodSecurity High |
| `harvest_mature_crops` | MatureCropTiles > 0 OR wild harvest at rot/freeze risk | HarvestNow |
| `plant_before_winter` | DaysToWinter < 20 AND growing capacity available | ExpandGrowingCapacity, prefer rice unless guide/context says otherwise |
| `hunt_if_low_no_crops` | DaysOfFood < 20 AND no timely harvest path | HuntForFood |
| `meals_understocked` | MealsPerColonist < 5 AND DaysOfFood > 7 | ManageCookBills, request cooking labor |
| `meal_quality_upgrade` | MealsPerColonist >= 10 AND CookSkillLevel >= 6 AND DaysOfFood > 30 | ManageCookBills, fine meals acceptable |
| `meal_quality_downgrade` | DaysOfFood < 20 OR active raid | ManageCookBills, simple meals only |
| `butcher_backlog` | Butcherable corpses/meat backlog and safe kitchen | ManageButcherBills |
| `freezer_at_capacity` | FreezerCapacity.Used > 90% | ManageFreezer or ManageFoodStockpile |
| `spoilage_risk` | FreezerTemperature > 0C for stored perishables | ManageFreezer Critical |

Escalates when:

- Crop choice involves real trade-offs: rice vs potatoes vs corn vs hydroponics.
- Hunting target value/risk is ambiguous.
- Multiple food-chain bottlenecks compete: no cooks, full freezer, low raw food, active threat.
- Unusual food event appears: blight, toxic fallout, heat wave/freezer loss, animal revenge risk.
- Drug/textile crops compete with food crops.
- Caravan/trade food policy matters.

M3 severity calibration: Food emits `High` for urgent shortage by default. `Critical` is reserved for true immediate starvation evidence, not just a low buffer.

---

## Resource requests

Food may request:

- Tiles: growing zone area, wild harvest area, freezer expansion, food stockpile space.
- Labor: growing, plant cutting, hunting, cooking, butchering, hauling.
- Items/buildings: coolers, butcher table, fueled/electric stove, shelves, power support.
- Bills/settings: cook bill targets, butcher bill state, stockpile filters, forbid/unforbid food.

In MVP, these are surfaced as suggested actions and flags. They are not writes. In Auto, these become inputs to HTN/Labor/RIMAPI execution.

---

## Owned action families

Clear-cut Food ownership:

| Action family | Food ownership |
|---|---|
| Growing zones for food crops | Owns size, crop, timing, urgency |
| Wild plant harvest for nutrition | Owns target and timing |
| Hunting for nutrition | Owns need and target recommendation; Defense may flag combat risk |
| Butchering | Owns bill need and backlog |
| Cooking | Owns bill type and stock target |
| Food stockpile/freezer | Owns filters, capacity need, spoilage urgency |
| Food emergency trade flag | Owns need; future Trade/Treasury owns execution |

Hard cases:

- Psychoid/smokeleaf/devilstrand: Food can comment on tile opportunity cost, but Trade/Treasury or Welfare may own the strategic reason to grow it.
- Animal breeding/culling/training: likely future Animal/Logistics subdomain; Food only owns slaughter-for-food pressure for now.
- Nutrient paste: Food owns the food-chain recommendation; Welfare owns the mood cost context.
- Caravan provisioning: Food owns nutrition sufficiency; future Trade/Travel owner owns caravan execution.

---

## Success metrics

- `DaysOfFoodRemaining` stays above 20 for >90% of in-game days in year 1.
- No colonist death from starvation in year 1.
- Freezer has food surplus heading into each winter.
- Cooked meals remain stocked without wasting ingredients during shortage windows.
- Escalation rate below 20% after rule refinement has run.

---

## RAG retrieval profile

Topics: `["food", "farming", "crops", "wild harvest", "hunting", "freezer", "cooking", "nutrition", "spoilage"]`

Retrieval required for: crop choice decisions, seasonal timing, freezer/cooking policy, first devilstrand/drug crop decision, biome-specific food recovery.

---

## Open questions / TODO

- [ ] Define exact DaysOfFood thresholds; 30/20/15/7 are placeholders.
- [ ] Hunting value/risk scoring for target selection.
- [ ] How does Food account for caravan provisioning and food removed from the home map?
- [ ] Decide ownership for drug/textile crops once Trade/Treasury exists.
- [ ] Decide whether animal economy deserves its own minister or stays a Food hard case.
- [ ] Map every RIMAPI food-chain read/write endpoint before Auto graduation.
