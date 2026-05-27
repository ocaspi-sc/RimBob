# Willie Briefing Fields — Per-Concern Signal Tables

> Companion to [`willie-briefing-schema.md`](willie-briefing-schema.md). S1 of that doc is offloaded here so the parent stays readable.
>
> Per-row contract: `signal | source | availability | consumers | notes`. Availability is `have` / `need-fork` / `defer`. `need-fork` rows name the missing endpoint (or sibling HumanTodo). Consumers reference Slice-A rule names (see schema doc S4) and Placement Solver pipeline stages ([`placement-solver.md`](placement-solver.md) §3 / §3.2).
>
> `basic_shelter` is **not** covered here — see [`willie-briefing-schema.md`](willie-briefing-schema.md) Open Questions for the Welfare-ownership dispute.

---

## Per-concern tables

### `power_stability`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Aggregate generation (W) | `/api/v1/map/power/info` → `CurrentPower` / `TotalPossiblePower`; state-store `PowerNetwork.ProductionW` | `have` | rule `power_net_deficit`; solver `power_access` | `PowerInfoDto` fix landed; aggregate fields flow end-to-end. |
| Aggregate consumption (W) | `/api/v1/map/power/info` → `TotalConsumption`; state-store `PowerNetwork.ConsumptionW` | `have` | rule `power_net_deficit`; solver `power_access` | — |
| Battery reserve (Wd) | `/api/v1/map/power/info` → `CurrentlyStoredPower`; state-store `PowerNetwork.StoredWd` | `have` | rule `low_battery_reserve`; solver `battery_margin` | Aggregate; per-battery breakdown not exposed. |
| Battery capacity (Wd) | `/api/v1/map/power/info` → `TotalPowerStorage`; state-store `PowerNetwork.CapacityWd` | `have` | rule `low_battery_reserve`; solver `battery_margin` | — |
| Producer / storage / consumer building IDs | `/api/v1/map/power/info` → `ProducePowerBuildings` / `StorePowerBuildings` / `ConsumePowerBuildings` | `have` | solver `power_access` (conduit-distance precompute) | RIMAPI returns int lists; map ↔ `BuildingRecord` by id. |
| Per-net split (gen/draw/stored per `PowerNet`) | `rimapi-power-net-read` (sibling plan) | `need-fork` | rule `outage_prone_net`; solver `power_access` per-net | HumanTodo `rimapi-power-net-read`. |
| Per-net outage flag (was net offline last cycle?) | `rimapi-power-net-read` + state-store diff | `need-fork` | rule `outage_prone_net` | Endpoint exposes online/offline; diff over cycles is RimBob-side. |
| Disconnected critical asset (cooler/turret/hospital not on any net) | `rimapi-power-net-read` | `need-fork` | rule `disconnected_critical_asset` | Cross-references room role / building def with net membership. |

### `thermal_control`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Room temperature (per room) | `/api/v1/map/rooms` → `RoomDto.Temperature`; state-store `RoomRecord.Temperature` | `have` | rule `room_too_hot` / `room_too_cold`; solver `temperature_fit` | Room-purpose tagging from `RoleLabel`. |
| Room purpose tag | `/api/v1/map/rooms` → `RoomDto.RoleLabel`; state-store `RoomRecord.RoleLabel` | `have` | rule `freezer_request_active` (target room class); solver `temperature_fit` | Translate raw RimWorld role label → `RoomClass` enum. |
| Cooler / heater building positions | `/api/v1/map/buildings` → `BuildingDto.Def in {Cooler, Heater}` + `Position`; state-store `BuildingRegistry` + `BuildingClassifier.IsCooler` | `have` | rule `cooler_missing_for_food_storage`; solver `temperature_fit` precompute | `BuildingClassifier.IsCooler` already lives in `Src/StateStore/Derivations/Common`. |
| Inbound thermal request (Food → Willie) | `AgentFlag.BuildingRequests[].Temperature.TargetBand` (S2 typed array) | `have` | rule `freezer_request_active` (trigger) | Food emits `target_band:freezing` today. |
| Deadline window (current tick vs `Deadline.by_day`) | state-store `Economy.Tick` + `BuildingRequest.Deadline` | `have` | rule `deadline_window_violation`; solver `request_fit` | Mirror Food's `RimDateParser.Parse` use. |
| Open-roof status for freezer rooms | `/api/v1/map/rooms` → `RoomDto.OpenRoofCount`; state-store `RoomRecord.OpenRoofCount` | `have` | rule `freezer_lost_seal`; solver hard gate `violates_room_or_power_hard_rule` | A freezer with `OpenRoofCount > 0` will never hold temperature. |
| Wall material for thermal envelope | `rimapi-building-detail-read` | `need-fork` | rule (future) `freezer_wall_uninsulated`; solver `temperature_fit` refinement | Stuff name needed; see `source-todo-building-condition-read`. |

