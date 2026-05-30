# Willie Multi-Function Room Anchors (ingest work tables + per-function anchors)

## Summary

A room that serves multiple purposes (e.g. a barracks that also holds a stove)
must surface a `RoomClass.Kitchen` anchor so the freezer placement solver can
resolve "near kitchen". Today it cannot, for two stacked reasons:

1. **Work-table buildings never reach `state.Buildings`.** RimBob ingests
   buildings only from `/api/v1/map/buildings`, which excludes work tables
   (stoves, butcher/crafting spots, research benches). The stove is served only
   by `/api/v1/map/work-tables`, which RimBob never calls. So the
   contained-building classifier can never see a stove.
2. **The derivation short-circuits on the room's primary role.**
   `WillieAnchorInventoryDerivation` derives at most **one** `RoomClass` per room
   via `FromRoleLabel(role) ?? FromContainedBuildings(buildings)`. A room labelled
   `barracks` returns `Barracks` and the contained stove is never inspected.

This slice fixes both: **Part A** ingests `/api/v1/map/work-tables` into the
building registry; **Part B** makes the derivation emit one anchor **per detected
function** (primary role class **plus** each work-bench class), so a multi-purpose
room yields both a Barracks anchor and a Kitchen anchor.

Success: a barracks-with-stove produces a `RoomClass.Kitchen` anchor in the Willie
briefing, and the freezer solver advances past `NoFitReason.NoAnchors` (drafts /
path / validate are separate downstream links).

## Current Evidence (live, RIMAPI `:8765`, RimBob `:5000`, map_id=0)

- `/api/v1/map/work-tables?map_id=0` returns the stove:
  `{"id":44710,"thing_def":"FueledStove","label":"fueled stove","position":{"x":93,"y":0,"z":186},"bills_count":1}`
  plus `CraftingSpot` (40382) and `ButcherSpot` (40383).
- `/api/v1/map/buildings?map_id=0` returns **55 buildings, zero cooking** — stove
  `44710` is **absent**. `MapAggregateMapper.FromBuildings` does no filtering, so
  the gap is upstream (the endpoint itself excludes work tables).
- RIMAPI room `id=5` is `role_label="barracks"`, 64 cells, `contained_building_ids`
  = `[40382,40383,40501,40543,40620,40640,40855,40883,44710,44848]` (10 ids,
  includes the stove).
- Live Willie briefing barracks anchor (`room=5`) shows only **3** building ids
  (`40501,40640,40543`) — the 7 work-table / non-ingested ids (incl. stove 44710)
  are dropped by the derivation join `buildingsById.ContainsKey(id)` because they
  are not in `state.Buildings`.
- Live Willie anchors: `barracks` + `bedroom` only — **no Kitchen anchor** ⇒
  freezer solver returns `NoAnchors` ⇒ no `options[]` ⇒ Build Queue Proposed empty.
- `BuildingClassifier.IsCookingBuilding` = `Def.Contains("Stove")`;
  `thing_def="FueledStove"` matches once ingested. `ButcherSpot`→`IsButcherTable`
  ("Butcher") matches; `CraftingSpot` matches no current bench predicate (fine).
- No `GetWorkTablesAsync` client method exists (grep `Src/GameStateSync` = no match).

## Root Cause (one line)

`state.Buildings` has no stove (Part A) **and** even if it did, the barracks role
label masks the contained-stove kitchen classification (Part B).

## Scope

### Part A — Ingest work tables into the building registry

- `Src/GameStateSync/Dtos/MapDto.cs` — add `WorkTableDto` mirroring the live shape:
  `Id` (int), `ThingDef` (string, snake `thing_def`), `Label` (string),
  `Position` (`PositionDto`), `BillsCount` (int, `bills_count`). Envelope is the
  standard `{success,data:[…]}` list.
- `Src/GameStateSync/RimApiClient.cs` — add
  `GetWorkTablesAsync(int mapId, CancellationToken ct)` →
  `GetEnvelopedListAsync<WorkTableDto>($"api/v1/map/work-tables?map_id={mapId}", ct)`,
  mirroring `GetBuildingsAsync` (`:296`).
