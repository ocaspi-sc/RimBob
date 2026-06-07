# Zone Requests for Chef Growing Capacity

**Status:** LANDED - Slices 1-4 `14ffbcd`, Slice 5 Apply `8448e9e`, solver reuse refactor `e041c04`. Live proof still requires a running RIMAPI map with a valid Willie grow-zone option.
**Owner:** Chef + Willie + Dashboard.
**Scope:** Promote Chef's current growing-tile `attention[]` request into a typed `zone_requests[]` flag surface, then let Willie turn grow-zone requests into inspectable placement proposals and player-confirmed apply payloads.

---

## Goal

Chef currently emits the `expand_growing_capacity` dependency as an `attention[]` row because the existing typed arrays have no home for "create 36 rice growing tiles near storage." That is correct under the current schema, but it makes a real spatial dependency look like a generic alert. Add a typed `zone_requests[]` request family so grow-zone needs are explicit, routeable, dashboard-visible, and eventually solvable by Willie without pretending a growing zone is a constructed building.

---

## Locked Decisions

- Do **not** force growing zones into `BuildingRequest`. A growing zone is a map zone/designation, not a room, bench, cooler, wall, or other constructed asset.
- Add `ZoneRequest` and `AgentFlag.zone_requests[]`; keep `attention[]` as the fallback only for dependencies that still have no typed request kind.
- Chef owns the food-chain reason, crop choice, tile count, and urgency. Willie owns spatial placement proposals when a zone request is routed to `requested_from: "Willie"`.
- Player-facing Apply for a Willie-authored zone placement lives in Willie's scope, not on Chef's outbound request. Chef may keep a plain `designate_zone` advice action until Willie returns a concrete zone option.
- Chef's current `designate_zone` advice action is a request stub, not a placed-zone write. If that visible action token is renamed to `designate_zone_req`, do it as its own no-compat wire slice and regenerate persisted outputs.
- This is a wire/persistence shape change: **no compat code; wipe-and-regen on upgrade.** Persisted minister snapshots and replay corpus records that contain old `attention[]` growing-zone requests should be regenerated instead of read through a legacy branch.

---

## Current State

Implemented worktree state: `AgentFlag` now has `building_requests`, `labor_requests`, `item_requests`, `zone_requests`, and `attention`. `AttentionRequest` remains the catch-all for real dependencies that do not have a typed request array yet. Chef's `expand_growing_capacity` request now emits `zone_requests[]` with `requested_from: "Willie"` for the grow-zone dependency instead of hiding that dependency in `attention[]`.

`BuildingRequest` is deliberately richer and construction-oriented: target class, room class, capacity, adjacency, power, temperature, materials, urgency, and deadline. That works for freezer, kitchen, beds, shelves, heaters, coolers, and rooms. It does not describe crop zones cleanly.

Willie's building-request solver path remains building-request-only. `CabinetCycle` wakes Willie on both Willie-routed `building_requests` and `zone_requests`; `WillieSolverStore` records a separate zone board exposed through `/api/ministers/willie/zone-requests`, while `/api/ministers/willie/solver/requests` stays the building solver board.

Slice 4 adds the first coordinate-addressable spatial evidence: `TerrainSnapshot` preserves decoded terrain/fertility cells when the RIMAPI terrain grid supplies them, `MapZoneRegistry` preserves existing map zones and cells, and stockpile/Home area ingestion keeps cell masks when available. Food still consumes compact terrain summaries; Willie uses the code-only cell evidence for placement.

Current zone placement behavior is player-click applyable when Willie can prove a complete rectangular option. Willie emits a `zone_request_active` advice card, runs the grow-zone solver when cell-level terrain exists, records options/no-fit in the zone board, and attaches a `create_growing_zone` Apply action to Willie advice for concrete rectangular zone options. `AssistedApplyService` refreshes live state, validates map, plant def, terrain growability, and unoccupied/unzoned cells, calls `/api/v1/map/zone/growing`, refreshes readback, and records `apply_result`. Chef's outbound `zone_requests[]` row remains inspect-only.

---

## Proposed Schema

Add `ZoneRequest` under `RimBob.Core.Advice`:

