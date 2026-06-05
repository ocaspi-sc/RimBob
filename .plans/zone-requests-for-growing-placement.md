# Zone Requests for Chef Growing Capacity

**Status:** PLANNED - design only; do not implement until the user says to execute it.
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
- This is a wire/persistence shape change: **no compat code; wipe-and-regen on upgrade.** Persisted minister snapshots and replay corpus records that contain old `attention[]` growing-zone requests should be regenerated instead of read through a legacy branch.

---

## Current State

`AgentFlag` currently has `building_requests`, `labor_requests`, `item_requests`, and `attention`. `AttentionRequest` is documented as the catch-all for real dependencies that do not have a typed request array yet. The live growing-capacity request is exactly that: Chef emits `attention[]` with `requested_from: "Willie"` for "36 rice growing tiles near fertile soil and food storage."

`BuildingRequest` is deliberately richer and construction-oriented: target class, room class, capacity, adjacency, power, temperature, materials, urgency, and deadline. That works for freezer, kitchen, beds, shelves, heaters, coolers, and rooms. It does not describe crop zones cleanly.

Willie's current request path and Requests tab are building-request-only. `CabinetCycle` wakes Willie on `building_requests` routed to Willie, and `WillieSolverStore` records a building-request board joined to placement outcomes. That machinery should be mirrored for zones rather than overloaded.

The main implementation gap for real placement is spatial terrain evidence. `TerrainSnapshot` currently stores width, height, per-terrain cell counts, and terrain defs; it does not store cell coordinates. Food can say "there are 52020 growable cells" but Willie cannot choose a concrete 36-cell rect from that aggregate alone.

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

## Slice 1 - Typed Request Wire

Add `ZoneRequest` and `ZoneClass` to `Src/Common/Advice/FlagRequests.cs`; add nullable `ZoneRequests` to `AgentFlag`; add `RequestZone` to `Decision`; teach `DecisionProjection` to project it into `zone_requests`; teach `MinisterRuleTableEvaluator` to sort, dedupe, and emit `request_zone` traces.

Update `ResourceRequestNormalizer` and strict LLM parsing so model-authored flags can emit `zone_requests[]`; update `food.system.md` and `welfare.system.md` flag instructions to list `zone_requests` as a valid typed request array and to keep old vague spatial asks out of `attention[]` when the request is a real zone dependency.

Update dashboard types and renderers: `Dashboard/src/types/advice.ts` gets `ZoneRequest`; `MinisterAdviceView` and HOME output sections render a `Zone` group beside Build/Labor/Item/Attention; semantic icons should use the crop/zone icon, not the generic attention icon.

Tests: projection tests for `RequestZone`; normalizer/parser tests for strict `zone_requests`; dashboard TypeScript build. Wipe/regenerate persisted minister snapshots and replay corpus records after the schema lands; do not add legacy readers.

---

## Slice 2 - Chef Producer

Change `Src/Ministers/Food/Rules.cs` `BuildExpandGrowingCapacity` so the grow-tile dependency uses `FoodFlagRequests.Zone(...)` instead of `FoodFlagRequests.AttentionRequest(...)`. Keep the existing player-facing `designate_zone` advice action for now; this slice only promotes the cross-minister request shape.

The emitted request should carry `ZoneClass.Growing`, `PlantDef = cropCandidate.CropDef`, `TileCount = cropCandidate.Tiles`, `Terrain.MustSupportGrowing = true`, `Terrain.PreferredFertility = cropCandidate.TerrainFertility`, `Priority = priority`, and `RequestedFrom = "Willie"`. Existing cooking-building support stays as `building_requests[]`; this slice should not weaken starter-kitchen/freezer build requests.

Update Food rule tests that currently assert `Attention` contains "growing tiles" so they assert `ZoneRequests` instead. Update replay corpus fixtures or regenerate them under the no-compat rule.

Expected dashboard result after Slice 2: the current live count of "attentions" for this issue drops to zero, and Chef's flag shows a typed Zone request for growing tiles.

---

## Slice 3 - Willie Routing and Board Visibility