- `Src/StateStore/IngestionDispatcher.cs` — fetch work tables alongside buildings
  (parallel task near `:47`) and pass both into the building registry build.
- `Src/StateStore/Ingestion/MapAggregateMapper.cs` — extend `FromBuildings`
  (`:260`) (or add a `FromBuildings(buildings, workTables)` overload) to append a
  `BuildingRecord` per work table: `Id = id.ToString()`, `Def = thing_def`,
  `Position`, `Label`. **Union, deduped by id, `/map/buildings` records win** on
  collision (the two id sets are disjoint in practice; guard anyway).
- Net effect: `state.Buildings` now contains the stove (`44710`, Def `FueledStove`),
  so the existing derivation join + `BuildingClassifier` see it with no further
  change. Power/turret/cooler/battery counts are unaffected (work-table defs don't
  match those predicates).

### Part B — Emit one anchor per detected function

- `Src/StateStore/Derivations/Common/RoomClassMapper.cs` — add
  `WorkFunctions(IReadOnlyList<BuildingRecord> buildings)` returning **every**
  work-bench class present (not first-match): Kitchen (`IsCookingBuilding`),
  Butcher (`IsButcherTable`), Research (`IsResearchBench`), Workshop
  (`IsWorkshopBench`), Hospital (`IsHospitalBed`). Each result carries its source
  `BuildingRecord` so the anchor locus can be the bench cell. Keep existing
  `FromRoleLabel` / `FromContainedBuildings` unchanged. **Exclude plain
  `IsBed`→Bedroom** here so a barracks doesn't also emit a Bedroom anchor (beds
  stay the primary-only fallback).
- `Src/StateStore/Derivations/WillieAnchorInventoryDerivation.cs` — per room:
  - `primary = FromRoleLabel(role) ?? FromContainedBuildings(buildings)`; skip if null (unchanged gate).
  - Build the class set `{ primary } ∪ WorkFunctions(buildings)`.
  - Emit one `WillieRoomAnchor` **per distinct `RoomClass`**, deduped by
    `(RoomId, Class)`. Primary anchor keeps the room-centroid `Centroid`; each
    work-function anchor uses its **bench position** as `Centroid` (more precise
    "near kitchen" locus). All anchors share the room's `Bounds`, `Cells`,
    `EntryCells`, `CellsCount`, `RoleLabel`, `ContainedBuildingIds`.
- `Src/Common/Briefings/WillieRoomAnchor.cs` — **(recommended, optional)** add a
  diagnostic `AnchorOrigin Origin` (`PrimaryRole` | `ContainedBuilding`) and
  `string? SourceBuildingId`, so the HUD/replay can explain "Kitchen anchor from
  FueledStove 44710". If included, thread through serialization + the dashboard
  anchor readout; if deferred, leave the model shape unchanged.

### Unchanged on purpose (verify, do not edit)

- `Src/Ministers/Willie/Placement/AnchorResolver.cs` — `ResolveNear` filters
  `Anchors.Where(a => a.Class == targetClass)` (`:37`); multiple anchors per room
  resolve correctly with no change.
- Generators (`TemplateAnchoredGenerator`, `LargestEmptyRectangleGenerator`,
  `ReuseExistingFootprintGenerator`) consume **resolved** anchors, already
  Class-filtered — no change.
- `Src/StateStore/Derivations/WillieBriefingDerivation.cs` — `DeriveFunctionalRooms`
  (`GroupBy Class`, `:61`) now reflects multi-function capability for free;
  `FreezerAnchorCount` (`:59`, Freezer comes from role label only) is unaffected.
  Verify both; no edit expected.
- `Src/Ministers/Willie/WillieStateSummary.cs` — "N classified anchors" (`:13`)
  count grows; cosmetic, arguably more correct. Leave as-is.

## Decisions

- **Merge work tables into the single `BuildingRegistry`** (not a parallel store):
  the derivation join and `BuildingClassifier` then work unchanged. Dedup by id.
- **One anchor per class, deduped by `(RoomId, Class)`.** A dedicated kitchen
  (role `Kitchen` + stove) still emits exactly one Kitchen anchor, so the existing
  derivation test's `HaveCount(2)` invariant holds.
