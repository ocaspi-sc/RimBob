# Willie Briefing Schema

> Design doc for the **one undesigned piece** flagged in [`willie-meta-plan.md`](willie-meta-plan.md) §4. Resolves which Slice-A rules are real vs aspirational and feeds the Placement Solver's anchor resolution.
>
> Pure design output. No code lands here.

---

## Motivation

The Willie minister (Construction) cannot move past schema-landing until its **briefing record** is specified. Without a per-field map, two downstream tracks stall:

- **Willie Rules Slice A** writes deterministic rules that read briefing fields. If a rule reads a field that turns out to be `need-fork` or `defer`, the rule ships dead. The cut between "rules I can ship now" and "rules that wait for the next fork slice" must be explicit before the first `Rules.cs` line is written.
- **Placement Solver PS1** ([`placement-solver.md`](placement-solver.md)) resolves `near:<class>` against an anchor inventory the briefing publishes. If the anchor contract is wrong (e.g. centroid-only when the locked decision is `{room_id, entry_cells[], region_id}`), PS1 ships against a contract the rest of the solver pipeline can't honour.

Each slice carries its own motivation:

- **S1** — Per-concern signal tables. Without explicit `have / need-fork / defer` per row, every consumer guesses. The tables are the single source of truth for "is signal X readable today?"
- **S2** — RimMind cross-walk. Two RimMind world-data parts (`ConstructionBacklogPart`, `StorageSaturationPart`) already compute Construction-relevant signals. Ingesting an existing implementation beats reinventing it; the cross-walk decides per-part whether to ingest, mirror in RIMAPI, or skip.
- **S3** — `AnchorInventory` contract. The 2026-05-27 locked decision changed anchor representation from centroid to `{room_id, entry_cells[], region_id}`. PS1 cannot start until the record shape and per-cycle construction are written down.
- **S4** — Slice-A rule cut. Rules-vs-aspiration classification per concern is the actionable output. Willie Rules Slice A reads this list to scope its first PR.
- **S5** — HumanTodo promotions. Every `need-fork` row must trace to a HumanTodo. Without that trace, the gap is invisible to future implementers and the meta-plan §4 graph rots.

---

## Goal

Produce a per-field map of the `ConstructionBriefing` (Willie minister briefing record) so every downstream consumer can tell **what's already readable, what needs RIMAPI work, and what to defer**.

Per candidate field, decide:

- **Signal** — what the field means.
- **Source** — RIMAPI endpoint, state-store derivation, or RimMind part.
- **Availability** — `have` / `need-fork` / `defer`.
- **Consumers** — Slice-A rule(s), solver gate(s).
- **Notes** — known gaps, RimMind reuse, follow-up todos.

---

## Context

- Meta-plan: [`willie-meta-plan.md`](willie-meta-plan.md) — Willie Briefing Schema node in §4.
- Schema landed: S1 `7d0818c`, S2 `8676d59`, S3 `4927741`. Food emits typed `building_request`.
- FORK1 landed: RIMAPI single-asset triplet `3eb1d84`. Validate/place/read primitive available. RimBob meta `300f5ce`.
- FORK2 pending: blueprint groups (Capability A). Some signals may need it.
- **FORK3 landed:** `/api/v1/map/reach`, `/api/v1/map/path-cost`, `/api/v1/map/path-cost/batch` per [`rimapi-map-reach-and-path-cost.md`](rimapi-map-reach-and-path-cost.md). Slice-A solver may still use euclidean fallback; Slice-B reads walkable cost once the RimBob client method lands.
- **`PowerInfoDto` fix landed:** aggregate power fields now surface end-to-end; sibling plan [`rimapi-power-info-dto.md`](rimapi-power-info-dto.md) closed.
- RimMind candidates mined (S2): `C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/ConstructionBacklogPart.cs`, `.../StorageSaturationPart.cs`.
- Known data-gap todos: `source-todo-building-condition-read`, `source-todo-room-quality-read` (landed), `source-todo-mayor-briefing-context`, `rimapi-power-net-read`, `rimapi-stockpile-detail-read`, `rimapi-room-detail-read`, `rimapi-building-detail-read`, `rimapi-buildability-layers-read`.
- Mirror reference: `Src/StateStore/Derivations/FoodBriefingDerivation.cs` is the only fully-built feeder briefing; Willie's briefing follows its shape and per-cycle cadence.

