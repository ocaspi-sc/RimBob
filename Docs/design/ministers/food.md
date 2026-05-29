# Chef - Food-Chain Minister Design

> **Living document.** See `AGENTS.md` for update rules.
> Chef is the first feeder advisor. This doc records domain ownership and
> design constraints; exact advice enum values, briefing fields, rule names,
> parser behavior, and fixture expectations live in code/tests.

---

## Domain

Chef owns food security across the full nutrition chain:

- Acquisition: crops, forage/edible plant harvest, hunting-for-food, and emergency procurement
  pressure.
- Processing: butchering, cooking, meal mix, and preserving bills.
- Storage: food stockpiles, freezer capacity, freezer temperature, and spoilage
  risk.
- Recovery: blight, cold snap, food poisoning, lost freezer, caravan drain, and
  post-raid interruptions.

Chef is broader than Agriculture. Farming is only one method inside the chain;
the minister is accountable for whether the colony can keep eating.

Chef does not own pawn allocation. It may request work-type-qualified labor,
but in Suggest mode that request is advice to the player. The deferred Labor
minister owns actual pawn assignment once Auto exists.

Chef advice should be near-term and actionable. It should usually emit only the
most important food-chain interventions. If the right answer is strategic or
cross-domain, Chef should flag pressure upward rather than overreach.

---

## Runtime Role

Chef follows the universal minister shape:

- First live cycle may bootstrap through escalation for a grounded first read.
- Normal cycles are rules-first.
- Escalation uses the Chef system prompt, Food briefing, current minister
  context, and food-focused guide context when available.
- Chef publishes a complete active-advice snapshot on successful cycles.
- Medium+ Chef pressure can feed Mayor synthesis through flags.

Chef remains in `Suggest` mode in MVP. Any game-state mutation is limited to
future Assisted Apply controls on allowlisted Chef actions after a player click.

---

## Concerns

Chef concerns are a closed code contract and future autonomy-dial units.
Adding or removing one is a design decision, but the exact enum list belongs in
`Src/Common/Advice/FoodAdviceType.cs`.

Chef concerns should cover food security, growing capacity, harvest,
forage/edible-plant harvest, hunting, cooking, butchering, freezer/storage, trade/procurement
pressure, and food-event recovery.

---

## Briefing

Chef's briefing should answer:

- Is the current food buffer safe?
- Is the nutrition signal trusted, fallback-derived, or missing?
- Which chain stage is limiting: acquisition, cooking, storage, freezer, labor,
  season, active threat, or data coverage?
- What immediate opportunity exists: ready harvest, edible forage cluster, crop
  expansion, meal production, storage visibility, freezer/building request, or
  escalation?
- What facts are missing and therefore should temper advice confidence?

Spatial and operational data stays aggregated. Chef should receive counts,
proximity strings, nearest clusters, distances, and coverage flags rather than
raw plant/tile/building lists.

Hunting input should stay compact: counts, nearest low-risk target summaries,
and short risk/value reasons are enough for Suggest-mode mark-hunt advice. The
state store may combine live animals with static animal-def metadata to rank
safe targets and explain why risky visible animals were skipped, but Chef should
not receive raw animal lists.

When upstream food totals cannot be classified into meals or raw food, the
briefing should expose the unclassified count directly. It should also name
missing or unimplemented signals so Chef can temper confidence without inventing
player chores from data gaps.

When item stacks and item definitions are available, Chef should classify stored
meals and raw food from those sources instead of trusting summary rollups. The
raw item list remains an ingestion/debug input; the briefing receives compact
counts, nutrition source, unclassified remainder, and coverage flags.
When the unclassified remainder can be explained by compact map evidence, such
as forbidden meals or other visible food-like items excluded from the reachable
stored-food count, the briefing should carry that explanation so advice can name
the concrete player action instead of saying only "visibility."

Chef should carry compact current bill state for known cooking workbenches.
Rules use it to avoid repeating a simple-meal bill suggestion once a matching
do-until bill already satisfies the desired target.
When a Chef action targets one known cooking workbench, the player-facing
instruction should name the station and position so the player can find the
right kitchen in RimWorld.

Crop counts and progress should prefer the farm summary crop-type rows when
available. Plant positions are useful spatial context, but the live plant list
can be thing-like and omit crop/growth/zone fields.

