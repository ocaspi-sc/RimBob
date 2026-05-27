# Construction Briefing + Data-Gap Anchor (the GATE)

> Design doc for the **one undesigned piece** flagged in
> [`willie-meta-plan.md`](willie-meta-plan.md) §4. Resolves which Slice-A rules
> are real vs aspirational and feeds the Placement Solver's anchor resolution.
>
> Pure design output. No code lands here.

---

## Goal

Produce a per-field map of the `ConstructionBriefing` (Willie minister briefing
record) so every downstream consumer can tell **what's already readable, what
needs RIMAPI work, and what to defer**. Without this, PS1 and Willie Rules
Slice A are guessing about their inputs.

For each candidate briefing field, decide:

- **Signal** — what the field means (one short sentence).
- **Source** — RIMAPI endpoint, state-store derivation, or RimMind part.
- **Availability** — `have` / `need-fork` / `defer`.
- **Consumers** — which Slice-A rule(s), which solver gate(s).
- **Notes** — known gaps, RimMind reuse opportunities, follow-up todos.

---

## Context

- Meta-plan: [`willie-meta-plan.md`](willie-meta-plan.md) — GATE node in §4.
- Schema landed: S1 `7d0818c`, S2 `8676d59`, S3 `4927741`. Food emits typed
  `building_request`.
- FORK1 landed: RIMAPI single-asset triplet `3eb1d84`. Validate/place/read
  primitive available. RimBob meta `300f5ce`.
- FORK2 pending: blueprint groups (Capability A). Some signals may need it.
- FORK3 pending:
  [`rimapi-map-reach-and-path-cost.md`](rimapi-map-reach-and-path-cost.md). Not
  a hard PS1 gate; Slice A uses euclidean-from-centroid fallback.
- `PowerInfoDto` mismatch: separate plan
  [`rimapi-power-info-dto.md`](rimapi-power-info-dto.md). Aggregate fields
  surface as `have` only **after** that bug fix lands.
- RimMind candidates mined (S2):
  `C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/ConstructionBacklogPart.cs`,
  `.../StorageSaturationPart.cs`.
- Known data-gap todos already in `HumanTodo.md`:
  `source-todo-building-condition-read`, `source-todo-room-quality-read`
  (landed), `source-todo-mayor-briefing-context`, `rimapi-power-net-read`,
  `rimapi-stockpile-detail-read`, `rimapi-room-detail-read`,
  `rimapi-building-detail-read`, `rimapi-buildability-layers-read`,
  `rimapi-map-reach-and-path-cost`.
- Mirror reference: `Src/StateStore/Derivations/FoodBriefingDerivation.cs` is
  the only fully-built feeder briefing; Willie's briefing follows its shape
  and per-cycle cadence (Food computes per cycle; Willie mirrors).

---

## S1 — Per-concern signal tables

Each row carries `signal | source | availability | consumers | notes`.
**Source** names a concrete RIMAPI endpoint (or state-store record) where one
exists today; otherwise the missing endpoint slug. **Availability** = `have` /
`need-fork` / `defer`. **Consumers** point at Slice-A rule names (see S4) and
Placement Solver pipeline stages (see [`placement-solver.md`](placement-solver.md)
§3 / §3.2).

### `power_stability`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Aggregate generation (W) | `/api/v1/map/power/info` → `CurrentPower` / `TotalPossiblePower`; state-store `PowerNetwork.ProductionW` | `have` (after PowerInfoDto fix) | rule `power_net_deficit`; solver `power_access` | DTO mismatch blocks today — see [`rimapi-power-info-dto.md`](rimapi-power-info-dto.md). Fix is a sibling plan, not part of GATE. |
| Aggregate consumption (W) | `/api/v1/map/power/info` → `TotalConsumption`; state-store `PowerNetwork.ConsumptionW` | `have` (after DTO fix) | rule `power_net_deficit`; solver `power_access` | Same blocker as above. |
| Battery reserve (Wd) | `/api/v1/map/power/info` → `CurrentlyStoredPower`; state-store `PowerNetwork.StoredWd` | `have` (after DTO fix) | rule `low_battery_reserve`; solver `battery_margin` | Aggregate; per-battery breakdown not exposed. |
| Battery capacity (Wd) | `/api/v1/map/power/info` → `TotalPowerStorage`; state-store `PowerNetwork.CapacityWd` | `have` (after DTO fix) | rule `low_battery_reserve`; solver `battery_margin` | — |
| Producer / storage / consumer building IDs | `/api/v1/map/power/info` → `ProducePowerBuildings` / `StorePowerBuildings` / `ConsumePowerBuildings` | `have` (after DTO fix) | solver `power_access` (conduit-distance precompute) | RIMAPI returns int lists; map ↔ `BuildingRecord` by id. |
| Per-net split (gen/draw/stored per `PowerNet`) | `rimapi-power-net-read` (sibling plan) | `need-fork` | rule `outage_prone_net`; solver `power_access` per-net | HumanTodo `rimapi-power-net-read`. |
| Per-net outage flag (was net offline last cycle?) | `rimapi-power-net-read` + state-store diff | `need-fork` | rule `outage_prone_net` | Endpoint exposes online/offline; diff over cycles is RimBob-side. |
| Disconnected critical asset (cooler/turret/hospital not on any net) | `rimapi-power-net-read` | `need-fork` | rule `disconnected_critical_asset` | Cross-references room role / building def with net membership. |