---

## S1 — Per-concern signal tables

**Offloaded** to [`willie-briefing-fields.md`](willie-briefing-fields.md) to keep this doc readable. 8 concern tables + 2 cross-cutting tables. Per-row contract: `signal | source | availability | consumers | notes`.

`basic_shelter` is **excluded** from the table set — see Open Questions for the ownership dispute (likely Welfare-owned).

Concerns covered in the fields doc:

1. `power_stability`
2. `thermal_control`
3. `functional_rooms`
4. `storage_placement`
5. `material_bottleneck`
6. `fire_risk`
7. `stalled_builds`
8. `base_layout`

Cross-cutting tables in the fields doc:

- `map_topology` — region IDs, navmesh-reachable cells, indoor/outdoor segmentation, terrain affordance grid.
- `anchor_inventory` — discovered named anchors per class (kitchen, freezer, hospital, storage, …); feeds `near:<class>` resolution.

---

## S2 — RimMind cross-walk

Two RimMind world-data parts overlap the Construction briefing. Both predate RIMAPI's matching endpoints; the question is whether to ingest the RimMind shape directly or to consume the RIMAPI fork endpoint and let RimMind keep its own snapshot.

### `ConstructionBacklogPart`

**File.** [`ConstructionBacklogPart.cs`](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/ConstructionBacklogPart.cs)

**Produces.** `ConstructionBacklogSnapshot { Builds: List<ConstructionBuildItem> }` where each item is `{ DefName, Thing (label), Count, Missing: List<ConstructionMissingItem { Res, Qty }> }`.

**Method.** Scans `map.listerThings.ThingsInGroup(Blueprint)` and `ThingRequestGroup.BuildingFrame`, reads `bp.TotalMaterialCost()` per blueprint, subtracts `map.resourceCounter.GetCount(def)` for a rough gap. Groups by `(DefName, Label)`, sums missing per resource, sorts by total Qty.

**Caveat** (per RimMind inline comments): "粗略" — rough. Does not subtract material already fed into the frame, does not consider in-transit or reserved items, does not account for per-container reach.

**Maps onto briefing rows.**

| RimMind field | Briefing row | Action |
|---|---|---|
| `ConstructionBuildItem.DefName` / `Thing` / `Count` | `material_bottleneck` → "Pending blueprint cost per def" | **Already covered by RIMAPI** `/api/v1/map/construction/backlog` (FORK1 landed `1a3126e`); `ConstructionBacklogGroupDto.def_name` / `count` are the same shape. |
| `ConstructionBuildItem.Missing[].{Res, Qty}` | `material_bottleneck` / `stalled_builds` → "Materials missing per group" | **Already covered** by `materials_missing[]` on the same DTO. |
| Aggregation (group by def, sum missing) | both rows above | **Done in RIMAPI**, not RimBob. |

**Net recommendation.** Do **not** ingest RimMind's part. RIMAPI's `/api/v1/map/construction/backlog` is the canonical source; the briefing only needs RimBob to add a state-store ingest of the existing DTO and a Willie briefing field. No fork extension needed for this concern.

### `StorageSaturationPart`

**File.** [`StorageSaturationPart.cs`](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/StorageSaturationPart.cs)

**Produces.** `StorageSaturationSnapshot { Storages: List<StorageSaturationItem> }` where each item is `{ Name, UsedPct, Critical (UsedPct ≥ 0.85), Notes }`.

**Method.** Walks `map.haulDestinationManager.AllGroupsListInPriorityOrder` per `SlotGroup`. Per-type capacity math: `Zone_Stockpile` uses `CellCount` cap (one stack per cell); `Building_Storage` (shelf) uses `Size.Area × def.building.maxItemsInCell`; `StorageGroup` sums member `CellsList.Count`; fallback uses `CellsList.Count`. Merges same-name items (e.g. unnamed shelves) into a weighted-average saturation.

