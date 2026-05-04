# Minister of Agriculture — Minister Design

> **Living document.** See `CLAUDE.md` for update rules.
> Slice: M3 — first feeder advisor for the Mayor's daily memo. (Was M1 in pre-pivot roadmap.)
>
> ⚠️ **Pivot translation needed.** This doc still uses pre-pivot vocabulary (`goal_id`, HTN compounds). Under the assisted-gameplay pivot ([`../../DESIGN.md`](../../DESIGN.md)), Agriculture emits a sub-briefing + flags into the Mayor's digest, not HTN goals. Translate `goal_id` → `advice_type` (closed enum per minister) and treat HTN compound mentions as Auto-epic concerns. Full rewrite happens when the M3 slice opens.

---

## Domain

Food security. Farming, hunting, cooking, freezer management. Does not manage animals beyond hunting targets.

---

## goal_id enum

```csharp
public enum AgricultureGoal
{
    FoodSecurity,           // maintain days-of-food above threshold
    ExpandGrowingCapacity,  // add growing zones or switch to higher-yield crops
    ExecuteHarvest,         // harvest mature crops before they rot or freeze
    HuntForFood,            // hunting when farming is insufficient
    ManageFreezer,          // freezer temperature, capacity, spoilage prevention
    TradeForFood,           // signal Welfare/Trade that buying food is needed
    RecoverFromFoodEvent    // post-raid, post-cold-snap recovery
}
```

---

## Briefing

See [`design/state-store.md`](../state-store.md#agriculture) for full field list.

Key derived facts:
- `DaysOfFoodRemaining`: stockpile nutrition / average daily consumption rate
- `PlannedHarvestWindow`: crops with growth > 90% and their estimated yield
- `ColdSnapForecast`: from weather data + season; if within 15 days, fast-track harvest

---

## Rules layer (target ~80% coverage)

Known rules (first pass — refine in dedicated session):

| Rule | Condition | Output |
|---|---|---|
| `maintain_security_threshold` | DaysOfFood >= 30 AND no mature crops | No-op goal |
| `harvest_mature_crops` | MatureCropTiles > 0 | ExecuteHarvest (High) |
| `plant_before_winter` | DaysToWinter < 20 AND growing zone capacity available | ExpandGrowingCapacity (rice) |
| `emergency_food_flag` | DaysOfFood < 7 | FoodSecurity (Critical), flag CoS |
| `low_food_flag` | DaysOfFood < 15 | FoodSecurity (High) |
| `hunt_if_low_no_crops` | DaysOfFood < 20 AND no plantable zones | HuntForFood |
| `freezer_at_capacity` | FreezerCapacity.Used > 90% | ManageFreezer |
| `spoilage_risk` | FreezerTemperature > 0°C | ManageFreezer (Critical) |

**Escalates when:**
- Crop choice involves trade-offs (rice vs potatoes vs devilstrand — guide knowledge needed)
- Multiple competing goals at same priority
- Unusual event detected (blight, infestation in growing zone)
- First devilstrand harvest decision
- Drug-crop policy

---

## RIMAPI writes owned

- Growing zone designation (create, resize, crop selection)
- Hunting restriction zone
- Butcher bill
- Stockpile zone configuration (food category)

Labor requests posted (not owned RIMAPI writes):
- Sow, harvest, hunt, cook, haul to freezer

---

## HTN domain

```
EnsureFoodSecurity
├── already_secure → []
├── harvest_now → PostLaborRequest(Plants, Harvest)
├── expand_zone → ChooseCrop, DesignateZone(RIMAPI), PostLaborRequest(Plants, Sow)
├── hunt → PostLaborRequest(Hunting, Hunt)
└── trade_flag → EmitFlag(TradeForFood)

ChooseCrop
├── cold_snap_imminent → Rice (fast maturation)
├── stable_season → Corn (high yield, storage stable)
└── escalate → LLM (devilstrand, drug crops, unusual conditions)
```

---

## Success metrics

- `DaysOfFoodRemaining` stays above 20 for >90% of in-game days in year 1.
- No colonist death from starvation in year 1.
- Freezer has food surplus heading into each winter.
- Escalation rate below 20% after rule refinement has run.

---

## RAG retrieval profile

Topics: `["food", "farming", "crops", "hunting", "freezer", "cooking", "nutrition"]`
Retrieval required for: crop choice decisions, seasonal timing, first devilstrand decision.

---

## Open questions / TODO

- [ ] Define exact DaysOfFood threshold values (20/15/7 are placeholders)
- [ ] Hunting value/risk scoring for target selection
- [ ] How does Agriculture handle caravans taking food?
- [ ] Psychoid/smokeleaf — Agriculture grows it; Trade sells it. Who decides to grow it?
- [ ] Nutrient paste vs fine meals — does Agriculture or Welfare own cook quality decisions?