### `thermal_control`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Room temperature (per room) | `/api/v1/map/rooms` → `RoomDto.Temperature`; state-store `RoomRecord.Temperature` | `have` | rule `room_too_hot` / `room_too_cold`; solver `temperature_fit` | Room-purpose tagging from `RoleLabel`. |
| Room purpose tag | `/api/v1/map/rooms` → `RoomDto.RoleLabel`; state-store `RoomRecord.RoleLabel` | `have` | rule `freezer_request_active` (target room class); solver `temperature_fit` | Translate raw RimWorld role label → `RoomClass` enum. |
| Cooler / heater building positions | `/api/v1/map/buildings` → `BuildingDto.Def in {Cooler, Heater}` + `Position`; state-store `BuildingRegistry` + `BuildingClassifier.IsCooler` | `have` | rule `cooler_missing_for_food_storage`; solver `temperature_fit` precompute | `BuildingClassifier.IsCooler` already lives in `Src/StateStore/Derivations/Common`. |
| Inbound thermal request (Food → Willie) | `AgentFlag.BuildingRequests[].Temperature.TargetBand` (S2 typed array) | `have` | rule `freezer_request_active` (trigger) | Source: Food emits `target_band:freezing` today (S2 landed). |
| Deadline window (current tick vs `Deadline.by_day`) | state-store `Economy.Tick` + `BuildingRequest.Deadline` | `have` | rule `deadline_window_violation`; solver `request_fit` | Mirror Food's `RimDateParser.Parse` use. |
| Open-roof status for freezer rooms | `/api/v1/map/rooms` → `RoomDto.OpenRoofCount`; state-store `RoomRecord.OpenRoofCount` | `have` | rule `freezer_lost_seal` (sub-signal of `cooler_missing_for_food_storage`); solver hard gate `violates_room_or_power_hard_rule` | A freezer with `OpenRoofCount > 0` will never hold temperature. |
| Wall material for thermal envelope | `rimapi-building-detail-read` | `need-fork` | rule (future) `freezer_wall_uninsulated`; solver `temperature_fit` refinement | Stuff name needed; see `source-todo-building-condition-read`. |

### `basic_shelter`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Colonist count | state-store `Colonists.Colonists` + `PawnDeriver.LivingColonists` | `have` | rule `not_enough_beds` | Shared with Food briefing. |
| Bed count (per room) | state-store `RoomRecord.ContainedBedIds` (sum across rooms) | `have` | rule `not_enough_beds` | Counts beds attached to rooms; ground-placed beds not in any room are excluded — acceptable approximation for Slice A. |
| Room enclosure / open-roof flag | `RoomRecord.OpenRoofCount` + `TouchesMapEdge` | `have` | rule `unenclosed_sleeping` | A bedroom with `OpenRoofCount > 0` or `TouchesMapEdge=true` is failing the survival floor. |
| Door positions on indoor-room boundary | `/api/v1/map/buildings` → `BuildingDto.Def == "Door"` + `Position`; per-room door membership | `need-fork` | rule `missing_door`; anchor inventory `EntryCells` (S3) | Door is exposed as a building, but room↔door mapping is not. Fold into `rimapi-room-detail-read` extension (see S5). |
| Sleeping-spot adequacy (bed quality vs colonist count) | `RoomRecord.ContainedBedIds` + room `Impressiveness` | `have` (count) / `need-fork` (per-bed quality) | rule `not_enough_beds` (count); LLM-tier (quality) | Per-bed read = `rimapi-building-detail-read` scope. |

### `functional_rooms`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Room presence by class (kitchen / hospital / workshop / research / bedroom / prison / recreation / storage) | `RoomRecord.RoleLabel` mapped to `RoomClass` enum | `have` | rule `kitchen_missing` / `hospital_missing` / `storage_room_missing`; anchor inventory (S3) | Slice-A enum mapping is a deterministic table on `RoleLabel`; unknown labels fall back to `BuildingClassifier` inference (kitchen if contains `Stove`, etc.). |
| Room cell count | `RoomRecord.CellsCount` | `have` | rule `room_undersized_for_purpose`; solver size fitting | Per-class minimum cell thresholds live in solver templates. |
| Room quality stats | `RoomRecord.Impressiveness` / `Beauty` / `Cleanliness` / `Space` / `Wealth` | `have` (landed via `source-todo-room-quality-read`) | rule `room_quality_low`; LLM-tier Welfare judgment | These are nullable; treat null as "unknown, don't fire". |
| Contained-building IDs per room | `RoomRecord.ContainedBedIds` only; full contained-list = `rimapi-room-detail-read` | partial `have` (beds) / `need-fork` (general) | rule `wrong_purpose_room` (bedroom contains workbench); solver `ReuseExistingFootprintGenerator` | Today only beds; need general list to detect e.g. workbench in bedroom. |
| Room bounds (rect or cell list) | `rimapi-room-detail-read` | `need-fork` | solver candidate generation inside existing rooms; anchor inventory `EntryCells` derivation | `CellsCount` exists today; bounds/cells do not. |
| Room temperature (cross-link) | `RoomRecord.Temperature` | `have` | LLM-tier (hospital sterility, etc.) | Reused from `thermal_control`. |

### `storage_placement`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Stockpile zones (count, cells, label, type) | state-store `StockpileLedger.Zones` (`StockpileZone`) | `have` | rule `food_stockpile_far_from_kitchen`; solver anchor `storage` | Already used by Food (`FoodStorageSummary`). |
| Stockpile centroid (cell) | `StockpileZone.Center` | `have` | distance metrics; anchor inventory fallback | `MapPosition?`; null when zone has no cells. |
| Stockpile saturation (used/cap) | RimMind `StorageSaturationPart` reads `map.haulDestinationManager` per `SlotGroup`; RIMAPI exposes only `/api/v1/resources/storages/summary` (coarse) | `need-fork` | rule `stockpile_saturation_high`; solver `material_flow` | See S2 cross-walk; `rimapi-stockpile-detail-read` is the natural home. |
| Stockpile allowed filters | `rimapi-stockpile-detail-read` | `need-fork` | rule `stockpile_allowed_filters_mismatch` | E.g. a stockpile labelled "food" that accepts steel is a config bug Willie should surface. |
| Distance: nearest stockpile ↔ cooking building | derived from `BuildingClassifier.IsCookingBuilding` + `StockpileZone.Center` via `MapDistance.Nearest` | `have` | rule `food_stockpile_far_from_kitchen`; solver `freezer_to_kitchen_distance` | Mirror `FoodStorageSummary.NearestKitchenDistanceCells`. Slice-A uses Manhattan; Slice-B uses FORK3 walkable. |
| Adjacency: food positions within cooler radius | derived from `StoredResourceRegistry.Position` + cooler positions (see `FoodStorageSummary.CoolerAdjacentFoodUnits` pattern) | `have` | rule `food_storage_split_from_freezer`; solver spoilage scoring | Existing Food derivation is the template. |
| Bench ↔ input-storage distance | `BuildingClassifier.IsProductionBench` + nearest stockpile | partial `have` (positions) / `need-fork` (bench classification beyond cooking) | rule `bench_without_input_storage`; solver `storage_to_workbench_distance` | Needs production-bench taxonomy beyond `IsCookingBuilding`. Fold into `rimapi-building-detail-read`. |