```csharp
public sealed record ZoneRequest(
    string Request,
    string Reason,
    ZoneClass ZoneClass,
    string? PlantDef = null,
    int? TileCount = null,
    IReadOnlyList<AdjacencyHint>? Adjacency = null,
    TerrainNeed? Terrain = null,
    Urgency? Urgency = null,
    Deadline? Deadline = null,
    Priority? Priority = null,
    string? RequestedFrom = null);
```

Initial enum/supporting shape:

```csharp
public enum ZoneClass
{
    Growing
}

public sealed record TerrainNeed(
    bool MustSupportGrowing,
    float? PreferredFertility = null,
    IReadOnlyList<string>? PreferredTerrainDefs = null);
```

JSON fields: `request`, `reason`, `zone_class`, `plant_def`, `tile_count`, `adjacency`, `terrain`, `urgency`, `deadline`, `priority`, `requested_from`.

Worked flag row:

```json
{
  "request": "36 rice growing tiles near fertile soil and food storage",
  "reason": "fastest food crop with 40.9 days of winter margin; fertility 1.4, zone placement not computed.",
  "zone_class": "growing",
  "plant_def": "Plant_Rice",
  "tile_count": 36,
  "adjacency": [{ "relation": "near", "target": "storage" }],
  "terrain": { "must_support_growing": true, "preferred_fertility": 1.4 },
  "priority": "high",
  "requested_from": "Willie"
}
```

---

## Slice 1 - Typed Request Wire - Implemented

Add `ZoneRequest` and `ZoneClass` to `Src/Common/Advice/FlagRequests.cs`; add nullable `ZoneRequests` to `AgentFlag`; add `RequestZone` to `Decision`; teach `DecisionProjection` to project it into `zone_requests`, including the request grouping and request-priority helper switches; teach `MinisterRuleTableEvaluator` to sort, dedupe, and emit `request_zone` traces.

Update `ResourceRequestNormalizer` and strict LLM parsing so model-authored flags can emit `zone_requests[]`; update `food.system.md` and `welfare.system.md` flag instructions to list `zone_requests` as a valid typed request array and to keep old vague spatial asks out of `attention[]` when the request is a real zone dependency.

Update dashboard types and renderers: `Dashboard/src/types/advice.ts` gets `ZoneRequest`; `MinisterAdviceView` and HOME output sections render a `Zone` group beside Build/Labor/Item/Attention; semantic icons should use the crop/zone icon, not the generic attention icon.

Tests: projection tests for `RequestZone`; normalizer/parser tests for strict `zone_requests`; dashboard TypeScript build. Wipe/regenerate persisted minister snapshots and replay corpus records after the schema lands; do not add legacy readers.

---

## Slice 2 - Chef Producer - Implemented

Change `Src/Ministers/Food/Rules.cs` `BuildExpandGrowingCapacity` so the grow-tile dependency uses `FoodFlagRequests.Zone(...)` instead of `FoodFlagRequests.AttentionRequest(...)`. Keep the existing player-facing `designate_zone` advice action for now; this slice only promotes the cross-minister request shape.

The emitted request should carry `ZoneClass.Growing`, `PlantDef = cropCandidate.CropDef`, `TileCount = cropCandidate.Tiles`, `Terrain.MustSupportGrowing = true`, `Terrain.PreferredFertility = cropCandidate.TerrainFertility`, `Priority = priority`, and `RequestedFrom = "Willie"`. Existing cooking-building support stays as `building_requests[]`; this slice should not weaken starter-kitchen/freezer build requests.

Update Food rule tests that currently assert `Attention` contains "growing tiles" so they assert `ZoneRequests` instead. Update replay corpus fixtures or regenerate them under the no-compat rule.

Expected dashboard result after Slice 2: the current live count of "attentions" for this issue drops to zero, and Chef's flag shows a typed Zone request for growing tiles.

---

## Slice 3 - Willie Routing and Board Visibility - Implemented

Teach `CabinetCycle` to wake Willie for `zone_requests[]` where `requested_from` is `Willie`, analogous to the current building-request follow-up path. This should be a same-cycle `FlagFired` rules-only follow-up, so the operator does not need to click `Run Rules`.

