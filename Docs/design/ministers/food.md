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

Food does not own pawn allocation. It may request work-type-qualified labor, e.g. `Cook`, `Grow`, `PlantCut`, `Hunt`, or urgent `Haul`, but in Suggest mode that request is advice to the player. The deferred Labor minister owns actual pawn assignment once Auto exists.

Food advice should be near-term and actionable. Food should usually emit the most important few food-chain interventions, not a full strategic menu. If the right answer is strategic or cross-domain, Food should flag the pressure upward for the Mayor rather than overreaching.

M3 runtime path:

1. `CabinetCycle` refreshes ingestion, runs Food, then runs the Mayor.
2. Food follows the universal minister bootstrap rule from [`design/ministers.md`](../ministers.md): on the first live cycle after Host startup, or the first cycle after the minister is newly introduced into a save, it should bootstrap via escalation rather than trusting only coarse deterministic rules.
3. After bootstrap, Food reads `FoodBriefing` from `BriefingCache`, evaluates `Rules.cs`, and publishes the full current Food `AdviceItem` snapshot plus optional `AgentFlag`s.
4. If rules return `Escalate`, Food calls Gemini with `food.system.md`, `FoodBriefing`, `MinisterBriefingContext`, and food-focused `guide_context`.
5. The Mayor reads active Medium+ flags and reflects relevant Food pressure in `state_of_the_union.food`, `update_notes`, and short-term priorities.

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
- Aggregated spatial/operational summaries: crop-zone summaries, nearest wild-harvest clusters, kitchen/cooking-building presence, butcher-table presence, food-storage summary, kitchen-to-stockpile distance when positions exist, and data-coverage flags.

Spatial briefing data stays aggregated. Food receives proximity strings, counts, and coverage flags rather than raw plant/tile/building lists.

Deferred from the richer target briefing: exact zone-level yield, hunting risk scoring, kitchen/butchery bills, room temperature, item spoilage, work-priority state, and caravan/trade availability.

---

## Rules layer (target ~80% coverage)

Implemented M3 rules:

| Rule | Condition | Output |
|---|---|---|
| `maintain_security_threshold` | DaysOfFood >= 30 AND no urgent spoilage/harvest issue | No advice |
| `nutrition_signal_gap` | Food units exist but nutrition is unknown/fallback-derived | ManageFoodStockpile, request reachable stockpile visibility, Medium priority 6 |
| `unknown_food_state` | No food units and no reliable nutrition estimate | FoodSecurity High, request visible reachable food stockpile |
| `emergency_food_flag` | DaysOfFood < 7 | FoodSecurity High/Critical, dynamic priority, concrete intake/cooking/growing requests and actions |
| `harvest_mature_crops` | Ready harvest count > 0 | HarvestNow, nearest crop-zone summary when available; labor request only if urgent or Plants coverage is missing |
| `meals_understocked` | Meals below two per colonist, days > 7, raw food exists | ManageCookBills, simple meal target, cooking labor only if urgent/no cook coverage |
| `wild_harvest_available` | DaysOfFood < 20, no ready crop harvest, edible wild cluster exists | WildHarvest, mark nearest edible cluster; no routine labor request unless urgent/no Plants coverage |
| `expand_growing_capacity` | DaysOfFood < 20 and growing window remains open | ExpandGrowingCapacity, Tile request with quantity and placement constraints |
| `freezer_missing` | Food exists, no cooler visible, buffer is otherwise stable | ManageFreezer, building request to Construction |

Escalates when:

- The first live Food cycle needs a concrete bootstrap memo, even if a coarse deterministic shortage rule also matches.
- Crop choice involves real trade-offs: rice vs potatoes vs corn vs hydroponics.
- Hunting target value/risk is ambiguous.
- Multiple food-chain bottlenecks compete: no cooks, full freezer, low raw food, active threat.
- Unusual food event appears: blight, toxic fallout, heat wave/freezer loss, animal revenge risk.
- Drug/textile crops compete with food crops.
- Food procurement pressure exists but the action belongs to Economy/Trade or the Mayor.

M3 severity calibration: Food emits `High` for urgent shortage by default. `Critical` is reserved for true immediate starvation evidence, not just a low buffer.

Food should compute severity and `priority_score` from live state when possible: days of food, nutrition confidence, colonist count, season/growing window, active threat, and whether a concrete action can be taken now.

Food publishes a complete active-advice snapshot each successful play cycle. The snapshot replaces earlier active Food cards, including bootstrap LLM cards with unique ids, so the dashboard reflects Food's latest view instead of accumulating stale prior-cycle advice. Advice IDs should still be stable per rule issue where possible (for example `food_emergency_food_flag`) because stable ids make logs, tests, and future supersession chains easier to read. Historical logging can still record each emission separately later.

Emergency Food output should avoid vague catch-all wording such as "audit" or generic `note` actions. If the briefing has reported `FoodUnits` but no meal/raw-food category, Food should say that the reported food units need reachable stockpile visibility. If the briefing shows no harvest/cook path, Food should still emit concrete food-chain setup work when possible: emergency growing tiles, Grow/PlantCut labor, a campfire/stove request, and simple-meal bill/cook labor when raw food exists. Food may request trade capacity only when no stored, harvestable, cookable, or sowable path is visible.

Bootstrap-escalation rule: the first memo should bias toward specific, player-usable advice when the current state supports it, such as crop choice, immediate sow/harvest priorities, hunting vs wild-harvest tradeoff, freezer need, or bill changes. If the current `FoodBriefing` cannot support that specificity, the minister should say so explicitly rather than pretending to know tile counts or exact layouts.

---

## Resource requests

Food may request:

- Tiles: growing zone area, wild harvest area, freezer expansion, food stockpile space. Tile requests should include amount plus proximity/constraint text; Construction/Base Layout handles exact placement.
- Labor: specific RimWorld work types such as `Cook`, `Grow`, `PlantCut`, `Hunt`, or urgent `Haul`. Avoid generic "labor capacity."
- Items/buildings: coolers, butcher table, fueled/electric stove, shelves, power support.
- Bills/settings: cook bill targets, butcher bill state, stockpile filters, forbid/unforbid food.

In MVP, these are surfaced as suggested actions and flags. They are not writes. In Auto, these become inputs to HTN/Labor/RIMAPI execution.

Trade is not a normal Food action in M3. Food may flag "food procurement needed" when local food paths are insufficient, but Economy/Trade or the Mayor owns the trade framing and caravan decision. Food rules should not tell the player to caravan or trade unless the briefing eventually carries explicit current trade availability.

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

RAG is subordinate to live state. Guide passages can justify crop, bill, freezer, and seasonal choices, but they must not invent a current trade route, exact plant location, or available work capacity that the briefing does not show.

---

## Open questions / TODO

- [ ] Define exact DaysOfFood thresholds; 30/20/15/7 are placeholders.
- [ ] Hunting value/risk scoring for target selection.
- [ ] How does Food account for caravan provisioning and food removed from the home map?
- [ ] Decide ownership for drug/textile crops once Trade/Treasury exists.
- [ ] Decide whether animal economy deserves its own minister or stays a Food hard case.
- [ ] Map every RIMAPI food-chain read/write endpoint before Auto graduation.