### `material_bottleneck`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Stored material counts per def (steel, wood, components, stone blocks, chunks) | state-store `StoredResourceRegistry` + `ResourceSummary` | `have` | rule `requested_material_short`; solver affordability gate | Shared with Food. |
| Pending blueprint cost (per blueprint + aggregated) | `/api/v1/map/construction/backlog` → `ConstructionBacklogGroupDto.cost[]` (FORK1 landed `1a3126e`) | `have` (endpoint) / `need-derive` (state-store ingestion + briefing field) | rule `backlog_material_gap`; solver no-fit fallback | Endpoint exists; RimBob has no state-store record for it yet. Derivation = new RimBob-side work, NOT a fork todo. |
| Materials missing per group | `/api/v1/map/construction/backlog` → `materials_missing[]` | `have` (endpoint) / `need-derive` | rule `backlog_material_gap` | Same as above. |
| Materials-on-hand hint from requester | `BuildingRequest.MaterialsOnHand` | `have` | rule `requested_material_short`; solver pre-validate filter | S2 typed array carries this. |
| Production rate (units/day from benches) | n/a — no signal | `defer` | (future) `production_rate_too_low` | Would need bench output history; out of GATE scope. |

### `fire_risk`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Wall material per building | `rimapi-building-detail-read` (Stuff field) | `need-fork` | rule `wood_wall_in_critical_room`; solver `critical_room_wood_wall_ratio` | `BuildingRecord` today has no `Stuff`. Blocks the **material** half of Slice-A `fire_risk`. |
| Available stone blocks | `StoredResourceRegistry.ItemsByDef` (`BlocksGranite`, `BlocksLimestone`, etc.) | `have` | rule `wood_wall_in_critical_room` (counterfactual: rebuild possible?) | Already in state store. |
| Firefoam pump presence in critical rooms | `BuildingRecord.Def == "FirefoamPopper"` + room membership | partial `have` (presence on map) / `need-fork` (per-room membership) | rule `missing_firefoam_in_critical_room` | Per-room requires `rimapi-room-detail-read` contained-building extension. |
| Conduit adjacency to flammable structures | `/api/v1/map/buildings` (conduit Def + Position) + flammable-def list | `need-fork` (per-cell adjacency derivation needs bounded grid read) | rule `flammable_adjacent_to_conduit`; solver `fire_risk` | `rimapi-buildability-layers-read` over caller-supplied rect is the cheapest source. |
| Inter-structure firebreak gap | requires inter-building distance derivation | `defer` | (future) `firebreak_gap_too_small` | Partial from FORK3 path-cost batch, but Slice-A defers per `willie-advice-types.md` §5 ("spacing half may slip later"). |
| Critical-room classification | `RoomRecord.RoleLabel ∈ {kitchen, freezer, power, hospital, storage, bedroom}` | `have` | rule `wood_wall_in_critical_room` (set membership only); solver `fire_risk` weighting | Set membership is deterministic; the gating signal is wall material. |

### `stalled_builds`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Pending frame backlog (count, def, missing materials) | `/api/v1/map/construction/backlog` (FORK1 landed) | `have` (endpoint) / `need-derive` (briefing field) | rule `frame_blocked_by_material`; solver no-fit fallback | Shared with `material_bottleneck` row. |
| Frame age (cycles since first seen) | state-store snapshot diff: persist `(frame_id, first_seen_cycle)` and subtract current cycle | `have` (derivation only; no DTO change) | rule `frame_age_exceeded` | New state-store derivation; no fork work. Bound the map by `MaxBlueprintGroupAssets`-style cap. |
| Frame placement tick (true game tick when blueprint was placed) | `rimapi-building-detail-read` extension (or `/api/v1/map/blueprints` extension to include `tick_placed`) | `need-fork` | rule `frame_age_exceeded` (precise) | Alternative to state-store diff; cheaper if upstream exposes it. |
| Frame reachability (can colonists path to the frame) | `/api/v1/map/reach` (FORK3) | `need-fork` | rule `frame_unreachable` | Solver also wants this for placement; same endpoint serves both. |
| Constructor work-priority gap | `/api/v1/colonists/work` (existing) — count pawns with `Construction` work enabled | `have` | rule `no_assigned_constructor` | Already in `PawnEditController` / `ColonistsWorkController`. |

### `base_layout`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Named route-chain euclidean length (field → freezer → kitchen → dining) | derived from anchor centroids via `MapDistance` | `have` | rule `food_chain_loop_length_long`; solver `named_loop_cost` | Slice-A fallback. |
| Named route-chain walkable length | FORK3 batch `path-cost` | `need-fork` | rule `walkable_loop_length_long`; solver `path_cost` | Same anchors, just walkable instead of euclidean. |
| Corridor widths / choke points | `rimapi-buildability-layers-read` over corridor rect | `defer` | (future) `corridor_too_narrow`; solver `primary_circulation_spine` | LLM-tier per `willie-advice-types.md` §5; defer until topology graph exists. |
| Blueprint density (planned tiles / map area) | state-store `BuildingRegistry.Count` + `TerrainSnapshot.Width × Height` | `have` | informational; solver `expansion_room` | Coarse. Per-cell density needs buildability layer. |
| Planning overlay state | Capability B (vanilla `Designator_Plan`) | `defer` | (future) `planning_overlay_drift` | Covered by `willie-plan-new-base` HumanTodo. |
| Critical-room depth from perimeter | needs region/topology graph | `defer` | LLM-tier `defense_exposure`; cross-cuts `fire_risk` | Deferred per §5 advice-types. |