### `functional_rooms`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Room presence by class (kitchen / hospital / workshop / research / bedroom / prison / recreation / storage) | `RoomRecord.RoleLabel` mapped to `RoomClass` enum | `have` | rule `kitchen_missing` / `hospital_missing` / `storage_room_missing`; anchor inventory | Slice-A enum mapping is a deterministic table on `RoleLabel`; unknown labels fall back to `BuildingClassifier` inference (kitchen if contains `Stove`, etc.). |
| Room cell count | `RoomRecord.CellsCount` | `have` | rule `room_undersized_for_purpose`; solver size fitting | Per-class minimum cell thresholds live in solver templates. |
| Room quality stats | `RoomRecord.Impressiveness` / `Beauty` / `Cleanliness` / `Space` / `Wealth` | `have` (landed via `source-todo-room-quality-read`) | rule `room_quality_low`; LLM-tier Welfare judgment | Nullable; treat null as "unknown, don't fire". |
| Contained-building IDs per room | `RoomRecord.ContainedBedIds` only; full contained-list = `rimapi-room-detail-read` | partial `have` (beds) / `need-fork` (general) | rule `wrong_purpose_room`; solver `ReuseExistingFootprintGenerator` | Today only beds; need general list to detect e.g. workbench in bedroom. |
| Room bounds (rect or cell list) | `rimapi-room-detail-read` | `need-fork` | solver candidate generation inside existing rooms; anchor inventory `EntryCells` derivation | `CellsCount` exists today; bounds/cells do not. |
| Room temperature (cross-link) | `RoomRecord.Temperature` | `have` | LLM-tier (hospital sterility, etc.) | Reused from `thermal_control`. |

### `storage_placement`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Stockpile zones (count, cells, label, type) | state-store `StockpileLedger.Zones` (`StockpileZone`) | `have` | rule `food_stockpile_far_from_kitchen`; solver anchor `storage` | Already used by Food (`FoodStorageSummary`). |
| Stockpile centroid (cell) | `StockpileZone.Center` | `have` | distance metrics; anchor inventory fallback | `MapPosition?`; null when zone has no cells. |
| Stockpile saturation (used/cap) | RimMind `StorageSaturationPart` reads `map.haulDestinationManager` per `SlotGroup`; RIMAPI exposes only `/api/v1/resources/storages/summary` (coarse) | `need-fork` | rule `stockpile_saturation_high`; solver `material_flow` | See schema doc S2 cross-walk; `rimapi-stockpile-detail-read` is the natural home. |
| Stockpile allowed filters | `rimapi-stockpile-detail-read` | `need-fork` | rule `stockpile_allowed_filters_mismatch` | A stockpile labelled "food" that accepts steel is a config bug Willie should surface. |
| Distance: nearest stockpile ↔ cooking building | derived from `BuildingClassifier.IsCookingBuilding` + `StockpileZone.Center` via `MapDistance.Nearest` | `have` | rule `food_stockpile_far_from_kitchen`; solver `freezer_to_kitchen_distance` | Mirrors `FoodStorageSummary.NearestKitchenDistanceCells`. Slice-A uses Manhattan; Slice-B uses FORK3 walkable. |
| Adjacency: food positions within cooler radius | derived from `StoredResourceRegistry.Position` + cooler positions (see `FoodStorageSummary.CoolerAdjacentFoodUnits` pattern) | `have` | rule `food_storage_split_from_freezer`; solver spoilage scoring | Existing Food derivation is the template. |
| Bench ↔ input-storage distance | `BuildingClassifier.IsProductionBench` + nearest stockpile | partial `have` (positions) / `need-fork` (bench classification beyond cooking) | rule `bench_without_input_storage`; solver `storage_to_workbench_distance` | Needs production-bench taxonomy beyond `IsCookingBuilding`. Fold into `rimapi-building-detail-read`. |

