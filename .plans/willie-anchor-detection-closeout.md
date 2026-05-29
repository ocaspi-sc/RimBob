# Willie - Room/Anchor Detection Closeout

> Agent-created design plan (design mode: Claude writes the plan, Codex lands it).
> Closes the residual loop on "Room/anchor detection" — the three RIMAPI reads
> that fed it already landed; this plan covers consumption, coverage truth,
> live verification, and doc reconciliation. **No new RIMAPI endpoints.**

## Correction / context (verified against source 2026-05-30)

The meta-plan ([`willie-meta-plan.md`](willie-meta-plan.md) §2 mermaid `ROOM` node,
§4 step 4) flags **Room/anchor detection** — resolve `near:<class>` → map
location — as the "hard sub-problem" that "may gate Solver1." That framing is
**stale**. All three feeder reads landed end-to-end:

| Read | Tasks | RIMAPI side | RimBob side |
|---|---|---|---|
| `rimapi-room-entry-cells` | [Tasks.md:26](../Tasks.md) `[x]` | `/api/v1/map/rooms?include_entry_cells=true` | `RoomDto.entry_cells` → `RoomRecord.EntryCells` |
| `rimapi-map-region-at` | [Tasks.md:25](../Tasks.md) `[x]` | `/api/v1/map/region-at` + `region_id` on rooms | `RoomDto.region_id` → `RoomRecord.RegionId` |
| `rimapi-room-detail-read` | [Tasks.md:95](../Tasks.md) `[x]` | `contained_building_ids` / `cells` / `bounds` on rooms | `RoomRecord.{ContainedBuildingIds,Cells,Bounds}` |

Consumption that already shipped:

- [`MapAggregateMapper.cs:229-231,303-307`](../Src/StateStore/Ingestion/MapAggregateMapper.cs) ingests all three fields (`ContainedBuildingIds` falls back to `ContainedBedIds`).
- [`WillieAnchorInventoryDerivation.cs:19,34,39`](../Src/StateStore/Derivations/WillieAnchorInventoryDerivation.cs) populates anchor `ContainedBuildingIds`, `EntryCells`, `Bounds`, `Cells`; `Centroid` now computed from room cells, not just bed positions. `RegionId` remains on raw `RoomRecord`, not the derived anchor layer.
- [`AnchorResolver.cs:53-61`](../Src/Ministers/Willie/Placement/AnchorResolver.cs) resolves target cell as **EntryCells-first**, centroid fallback (`AnchorMatchReason`).
- [`WalkablePathCostScorer.cs:27-29`](../Src/Ministers/Willie/Placement/WalkablePathCostScorer.cs) + [`PathCostLookup.cs:24-28`](../Src/Ministers/Willie/Placement/PathCostLookup.cs) feed the EntryCells-derived `TargetCell` into FORK3 path-cost scoring (Manhattan fallback when the probe is unavailable).
- `ReuseExistingFootprintGenerator` consumes `EntryCells`.
- Round-trip tests: `AggregateMapperTests`, `RimApiClientTests`, `WillieAnchorInventoryDerivationTests`.

**Conclusion:** Room/anchor detection is **not blocking** anything. Solver1/2/3
shipped consuming it (`5a8e6c9` → `7e3be6f`). What remains is residue, not the
hard sub-problem. This plan addresses that residue.

## Scope

Four small slices. A and C are independent and clearly worth doing; B is a
**decision** (may net-delete code); D is hygiene.

### Slice A — data-coverage truth (`HasReachability`)

[`WillieBriefingDerivation.cs:106-107`](../Src/StateStore/Derivations/WillieBriefingDerivation.cs)
hardcodes `HasReachability: false` with `// TODO: flip true once PS1 consumes the
FORK3 client methods.` — and PS1 now **does** consume them
(`WalkablePathCostScorer`). The flag is stale-false, so the dashboard
under-reports coverage. AGENTS rule: "dashboard reflects exact state."

The derivation runs in the state-store pass and cannot see whether a *solver*
probe succeeded mid-cycle, which is why it was punted. Resolve by deriving
reachability availability from **state the derivation can see**, not from a
solver round-trip:

- `HasReachability = state.LastRefreshSource == Live` **and** anchors carry a
  usable target signal (`Anchors.Any(a => a.EntryCells.Count > 0 || a.Centroid is not null)`).
  This is the honest "can we rank `near:<class>` at all" signal.
- Update expectations: [`WillieBriefingShapeTests.cs:24`](../Src/Tests/Willie/WillieBriefingShapeTests.cs), [`WillieRulesTests.cs:207`](../Src/Tests/Willie/WillieRulesTests.cs).

**Decision A1 (defer to human):** single `HasReachability` flip vs. adding
granular `HasEntryCells` / `HasRegionData` / `HasRoomCells` flags. The
`willie-briefing-hud` plan already wants a 3-tier have/need-fork/defer signal
and notes the bool→tier change is a follow-up wire change. **Lean:** flip the
existing bool now (no schema churn); fold granular flags into the HUD wire
change, not here.

### Slice B — `RegionId` consumption — DECIDED: B2-remove (anchor layer)

`RegionId` is populated on every anchor ([derivation:40](../Src/StateStore/Derivations/WillieAnchorInventoryDerivation.cs)) and asserted in tests, but **grep finds zero readers** anywhere under `Src/Ministers/Willie/`. The briefing-schema
promised "region-tier batch rank → A* top-K tiebreak," but
[`willie-briefing-fields.md:127`](willie-briefing-fields.md) records that the
region tiering happens **RIMAPI-side inside path-cost batch** (`tier:"region"`),
so the anchor's `region_id` is not required for that. Today it is dead state.