### Cross-cutting: `map_topology`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Map width / height | state-store `TerrainSnapshot.Width / Height` | `have` | solver bounds gate `out_of_bounds` | Already used. |
| Terrain affordance (per-def aggregate) | state-store `TerrainSnapshot.CellCountsByDef` + `DefsByName` | `have` | informational; solver `terrain_risk` aggregate | Per-cell affordance is below. |
| Per-cell terrain / passability / roof / fog | `rimapi-buildability-layers-read` (caller-supplied rect) | `need-fork` | solver hard gates `terrain_affordance_invalid` / `occupied_or_reserved`; precompute `evidence` | Bounded by rect to keep payload small. |
| Walkable reachability between two cells | `/api/v1/map/reach` (FORK3) | `need-fork` | solver `path_cost`; rule `frame_unreachable` | FORK3 §"Endpoint Contract". |
| Walkable path cost (pair / batch) | `/api/v1/map/path-cost` + `/api/v1/map/path-cost/batch` (FORK3) | `need-fork` | solver `path_cost`; anchor inventory ranking (S3) | Batch sized at K-candidates × M-anchors. |
| Region ID per cell | `/api/v1/map/region-at` (NEW — see S5) | `need-fork` | anchor inventory `RegionId`; solver region-tier bulk rank | FORK3 §"Follow-Up Boundaries" deferred this; the GATE makes us the consumer asking. |
| Indoor / outdoor segmentation | partial via `RoomRecord.TouchesMapEdge`; per-cell = `rimapi-buildability-layers-read` | partial `have` (per-room) / `need-fork` (per-cell) | solver `mountain_roof_exposure` / `bridge_tiles_needed` | Per-room is enough for anchor-level decisions; per-cell only for candidate scoring. |

### Cross-cutting: `anchor_inventory`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Per named anchor: `room_id` | `RoomRecord.Id` | `have` | solver `resolve_anchors` | RoomRegistry landed. |
| Per named anchor: `class` (kitchen / freezer / bedroom / hospital / storage / workshop / research / dining / butcher / prison / recreation) | derived: `RoleLabel` → `RoomClass` enum + `BuildingClassifier` fallback (room with `Stove`/`ButcherTable` → kitchen if role unset) | `have` | solver `resolve_anchors`; rule `near:<class>` resolution | Slice-A enum table; unknown rooms omitted. |
| Per named anchor: contained building IDs | `RoomRecord.ContainedBedIds` only today; general list = `rimapi-room-detail-read` | partial `have` (beds) / `need-fork` (general) | solver `ReuseExistingFootprintGenerator`; rule `wrong_purpose_room` | Used to cross-check class inference. |
| Per named anchor: centroid cell | derived: mean of contained-building `Position`s, else null | `have` (when room has contained buildings) | Slice-A solver scoring fallback (euclidean) | Slice-A only; not the locked anchor-scoring primary. |
| Per named anchor: `entry_cells[]` (door cells on room boundary) | requires room↔door mapping → `rimapi-room-detail-read` extension (or new `rimapi-room-entry-cells`, see S5) | `need-fork` | solver `path_cost` `to` endpoints; FORK3 batch ranking | Slice-A: empty list; solver falls back to centroid. Slice-B: populates entry cells for walkable-cost batch. |
| Per named anchor: `region_id` | `/api/v1/map/region-at` (S5 new capture) | `need-fork` | solver region-tier bulk rank pre-A* tiebreak | Empty in Slice A; FORK3 path-cost batch internally uses regions even without exposing the id, so anchor records can ship without `region_id` populated for first PS1 cut. |

---

## S2 — RimMind cross-walk

Two RimMind world-data parts overlap the Construction briefing. Both predate
RIMAPI's matching endpoints; the question is whether to ingest the RimMind
shape directly (where the parts live in `RimAI.Core/.../Modules/World/Parts/`)
or to consume the RIMAPI fork endpoint (which we control) and let RimMind keep
its own snapshot.

### `ConstructionBacklogPart`

**File.** [`ConstructionBacklogPart.cs`](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/ConstructionBacklogPart.cs)

**Produces.** `ConstructionBacklogSnapshot { Builds: List<ConstructionBuildItem> }`
where each item is `{ DefName, Thing (label), Count, Missing: List<ConstructionMissingItem { Res, Qty }> }`.

**Method.** Scans `map.listerThings.ThingsInGroup(Blueprint)` and
`ThingRequestGroup.BuildingFrame`, reads `bp.TotalMaterialCost()` per
blueprint, subtracts `map.resourceCounter.GetCount(def)` for a rough gap.
Groups by `(DefName, Label)`, sums missing per resource, sorts by total Qty.

**Caveat** (per RimMind inline comments): "粗略" — rough. Does not subtract
material already fed into the frame, does not consider in-transit or reserved
items, does not account for per-container reach.

**Maps onto S1 rows.**

| RimMind field | GATE row | Action |
|---|---|---|
| `ConstructionBuildItem.DefName` / `Thing` / `Count` | `material_bottleneck` → "Pending blueprint cost per def" | **Already covered by RIMAPI** `/api/v1/map/construction/backlog` (FORK1 landed `1a3126e`); `ConstructionBacklogGroupDto.def_name` / `count` are the same shape. |
| `ConstructionBuildItem.Missing[].{Res, Qty}` | `material_bottleneck` / `stalled_builds` → "Materials missing per group" | **Already covered** by `materials_missing[]` on the same DTO. |
| Aggregation (group by def, sum missing) | both rows above | **Done in RIMAPI**, not RimBob. |

**Net recommendation.** Do **not** ingest RimMind's part. RIMAPI's
`/api/v1/map/construction/backlog` is the canonical source; the GATE only
needs RimBob to add a state-store ingest of the existing DTO and a Willie
briefing field. No fork extension needed for this concern.

### `StorageSaturationPart`

**File.** [`StorageSaturationPart.cs`](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/StorageSaturationPart.cs)

**Produces.** `StorageSaturationSnapshot { Storages: List<StorageSaturationItem> }`
where each item is `{ Name, UsedPct, Critical (UsedPct ≥ 0.85), Notes }`.

**Method.** Walks `map.haulDestinationManager.AllGroupsListInPriorityOrder`
per `SlotGroup`. Per-type capacity math:
- `Zone_Stockpile`: cap = `CellCount` (one stack per cell), used = `HeldThingsCount`.
- `Building_Storage` (shelf): cap = `Size.Area × def.building.maxItemsInCell`.
- `StorageGroup`: cap = sum of member `CellsList.Count`, used = `HeldThings.Count`.
- Fallback: `CellsList.Count` cap, `HeldThingsCount` used.