**Maps onto briefing rows.**

| RimMind field | Briefing row | Action |
|---|---|---|
| `StorageSaturationItem.{Name, UsedPct, Critical}` | `storage_placement` → "Stockpile saturation (used/cap)" | **No RIMAPI equivalent.** `/api/v1/resources/storages/summary` is coarse (totals, no per-zone UsedPct). RimMind's per-SlotGroup math is the design template. |
| Per-type cap math (zone vs shelf vs StorageGroup) | same row | Fork should reproduce; folded into `rimapi-stockpile-detail-read` (HumanTodo). |

**Net recommendation.** Extend `rimapi-stockpile-detail-read` to include the RimMind per-`SlotGroup` saturation math. Until that endpoint lands, the briefing treats the row as `need-fork`; RimBob does NOT reimplement RimMind's logic in the state store because RIMAPI is the cleaner integration surface (RimBob talks to RimWorld over HTTP only; RimMind talks in-process; reaching into `map.haulDestinationManager` is the fork's job).

### Overlaps & gaps summary

- **Overlap.** Construction backlog: RimMind and the fork now both compute it; the fork's DTO is the source of truth for RimBob. RimMind reuse is moot.
- **Gap (fork-side).** Storage saturation: extend `rimapi-stockpile-detail-read` to emit RimMind's SlotGroup math.
- **Gap (out of RimMind scope).** Per-room temperature, wall material, room bounds, door positions, region IDs — none of these live in RimMind; all are fresh RIMAPI work tracked by existing HumanTodos (see S5).

---

## S3 — `AnchorInventory` contract

