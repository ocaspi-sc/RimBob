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
- Player-facing Apply for a Willie-authored zone placement lives in Willie's scope, not on Chef's outbound request. Chef may keep a plain `designate_zone_req` advice action until Willie returns a concrete zone option.
- Rename Chef's player-facing advice action token `designate_zone` → `designate_zone_req` (the `AdviceActionKind.DesignateZone` wire string). It marks a request stub, not a placed zone; the concrete placed write stays Willie's `create_growing_zone` apply. This wire-token change falls under the no-compat rule below. **Scope: only the action token changes** — the typed request family stays named `ZoneRequest` / `zone_requests[]` / `ZoneClass`.
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

Add `ZoneRequest` and `ZoneClass` to `Src/Common/Advice/FlagRequests.cs`; add nullable `ZoneRequests` to `AgentFlag`; add `RequestZone` to `Decision`; teach `DecisionProjection` to project it into `zone_requests` — add the `RequestZone` case to the request switch **and** to the `IsRequest` and `RequestPriority` helper switches (`Src/Common/Ministers/DecisionProjection.cs:126`/`:129`), else zone requests are never grouped or prioritized; teach `MinisterRuleTableEvaluator` to sort, dedupe, and emit `request_zone` traces.

Update `ResourceRequestNormalizer` and strict LLM parsing so model-authored flags can emit `zone_requests[]`; update `food.system.md` and `welfare.system.md` flag instructions to list `zone_requests` as a valid typed request array and to keep old vague spatial asks out of `attention[]` when the request is a real zone dependency.

Update dashboard types and renderers: `Dashboard/src/types/advice.ts` gets `ZoneRequest`; `MinisterAdviceView` and HOME output sections render a `Zone` group beside Build/Labor/Item/Attention; semantic icons should use the crop/zone icon, not the generic attention icon.

Tests: projection tests for `RequestZone`; normalizer/parser tests for strict `zone_requests`; dashboard TypeScript build. Wipe/regenerate persisted minister snapshots and replay corpus records after the schema lands; do not add legacy readers.

---

## Slice 2 - Chef Producer

Change `Src/Ministers/Food/Rules.cs` `BuildExpandGrowingCapacity` so the grow-tile dependency uses `FoodFlagRequests.Zone(...)` instead of `FoodFlagRequests.AttentionRequest(...)`. Keep the existing player-facing advice action for now (renamed `designate_zone` → `designate_zone_req`; emitted by `GrowingZoneAction` in `Src/Ministers/Food/Rules.cs`); this slice only promotes the cross-minister request shape.

The emitted request should carry `ZoneClass.Growing`, `PlantDef = cropCandidate.CropDef`, `TileCount = cropCandidate.Tiles`, `Terrain.MustSupportGrowing = true`, `Terrain.PreferredFertility = cropCandidate.TerrainFertility`, `Priority = priority`, and `RequestedFrom = "Willie"`. Existing cooking-building support stays as `building_requests[]`; this slice should not weaken starter-kitchen/freezer build requests.

Update Food rule tests that currently assert `Attention` contains "growing tiles" so they assert `ZoneRequests` instead. Update replay corpus fixtures or regenerate them under the no-compat rule.

Expected dashboard result after Slice 2: the current live count of "attentions" for this issue drops to zero, and Chef's flag shows a typed Zone request for growing tiles.

---

## Slice 3 - Willie Routing and Board Visibility

**Sequencing dependency:** Slices 3-5 touch Willie solver/apply/normalizer code currently in flight in worktrees `willie-freezer-apply-readiness` and `willie-nonfreezer-solver-wiring` (`PlacementSolver.cs`, `AssistedApplyService.cs`, `ResourceRequestNormalizer.cs`, `MinisterOfWillie.cs`). Land or rebase on those first; do not branch 3-5 off a master that predates them.

Teach `CabinetCycle` to wake Willie for `zone_requests[]` where `requested_from` is `Willie`, analogous to the current building-request follow-up path. This should be a same-cycle `FlagFired` rules-only follow-up, so the operator does not need to click `Run Rules`.

Extend Willie request ingestion with a zone board. Conservative option: add zone rows to the existing Willie Requests tab with a request-kind discriminator and a "Zone placement pending" outcome until Slice 4. Cleaner option: keep `/api/ministers/willie/solver/requests` building-only and add `/api/ministers/willie/zone-requests` for zone rows. Prefer the cleaner option if it keeps the existing building solver contract simple.

Add a Willie deterministic rule `zone_request_active` that emits visible advice saying a growing-zone request exists and whether a placement option is available. Before Slice 4, it should clearly say placement is not computed yet rather than producing generic prose.