### `material_bottleneck`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Stored material counts per def (steel, wood, components, stone blocks, chunks) | state-store `StoredResourceRegistry` + `ResourceSummary` | `have` | rule `requested_material_short`; solver affordability gate | Shared with Food. |
| Pending blueprint cost (per blueprint + aggregated) | `/api/v1/map/construction/backlog` → `ConstructionBacklogGroupDto.cost[]` (FORK1 landed `1a3126e`) | `have` (endpoint) / `need-derive` (state-store ingestion + briefing field) | rule `backlog_material_gap`; solver no-fit fallback | Endpoint exists; RimBob has no state-store record for it yet. Derivation = new RimBob-side work, NOT a fork todo. |
| Materials missing per group | `/api/v1/map/construction/backlog` → `materials_missing[]` | `have` (endpoint) / `need-derive` | rule `backlog_material_gap` | Same as above. |
| Materials-on-hand hint from requester | `BuildingRequest.MaterialsOnHand` | `have` | rule `requested_material_short`; solver pre-validate filter | S2 typed array carries this. |
| Production rate (units/day from benches) | n/a — no signal | `defer` | (future) `production_rate_too_low` | Would need bench output history; out of briefing scope. |

### `fire_risk`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Wall material per building | `rimapi-building-detail-read` (Stuff field) | `need-fork` | rule `wood_wall_in_critical_room`; solver `critical_room_wood_wall_ratio` | `BuildingRecord` today has no `Stuff`. Blocks the material half of Slice-A `fire_risk`. |
| Available stone blocks | `StoredResourceRegistry.ItemsByDef` (`BlocksGranite`, `BlocksLimestone`, etc.) | `have` | rule `wood_wall_in_critical_room` (counterfactual: rebuild possible?) | Already in state store. |
| Firefoam pump presence in critical rooms | `BuildingRecord.Def == "FirefoamPopper"` + room membership | partial `have` (presence on map) / `need-fork` (per-room membership) | rule `missing_firefoam_in_critical_room` | Per-room requires `rimapi-room-detail-read` contained-building extension. |
| Conduit adjacency to flammable structures | `/api/v1/map/buildings` (conduit Def + Position) + flammable-def list | `need-fork` (per-cell adjacency derivation needs bounded grid read) | rule `flammable_adjacent_to_conduit`; solver `fire_risk` | `rimapi-buildability-layers-read` over caller-supplied rect is the cheapest source. |
| Inter-structure firebreak gap | requires inter-building distance derivation | `defer` | (future) `firebreak_gap_too_small` | Partial from FORK3 path-cost batch, but Slice-A defers per `willie-advice-types.md` §5. |
| Critical-room classification | `RoomRecord.RoleLabel ∈ {kitchen, freezer, power, hospital, storage, bedroom}` | `have` | rule `wood_wall_in_critical_room` (set membership only); solver `fire_risk` weighting | Set membership is deterministic; the gating signal is wall material. |

### `stalled_builds`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Pending frame backlog (count, def, missing materials) | `/api/v1/map/construction/backlog` (FORK1 landed) | `have` (endpoint) / `need-derive` (briefing field) | rule `frame_blocked_by_material`; solver no-fit fallback | Shared with `material_bottleneck` row. |
| Frame age (cycles since first seen) | state-store snapshot diff: persist `(frame_id, first_seen_cycle)` and subtract current cycle | `have` (derivation only; no DTO change) | rule `frame_age_exceeded` | New state-store derivation; no fork work. Bound the map by `MaxBlueprintGroupAssets`-style cap. |
| Frame placement tick (true game tick when blueprint was placed) | `rimapi-building-detail-read` extension (or `/api/v1/map/blueprints` extension to include `tick_placed`) | `need-fork` | rule `frame_age_exceeded` (precise) | Alternative to state-store diff; cheaper if upstream exposes it. |
| Frame reachability (can colonists path to the frame) | `/api/v1/map/reach` (FORK3 landed; RimBob client pending) | `have` (endpoint) / `need-derive` (RimBob client + ingestion) | rule `frame_unreachable` | Solver also wants this for placement; same endpoint serves both. |
| Constructor work-priority gap | `/api/v1/colonists/work` (existing) — count pawns with `Construction` work enabled | `have` | rule `no_assigned_constructor` | Already in `PawnEditController` / `ColonistsWorkController`. |