The `near:<class>` resolution step in the Placement Solver ([`placement-solver.md`](placement-solver.md) §3.1) reads `AnchorInventory` from the briefing. Locked decision (2026-05-27): anchor representation is `{room_id, entry_cells[], region_id}` with FORK3 region-BFS scoring; centroid is the Slice-A fallback only.

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
    int? RegionId,                             // rimapi-map-region-at follow-up; null in Slice A
    MapPosition? Centroid,                     // Slice-A fallback: mean of contained-building Positions
    IReadOnlyList<string> ContainedBuildingIds // beds today; general after rimapi-room-detail-read
);
```

`RoomClass` reuses the enum proposed in [`willie-request-taxonomy.md`](willie-request-taxonomy.md) §1a so the solver's `near:kitchen` request and the briefing's anchor class are the same type — no string-matching fragility at the boundary.

### Construction (per-cycle, mirrors Food)

`AnchorInventoryDerivation.Compute(ColonyState s)` runs once per cycle in the state-store derivation pass:

1. For every `RoomRecord` in `s.Rooms.Value`: map `RoleLabel` → `RoomClass` via a deterministic table. If unmapped, fall back to `BuildingClassifier` inference on building records whose `Position` lies inside the room (Slice A: cannot do this accurately without `rimapi-room-detail-read` because `RoomRecord` has no cell list — use `ContainedBedIds` as the only deterministic room-membership signal today, which covers bedrooms only). If still unmapped, skip the room.
2. Compute `Centroid` from contained buildings' `Position` mean. When no building is known to belong to the room, leave `Centroid = null`; solver treats the anchor as `EntryCells`-only (Slice B) or skips it (Slice A).
3. `EntryCells = []` until `rimapi-room-entry-cells` (S5) lands.
4. `RegionId = null` until `rimapi-map-region-at` (S5) lands.
5. Emit `AnchorInventory(Anchors: ...)` into `ConstructionBriefing`.

### `near:kitchen` end-to-end (north-star demo)

Tracing the [`willie-meta-plan.md`](willie-meta-plan.md) §1 chain through the anchor inventory:

1. **Inbound request.** Food emits `building_request { target_class: freezer, adjacency: [{ relation: near, target: kitchen }], temperature: { freezing }, capacity_need: { food_units, 200 }, deadline: { by_day, 15 } }` on an `AgentFlag`.
2. **Willie Rules.** Detects active `building_request`; constructs `PlacementSpec` (per [`placement-solver.md`](placement-solver.md) §2).
3. **Anchor resolution.** Solver reads `ConstructionBriefing.AnchorInventory`, filters `Anchors.Where(a => a.Class == RoomClass.Kitchen)`. Result: 0..N `RoomAnchor`s, each with `RoomId`, `Centroid`, and (in Slice B) `EntryCells` + `RegionId`.
4. **Sizing.** `capacity_need.food_units = 200` → 5×5 freezer template (per §3 step 2).
5. **Candidate generation.** `TemplateAnchoredGenerator` (PS1) emits one freezer-shell-plus-cooler `BlueprintGroup` per kitchen anchor, anchored adjacent to the kitchen's `Centroid` (Slice A) or `EntryCells` (Slice B).
6. **Scoring — Slice A.** `freezer_to_kitchen_distance` = `MapDistance.Manhattan(candidate_centroid, kitchen.Centroid)`. Rank ascending. No FORK3 read needed.
7. **Scoring — Slice B (post-RimBob FORK3 client wiring).** Batch `POST /api/v1/map/path-cost/batch { tier: region, pairs: candidates × kitchen.EntryCells }`; rank by min walkable cost; A* tiebreak on the top K. (FORK3 endpoints landed; RimBob client method pending.)
8. **Validation.** FORK2 `blueprint-group/validate` survives.
9. **Emit.** `AdviceItem(concern=thermal_control, options=[…])`.

The contract supports the full chain even with `EntryCells = []` and `RegionId = null`, because step 6 (Slice-A fallback) only needs `Centroid`. Step 7 (Slice-B upgrade) drops in without changing the briefing record.

### Validation gate (re-stated from doc validation §)

- North-star freezer demo: ✓ covered (anchor → spec → solver pipeline reads `AnchorInventory` and produces a ranked candidate; Slice A uses centroid euclidean; Slice B swaps in FORK3 walkable cost; same record).
- `entry_cells` / `region_id` empty in Slice A: ✓ acceptable — solver fallback path is part of the contract, not a gap.

---

## S4 — Slice-A rule-vs-aspiration cut

Per concern: which rules can ship deterministic in Willie Rules Slice A (against `have` rows only) vs which slip to "after `rimapi-*-read` lands" (`need-fork`) vs which are punted past first-slice (`defer`).

Slice A's bar: a rule fires from briefing fields whose source row is `have`. A rule that depends on **any** `need-fork` field becomes `need-fork` until that field's source lands.

Top-3 easiest concerns to ship first (per user-locked decision): **`functional_rooms`**, **`thermal_control`**, **`storage_placement`** — room read landed, RimMind reusable for storage, Food already emits the thermal trigger.

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
| `power_net_deficit` | **real-now** | Aggregate gen/draw via `/api/v1/map/power/info` (`PowerInfoDto` fix landed). |
| `low_battery_reserve` | **real-now** | Aggregate `CurrentlyStoredPower / TotalPowerStorage < 0.25`. |
| `outage_prone_net` | **need-fork** | Per-net split + outage flag → `rimapi-power-net-read`. |
| `disconnected_critical_asset` | **need-fork** | Per-net membership cross-referenced with critical room → `rimapi-power-net-read`. |

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
| `frame_unreachable` | **real-now (after RimBob FORK3 client wiring)** | `/api/v1/map/reach` landed; RimBob `RimApiClient` method + ingestion still to land before the briefing field can read it. |

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
| `walkable_loop_length_long` | **real-now (after RimBob FORK3 client wiring)** | `/api/v1/map/path-cost/batch` landed; RimBob ingestion still to land. |
| `corridor_too_narrow` | **defer** | Topology graph not in scope for first slice. |
| `expansion_blocked` | **defer** | Same. |
| `planning_overlay_drift` | **defer** | Capability B. |

### Slice-A summary

Concerns with non-empty `real-now` rule sets (the actionable cut for Willie Rules Slice A first PR):

1. `functional_rooms` (5 rules) — **first-slice target**.
2. `thermal_control` (5 rules) — **first-slice target**.
3. `storage_placement` (3 rules) — **first-slice target**.
4. `material_bottleneck` (2 rules; one needs backlog ingest first).
5. `stalled_builds` (4 rules; two need backlog ingest, one needs FORK3 client wiring).
6. `power_stability` (2 rules).
7. `base_layout` (2 rules; one needs FORK3 client wiring).

`fire_risk` has zero `real-now` rules — every Slice-A path is blocked on `rimapi-building-detail-read` or `rimapi-room-detail-read`. Slice A ships **without** `fire_risk`.

Validation gate: "S4 non-empty for at least 3 concerns" → **7 of 8 concerns satisfy**. ✓

---

## S5 — Follow-up HumanTodo promotions

Every `need-fork` row in the field doc / S4 must trace to an explicit HumanTodo. Most are already filed; this section maps each `need-fork` signal to its existing todo, and lists the two **new** captures the briefing introduces.

### Already covered by existing HumanTodos

| `need-fork` signal | HumanTodo (`HumanTodo.md`) |
|---|---|
| Wall material per building; building working state / hp / power | `source-todo-building-condition-read` → finer-grained `rimapi-building-detail-read` |
| Room contained-building list (general, not just beds); room bounds | `rimapi-room-detail-read` |
| Stockpile saturation, allowed filters, used/total cells | `rimapi-stockpile-detail-read` — extend scope to include RimMind `StorageSaturationPart` SlotGroup math (see S2) |
| Per-net power split + outage flag + disconnected-critical detection | `rimapi-power-net-read` |
| Per-cell terrain / passability / roof / fertility (bounded rect) | `rimapi-buildability-layers-read` |
| Walkable reachability + path cost (pair + batch) | `rimapi-map-reach-and-path-cost` **(LANDED)** — endpoints `/api/v1/map/reach`, `/api/v1/map/path-cost`, `/api/v1/map/path-cost/batch`. RimBob client wiring still pending; tracked as follow-on inside Willie minister implementation slice. |
| `PowerInfoDto` mismatch | `rimapi-power-info-dto` **(LANDED)** |
| Blueprint group atomic place (FORK2) | `rimapi-blueprint-groups-overlay` → plan [`rimapi-blueprint-groups-and-planning-overlay.md`](rimapi-blueprint-groups-and-planning-overlay.md) |
| Backlog state-store ingestion + briefing field | RimBob-side derivation work; no fork todo needed. Tracked as a follow-on inside the Willie minister implementation slice (`base-construction-layout-agent` / Slices A–C). |
| Frame age via state-store diff | Same — pure RimBob derivation, no fork todo. |

### New HumanTodo captures the briefing introduces

Two `need-fork` rows have no existing HumanTodo and need fresh entries (appended to the `Captured by /todo` section of [`HumanTodo.md`](../HumanTodo.md) in the same commit as this fill):

1. **`rimapi-map-region-at`** `[2026-05-27]` `#rimapi #construction #pathfinding #willie` — Add `/api/v1/map/region-at?map_id=…&x=…&z=…` returning the `Region.id` for the cell (deferred follow-up from [`rimapi-map-reach-and-path-cost.md`](rimapi-map-reach-and-path-cost.md) §"Follow-Up Boundaries"). Willie's `AnchorInventory.RegionId` (S3) is the first consumer; without it the solver still works via FORK3 batch path-cost (which uses regions internally) but cannot cluster candidates by region pre-validation. Cheap wrapper around `map.regionGrid.GetValidRegionAt_NoRebuild`.
2. **`rimapi-room-entry-cells`** `[2026-05-27]` `#rimapi #construction #willie` — Extend `rimapi-room-detail-read` (or land as a sibling endpoint) to emit the cells on each room's boundary that are doors / door openings — the `AnchorInventory.EntryCells[]` source. Required for `near:<class>` walkable ranking and (if `basic_shelter` ever lands in Willie's scope) the `missing_door` rule.