- **Work-function anchor `Centroid` = bench cell**, not room centroid, for tighter
  "near kitchen" placement.
- **Beds do not add a Bedroom function anchor** (barracks-noise guard); the bed
  fallback stays primary-only via `FromContainedBuildings`.
- **No RIMAPI/fork change.** `/api/v1/map/work-tables` already exists and serves
  the stove; this is pure RimBob ingestion + derivation.

## Test Plan

- `Src/Tests/Ingestion/RimApiClientTests.cs` — `GetWorkTablesAsync` hits
  `/api/v1/map/work-tables?map_id=…` and parses `thing_def` + `position`.
- `Src/Tests/State/IngestionDispatcherTests.cs` — work-table ids merge into the
  building registry (stove id present after a refresh); id-collision dedup guard.
- `Src/Tests/State/WillieAnchorInventoryDerivationTests.cs` —
  - keep existing single-function `HaveCount(2)` case green;
  - **add** a barracks room (`role="Barracks"`) containing a `FueledStove` →
    yields **both** a `Barracks` anchor (room centroid) and a `Kitchen` anchor
    (stove position), deduped, same `EntryCells`/`Cells`.
- `Src/Tests/Willie/AnchorResolverTests.cs` — a multi-purpose room resolves a
  Kitchen anchor for a freezer spec.
- `Src/Tests/Willie/PlacementSolverTests.cs` — freezer request + barracks-with-stove
  evidence → solver finds the kitchen anchor and produces drafts (no longer
  `NoAnchors`).
- `Src/Tests/Willie/WillieBriefingShapeTests.cs` — briefing shape stable with
  repeated `RoomId` across anchors; `DeriveFunctionalRooms` counts the kitchen.
- Run targeted Willie + State + Ingestion tests, then
  `dotnet build .\Src\ApiHost\RimBob.Host.csproj --configuration Debug`.
  (Stop any live `RimBob.Host` first — running hosts lock the output DLLs.)

## Live Verification (after land + rebuild + host restart)

- RIMAPI is unchanged, so no DLL redeploy is required.
- `/api/briefings/willie/latest` anchors include a `Kitchen` anchor for room 5
  (sourced from stove 44710).
- Trigger the freezer request; the freezer advice Rationale no longer says
  `"…no kitchen anchor is available…"`. If options attach, Build Queue Proposed
  renders cards; if a **downstream** link no-fits (`NoReachablePath` /
  `NoDrafts` / `HardGateRejected` / `ValidationRejected`), that is the next slice —
  this one's success is the Kitchen anchor surfacing + the solver passing
  `NoAnchors`.

## Relationship to `rimapi-room-reads-live-verify`

This is the broken link that the live-verify runbook (L3) predicted. Landing this
converts the current colony's `NoAnchors` into a real kitchen anchor without
requiring the player to build a dedicated single-purpose kitchen — i.e. it
unblocks the freezer→solver→options chain for multi-purpose colonies and lets the
live-verify capture proceed.

## Docs

- `Docs/design/ministers/construction.md` — record that (1) work tables are
  ingested into the building registry, and (2) rooms surface an anchor per
  functional building, so multi-purpose rooms (barracks-with-stove) yield a
  Kitchen anchor in addition to their primary-role anchor.

## Out of Scope

- Any RIMAPI/fork code change (the endpoint already exists).
- Carving a kitchen sub-room out of a multi-purpose room; the freezer is placed
  near the room, anchored at the bench cell.
- Downstream no-fit links (path-cost, draft fit, group-validate) — separate slices
  diagnosed via the freezer Rationale + Analytics Candidates.
- Non-freezer → solver wiring (tracked separately).

## Assumptions

- `/api/v1/map/work-tables` keeps returning `id` matching room
  `contained_building_ids`, plus `thing_def` + `position`.
- Work-table ids and `/map/buildings` ids are disjoint (dedup guard covers
  collisions regardless).
- A multi-purpose room's `contained_building_ids` already lists its work tables
  (confirmed: room 5 includes 44710).
