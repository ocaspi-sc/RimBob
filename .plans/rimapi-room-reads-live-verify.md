# RIMAPI Room Reads — Live Verify (freezer options in the dashboard)

> **Verification runbook, not a code slice.** Goal: drive a live colony until the
> Willie **Build Queue → Proposed** section renders real solver option cards
> (today it shows the empty `LaneEmpty`: *"Solver placement options appear here
> when Willie emits `options[]`."*), then **capture a snapshot with options**.
>
> No source edits expected. Each link in the chain is probed; if a link is broken
> by a real code/RIMAPI gap, that becomes a **separate follow-up slice** (flag it,
> do not widen this one). The named todo's narrow scope (RIMAPI room reads →
> anchors populate) is **link L3** below — the most likely break — but the
> runbook walks the whole chain so any failure is localized.

---

## 0. Why Proposed is empty

Options attach **only** to the freezer advice (`willie_freezer_request_active`),
**only** when the solver emits validated `options[]`. The render path is fine and
landed — `buildProposedOptions`
([`MinisterBuildQueueView.tsx:356`](../Dashboard/src/components/minister/MinisterBuildQueueView.tsx))
reads `AdviceItem.options[]` for the Willie scope. So an empty Proposed means
**no options are attaching upstream**, not a UI bug. This runbook finds which link
drops them.

The good news: the solver already **records why**. `MinisterOfWillie.NoteFor`
([`MinisterOfWillie.cs:207`](../Src/Ministers/Willie/MinisterOfWillie.cs)) writes
`"Placement solver no-fit: {reason}."` into the freezer advice **Rationale**, and
the full `PlacementTrace` lands in the replay corpus. So the diagnosis is mostly
*reading*, not guessing.

---

## 1. Triage tree (start in the dashboard)

Open **Willie → Advice** tab and look for the `willie_freezer_request_active`
item:

1. **No freezer advice item at all** → the break is **upstream of the solver**
   (Food not emitting the request, or Willie not seeing it). Probe **L1–L2**.
2. **Freezer item present, Rationale ends "Placement solver no-fit: …"** → read
   the reason; it maps 1:1 to a `NoFitReason`
   ([`PlacementResult.cs:43`](../Src/Ministers/Willie/PlacementResult.cs)). Probe
   the matching link **L3–L5**.
3. **Freezer item present with options** → they should render in Build Queue
   Proposed. If they don't, it's a frontend/scope mismatch (`isScopeMinister`),
   not the solver.

`NoFitReason` → link map:

| Rationale reason | NoFitReason | Probe |
|---|---|---|
| "no kitchen anchor is available…" | `NoAnchors` | **L3** (room reads) |
| "no walkable route to a kitchen anchor" | `NoReachablePath` | **L4** (path-cost) |
| "no freezer drafts were generated" | `NoDrafts` | **L5** (generators) |
| "all freezer drafts failed shared hard gates" | `HardGateRejected` | **L5** |
| "no buildable footprint … passed fork validation" | `ValidationRejected` | **L5** (group-validate) |

---

## 2. Per-link probes

### L1 — Food emits the freezer `building_request`
Precondition ([`Food/Rules.cs:162`](../Src/Ministers/Food/Rules.cs)):
`Infrastructure.Coolers == 0 && daysOfFood >= 20 && FoodUnits > 0`.
- **Fastest trigger:** a colony with food + ≥20-day buffer + **no cooler built**.
- Verify in **Food → Advice**: a `ManageFreezer` item exists carrying a
  `BuildingRequest{ RequestedFrom: "Willie", temperature: freezing }`.
- If absent: the precondition isn't met (a cooler exists, or <20 days food, or
  `FoodUnits == 0`), or an earlier Food rule short-circuited (winter escalate at
  `Rules.cs:156`).

### L2 — Willie sees the request, trace = `freezer_request_active`
- `ActiveWillieBuildingRequests` filters flags to `RequestedFrom == "Willie"`
  ([`MinisterOfWillie.cs:108`](../Src/Ministers/Willie/MinisterOfWillie.cs));
  `ShouldRunPlacementSolver` needs `trace == "freezer_request_active"` **and** a
  freezer request present (`:128`).
- Verify in the **replay corpus** (§3): the Willie entry's `RuleTrace` is
  `freezer_request_active`.

### L3 — Kitchen anchor exists *(the named todo — most likely break)*
The freezer spec resolves **"near kitchen"**, so
`briefing.AnchorInventory.Anchors` must contain a `Class == RoomClass.Kitchen`
anchor (`AnchorResolver.ResolveNear`,
[`AnchorResolver.cs:36`](../Src/Ministers/Willie/Placement/AnchorResolver.cs)).
That anchor is derived from RIMAPI room reads
([`WillieAnchorInventoryDerivation.cs:24`](../Src/StateStore/Derivations/WillieAnchorInventoryDerivation.cs)):
`RoomClassMapper.FromRoleLabel(room.RoleLabel)` **or**
`FromContainedBuildings(containedBuildings)` (a cooking bench).

**Probe RIMAPI directly** (colony loaded):
```
GET /api/v1/map/rooms?map_id=<id>&include_entry_cells=true&include_cells=true&include_contained_buildings=true&include_region=true
GET /api/v1/map/region-at?map_id=<id>&x=<x>&z=<z>
```
Confirm for a real kitchen room: `role`/role-label maps to kitchen **OR**
`contained_building_ids` includes a cooking bench id; `entry_cells`, `region_id`
present and non-empty.