Both captures point back to this design doc so the future implementer has the motivation in one place.

---

## Validation

- ✓ Every briefing field has all five columns filled. No `TBD` left. (See [`willie-briefing-fields.md`](willie-briefing-fields.md): every row has `signal | source | availability | consumers | notes`.)
- ✓ Every `need-fork` row names an explicit endpoint (existing or new todo).
- ✓ Slice-A rules list (S4) is non-empty for at least 3 concerns. **7 of 8** concerns carry ≥1 `real-now` rule.
- ✓ Anchor inventory contract (S3) covers `near:kitchen` end-to-end for the north-star freezer demo. Slice-A path uses centroid; Slice-B path uses FORK3 batch path-cost over `EntryCells`. Same record.

---

## Open questions / dependencies

- **`basic_shelter` ownership.** Reviewer flagged that `basic_shelter` (colonists ↔ beds, enclosed sleeping, doors) reads as a **Welfare** concern, not Construction. Canonical concern list in [`willie-advice-types.md`](willie-advice-types.md) §1 currently assigns it to Willie. Either: (a) keep in Willie because the *resolution* is a build, not mood management; or (b) move to Welfare because the *trigger* (mood / survival floor) lives there and Willie merely receives a `building_request{ target_class: bed }`. Lean (b): once Welfare exists, Willie sees this concern only through inbound build requests, same shape as Food's freezer request. This doc excludes `basic_shelter` from the field/rule tables pending reconciliation. Open in `willie-advice-types.md` for the canonical decision.
- **Backlog ingestion shape.** Should the state-store cache the full `ConstructionBacklogGroupDto` list, or pre-aggregate per-def for cheaper briefing computation? Lean: cache the full DTO (mirrors how `StockpileLedger` already carries raw `StockpileZone`s) and aggregate at derivation time.
- **`stalled_builds` frame-age threshold.** What N (cycles) constitutes "stalled"? Calibrate against playtest data once PS1 is live; document the default in the rule itself.
- **`functional_rooms` per-class minimum cell counts.** Need a defaults table (kitchen ≥ N₁, hospital ≥ N₂, …). Lean: import from the solver template sizing constants (PS1 will need the same numbers; single source of truth).

