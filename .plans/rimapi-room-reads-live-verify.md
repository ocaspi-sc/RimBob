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

## 4. Deliverable — evidence that options attach

Acceptance is **observable state**, not a specific capture tool. Once freezer
options render live, the proof is JSON:

1. **Primary (JSON evidence, no browser):**
   - `GET /api/ministers/willie/snapshot` → the `willie_freezer_request_active`
     advice item carries `options[]` (≥1) with `place_blueprint_group` action
     payloads; Rationale reports "N validated options".
   - `GET /api/briefings/willie/latest` → `anchorInventory.anchors` contains the
     resolved `Class:"kitchen"` anchor; `functionalRooms.roomCountsByClass`
     counts it.
   - Replay corpus `logs/replay/willie-*.jsonl` → newest
     `output_kind:"placement_solver"` entry shows `status:"options"`,
     `no_fit:null` (NOT `NoAnchors`).
2. **Optional (visual only):** run the **`update-dashboard-snapshot`** skill to
   regenerate `web/Snapshot/` if a static HTML artifact of the rendered Build
   Queue → Proposed cards is wanted. This is a presentation export, **not** the
   acceptance proof — skip it for routine verification.
3. **Optional round-trip (mutation):** click **Apply** on an option once, confirm
   the freezer group places (RIMAPI `blueprint-group/place`) and readback reports
   `thing_id`s. *(Real map write — throwaway save only.)*

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

## 9. Live verification — 2026-05-30 (closed via JSON evidence)

**Commit under test:** `edd17b90` ("feat(willie): add multifunction anchors")
**Host:** `http://localhost:5000`, map_id=0, 3 colonists, RimWorld live.
**Evidence sources:** `GET /api/ministers/willie/snapshot` · `GET /api/briefings/willie/latest` · `C:\Users\orca\AppData\Local\RimBob\logs\replay\willie-20260530.jsonl`

### L3 — RESOLVED: Kitchen anchor surfaced from FueledStove 44710

`GET /api/briefings/willie/latest` → `.anchorInventory.anchors` contains three anchors, all on `roomId == "5"` (the barracks room that also holds the stove):

| class | centroid | containedBuildingIds (excerpt) |
|---|---|---|
| `barracks` | {x:90, y:0, z:188} | …44710, 44848 |
| `butcher`  | {x:94, y:0, z:186} | …44710, 44848 |
| **`kitchen`** | **{x:93, y:0, z:186}** | …**44710**, 44848 |

Building `44710` (FueledStove) appears in all three anchors' `containedBuildingIds`, confirming it was ingested via `/api/v1/map/work-tables` into `state.Buildings` and drove the `kitchen` class via `FromContainedBuildings`. The anchor's `roleLabel` is still `"barracks"` (the RIMAPI room label), but the `class` override to `"kitchen"` is correct.

`.functionalRooms.roomCountsByClass` = `{ Barracks: 1, Butcher: 1, Kitchen: 1 }` — all three multifunction anchors registered.

### Full chain L1 → L6 result

| Link | Result |
|---|---|
| **L1** Food emits freezer request | PASS — advice item `willie_freezer_request_active` present in snapshot |
| **L2** Willie trace = `freezer_request_active` | PASS — replay line 4: `selected_rule: "placement_solver"` |
| **L3** Kitchen anchor exists | **RESOLVED** — `class: "kitchen"`, roomId 5, FueledStove 44710 (centroid {x:93,y:0,z:186}) |
| **L4** Walkable route | PASS — solver proceeded past anchor resolution; no `NoReachablePath` |
| **L5** Drafts generated, gated, fork-validated | PASS — 14 drafts in trace: 2 `validated` (template_anchored + largest_empty_rect), 1 `validation_rejected` (can_place_all_false on third candidate) |
| **L6** Options attach | PASS — 2 options attached to advice item |

**Willie snapshot** (`/api/ministers/willie/snapshot`) — `willie_freezer_request_active` rationale:
> "Placement solver: 2 validated options; materials_ready=unknown, apply_ready=blocked."

Both options: label `"Starter freezer"`, summary `"Validated 36-asset starter freezer near kitchen."`, asset_count 36. Option IDs: `placement_freezer_96_194` and `placement_freezer_81_194`.

**Replay corpus** — `willie-20260530.jsonl` line 4 (captured 2026-05-30T17:33:55Z):
- `output_kind: "placement_solver"`
- `status: "options"` (not a no-fit)
- `no_fit: null` — `NoAnchors` is NOT present
- Trace: 2 drafts reach `status: "validated"`, anchor_room_id `"5"` throughout

### §4 browser-snapshot deliverable superseded

The original §4 deliverable (run `update-dashboard-snapshot`, capture `willie/build_queue.html` with populated Proposed cards) was replaced by operator decision with live API + replay-corpus JSON evidence. No browser tool was used. Evidence logged above from:
- `GET http://localhost:5000/api/ministers/willie/snapshot`
- `GET http://localhost:5000/api/briefings/willie/latest`
- `C:\Users\orca\AppData\Local\RimBob\logs\replay\willie-20260530.jsonl` (line 4, `output_kind: placement_solver`)

### Remaining downstream concern

`apply_ready=blocked` and `materials_ready=unknown` on both options are expected at this stage — the apply-gate and material-readiness check are a separate concern not in scope of this runbook. This is tracked as the **freezer apply-readiness** follow-up slice (being authored in parallel as a sibling plan).

---

## 8. HumanTodo capture (already in Tasks.md — relink to this plan)

```
- [ ] rimapi-room-reads-live-verify [2026-05-30] #rimapi #construction #willie #live Live-verify the freezer-request→solver→options chain end-to-end so Build Queue Proposed renders real option cards: hit /api/v1/map/rooms (entry_cells+cells+contained_buildings+region) and /api/v1/map/region-at, confirm a kitchen room yields a RoomClass.Kitchen anchor, walk the NoFitReason ladder (anchor→path→drafts→validate) via the freezer advice Rationale + Analytics Candidates + replay corpus, then capture a dashboard snapshot with options. Read-only runbook; any broken link is a separate slice. [plan](.plans/rimapi-room-reads-live-verify.md)
```