Merges same-name items (e.g. unnamed shelves) into a weighted-average
saturation.

**Maps onto S1 rows.**

| RimMind field | GATE row | Action |
|---|---|---|
| `StorageSaturationItem.{Name, UsedPct, Critical}` | `storage_placement` → "Stockpile saturation (used/cap)" | **No RIMAPI equivalent.** `/api/v1/resources/storages/summary` is coarse (totals, no per-zone UsedPct). RimMind's per-SlotGroup math is the design template. |
| Per-type cap math (zone vs shelf vs StorageGroup) | same row | Fork should reproduce; folded into `rimapi-stockpile-detail-read` (HumanTodo line 80). |

**Net recommendation.** Extend `rimapi-stockpile-detail-read` to include the
RimMind per-`SlotGroup` saturation math. Until that endpoint lands, the GATE
treats the row as `need-fork`; RimBob does NOT reimplement RimMind's logic in
the state store because RIMAPI is the cleaner integration surface. (RimBob
talks to RimWorld over HTTP only; RimMind talks in-process. Reaching into
`map.haulDestinationManager` is the fork's job, not RimBob's.)

### Overlaps & gaps summary

- **Overlap.** Construction backlog: RimMind and the fork now both compute it;
  the fork's DTO is the source of truth for RimBob. RimMind reuse is moot.
- **Gap (fork-side).** Storage saturation: extend `rimapi-stockpile-detail-read`
  to emit RimMind's SlotGroup math.
- **Gap (out of RimMind scope).** Per-room temperature, wall material, room
  bounds, door positions, region IDs — none of these live in RimMind; all are
  fresh RIMAPI work tracked by existing HumanTodos (see S5).

---

## S3 — `AnchorInventory` contract

The `near:<class>` resolution step in the Placement Solver
([`placement-solver.md`](placement-solver.md) §3.1) reads `AnchorInventory`
from the briefing. Locked decision (2026-05-27): anchor representation is
`{room_id, entry_cells[], region_id}` with FORK3 region-BFS scoring;
centroid is the Slice-A fallback only.

### Record shape

```csharp
// Src/Common/Briefings/ConstructionBriefing.cs (proposed)
public sealed record AnchorInventory(
    IReadOnlyList<RoomAnchor> Anchors);

public sealed record RoomAnchor(
    string RoomId,                             // RoomRecord.Id
    RoomClass Class,                           // closed enum from BuildingRequest taxonomy
    string RoleLabel,                          // raw RIMAPI role label, kept for trace
    int CellsCount,                            // RoomRecord.CellsCount
    IReadOnlyList<MapPosition> EntryCells,     // door cells on room boundary; empty in Slice A
    int? RegionId,                             // FORK3 follow-up; null in Slice A
    MapPosition? Centroid,                     // Slice-A fallback: mean of contained-building Positions
    IReadOnlyList<string> ContainedBuildingIds // beds today; general after rimapi-room-detail-read
);
```

`RoomClass` reuses the enum proposed in
[`willie-request-taxonomy.md`](willie-request-taxonomy.md) §1a so the
solver's `near:kitchen` request and the briefing's anchor class are the same
type — no string-matching fragility at the boundary.

### Construction (per-cycle, mirrors Food)

`AnchorInventoryDerivation.Compute(ColonyState s)` runs once per cycle in the
state-store derivation pass:

1. For every `RoomRecord` in `s.Rooms.Value`:
   - Map `RoleLabel` → `RoomClass` via a deterministic table.
   - If unmapped, fall back to `BuildingClassifier` inference on building
     records whose `Position` lies inside the room (Slice A: cannot do this
     accurately without `rimapi-room-detail-range` because `RoomRecord` has
     no cell list — use `ContainedBedIds` as the only deterministic
     room-membership signal today, which covers bedrooms only).
   - If still unmapped, skip the room.
2. Compute `Centroid` from contained buildings' `Position` mean. When no
   building is known to belong to the room, leave `Centroid = null`; solver
   treats the anchor as `EntryCells`-only (Slice B) or skips it (Slice A).
3. `EntryCells = []` until `rimapi-room-entry-cells` (S5) lands.
4. `RegionId = null` until `rimapi-map-region-at` (S5) lands.
5. Emit `AnchorInventory(Anchors: ...)` into `ConstructionBriefing`.

### `near:kitchen` end-to-end (north-star demo)

Tracing the [`willie-meta-plan.md`](willie-meta-plan.md) §1 chain through the
anchor inventory:

1. **Inbound request.** Food emits `building_request { target_class: freezer,
   adjacency: [{ relation: near, target: kitchen }], temperature: { freezing
   }, capacity_need: { food_units, 200 }, deadline: { by_day, 15 } }` on an
   `AgentFlag`.
2. **Willie Rules.** Detects active `building_request`; constructs
   `PlacementSpec` (per [`placement-solver.md`](placement-solver.md) §2).
3. **Anchor resolution.** Solver reads `ConstructionBriefing.AnchorInventory`,
   filters `Anchors.Where(a => a.Class == RoomClass.Kitchen)`. Result: 0..N
   `RoomAnchor`s, each with `RoomId`, `Centroid`, and (in Slice B)
   `EntryCells` + `RegionId`.
4. **Sizing.** `capacity_need.food_units = 200` → 5×5 freezer template (per
   §3 step 2).
5. **Candidate generation.** `TemplateAnchoredGenerator` (PS1) emits one
   freezer-shell-plus-cooler `BlueprintGroup` per kitchen anchor, anchored
   adjacent to the kitchen's `Centroid` (Slice A) or `EntryCells` (Slice B).
6. **Scoring — Slice A.** `freezer_to_kitchen_distance` =
   `MapDistance.Manhattan(candidate_centroid, kitchen.Centroid)`. Rank
   ascending. No FORK3 needed.
7. **Scoring — Slice B (post-FORK3).** Batch `POST /api/v1/map/path-cost/batch
   { tier: region, pairs: candidates × kitchen.EntryCells }`; rank by min
   walkable cost; A* tiebreak on the top K.
8. **Validation.** FORK2 `blueprint-group/validate` survives.
9. **Emit.** `AdviceItem(concern=thermal_control, options=[…])`.