Crop selection should use deterministic candidate math before escalation. The
candidate math accounts for crop grow time, yield, current food buffer,
inventory classification confidence, freezer/storage posture, season window,
compact terrain-fertility context, and def-backed harvest nutrition when the
thing definition catalogue exposes it. Exact grow-zone placement and zone-yield
optimization remain out of the current Suggest-mode scope. Chef LLM escalation
receives the compact computed candidate table and must treat it as the source of
truth for crop math.

The implemented briefing can be narrower than the target. Use
`FoodBriefing`, `FoodBriefingDerivation`, and Food briefing tests for current
fields.

Deferred richer signals include exact zone yield/location, freezer room
temperature, spoilage timers, work-priority state, and caravan/trade
availability.

---

## Rules And Escalation

Rules should cover obvious food-chain states: safe buffer, unknown or unreliable
nutrition signal, emergency shortage, mature harvest, understocked meals with raw
food, forage availability, growing capacity, and missing freezer/storage
support. Freezer posture is a standing chain dependency: when Chef recommends
harvest, forage, hunt, cooking, or growing work that will create or depend on
perishable food, missing cooler/freezer support should be attached as a
secondary Willie request rather than waiting until surplus already exists.

Rules should compute priority from live state where possible: days of food,
nutrition confidence, colonist count, season/growing window, active threat, and
whether a concrete action can be taken now.

Escalate when:

- The first live Chef cycle needs a concrete bootstrap memo.
- Crop choice involves real trade-offs.
- Hunting target value/risk is ambiguous.
- Multiple bottlenecks compete.
- An unusual food event appears.
- Drug/textile crops compete with food crops.
- Food procurement pressure exists but execution belongs to Economy/Trade or
  Mayor.

`Critical` should mean immediate starvation evidence, not merely a low buffer.

Chef publishes active advice as a minister snapshot. Stable same-issue ids are
preferred where possible so the dashboard updates the current card instead of
accumulating duplicates.

Chef advice freshness is based on game ticks. Wall-clock issue timestamps remain
audit metadata, but a paused or closed RimWorld session should not make Chef
advice disappear. The dashboard keeps showing the latest persisted Chef
snapshot and marks expired advice; Assisted Apply still requires live
validation before executing any target.

---

## Output Quality

Emergency Chef output should avoid vague catch-all wording such as "audit" or
generic `note` actions when the briefing supports a concrete next step.

If reported food units exist but meal/raw-food classification is missing, Chef
should say that reachable stockpile visibility is needed. If no harvest/cook
path is visible, it should still request or suggest concrete setup work when the
briefing supports it: emergency growing tiles, relevant work-type labor, cooking
building, or simple meal bill.

Chef should not let freezer advice displace starvation recovery, but it should
ask for freezer capacity in advance when the active path is about to create
perishable intake. Chef owns the requirement and size class; Willie owns
room layout, cooler count, power, materials, and placement.

Chef may request trade capacity only when no stored, harvestable, cookable, or
sowable path is visible.

Chef LLM notes are trace labels, not player advice. Keep them terse and aligned
with advice vocabulary.

Chef's player-facing output should use forage/edible-plant language for
natural map plants and reserve backend terms such as `wild_harvest` for raw
debug contracts. It should name the actual edible plant when possible.

Chef's player-facing output starts with a short current-state summary before
the advice list. This summary is derived from the briefing rather than trusted
to LLM prose; the dashboard may render the labelled lines as a compact table. It
should summarize concrete food situation facts such as stored
meals/raw/unclassified food, days-of-food, growing areas and crop progress,
acquisition opportunities, kitchen/storage/freezer signals, and confidence data
gaps; individual advice items expose one concrete `actions[]` list for the player.

Chef exposes deterministic hunt-risk diagnostics for the dashboard
Infographics view. The Host derives this model from live `ColonyState` animal
rows, `FoodHuntSafety`, and animal-def metadata, then emits animal-type
diagnostics plus candidate summaries. The dashboard groups animal types by
safety bucket and sorts them by the Host-emitted hunt score so the player can
inspect exactly why each type can or cannot flow into `mark_hunt` advice and
Assisted Apply. The briefing stays compact: Chef advice gets low-risk target
summaries and reasons, not raw animal lists.

Crop selection should be grounded in deterministic yield math exposed through
briefing context and rule decisions. The LLM may use guides to explain or adjust
a candidate, but it should not invent crop math.