Already resolved (do not re-open — see meta-plan §3 / §5):

- Recompute cadence = per-cycle (mirrors Food).
- Anchor scoring representation = `{room_id, entry_cells[], region_id}` + region-BFS via FORK3 endpoints. Centroid is Slice-A fallback only.
- Top-3 easiest concerns for first Slice-A cut = `functional_rooms`, `thermal_control`, `storage_placement`.
- Power-net aggregate = `have` via `/api/v1/map/power/info` (DTO fix landed); per-net + outage flag = `need-fork` via `rimapi-power-net-read`.
- FORK3 endpoints landed; RimBob client wiring is the next slice (not a fork dependency).

---

## Out of scope

- Code landing — this is a design doc only.
- Placement Solver internals — covered by [`placement-solver.md`](placement-solver.md) now that the briefing schema unblocks it.
- Promoting design into `Docs/design/ministers/construction.md` — separate phase 7 in the meta-plan.
- FORK2 blueprint-group endpoint specification — owned by [`rimapi-blueprint-groups-and-planning-overlay.md`](rimapi-blueprint-groups-and-planning-overlay.md).
- The two new HumanTodo plans (`rimapi-map-region-at`, `rimapi-room-entry-cells`) — captures only in this slice; full plans land when those endpoints become the next active fork slice.

---

## Summary (landed 2026-05-27, refreshed)

**Motivation.** The Willie Placement Solver (PS1) and Willie Rules (Slice A) cannot start until each briefing field is classified `have` / `need-fork` / `defer`. The skeleton existed but the per-concern tables, RimMind cross-walk, anchor contract, and Slice-A rule cut were all empty placeholders.

