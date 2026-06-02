# Willie Home-Area Buildable-Region Anchor (fallback anchor from the player's Home area)

## Summary

A new/sparse colony has a player-painted **Home area** but no enclosed
role-bearing rooms. The Placement Solver only ever resolves anchors from
detected **rooms** (`AnchorResolver.ResolveNear` → `briefing.AnchorInventory`,
which is derived from `state.Rooms` only), so it returns
`NoFitReason.NoAnchors` and dead-ends — no drafts, no options, Build Queue
Proposed empty. The user's own report: *"Willie solver says 'no anchors'.
However, I set a home zone in the map, that could be an anchor."* It can't be
one today because the Home area never reaches the anchor pipeline.

This slice makes the **Home area** an ingestible, **low-priority fallback
"buildable region" anchor**: when no room anchor resolves, the solver anchors
placement at the Home area instead of dead-ending. It does **not** compete with
real room anchors — it is used **only** when `ResolveNear` yields zero — which
is exactly the "low-priority" semantics requested, achieved by resolution order
rather than score weights.

Success: a colony with a Home area but **no recognized rooms** advances past
`NoFitReason.NoAnchors` and the solver emits drafts anchored inside the Home
area. (Downstream links — drafts / hard-gate / path / validate — are separate;
this slice's win is converting `NoAnchors` into a real fallback anchor.)

## Background: anchor vs zone vs area (why "home zone" isn't an anchor today)

- **Anchor** = derived only from `RoomRecord`s that map to a known `RoomClass`
  via role label or contained buildings
  (`WillieAnchorInventoryDerivation.Derive`, `:17-44`; skip-if-unclassified
  `:26`). Model: `WillieRoomAnchor` (`WillieRoomAnchor.cs:11-24`).
- **Zone** = RimWorld `Zone` (Growing / Stockpile). Ingested via `ZoneDto`
  (`MapDto.cs:195-203`); the mapper keeps **stockpile rows only**
  (`MapAggregateMapper.IsStockpileZone`, `:255-258`) into `StockpileZone`
  (`Snapshots.cs:106`). Growing/other rows are discarded.
- **Area** = RimWorld `Area` (Home, custom Allowed areas). This is **not** a
  `Zone`. It is the "where colonists clean/maintain and where the player intends
  to build" designation the user painted. RimBob ingests **none** of it today.

`/api/v1/map/zones` is documented as returning **"zones + areas"**
(`Docs/design/RimAPI.md:110`), and `IngestionDispatcher` **already fetches it**
every cycle (`zonesTask`, `:45`) — but `FromStockpiles` throws away every
non-stockpile row (`:80`). So the Home-area row is very likely **already
arriving and being discarded**. (Verification gate below — confirm the live
`type`/`label` string before relying on it.)

## Root cause (one line)

The anchor inventory is sourced from rooms only; the Home area is never
ingested, so `AnchorResolver.ResolveNear` has nothing to fall back on and the
solver returns `NoFitReason.NoAnchors` (`PlacementSolver.cs:63-71`).

## Verification gate (do this FIRST — per `feedback_verify_json_first`)

Before writing the mapper, confirm what `/api/v1/map/zones?map_id=0` actually
emits for the Home area on a live colony that has one painted:

```
GET http://localhost:8765/api/v1/map/zones?map_id=0
```

Capture and record in this plan's evidence:
- The **`type`** string for the Home-area row (candidates: `Area_Home`,
  `Home`, `Area_Allowed`, or similar — RimWorld's class is `Area_Home`).
- Whether the row carries `cells[]`, `cells_count`, `label`, and `id`.
- Whether **other** areas (custom allowed areas, no-roof, build-roof) also
  appear, and their `type` strings (so the keep-filter is specific).

**Branch:**
- **(Expected) Home area appears as a `/map/zones` row** → pure RimBob ingestion
  (Scope below). No RIMAPI change.
- **Home area is absent from `/map/zones`** → the doc's "+ areas" is aspirational
  for Home. Stop and split: file a RIMAPI follow-up to expose
  `map.areaManager.Home` (cells/bounds/count) on `/map/zones` (or a new
  `/map/areas`), and (optionally) land the **map-bounds free-space fallback**
  described in "Fallback if no Home-area data" as an interim. Do not fabricate a
  type string.

## Scope (assumes the gate confirms a Home-area `/map/zones` row)

### Part A — Ingest the Home area into a new aggregate

- **`Src/Common/Aggregates/Snapshots.cs`** — add a small map-area aggregate next
  to `StockpileLedger`/`StockpileZone` (`:100-106`):
  ```csharp
  public sealed record MapAreaRegistry(IReadOnlyList<MapArea> Areas)
  {
      public static MapAreaRegistry Empty { get; } = new([]);
  }

  public sealed record MapArea(
      string Id,
      string Type,            // raw RIMAPI type, e.g. "Area_Home"
      string? Label,
      int CellCount,
      MapRect? Bounds = null,
      MapPosition? Centroid = null);
  ```
  **Do not store the full cell list.** A Home area on a 250×250 map can be
  thousands of cells; the anchor only needs a target cell (centroid) + a region
  (bounds). Keep the aggregate light.
- **`Src/StateStore/.../AggregateDefaults`** (same place `Stockpiles`/`Rooms`
  defaults live) — add `Areas = MapAreaRegistry.Empty`.
- **`Src/StateStore/ColonyState.cs`** — add
  `public Versioned<MapAreaRegistry> Areas { get; } = new(AggregateDefaults.Areas);`
  next to `Stockpiles` (`:24`). Add `Areas.Version` to
  `GetVersionsForWillieBriefing()` (`:86-89`) so the briefing recomputes when the
  Home area changes.
- **`Src/StateStore/Ingestion/MapAggregateMapper.cs`** — add
  `FromAreas(IReadOnlyList<ZoneDto> zones)`:
  - keep rows where `IsHomeArea(type)` (use the **verified** type string; match
    case-insensitively, tolerate `Area_Home` / `Home`).
  - `CellCount = zone.Cells?.Count ?? zone.CellsCount ?? 0`.
  - `Bounds`/`Centroid` from `zone.Cells` when present (min/max for bounds,
    rounded average for centroid — mirror `CenterOf` at `:247`); both null when
    cells absent (centroid then falls back to bounds-center, see Part C).
  - Scope to the Home area only for v1; the registry shape allows more areas
    later. Keep `IsStockpileZone` (`:255`) untouched.
- **`Src/StateStore/IngestionDispatcher.cs`** — after the stockpiles update
  (`:80`), add `state.Areas.Update(MapAggregateMapper.FromAreas(zonesTask.Result));`
  Reuses the **already-fetched** `zonesTask` — no new client call, no new
  endpoint.

### Part B — Emit a low-priority BuildableRegion anchor

- **`Src/Common/Advice/FlagRequests.cs`** — add a sentinel to the `RoomClass`
  enum (`:156-170`):
  ```csharp
  BuildableRegion
  ```
  Serializes snake_case as `buildable_region` via the existing
  `SnakeCaseLowerEnumConverter`. **Blast radius is small** (verified): the enum
  is consumed by if-chains (`RoomClassMapper`) and a spec-keyed template lookup
  (`RoomTemplateSet.TemplateFor` keys off **`spec.RoomClass`**, not the anchor
  class, `:32-45`) — neither breaks. The only consumers needing a touch are the
  dashboard label/icon maps (Part D).
- **`Src/StateStore/Derivations/WillieAnchorInventoryDerivation.cs`** — change
  `Derive` to also accept the area registry and append **one**
  `BuildableRegion` anchor for the Home area:
  - signature: `Derive(ColonyState state)` stays, but read `state.Areas.Value`
    inside (it already reads `state.Buildings`/`state.Rooms` off `state`).
  - after the per-room loop (`:65`), for the Home area emit:
    ```csharp
    new WillieRoomAnchor(
        RoomId: $"area:{area.Id}",
        Class: RoomClass.BuildableRegion,
        RoleLabel: area.Label ?? "home area",
        CellsCount: area.CellCount,
        Centroid: area.Centroid,            // may be null → resolved in Part C
        ContainedBuildingIds: [])
    { Bounds = area.Bounds, Cells = [], EntryCells = [] }
    ```
  - Keep it **last** and deterministic (single Home area expected; if multiple
    areas ever ingested, order by `Id`).

### Part C — Solver fallback (the "only when nothing else" wiring)

- **`Src/Ministers/Willie/Placement/AnchorResolver.cs`** — add a sibling to
  `ResolveNear`:
  ```csharp
  public static IReadOnlyList<ResolvedAnchor> ResolveBuildableRegion(WillieBriefing briefing)
  ```
  Select `AnchorInventory.Anchors.Where(a => a.Class == RoomClass.BuildableRegion)`,
  resolve each via the existing `ResolveAnchor` (`:51-64`) — which prefers entry
  cells, then centroid. For a region anchor with **no centroid**, fall back to
  the **bounds center** (add that branch in `ResolveAnchor`, or compute it before
  constructing). Tag the match reason (extend `AnchorMatchReason` with
  `BuildableRegionFallback`, or reuse `CentroidFallback`).
- **`Src/Ministers/Willie/PlacementSolver.cs`** — at the `NoAnchors` gate
  (`:63-71`), instead of returning immediately when `ResolveNear` is empty:
  ```csharp
  IReadOnlyList<ResolvedAnchor> anchors = AnchorResolver.ResolveNear(spec, briefing);
  if (anchors.Count == 0)
  {
      anchors = AnchorResolver.ResolveBuildableRegion(briefing);
      if (anchors.Count > 0)
          notes.Add("no room anchor matched; using Home-area buildable region as fallback locus");
  }
  if (anchors.Count == 0)
      return NoFit(NoFitReason.NoAnchors, draftTraces, notes, "no room anchor and no Home-area buildable region");
  ```
  Room anchors keep **strict priority** (fallback only runs when `ResolveNear`
  is empty), so this never weakens existing multi-room behaviour.
- **Constrain drafts to the Home area (cheap version).** Generators already
  consume resolved anchors + `PlacementEvidence.FreeRects` (occupancy-only free
  space, `PlacementEvidence.cs:42`). For v1, clamp candidate placement to the
  region anchor's `Bounds` so drafts land **inside** the painted Home area rather
  than anywhere on the map. Full Home-area **cell-mask** intersection (for
  non-rectangular areas) is deferred — it ties into the existing
  `rimapi-buildability-layers` TODO (`PlacementEvidence.cs:41`) and needs the
  cell list this plan deliberately does not store.
- **Generator behaviour (verify, no edit expected):** `TemplateAnchoredGenerator`
  grows a `spec.RoomClass` template at the anchor locus → works with a region
  locus. `LargestEmptyRectangleGenerator` works off free-rects → works.
  `ReuseExistingFootprintGenerator` needs an existing same-class room footprint →
  naturally **no-ops** for a region anchor (`Cells = []`). Confirm none hard-crash
  on an anchor whose `Cells`/`EntryCells` are empty.

### Part D — Dashboard label/icon fallback (small)

- The 6 dashboard files referencing `RoomClass`/`room_class`
  (`MinisterSolverView.tsx`, `MinisterBuildQueueView.tsx`, `MinisterAdviceView.tsx`,
  `ministers.ts`, `InfoOverview.tsx`, `types/advice.ts`) — ensure
  `buildable_region` renders a friendly label ("Buildable region / Home area")
  and a sane icon (`semanticIcons.ts`) instead of raw snake-case. Additive; no
  behavioural change.

## Decisions

- **New `MapAreaRegistry` aggregate**, not a field on `StockpileLedger` — areas
  and stockpiles are distinct RimWorld concepts; the stockpile ledger stays
  resource-focused.
- **Reuse the already-fetched `/map/zones`** rather than add a client method or
  RIMAPI endpoint — the dispatcher already calls it and discards the area rows.
- **Light aggregate (bounds + centroid + count, no cell list)** — Home areas are
  large; the fallback anchor only needs a locus + region.
- **`RoomClass.BuildableRegion` sentinel + fallback-only resolution** is the
  "low-priority" mechanism. No score-weight change: a region anchor is used
  **iff** no room anchor resolves, so it can never outrank a real room.
- **Bounds clamp now, cell-mask later.** Rectangular over-approximation is
  acceptable for a fallback locus; precise masking waits on buildability layers.
- **Home area only for v1.** Custom Allowed areas are out of scope; the registry
  shape leaves room for them.
- **Positive `cells_count` without `cells[]` is still a valid Home fallback.** Live `Area_Home` rows can report a painted count such as `cells_count:20` while omitting the cell list; RimBob treats that as a geometry-light Home anchor, uses map bounds plus a bounds-center target, and exposes the approximation in solver trace notes until the fork supplies area cells.

## Test plan

- **`Src/Tests/Ingestion/RimApiClientTests.cs`** (or mapper test) — a `/map/zones`
  payload containing a Home-area row + stockpile + growing rows: `FromAreas`
  keeps only the Home area (correct `Type`/`CellCount`/`Bounds`/`Centroid`);
  `FromStockpiles` still keeps only the stockpile (no regression).
- **`Src/Tests/State/WillieAnchorInventoryDerivationTests.cs`** —
  - Home area present + no rooms → exactly one `BuildableRegion` anchor
    (`RoomId="area:…"`, centroid/bounds populated), zero room anchors.
  - Home area absent → no `BuildableRegion` anchor (existing room cases stay
    green; the `HaveCount(2)` multifunction invariant unaffected).
- **`Src/Tests/Willie/AnchorResolverTests.cs`** —
  - `ResolveBuildableRegion` resolves the region anchor to its centroid; to
    bounds-center when centroid is null.
  - room anchors still preferred: a spec with a matching room resolves the room
    anchor and **does not** fall back.
- **`Src/Tests/Willie/PlacementSolverTests.cs`** — freezer/bed request + **no
  matching room** + Home area present → solver no longer returns `NoAnchors`;
  emits drafts anchored within the Home-area bounds (or no-fits **downstream**,
  which is acceptable for this slice). Add the fallback trace-note assertion.
- **`Src/Tests/Willie/WillieBriefingShapeTests.cs`** — briefing shape stable with
  a `BuildableRegion` anchor present; `HasAnchorInventory`
  (`WillieBriefingDerivation.cs:105`) true when only the region anchor exists.
- Run targeted Willie + State + Ingestion suites, then
  `dotnet build .\Src\ApiHost\RimBob.Host.csproj --configuration Debug`.
  (Stop any live `RimBob.Host` first — running hosts lock the output DLLs.)

## Live verification (after land + rebuild + host restart)

- RIMAPI unchanged → no DLL redeploy.
- On a colony **with a Home area but no recognized rooms**:
  `/api/briefings/willie/latest` anchor inventory includes a
  `buildable_region` anchor with the Home-area centroid/bounds.
- Trigger a building request (or the missing-room self-trigger). The Willie
  Solver tab funnel advances **past the `anchors` stage**; the no-fit reason is
  no longer `NoAnchors` (it's a downstream link or a real option set). If options
  attach, Build Queue Proposed renders cards inside the Home area.
- Capture the `/map/zones` Home-area JSON row + the resolved anchor into this
  plan's evidence section (JSON-first).

## Fallback if no Home-area data (only if the gate fails)

If `/map/zones` does **not** include the Home area and a RIMAPI change is out of
appetite: land an interim **map-bounds buildable region** — derive a single
`BuildableRegion` anchor from `MapInfoSnapshot.Size` (whole-map bounds, centroid
= map center) so the solver still escapes `NoAnchors`. This loses the
"respect the player's painted area" intent (it's "anywhere on the map") but
keeps the fallback wiring identical, so swapping in real Home-area cells later is
a one-mapper change. Flag clearly in advice trace that placement is unconstrained.

## Docs

- **`Docs/design/RimAPI.md`** — record the verified Home-area `/map/zones` row
  shape (`type`/`label`/`cells`/`cells_count`), resolving the doc's vague
  "zones + areas".
- **`Docs/design/state-store.md`** — add the `MapAreaRegistry` aggregate.
- **`Docs/design/ministers/construction.md`** — record the BuildableRegion
  fallback anchor: when no room anchor resolves, the solver anchors at the Home
  area; bounds-clamped, fallback-only, never outranks a room.

## Out of scope

- Any RIMAPI change (assuming the gate passes; otherwise a separate follow-up).
- Custom Allowed areas, no-roof/build-roof areas — Home area only.
- Full Home-area **cell-mask** placement constraint (non-rectangular areas) —
  deferred to the `rimapi-buildability-layers` work; v1 clamps to bounds.
- Score-weighted competition between region and room anchors in the **same**
  solve — region stays fallback-only.
- Empty-state copy for the Build Queue (tracked separately in
  `willie-proposed-empty-state-copy.md`); this plan changes the **solver**, that
  one changes the **message**.

## Assumptions

- The player **has painted a Home area** (the user's case). A colony with no
  Home area and no rooms still correctly returns `NoAnchors`.
- `/map/zones` returns the Home area as a row with a stable `type` string and at
  least `cells_count` (cells preferred for bounds/centroid) — **to be confirmed
  by the gate**.
- The Home area is treated as a single rectangle via bounds; non-contiguous /
  L-shaped areas over-approximate, acceptable for a fallback locus.
- Generators tolerate a resolved anchor with empty `Cells`/`EntryCells`
  (region anchor) — to be confirmed in `PlacementSolverTests`.

## Relationship to existing work

- Extends the anchor pipeline established by `willie-briefing-derivation` and
  broadened by `willie-multifunction-anchors` (which added per-function room
  anchors). This adds a **non-room** fallback anchor class.
- Complements `willie-proposed-empty-state-copy` (message) and the
  `rimapi-room-reads-live-verify` chain (the live `NoAnchors` repro this fixes
  for room-less colonies).
- Indexed under `willie-meta-plan`.