---

## Advice Actions And Flag Requests

Chef advice actions should read as separate interventions that can each improve
food security: stockpile visibility, harvest, cooking, growing, then
trade/procurement only if local paths are missing. Crop choice appears directly
in the relevant action instruction.

Chef may use action or flag metadata for:

- Tiles: growing area, forage harvest area, freezer expansion, stockpile space.
- Labor: specific RimWorld work types such as cooking, growing, plant cutting,
  hunting, or urgent hauling.
- Items/buildings: coolers, butcher table, stove/campfire, shelves, power
  support.
- Bills/settings: cook bill targets, butcher bill state, stockpile filters,
  forbid/unforbid food.

Freezer requests should carry a compact capacity class instead of a vague
"build freezer" ask: starter, buffer, winter, or surplus. The target is cold
storage capacity for a colony-days buffer or incoming harvest/hunt/cooking
surplus; Willie turns that requirement into exact blueprints.

In MVP advice actions and flag requests are rendered by default. Assisted Apply
may later execute a narrow allowlist of Chef actions after player confirmation,
such as `unforbid` known food stacks, `mark_harvest` on validated safe plant
clusters, or one idempotent simple-meal cook-bill upsert when exactly one
cooking workbench is known. `mark_hunt` is eligible only for deterministic
low-risk animal batches with exact ids, bounded area designation, and fresh
validation that the rect contains no unsafe or off-target animals. Broad bill
editing, zones, pawn work priorities, and pawn assignment remain outside the
Chef apply slice. In Auto, actions become inputs to the deferred
planner/Labor/RIMAPI path, while flag requests remain the cross-minister
coordination signal.

Trade is not a normal Chef action in M3. Chef may flag procurement need when
local paths are insufficient, but Economy/Trade or Mayor owns trade framing.

---

## Owned Action Families

| Action family | Chef ownership |
|---|---|
| Growing zones for food crops | Size, crop, timing, urgency |
| Forage harvest for nutrition | Target and timing |
| Hunting for nutrition | Need and target recommendation, with Defense risk veto possible |
| Butchering | Bill need and backlog |
| Cooking | Bill type and stock target |
| Food stockpile/freezer | Filters, capacity need, spoilage urgency |
| Food emergency trade flag | Need; future Economy/Trade owns execution |

Hard cases:

- Psychoid/smokeleaf/devilstrand: Chef can comment on tile opportunity cost; Industry, Economy, or Welfare owns the strategic reason.
- Animal breeding/culling/training: Chef owns feed pressure and slaughter-for-food pressure only. Welfare owns tame-animal living-condition requests; Economy owns herd economics, sale, and trade; Defense owns dangerous or combat animals; Medical owns treatment.
- Nutrient paste: Chef owns the food-chain recommendation; Welfare owns mood cost.
- Caravan provisioning: Chef owns nutrition sufficiency; future travel/trade owner owns execution.

---

## Success Metrics

- Food buffer stays healthy for most of year 1.
- No colonist death from starvation in year 1.
- Freezer/storage path supports surplus heading into winter.
- Cooked meals stay stocked without wasting ingredients during shortage windows.
- Escalation rate falls as replay-backed rules mature.

---

## RAG Retrieval Profile

Chef retrieval topics include food, farming, crops, forage/edible plant harvest, hunting,
freezer, cooking, nutrition, and spoilage.

Retrieval is useful for crop choice, seasonal timing, freezer/cooking policy,
first devilstrand/drug-crop decisions, and biome-specific food recovery.

RAG is subordinate to live state and deterministic crop math. Guide passages can
justify choices, but they must not invent current trade routes, exact plant
locations, work capacity, or yield calculations absent from briefing/code.

---

## Open Questions / TODO

- [ ] Define exact food-buffer thresholds from play data.
- [x] Add deterministic crop-yield math and computed crop candidates.
- [x] Improve baseline hunting value/risk scoring from animal-def metadata.
- [ ] Account for caravan provisioning and food removed from the home map.
- [ ] Decide ownership for drug/textile crops once Economy/Industry/Welfare are live.
- [ ] Decide whether animal economy, training, and living-condition complexity deserves its own minister after Chef, Welfare, Economy, and Defense rules become noisy.
- [ ] Map Food-chain Assisted Apply candidates separately from full Auto writes.