Extend Willie request ingestion with a zone board. Conservative option: add zone rows to the existing Willie Requests tab with a request-kind discriminator and a "Zone placement pending" outcome until Slice 4. Cleaner option: keep `/api/ministers/willie/solver/requests` building-only and add `/api/ministers/willie/zone-requests` for zone rows. Prefer the cleaner option if it keeps the existing building solver contract simple.

Add a Willie deterministic rule `zone_request_active` that emits visible advice saying a growing-zone request exists and whether a placement option is available. Before Slice 4, it should clearly say placement is not computed yet rather than producing generic prose.

Tests: cabinet cycle wakes Willie on a `ZoneRequest` routed to Willie; zone board records full request fields; existing building-request board still records and solves building requests exactly as before.

---

## Slice 4 - Grow-Zone Placement Solver - Implemented

Added spatial terrain evidence first. `TerrainSnapshot` keeps per-def counts for summaries and a decoded coordinate grid for code consumers; `MapZoneRegistry` keeps existing zone cells; stockpile and Home-area aggregates preserve cells where RIMAPI supplies them. This is a state snapshot schema change: no compat code; wipe-and-regen on upgrade.

Added `GrowZonePlacementSolver` under Willie. Input: `ZoneRequest`, map id, terrain grid, existing growing zones, stockpile/room/building occupancy, Home/buildable-region bounds, and anchor inventory. Output is attached through the existing `AdviceOption` shape as read-only zone-cell option cards with plant def, tile count, score trace, and readiness fields.

Scoring favors growable fertile terrain, compact rectangles, proximity to storage/kitchen or the buildable-region anchor, avoiding existing zones and occupied buildings, and staying inside Home/buildable-region bounds when available. It does not invent path-cost precision. The solver uses a zone-specific rectangle search, not the building `PlacementSolver` or WFC; a grow zone is a homogeneous one-crop field, not a room shell or blueprint group.

No apply ships in this slice. Zone options use `apply_ready=blocked` and the dashboard names that `create_growing_zone` validation/wiring is deferred. If the solver cannot prove a safe concrete region, it emits a no-fit reason that is visible in Willie advice and the Requests/Zone view.

Tests: solver picks high-fertility growable cells over low-fertility cells; rejects occupied/existing-zone cells; returns no-fit when no cell-level terrain exists; records no-fit cause in visible Willie advice; dashboard TypeScript build covers the Requests rendering contract.

---

## Slice 5 - Assisted Apply for Growing Zones - Implemented

Added a new `create_growing_zone` apply payload with `map_id`, `plant_def`, `rect`, `target_count`, `label`, and `target_summary`. `RimApiClient.CreateGrowZoneAsync` accepts a rect (`point_a`/`point_b`), not an explicit cell list, so zone Apply is emitted only for solver options whose zone-cell assets form one complete rectangle. It does not reuse `place_blueprint_group`; this is a zone write, not a blueprint group.

The existing `RimApiClient.CreateGrowZoneAsync` is called only after fresh validation in `AssistedApplyService`: live RIMAPI reachable, loaded map, rect bounds valid, plant def supported, target cells still growable, target cells still unoccupied/unzoned, and request/advice not stale. After the write, RimBob refreshes state, checks matching growing-zone readback, and records `apply_result`.

Retagged `RimApiClient.CreateGrowZoneAsync` ownership to Willie-owned player-confirmed Assisted Apply.

Updated dashboard Apply typing/rendering for the new apply kind. The Apply button appears only on Willie's emitted advice action; Chef's outbound request remains inspect-only. The Willie Requests view remains diagnostic and points operators back to the Advice action for Apply.

Tests cover successful create-zone apply calling `POST /api/v1/map/zone/growing` with the selected plant def and rect, stale occupied cells rejecting before writing, failed RIMAPI writes returning a clear apply result, serialization, and Willie option action attachment.

---

## Docs and Dashboard

Update `Docs/design/communication.md` request discipline to say `Attention` is only for dependencies without typed arrays, and that `zone_requests[]` covers grow-zone and future zone/designation requests.

Update `Docs/design/ministers/food.md`: Chef owns grow-zone crop, size, timing, and food-chain reason; it emits `zone_requests[]` for spatial placement instead of `attention[]`.