**Context.** Schema S1/S2/S3 landed (`7d0818c`, `8676d59`, `4927741`); FORK1 single-asset blueprint triplet landed (`3eb1d84`); **FORK3 reach + path-cost endpoints landed**; **`PowerInfoDto` fix landed**; room-quality read landed (`source-todo-room-quality-read`). Anchor-scoring representation locked to `{room_id, entry_cells[], region_id}` 2026-05-27. RimMind parts `ConstructionBacklogPart` and `StorageSaturationPart` available for mining.

**Scope.** Filled the five slices of `.plans/willie-briefing-schema.md`:

- **S1** — Per-concern signal tables. **Offloaded to [`willie-briefing-fields.md`](willie-briefing-fields.md)** to keep this doc readable. 8 concern tables + 2 cross-cutting tables. `basic_shelter` excluded pending Welfare-ownership reconciliation (Open Questions). Every row carries `signal | source | availability | consumers | notes` and every `need-fork` row names a sibling HumanTodo.
- **S2** — RimMind cross-walk for `ConstructionBacklogPart` (already duplicated by RIMAPI `/api/v1/map/construction/backlog` — ingest the endpoint, do not reimplement) and `StorageSaturationPart` (no RIMAPI equivalent yet — extend `rimapi-stockpile-detail-read`).
- **S3** — `AnchorInventory` + `RoomAnchor` record, per-cycle construction steps, and an end-to-end `near:kitchen` walkthrough proving the contract covers the north-star freezer demo on both the Slice-A centroid path and the Slice-B FORK3 walkable path.
- **S4** — Slice-A rule-vs-aspiration cut. **7 of 8 concerns** carry ≥1 `real-now` rule; the top-3 first-slice targets are `functional_rooms`, `thermal_control`, `storage_placement`. `fire_risk` is the lone concern with zero `real-now` rules (everything blocked on `rimapi-building-detail-read` / `rimapi-room-detail-read`).
- **S5** — HumanTodo promotion. Every `need-fork` row already has a sibling todo except two; this slice introduces two new captures: `rimapi-map-region-at` (`AnchorInventory.RegionId` source) and `rimapi-room-entry-cells` (`AnchorInventory.EntryCells[]` + future `missing_door` rule).

**Revisions (post-review).**

- Renamed from "Construction Briefing + Data-Gap Anchor (the GATE)" to "Willie Briefing Schema"; doc filename `willie-gate-design.md` → `willie-briefing-schema.md`.
- S1 tables offloaded to companion doc `willie-briefing-fields.md`.
- Prose unwrapped; hard line breaks collapsed.
- `PowerInfoDto` row and FORK3 row promoted to `have` / landed (sibling plans closed).
- `basic_shelter` removed from field/rule tables; reconciliation question raised in Open Questions.
- Per-slice **Motivation** section added so each slice's purpose is explicit.

**How to verify (human).**

- Read `.plans/willie-briefing-schema.md` (this doc) and `.plans/willie-briefing-fields.md` (companion). Confirm:
  - Every row in every field-doc table has the five-column treatment (no `TBD`).
  - Every `need-fork` row references either a HumanTodo line number or one of the two new S5 captures.
  - S4 rule classifications match the field-doc source availabilities (a `real-now` rule should depend only on `have` rows).
  - S3 record shape is consistent with `RoomRecord` (state-store) and `RoomClass` (request taxonomy §1a).
- Open `HumanTodo.md` and confirm the two new captures (`rimapi-map-region-at`, `rimapi-room-entry-cells`) appear under "Captured by /todo" with the `[2026-05-27]` date tag and a link back to this plan.

**Outcome.**

- Willie Placement Solver **PS1** is unblocked: the anchor contract and the Slice-A vs Slice-B scoring fallback are both specified.
- Willie Rules **Slice A** is unblocked: the deterministic rule set covers 7 concerns and names the per-concern rule list to write first (`functional_rooms`, `thermal_control`, `storage_placement`).
- Meta-plan §4 Willie Briefing Schema node moves from `DRAFTED` to `LANDED`.