### `base_layout`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Named route-chain euclidean length (field → freezer → kitchen → dining) | derived from anchor centroids via `MapDistance` | `have` | rule `food_chain_loop_length_long`; solver `named_loop_cost` | Slice-A fallback. |
| Named route-chain walkable length | FORK3 batch `path-cost` (endpoint landed; RimBob client pending) | `have` (endpoint) / `need-derive` (RimBob client) | rule `walkable_loop_length_long`; solver `path_cost` | Same anchors, walkable instead of euclidean. |
| Corridor widths / choke points | `rimapi-buildability-layers-read` over corridor rect | `defer` | (future) `corridor_too_narrow`; solver `primary_circulation_spine` | LLM-tier per `willie-advice-types.md` §5; defer until topology graph exists. |
| Blueprint density (planned tiles / map area) | state-store `BuildingRegistry.Count` + `TerrainSnapshot.Width × Height` | `have` | informational; solver `expansion_room` | Coarse. Per-cell density needs buildability layer. |
| Planning overlay state | Capability B (vanilla `Designator_Plan`) | `defer` | (future) `planning_overlay_drift` | Covered by `willie-plan-new-base` HumanTodo. |
| Critical-room depth from perimeter | needs region/topology graph | `defer` | LLM-tier `defense_exposure`; cross-cuts `fire_risk` | Deferred per `willie-advice-types.md` §5. |

---

## Cross-cutting tables

### `map_topology`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Map width / height | state-store `TerrainSnapshot.Width / Height` | `have` | solver bounds gate `out_of_bounds` | Already used. |
| Terrain affordance (per-def aggregate) | state-store `TerrainSnapshot.CellCountsByDef` + `DefsByName` | `have` | informational; solver `terrain_risk` aggregate | Per-cell affordance below. |
| Per-cell terrain / passability / roof / fog | `rimapi-buildability-layers-read` (caller-supplied rect) | `need-fork` | solver hard gates `terrain_affordance_invalid` / `occupied_or_reserved`; precompute `evidence` | Bounded by rect to keep payload small. |
| Walkable reachability between two cells | `/api/v1/map/reach` (FORK3 landed; RimBob client pending) | `have` (endpoint) / `need-derive` (RimBob client) | solver `path_cost`; rule `frame_unreachable` | FORK3 §"Endpoint Contract". |
| Walkable path cost (pair / batch) | `/api/v1/map/path-cost` + `/api/v1/map/path-cost/batch` (FORK3 landed; RimBob client pending) | `have` (endpoint) / `need-derive` (RimBob client) | solver `path_cost`; anchor inventory ranking | Batch sized at K-candidates × M-anchors. |
| Region ID per cell | `/api/v1/map/region-at` (NEW capture, see schema S5) | `need-fork` | anchor inventory `RegionId`; solver region-tier bulk rank | FORK3 §"Follow-Up Boundaries" deferred this; the briefing schema makes us the consumer asking. |
| Indoor / outdoor segmentation | partial via `RoomRecord.TouchesMapEdge`; per-cell = `rimapi-buildability-layers-read` | partial `have` (per-room) / `need-fork` (per-cell) | solver `mountain_roof_exposure` / `bridge_tiles_needed` | Per-room is enough for anchor-level decisions; per-cell only for candidate scoring. |

### `anchor_inventory`

| Signal | Source | Availability | Consumers | Notes |
|---|---|---|---|---|
| Per named anchor: `room_id` | `RoomRecord.Id` | `have` | solver `resolve_anchors` | RoomRegistry landed. |
| Per named anchor: `class` (kitchen / freezer / bedroom / hospital / storage / workshop / research / dining / butcher / prison / recreation) | derived: `RoleLabel` → `RoomClass` enum + `BuildingClassifier` fallback (room with `Stove`/`ButcherTable` → kitchen if role unset) | `have` | solver `resolve_anchors`; rule `near:<class>` resolution | Slice-A enum table; unknown rooms omitted. |
| Per named anchor: contained building IDs | `RoomRecord.ContainedBedIds` only today; general list = `rimapi-room-detail-read` | partial `have` (beds) / `need-fork` (general) | solver `ReuseExistingFootprintGenerator`; rule `wrong_purpose_room` | Used to cross-check class inference. |
| Per named anchor: centroid cell | derived: mean of contained-building `Position`s, else null | `have` (when room has contained buildings) | Slice-A solver scoring fallback (euclidean) | Slice-A only; not the locked anchor-scoring primary. |
| Per named anchor: `entry_cells[]` (door cells on room boundary) | `rimapi-room-entry-cells` (NEW capture, see schema S5) | `need-fork` | solver `path_cost` `to` endpoints; FORK3 batch ranking | Slice-A: empty list; solver falls back to centroid. Slice-B: populates entry cells for walkable-cost batch. |
| Per named anchor: `region_id` | `/api/v1/map/region-at` (NEW capture, see schema S5) | `need-fork` | solver region-tier bulk rank pre-A* tiebreak | Empty in Slice A; FORK3 path-cost batch internally uses regions even without exposing the id, so anchor records can ship without `region_id` populated for first PS1 cut. |