The contract supports the full chain even with `EntryCells = []` and
`RegionId = null`, because step 6 (Slice-A fallback) only needs `Centroid`.
Step 7 (Slice-B upgrade) drops in without changing the briefing record.

### Validation gate (re-stated from doc validation §)

- North-star freezer demo: ✓ covered (anchor → spec → solver pipeline reads
  `AnchorInventory` and produces a ranked candidate; Slice A uses centroid
  euclidean; Slice B swaps in FORK3 walkable cost; same record).
- `entry_cells` / `region_id` empty in Slice A: ✓ acceptable — solver
  fallback path is part of the contract, not a gap.

---

## S4 — Slice-A rule-vs-aspiration cut

Per concern: which rules can ship deterministic in Willie Rules Slice A
(against `have` rows only) vs which slip to "after FORK2 / `rimapi-*-read`
land" (`need-fork`) vs which are punted past first-slice (`defer`).

Slice A's bar: a rule fires from briefing fields whose S1 row is `have` (or
`have` after a sibling-plan fix, e.g. `PowerInfoDto`). A rule that depends on
**any** `need-fork` field becomes `need-fork` until that field's source lands.

Top-3 easiest concerns to ship first (per user-locked decision):
**`functional_rooms`**, **`thermal_control`**, **`storage_placement`** — room
read landed, RimMind reusable for storage, Food already emits the thermal
trigger.

### `functional_rooms`

| Rule | Class | Reason |
|---|---|---|
| `kitchen_missing` | **real-now** | `RoomRecord.RoleLabel` enum table; `BuildingClassifier.IsCookingBuilding` fallback. |
| `hospital_missing` | **real-now** | Same — `RoleLabel == "Hospital"` or contained `HospitalBed`. |
| `storage_room_missing` | **real-now** | Either no `RoleLabel == "Storage"` AND no `StockpileZone`. |
| `room_undersized_for_purpose` | **real-now** | `RoomRecord.CellsCount` vs per-class minimum table. |
| `room_quality_low` | **real-now** | `RoomRecord.Impressiveness` / `Beauty` (landed via `source-todo-room-quality-read`). |
| `wrong_purpose_room` | **need-fork** | Needs general contained-building list per room → `rimapi-room-detail-read`. |
| (LLM-tier: "do we need a hospital yet?") | **defer** | Posture/phase judgment; not deterministic. |

### `thermal_control`

| Rule | Class | Reason |
|---|---|---|
| `freezer_request_active` | **real-now** | Inbound `AgentFlag.BuildingRequests[].Temperature.TargetBand == freezing` (S2 typed array landed). |
| `room_too_hot` / `room_too_cold` | **real-now** | `RoomRecord.Temperature` per role. |
| `cooler_missing_for_food_storage` | **real-now** | Food stockpile centroid + `BuildingClassifier.IsCooler` distance check; mirrors Food's existing derivation. |
| `freezer_lost_seal` | **real-now** | Freezer room with `OpenRoofCount > 0`. |
| `deadline_window_violation` | **real-now** | `BuildingRequest.Deadline.by_day` vs `Economy.Tick` via `RimDateParser`. |
| `freezer_wall_uninsulated` | **need-fork** | Wall material → `rimapi-building-detail-read`. |

### `storage_placement`

| Rule | Class | Reason |
|---|---|---|
| `food_stockpile_far_from_kitchen` | **real-now** | Mirrors `FoodStorageSummary.NearestKitchenDistanceCells`. |
| `food_storage_split_from_freezer` | **real-now** | Mirrors `FoodStorageSummary.CoolerAdjacentFoodUnits`. |
| `bench_without_input_storage` | **real-now (partial)** | Cooking benches today; general production benches blocked on richer building taxonomy → `rimapi-building-detail-read`. |
| `stockpile_saturation_high` | **need-fork** | RimMind SlotGroup math; fold into `rimapi-stockpile-detail-read`. |
| `stockpile_allowed_filters_mismatch` | **need-fork** | Stockpile filter detail → `rimapi-stockpile-detail-read`. |

### `power_stability`

| Rule | Class | Reason |
|---|---|---|
| `power_net_deficit` | **real-now (after PowerInfoDto fix)** | Aggregate gen/draw; fix is sibling plan `rimapi-power-info-dto`, not GATE blocker. |
| `low_battery_reserve` | **real-now (after DTO fix)** | Aggregate `CurrentlyStoredPower / TotalPowerStorage < 0.25`. |
| `outage_prone_net` | **need-fork** | Per-net split + outage flag → `rimapi-power-net-read`. |
| `disconnected_critical_asset` | **need-fork** | Per-net membership cross-referenced with critical room → `rimapi-power-net-read`. |

### `basic_shelter`

| Rule | Class | Reason |
|---|---|---|
| `not_enough_beds` | **real-now** | `colonists > sum(RoomRecord.ContainedBedIds.Count)`. |
| `unenclosed_sleeping` | **real-now** | Bedroom with `OpenRoofCount > 0` or `TouchesMapEdge`. |
| `missing_door` | **need-fork** | Per-room boundary cells + door membership; folds into `rimapi-room-entry-cells` (S5) or `rimapi-room-detail-read`. |

### `material_bottleneck`

| Rule | Class | Reason |
|---|---|---|
| `requested_material_short` | **real-now** | `BuildingRequest.MaterialsOnHand` vs `StoredResourceRegistry.ItemsByDef`. |
| `backlog_material_gap` | **real-now (after RimBob ingest)** | `/api/v1/map/construction/backlog` exists (FORK1); needs RimBob state-store ingestion + briefing field. No fork work. |
| `production_rate_too_low` | **defer** | No production-rate signal. |

### `stalled_builds`

| Rule | Class | Reason |
|---|---|---|
| `frame_blocked_by_material` | **real-now (after backlog ingest)** | `materials_missing[]` non-empty on backlog row. |
| `frame_age_exceeded` | **real-now** | State-store snapshot diff: persist `(frame_id, first_seen_cycle)`, compare to current. No fork work. |
| `no_assigned_constructor` | **real-now** | Pawn Edit Controller exposes work-priority enable bits today. |
| `frame_unreachable` | **need-fork** | `/api/v1/map/reach` (FORK3). |