Update `Docs/design/ministers/construction.md`: Willie owns spatial placement for zone requests when another minister routes them to Willie, but this is separate from building-request placement and blueprint apply.

Update `Docs/design/dashboard.md`: flag request tables and HOME output have a Zone group; Willie may have a Zone Requests/Requests surface; Apply stays on the emitting minister's concrete option, not on the requester row.

---

## Verification

Run `dotnet test Src\RimBob.sln --filter FullyQualifiedName~Food|FullyQualifiedName~Willie|FullyQualifiedName~Coordination` after backend slices, then full `dotnet test Src\RimBob.sln` before landing. Run `npm.cmd run build` after dashboard changes.

Live proof for Slice 2: run Chef rules, call `GET /api/ministers/food/snapshot`, confirm `food:expand_growing_capacity.zone_requests[0]` exists and the old growing-tile `attention[]` entry is gone.

Live proof for Slice 3/4: run a cabinet rules cycle, call the Willie zone request endpoint, confirm the growing-zone request is recorded with source `Chef` and an outcome of awaiting/no-fit/options. When options exist, Willie Requests must show read-only zone option cards with no Apply button.

Live proof for Slice 5: click the Willie-authored grow-zone Apply, confirm `/api/v1/map/zone/growing` succeeds through RIMAPI, refresh state, and confirm the new zone appears in `GET /api/v1/map/zones?map_id`.

---

## Non-Goals

- Do not implement Auto or pawn assignment.
- Do not broaden `ZoneClass` beyond `Growing` until a second live zone request proves the need.
- Do not add legacy readers for old snapshots with grow-zone `attention[]`.
- Do not make Chef directly place Willie-authored zone options.
- Do not claim exact placement if only aggregate terrain counts are available.

---

## Open Questions

- Answered for Slices 1-3: the dashboard uses the existing Willie Requests view, but the backend keeps a separate `/api/ministers/willie/zone-requests` endpoint so `/api/ministers/willie/solver/requests` remains building-solver-only.
- Should `ZoneRequest` use `tile_count` only, or also carry a typed `capacity_need` for future non-growing zones?
- Should grow-zone placement require the RIMAPI fork buildability-layer read before any Apply path ships, or is terrain grid plus occupancy enough for the first player-confirmed write?
- Should Chef's existing `designate_zone` action remain visible once Willie returns a concrete zone option, should it be renamed to `designate_zone_req`, or should the Willie option supersede it to avoid duplicate player instructions?

---

## Summary — landed

All five slices are on master:

- **Slices 1-4** (`14ffbcd`) — typed `zone_requests[]` wire (`ZoneRequest`/`ZoneClass`, `RequestZone`, projection + dedupe + `request_zone` traces, incl. the `IsRequest`/`RequestPriority` switch updates), Chef producer (`BuildExpandGrowingCapacity` emits `zone_requests[]` instead of `attention[]`), Willie routing (`CabinetCycle` wakes Willie on Willie-routed zone requests; separate zone board at `/api/ministers/willie/zone-requests`), coordinate terrain/zone/occupancy evidence, and the inspect-only grow-zone solver.
- **Slice 5 Apply** (`8448e9e`) — Willie-owned `create_growing_zone` Assisted Apply: live validation (map, plant def, growability, unoccupied/unzoned cells, staleness), `POST /api/v1/map/zone/growing` via `CreateGrowZoneAsync`, zone readback, `apply_result`. Rect-only client honored — Apply is emitted only when the option's zone cells form one complete rectangle; `CreateGrowZoneAsync` ownership retagged Chef→Willie.
- **Solver reuse refactor** (`e041c04`) — follow-up; the solver now shares `PlacementEvidence`'s free-rect engine + `AnchorResolver`. See [grow-zone-solver-reuse.md](grow-zone-solver-reuse.md).

All plan-review fixes landed: rect-only Apply, ownership retag, projection helper-switch updates, and the terrain-coordinate dependency met before the solver shipped.

**Deferred / still open:** the `designate_zone` → `designate_zone_req` action-token rename was not executed (token unchanged; would be its own no-compat slice). Live proof against a running RIMAPI map is still pending.