Teach `CabinetCycle` to wake Willie for `zone_requests[]` where `requested_from` is `Willie`, analogous to the current building-request follow-up path. This should be a same-cycle `FlagFired` rules-only follow-up, so the operator does not need to click `Run Rules`.

Extend Willie request ingestion with a zone board. Conservative option: add zone rows to the existing Willie Requests tab with a request-kind discriminator and a "Zone placement pending" outcome until Slice 4. Cleaner option: keep `/api/ministers/willie/solver/requests` building-only and add `/api/ministers/willie/zone-requests` for zone rows. Prefer the cleaner option if it keeps the existing building solver contract simple.

Add a Willie deterministic rule `zone_request_active` that emits visible advice saying a growing-zone request exists and whether a placement option is available. Before Slice 4, it should clearly say placement is not computed yet rather than producing generic prose.

Tests: cabinet cycle wakes Willie on a `ZoneRequest` routed to Willie; zone board records full request fields; existing building-request board still records and solves building requests exactly as before.

---

## Slice 4 - Grow-Zone Placement Solver

Add spatial terrain evidence first. Current `TerrainSnapshot` has only per-def counts, so Willie cannot choose exact cells. Options: preserve `TerrainGridDto` in state as a coordinate-addressable grid, or add a bounded buildability/fertility read through the RIMAPI fork. The existing `rimapi-buildability-layers-read` task is the richer long-term fit, but a first slice can use the already represented terrain grid if it contains enough cell-level terrain data.

Add `GrowZonePlacementSolver` or a small zone-specific solver under Willie. Input: `ZoneRequest`, map id, terrain grid, existing growing zones, stockpile/room/building occupancy, Home/buildable-region bounds, and anchor inventory. Output: 1-3 `ZoneOption`s with rect/cells, plant def, tile count, score trace, and validation/readiness fields.

Scoring should favor growable fertile terrain, compact rectangles, proximity to storage/kitchen or the buildable-region anchor, avoiding existing zones and occupied buildings, and staying inside Home/buildable-region bounds when available. Do not invent path-cost precision unless the live data supports it; if path-cost is used, label it as measured.

No apply yet in this slice unless validation is strong enough. If the solver cannot prove a safe concrete region, emit a no-fit reason that is visible in Willie advice and the Requests/Zone view.

Tests: solver picks high-fertility growable cells over low-fertility cells; rejects occupied/existing-zone cells; returns no-fit when no cell-level terrain exists; records no-fit cause in visible Willie advice.

---

## Slice 5 - Assisted Apply for Growing Zones

Add a new apply payload, for example `create_growing_zone`, with `map_id`, `plant_def`, `rect` or explicit cells, `target_count`, `label`, and `target_summary`. Do not reuse `place_blueprint_group`; this is a zone write, not a blueprint group.

Use the existing `RimApiClient.CreateGrowZoneAsync` only after adding fresh validation in `AssistedApplyService`: live RIMAPI reachable, loaded map, rect bounds valid, plant def supported, target cells still growable, target cells still sufficiently unoccupied/unzoned, and request/advice not stale. After the write, read back zones or refresh state and record `apply_result`.

Update dashboard Apply rendering for the new apply kind, with the Apply button only in Willie's emitted zone option/advice surface. Chef's outbound request should remain inspect-only.

Tests: successful create-zone apply calls `POST /api/v1/map/zone/growing` with the selected plant def and rect; stale terrain/occupied cells reject before writing; failed RIMAPI write returns a clear apply result.

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

Live proof for Slice 3+: run a cabinet rules cycle, call the Willie zone request endpoint, confirm the growing-zone request is recorded with source `Chef` and an outcome of awaiting/no-fit/options depending on implemented slice.

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

- Should the first Willie zone surface be a separate Zone Requests tab or a mixed request table inside the existing Requests tab?
- Should `ZoneRequest` use `tile_count` only, or also carry a typed `capacity_need` for future non-growing zones?
- Should grow-zone placement require the RIMAPI fork buildability-layer read before any Apply path ships, or is terrain grid plus occupancy enough for the first player-confirmed write?
- Should Chef's existing `designate_zone` action remain visible once Willie returns a concrete zone option, or should the Willie option supersede it to avoid duplicate player instructions?