### `fire_risk`

| Rule | Class | Reason |
|---|---|---|
| `missing_firefoam_in_critical_room` | **need-fork** | Per-room contained-building membership → `rimapi-room-detail-read`. (Map-wide firefoam pump count alone is too coarse.) |
| `wood_wall_in_critical_room` | **need-fork** | Wall material → `rimapi-building-detail-read`. |
| `flammable_adjacent_to_conduit` | **need-fork** | Per-cell adjacency → `rimapi-buildability-layers-read`. |
| `firebreak_gap_too_small` | **defer** | Spacing analysis; per `willie-advice-types.md` §5. |

### `base_layout`

| Rule | Class | Reason |
|---|---|---|
| `food_chain_loop_length_long` | **real-now** | Euclidean over named anchors (kitchen → freezer → dining). |
| `walkable_loop_length_long` | **need-fork** | FORK3 batch path-cost. |
| `corridor_too_narrow` | **defer** | Topology graph not in scope for first slice. |
| `expansion_blocked` | **defer** | Same. |
| `planning_overlay_drift` | **defer** | Capability B. |

### Slice-A summary

Concerns with non-empty `real-now` rule sets (the actionable cut for Willie
Rules Slice A first PR):

1. `functional_rooms` (5 rules) — **first-slice target**.
2. `thermal_control` (5 rules) — **first-slice target**.
3. `storage_placement` (3 rules) — **first-slice target**.
4. `basic_shelter` (2 rules).
5. `material_bottleneck` (2 rules; one needs backlog ingest first).
6. `stalled_builds` (3 rules; two need backlog ingest first).
7. `power_stability` (2 rules; both need `PowerInfoDto` fix first).
8. `base_layout` (1 rule; the food-chain loop).

`fire_risk` has zero `real-now` rules — every Slice-A path is blocked on
`rimapi-building-detail-read` or `rimapi-room-detail-read`. Slice A ships
**without** `fire_risk`.

Validation gate: "S4 non-empty for at least 3 concerns" → **8 of 9 concerns
satisfy**. ✓

---

## S5 — Follow-up HumanTodo promotions

Every `need-fork` row in S1 / S4 must trace to an explicit HumanTodo. Most
are already filed; this section maps each `need-fork` signal to its existing
todo, and lists the two **new** captures the GATE introduces.

### Already covered by existing HumanTodos

| `need-fork` signal | HumanTodo (`HumanTodo.md`) |
|---|---|
| Wall material per building; building working state / hp / power | `source-todo-building-condition-read` (line 43) → finer-grained `rimapi-building-detail-read` (line 79) |
| Room contained-building list (general, not just beds); room bounds | `rimapi-room-detail-read` (line 81) |
| Stockpile saturation, allowed filters, used/total cells | `rimapi-stockpile-detail-read` (line 80) — extend scope to include RimMind `StorageSaturationPart` SlotGroup math (see S2) |
| Per-net power split + outage flag + disconnected-critical detection | `rimapi-power-net-read` (line 82) |
| Per-cell terrain / passability / roof / fertility (bounded rect) | `rimapi-buildability-layers-read` (line 83) |
| Walkable reachability + path cost (pair + batch) | `rimapi-map-reach-and-path-cost` (line 15) — plan landed at [`rimapi-map-reach-and-path-cost.md`](rimapi-map-reach-and-path-cost.md) |
| `PowerInfoDto` mismatch blocking aggregate power | `rimapi-power-info-dto` (line 16) — plan landed at [`rimapi-power-info-dto.md`](rimapi-power-info-dto.md) |
| Blueprint group atomic place (FORK2) | `rimapi-blueprint-groups-overlay` (line 84) → plan [`rimapi-blueprint-groups-and-planning-overlay.md`](rimapi-blueprint-groups-and-planning-overlay.md) |
| Backlog state-store ingestion + briefing field | RimBob-side derivation work; no fork todo needed. Tracked as a follow-on inside the Willie minister implementation slice (`base-construction-layout-agent` / Slices A–C). |
| Frame age via state-store diff | Same — pure RimBob derivation, no fork todo. |

### New HumanTodo captures the GATE introduces

Two `need-fork` rows have no existing HumanTodo and need fresh entries (to be
appended to the `Captured by /todo` section of
[`HumanTodo.md`](../HumanTodo.md) when this plan lands):

1. **`rimapi-map-region-at`** `[2026-05-27]` `#rimapi #construction #pathfinding #willie`
   Add `/api/v1/map/region-at?map_id=…&x=…&z=…` returning the `Region.id` for
   the cell (deferred follow-up from
   [`rimapi-map-reach-and-path-cost.md`](rimapi-map-reach-and-path-cost.md)
   §"Follow-Up Boundaries"). Willie's `AnchorInventory.RegionId` (S3) is the
   first consumer; without it, the solver still works via FORK3 batch
   path-cost (which uses regions internally) but cannot cluster candidates by
   region pre-validation. Cheap wrapper around
   `map.regionGrid.GetValidRegionAt_NoRebuild`. Link: [this doc](.plans/willie-gate-design.md).

2. **`rimapi-room-entry-cells`** `[2026-05-27]` `#rimapi #construction #willie`
   Extend `rimapi-room-detail-read` (or land as a sibling endpoint) to emit
   the cells on each room's boundary that are doors / door openings — the
   `AnchorInventory.EntryCells[]` source. Required for `near:<class>` walkable
   ranking and for the `basic_shelter` `missing_door` rule (S4). May fold
   into `rimapi-room-detail-read` if its scope already covers boundary cells;
   if it does not, file as its own capture. Link: [this doc](.plans/willie-gate-design.md).

Both captures point back to this design doc so the future implementer has the
motivation in one place.

---

## Validation

- ✓ Every briefing field has all five columns filled. No `TBD` left. (S1
  tables: every row has `signal | source | availability | consumers | notes`.)
- ✓ Every `need-fork` row names an explicit endpoint (existing or new todo).
  S5 cross-walk lists each row's todo.
- ✓ Slice-A rules list (S4) is non-empty for at least 3 concerns. **8 of 9**
  concerns carry ≥1 `real-now` rule.