Tests: cabinet cycle wakes Willie on a `ZoneRequest` routed to Willie; zone board records full request fields; existing building-request board still records and solves building requests exactly as before.

---

## Slice 4 - Grow-Zone Placement Solver

Add spatial terrain evidence first. Current `TerrainSnapshot` (`Src/Common/Aggregates/Snapshots.cs:280`) has only per-def cell counts and terrain defs — **no cell coordinates** — so there is no usable terrain grid in state today. `TerrainGridDto` does not exist on master, and `LargestEmptyRectangleGenerator` already records `terrain_affordance=unknown`. Drop the "first slice can use the already represented terrain grid" fallback: it is not available. Slice 4 hard-depends on cell-level growable/fertility data landing first, via either the `rimapi-buildability-layers-read` task or new coordinate-addressable terrain ingestion. Do not start the solver until one lands.

Add `GrowZonePlacementSolver` or a small zone-specific solver under Willie. Input: `ZoneRequest`, map id, terrain grid, existing growing zones, stockpile/room/building occupancy, Home/buildable-region bounds, and anchor inventory. Output: 1-3 `ZoneOption`s with rect/cells, plant def, tile count, score trace, and validation/readiness fields.

**Algorithm reuse:** reuse the `AnchorResolver` (near storage/kitchen) and `PlacementEvidence` spine, the largest-empty-rectangle search (`evidence.FreeRects` + ranking in `LargestEmptyRectangleGenerator`), and the NoFit/trace plumbing — but feed the rect search a **growable+unzoned+unoccupied** mask instead of the buildable mask, and strip the `RoomTemplate`/`RoomShell`/`BlueprintAsset` parts (a zone has no walls, doors, or materials). Do **not** use the building `PlacementSolver` directly, and do **not** use WFC (`.plans/wfc-variant-generator.md`): a grow zone is a homogeneous one-crop field with no inter-tile constraints, so there is nothing for WFC to collapse, and its `supports(spec)` would return `[]` for a zone spec anyway.

Scoring should favor growable fertile terrain, compact rectangles, proximity to storage/kitchen or the buildable-region anchor, avoiding existing zones and occupied buildings, and staying inside Home/buildable-region bounds when available. Do not invent path-cost precision unless the live data supports it; if path-cost is used, label it as measured.

No apply yet in this slice unless validation is strong enough. If the solver cannot prove a safe concrete region, emit a no-fit reason that is visible in Willie advice and the Requests/Zone view.

Tests: solver picks high-fertility growable cells over low-fertility cells; rejects occupied/existing-zone cells; returns no-fit when no cell-level terrain exists; records no-fit cause in visible Willie advice.

---

## Slice 5 - Assisted Apply for Growing Zones

Add a new apply payload, for example `create_growing_zone`, with `map_id`, `plant_def`, `rect` (`point_a`/`point_b`), `target_count`, `label`, and `target_summary`. `RimApiClient.CreateGrowZoneAsync` accepts a **rect only** (`point_a`/`point_b`), not an explicit cell list — drop the cell-list option unless you also add a new client method. Do not reuse `place_blueprint_group`; this is a zone write, not a blueprint group.

Use the existing `RimApiClient.CreateGrowZoneAsync` only after adding fresh validation in `AssistedApplyService`: live RIMAPI reachable, loaded map, rect bounds valid, plant def supported, target cells still growable, target cells still sufficiently unoccupied/unzoned, and request/advice not stale. After the write, read back zones or refresh state and record `apply_result`.

**Ownership retag:** `CreateGrowZoneAsync` is documented `Owned by Chef. Only call via the HTN planner primitive` (`Src/GameStateSync/RimApiClient.cs:531`). This slice moves the player-confirmed call to Willie via `AssistedApplyService` (not HTN). Update that doc comment to Willie/assisted-apply ownership, or the code contradicts the Locked Decision that Apply lives in Willie's scope.

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
- ~~Should `ZoneRequest` use `tile_count` only, or also carry a typed `capacity_need`?~~ **Resolved:** `tile_count` only. Locked Decisions forbid broadening `ZoneClass` past `Growing`, so a general `capacity_need` is premature; defer until a second zone class proves the need.
- Should grow-zone placement require the RIMAPI fork buildability-layer read before any Apply path ships, or is terrain grid plus occupancy enough for the first player-confirmed write?
- Should Chef's existing `designate_zone_req` action remain visible once Willie returns a concrete zone option, or should the Willie option supersede it to avoid duplicate player instructions?