**Then confirm RimBob ingests it:** `RoomRecord.RoleLabel`,
`RoomRecord.ContainedBuildingIds`, `RoomRecord.EntryCells` populate → a
`WillieRoomAnchor{ Class: Kitchen, EntryCells: […] }` appears in the Willie
briefing (HUD anchor evidence / replay briefing). `NoFitReason.NoAnchors` ⇒ this
link failed: either RIMAPI didn't surface the role/contained buildings, or the
test colony has no enclosed kitchen room.

### L4 — Walkable route to the kitchen anchor
FORK3 scoring: `POST /api/v1/map/path-cost/batch` (region tier). A `NoReachablePath`
no-fit means the anchor entry cell isn't reachable from the candidate footprint —
usually an unenclosed/blocked test layout, not a code gap.

### L5 — Drafts generated, gated, and fork-validated
- `NoDrafts`/`HardGateRejected` → generators produced nothing or everything failed
  shared gates (footprint doesn't fit near the anchor). Read the per-draft trace
  (§3) for `generator_id` + `reason`.
- `ValidationRejected` → `POST /api/v1/builder/blueprint-group/validate` returned
  `can_place_all = false`. Read the failing item's `reason` in the trace.

### L6 — Options attach + Apply lights up *(landed `5dd05f5`)*
- `EnrichFreezerAdvice` sets `item.Options` **and** appends per-option
  `place_blueprint_group` actions. With options present, Build Queue Proposed
  renders cards and the **Apply** button enables (no longer "Apply not wired").

---

## 3. Diagnostic surfaces (read WHY without source)

- **Willie → Advice tab** — freezer item Rationale carries the no-fit note.
- **Analytics → Candidates** view (`AnalyticsViewKey 'candidates'`,
  [`scopes.ts:22`](../Dashboard/src/dashboard/scopes.ts)) — renders the placement
  trace: `generator_id`, draft `status`/`reason`, metric rows, diversity reasons.
- **Replay corpus on disk** — `logs/replay/willie-*.jsonl`, entries with
  `OutputKind = "placement_solver"` carry `PlacementSolverReplayOutput`
  (`Status`, `NoFit`, full `PlacementTrace`)
  ([`MinisterOfWillie.cs:290`](../Src/Ministers/Willie/MinisterOfWillie.cs)).
- **Health endpoint** — replay-corpus metadata is summarized by
  `SystemEndpoints` (`logs/replay/*-*.jsonl`,
  [`SystemEndpoints.cs:278`](../Src/ApiHost/Endpoints/SystemEndpoints.cs)).
- **Endpoint coverage** — `SystemEndpoints.cs:61-75` lists the live solver-client
  and `blueprint-group/place` endpoints, useful to confirm the loaded RIMAPI
  build actually serves them.

---

## 4. Deliverable — capture a snapshot with options

Once freezer options render live:
1. Run the **`update-dashboard-snapshot`** skill (added `f1276cd`) to regenerate
   `web/Snapshot/`. Today `web/Snapshot/pages/willie/` has only advice/briefing/
   rules and **no `build_queue.html`** — the capture should now include
   `willie/build_queue.html` **with populated Proposed cards**.
2. Optionally exercise the round-trip **once**: click **Apply** on an option,
   confirm the freezer group places (RIMAPI `blueprint-group/place`) and readback
   reports `thing_id`s. *(This is a real map write — only do it on a throwaway
   save. Skip if you want verification without mutation.)*

---

## 5. If a link is genuinely broken → follow-up, not inline fix

- **No kitchen role from RIMAPI** (`role` null/unmapped) → RIMAPI room-role gap.
  `rimapi-room-detail-read` is marked landed; this verify is what confirms it
  *live*. File a RIMAPI follow-up if the loaded build doesn't emit `role`.
- **No `contained_building_ids`** → contained-buildings read gap (RIMAPI).
- **Empty `entry_cells`** → entry-cell read gap; anchor falls back to centroid
  (still resolves, lower-quality scoring).
- **Non-freezer rooms never solve** → expected; that's the separate
  **non-freezer → solver wiring** slice (`willie-placement-wiring.md` §3), out of
  scope here.

## 6. Out of scope
- Non-freezer → solver wiring.
- Any RimBob/RIMAPI **code** change (each break is its own slice).
- Best-effort / partial-place UI (deferred from `willie-option-apply-payload` §2).

## 7. Verification (of the verify)
- A live colony hitting L1's precondition shows a freezer option card in Build
  Queue → Proposed with an enabled Apply button.
- When options are absent, the freezer advice Rationale + Candidates view name the
  exact `NoFitReason`.
- `update-dashboard-snapshot` captures `willie/build_queue.html` with options.

## 8. HumanTodo capture (already in Tasks.md — relink to this plan)

```
- [ ] rimapi-room-reads-live-verify [2026-05-30] #rimapi #construction #willie #live Live-verify the freezer-request→solver→options chain end-to-end so Build Queue Proposed renders real option cards: hit /api/v1/map/rooms (entry_cells+cells+contained_buildings+region) and /api/v1/map/region-at, confirm a kitchen room yields a RoomClass.Kitchen anchor, walk the NoFitReason ladder (anchor→path→drafts→validate) via the freezer advice Rationale + Analytics Candidates + replay corpus, then capture a dashboard snapshot with options. Read-only runbook; any broken link is a separate slice. [plan](.plans/rimapi-room-reads-live-verify.md)
```