- ✓ Anchor inventory contract (S3) covers `near:kitchen` end-to-end for the
  north-star freezer demo in [`willie-meta-plan.md`](willie-meta-plan.md) §1.
  Slice-A path uses centroid; Slice-B path uses FORK3 batch path-cost over
  `EntryCells`. Same record.

---

## Open questions / dependencies

Most prior open questions resolved by the locked decisions of 2026-05-27 and
the S1–S4 fills above. Remaining open items:

- **Backlog ingestion shape.** Should the state-store cache the full
  `ConstructionBacklogGroupDto` list, or pre-aggregate per-def for cheaper
  briefing computation? Lean: cache the full DTO (mirrors how
  `StockpileLedger` already carries raw `StockpileZone`s) and aggregate at
  derivation time.
- **`stalled_builds` frame-age threshold.** What N (cycles) constitutes
  "stalled"? Calibrate against playtest data once PS1 is live; document the
  default in the rule itself.
- **`functional_rooms` per-class minimum cell counts.** Need a defaults table
  (kitchen ≥ N₁, hospital ≥ N₂, …). Lean: import from the solver template
  sizing constants (PS1 will need the same numbers; single source of truth).

Already resolved (do not re-open — see meta-plan §3 / §5):

- Recompute cadence = per-cycle (mirrors Food).
- Anchor scoring representation = `{room_id, entry_cells[], region_id}` +
  region-BFS via FORK3 endpoints. Centroid is Slice-A fallback only.
- Top-3 easiest concerns for first Slice-A cut = `functional_rooms`,
  `thermal_control`, `storage_placement`.
- Power-net aggregate = `have` via `/api/v1/map/power/info`; per-net + outage
  flag = `need-fork` via `rimapi-power-net-read`.
- `PowerInfoDto` bug = sibling plan; not conflated with GATE.
- FORK3 = sibling plan; Slice A fallback to euclidean is acceptable.

---

## Out of scope

- Code landing — this is a design doc only.
- Placement Solver internals — covered by
  [`placement-solver.md`](placement-solver.md) now that the GATE unblocks it.
- Promoting design into `Docs/design/ministers/construction.md` — separate
  phase 7 in the meta-plan.
- FORK2 blueprint-group endpoint specification — owned by
  [`rimapi-blueprint-groups-and-planning-overlay.md`](rimapi-blueprint-groups-and-planning-overlay.md).
- The two new HumanTodo plans (`rimapi-map-region-at`,
  `rimapi-room-entry-cells`) — captures only in this slice; full plans land
  when those endpoints become the next active fork slice.

---

## Summary (landed 2026-05-27)

**Motivation.** The Willie Placement Solver (PS1) and Willie Rules (Slice A)
cannot start until each briefing field is classified `have` / `need-fork` /
`defer`. The GATE skeleton existed but the per-concern tables, RimMind
cross-walk, anchor contract, and Slice-A rule cut were all empty placeholders.

**Context.** Schema S1/S2/S3 landed (`7d0818c`, `8676d59`, `4927741`);
FORK1 single-asset blueprint triplet landed (`3eb1d84`); FORK3 reach +
path-cost plan landed (`rimapi-map-reach-and-path-cost`); `PowerInfoDto` bug
plan landed (`rimapi-power-info-dto`); room-quality read landed
(`source-todo-room-quality-read`). Anchor-scoring representation locked to
`{room_id, entry_cells[], region_id}` 2026-05-27. RimMind parts
`ConstructionBacklogPart` and `StorageSaturationPart` available for mining.

**Scope.** Filled the five slices of `.plans/willie-gate-design.md`:

- **S1** — 9 per-concern signal tables + 2 cross-cutting tables
  (`map_topology`, `anchor_inventory`). Every row carries
  `signal | source | availability | consumers | notes`. Each `need-fork` row
  names a sibling HumanTodo.
- **S2** — RimMind cross-walk for `ConstructionBacklogPart` (already
  duplicated by RIMAPI `/api/v1/map/construction/backlog` — ingest the
  endpoint, do not reimplement) and `StorageSaturationPart` (no RIMAPI
  equivalent yet — extend `rimapi-stockpile-detail-read`).
- **S3** — `AnchorInventory` + `RoomAnchor` record, per-cycle construction
  steps, and an end-to-end `near:kitchen` walkthrough proving the contract
  covers the north-star freezer demo on both the Slice-A centroid path and
  the Slice-B FORK3 walkable path.
- **S4** — Slice-A rule-vs-aspiration cut. **8 of 9 concerns** carry ≥1
  `real-now` rule; the top-3 first-slice targets are `functional_rooms`,
  `thermal_control`, `storage_placement`. `fire_risk` is the lone concern
  with zero `real-now` rules (everything blocked on
  `rimapi-building-detail-read` / `rimapi-room-detail-read`).
- **S5** — HumanTodo promotion. Every `need-fork` row already has a sibling
  todo except two; this slice introduces two new captures:
  `rimapi-map-region-at` (`AnchorInventory.RegionId` source) and
  `rimapi-room-entry-cells` (`AnchorInventory.EntryCells[]` +
  `basic_shelter.missing_door`).

**How to verify (human).**

- Read `.plans/willie-gate-design.md`. Confirm:
  - Every row in every S1 table has the five-column treatment (no `TBD`).
  - Every `need-fork` row references either a HumanTodo line number or one
    of the two new S5 captures.
  - S4 rule classifications match the S1 source availabilities (a `real-now`
    rule should depend only on `have` rows).
  - S3 record shape is consistent with `RoomRecord` (state-store) and
    `RoomClass` (request taxonomy §1a).
- Open `HumanTodo.md` and confirm the two new captures
  (`rimapi-map-region-at`, `rimapi-room-entry-cells`) appear under
  "Captured by /todo" with the `[2026-05-27]` date tag and a link back to
  this plan. (Captures are added in the same commit as this fill.)

**Outcome.**

- Willie Placement Solver **PS1** is unblocked: the anchor contract and the
  Slice-A vs Slice-B scoring fallback are both specified.
- Willie Rules **Slice A** is unblocked: the deterministic rule set covers 8
  concerns and names the per-concern rule list to write first
  (`functional_rooms`, `thermal_control`, `storage_placement`).
- Meta-plan §4 GATE node moves from `DRAFTED` to `LANDED`.