Options considered: **B1** wire client-side region pre-cluster (rejected —
duplicates server-side `tier:"region"`, optimizes an unmeasured batch-size cost);
**B2** retire (chosen).

**Decision (human call, 2026-05-30): B2-remove at the anchor layer.**

- Remove `RegionId` from [`WillieRoomAnchor`](../Src/Common/Briefings/WillieRoomAnchor.cs)
  (the derived, purpose-built record — this is where dead state is sharpest),
  its populate in [`WillieAnchorInventoryDerivation.cs:40`](../Src/StateStore/Derivations/WillieAnchorInventoryDerivation.cs),
  and the `RegionId` assertions in `WillieAnchorInventoryDerivationTests` /
  `WillieBriefingShapeTests` / `ReuseExistingFootprintGeneratorTests`.
- **Keep `RoomRecord.RegionId` + the ingest mapper + the standalone
  `/api/v1/map/region-at` client wrapper (`MapReachDto.RegionId`).** Rationale:
  `RoomRecord` is the raw state-mirror layer; mirroring what RIMAPI emits is
  cheap and complete, and churning the shared aggregate + `AggregateMapperTests`
  / `RimApiClientTests` carries blast radius for no gain. The dead-state problem
  lives on the *derived* anchor, not the raw mirror.
- **Re-surface path is cheap** if the dashboard solver-trace panel later wants
  region hops: RIMAPI still emits `region_id`, `RoomRecord` still carries it —
  re-add the anchor field + populate. No RIMAPI or ingest work needed to revive.

### Slice C — live verification + missing Tasks capture

The round-trip tests prove RimBob parses **mock** JSON. There is no check that a
running RIMAPI build actually emits `entry_cells` / `region_id` /
`contained_building_ids` in-game — the sibling FORK3 reach endpoints have
[`rimapi-map-reach-live-verify`](../Tasks.md) (`[ ]`, Tasks.md:28) but these
reads have no equivalent.

- Add a Tasks capture `rimapi-room-reads-live-verify` (sibling to the reach one):
  load the RIMAPI DLL, hit `/api/v1/map/rooms?include_entry_cells=true&include_cells=true`
  and `/api/v1/map/region-at`, confirm the three raw room fields are present for
  a real room, and snapshot a live colony to confirm anchors populate
  (`EntryCells`, `ContainedBuildingIds`) while raw `RoomRecord.RegionId` remains
  available — not just mock fixtures.
- This is a verification capture, not code; it closes the "tests pass but is it
  live?" gap.

### Slice D — doc reconciliation

Make the living docs match reality (historical landed-plan records left as-is —
they are point-in-time):

- [`willie-meta-plan.md`](willie-meta-plan.md): color the `ROOM` mermaid node
  **done**; rewrite §4 step 4 from "the hard sub-problem; may gate Solver1" to
  "landed — anchors carry entry-cells/region/contained-buildings; consumed by
  AnchorResolver + path-cost scorer"; drop the "New captures" framing in §6
  (lines 343-344) since both captures are `[x]`.
- [`willie-code-review.md:55-56`](willie-code-review.md) + [`willie-code-review-findings.md:86`](willie-code-review-findings.md): `EntryCells` is no longer a stub (fully consumed); `RegionId` remains on raw rooms and is intentionally retired from `WillieRoomAnchor`, not a "RIMAPI stub." Re-tag accordingly.

## Out of scope

- Any new RIMAPI endpoint — all three reads landed.
- `rimapi-building-detail-read`, `rimapi-power-net-read`,
  `rimapi-stockpile-detail-read`, `rimapi-buildability-layers-read` — separate
  captures, separate concerns (`fire_risk`, power, storage).
- Solver scoring algorithm changes beyond region pre-cluster (only if B1 chosen).
- Editing historical landed-plan records (`placement-solver-1.md`,
  `willie-briefing-derivation.md`) beyond an optional one-line "superseded" note.
- The bool→3-tier coverage wire change (owned by `willie-briefing-hud`).

## Dependencies / order

```
A (coverage flip)        ── independent ──┐
B (RegionId B2-remove)   ── independent ──┤→ D (doc reconcile, last)
C (live verify)          ── independent ──┘
```

A, B, C are independent and can land in any order. B is now a settled deletion
slice (B2-remove, anchor layer). D lands last so the docs reflect final state.

## Verification

- `dotnet build` + `dotnet test` for the Willie + State test projects
  (`Src/Tests/Willie`, `Src/Tests/State`, `Src/Tests/Ingestion`).
- Slice A: assert `HasReachability` true on a live-origin briefing with anchors,
  false on offline/empty.
- Slice C: manual live smoke per the new capture; record actuals in the landed
  Summary.

## Gimp slices

- **CO-A** — coverage flip + test updates (1-2 files: `WillieBriefingDerivation`,
  two test files). Small.
- **CO-B** — B2-remove deletion slice: drop `RegionId` from `WillieRoomAnchor`
  + derivation populate + 3 Willie test assertions. Keep `RoomRecord.RegionId`
  + ingest + `/map/region-at` wrapper untouched. Small, no behavior change
  (field was unread).
- **CO-C** — Tasks capture only (no source); pairs with a live run.
- **CO-D** — doc edits to `willie-meta-plan.md` + the two code-review docs.
