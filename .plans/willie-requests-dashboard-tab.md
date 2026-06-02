# Willie → Requests dashboard tab (per-request solver memory)

**Status:** PLANNED — not started. Design-only; awaiting "land it" before Codex executes.
**Owner:** Willie (Construction minister) + Dashboard.
**Scope:** `Src/Ministers/Willie/WillieSolverStore.cs`, `Src/Ministers/Willie/MinisterOfWillie.cs`, `Src/ApiHost/Endpoints/MinisterEndpoints.cs`, `Src/Tests/Willie/*`; `Dashboard/src/dashboard/scopes.ts`, `dashboard/ministerViewRegistry.tsx`, `dashboard/semanticIcons.ts`, `api/ministers.ts`, new `components/minister/MinisterRequestsView.tsx`, extracted `components/minister/BlueprintFootprintThumbnail.tsx` (+ import swap in `MinisterBuildQueueView.tsx`), `styles.css`; docs `Docs/design/dashboard.md`, `Docs/design/ministers/construction.md`.

**Locked decisions (this session):** (1) Add **per-request solver memory** in the backend so every listed request shows its own options/error — not frontend-only best-effort. (2) The list holds **building/placement requests only** (the only kind the solver processes). Labor/item/attention requests are out of scope for this tab.

---

## Goal

New Willie minister tab **Requests**: a left sidebar listing every inbound building request aimed at Willie, and a main detail pane showing **all** of the selected request's fields plus its placement-solver outcome — the validated option footprints if the solver produced them, or the no-fit / error message if it did not, or an "awaiting solve" state if Willie has not solved that request yet.

User intent: *"sidebar with list of requests; for each, the main view shows all of its fields, and the solver options if generated — or error message if not."*

---

## Investigation findings (why a backend change is required)

| Fact | Evidence | Consequence |
| --- | --- | --- |
| Willie solves **one** selected request per cabinet cycle | `Rules.SelectPlacementRequest` → `TryGetPlacementRequest` → single `TrySolvePlacementAsync` — [MinisterOfWillie.cs:40](../Src/Ministers/Willie/MinisterOfWillie.cs#L40) | Only one request gets a fresh outcome each cycle |
| `WillieSolverStore` keeps only the **latest** snapshot per minister | `Dictionary<string, WillieSolverSnapshot> _latest`, overwritten in `Record` — [WillieSolverStore.cs:8](../Src/Ministers/Willie/WillieSolverStore.cs#L8) | No per-request history exists today; `/solver/latest` returns exactly one outcome |
| Inbound requests are computed each cycle but **not persisted** as a board | `ActiveWillieBuildingRequests(activeFlags, cycle.Flag)` — [MinisterOfWillie.cs:32](../Src/Ministers/Willie/MinisterOfWillie.cs#L32) | The list of "current requests" is recomputable but currently lives only inside the cycle |
| Placeable **options** live on the advice item, **not** in the solver snapshot | `PlacementSolverReplayOutput` carries `Trace` only; options attached via `EnrichAdviceItem` from `result.Options` — [MinisterOfWillie.cs:264](../Src/Ministers/Willie/MinisterOfWillie.cs#L264), [PlacementResult.cs:6](../Src/Ministers/Willie/PlacementResult.cs#L6) | To show options per stored request, the snapshot must additionally carry `result.Options` |
| `WillieSolverRequestSnapshot` is a **reduced** projection | 8 fields; drops capacity/adjacency/power/temperature/materials/urgency/deadline/quantity — [WillieSolverStore.cs:65](../Src/Ministers/Willie/WillieSolverStore.cs#L65) | "Show all fields" needs the **full** `BuildingRequest`, not the reduced snapshot |

So the store must learn two new things: the **current inbound request board** (full requests, recorded every cycle) and a **per-request outcome memory** (keyed by request identity, carrying the solver output + its options). The read endpoint then joins board → outcome.

Reused machinery (no new engine): inbound-request derivation (`ActiveWillieBuildingRequests`), source-minister attribution (`SourceMinisterForRequest`), the `AdviceOption` core type (already serializes to the dashboard `AdviceOption` shape), the existing `BuildingRequest` JSON contract (already serialized inside flags' `building_requests`), and the dashboard's existing `.build-option-*` / `.blueprint-thumbnail` CSS.

---

## Design

### D1 — Store gains a request board + per-request outcome memory
`WillieSolverStore` (live, singleton — `Program.cs:145`) keeps its existing `_latest` (so `/solver/latest` and the current Solver tab are untouched) and adds, per minister:

- `_board: IReadOnlyList<WillieInboundRequest>` — the current inbound building requests aimed at Willie, in order. `WillieInboundRequest(BuildingRequest Request, string? SourceMinister, string RequestKey)` carries the **full** request.
- `_byRequest: Dictionary<string, WillieSolverSnapshot>` — most-recent solver snapshot keyed by `RequestKey`.

New members:
- `void RecordInbound(string minister, IReadOnlyList<WillieInboundRequest> board)` — replaces the board and **prunes** `_byRequest` to only keys present in the new board (bounds memory; a request that drops off the board loses its stale outcome and re-solves fresh if it returns).
- `Record(WillieSolverSnapshot)` — unchanged behavior for `_latest`, **plus** upserts `_byRequest[RequestKey(snapshot.Request)] = snapshot` when `snapshot.Request` is non-null.
- `IReadOnlyList<WillieRequestBoardRow> RequestBoard(string minister)` — for each board entry in order, returns `(WillieInboundRequest Inbound, WillieSolverSnapshot? Outcome)` where `Outcome = _byRequest.GetValueOrDefault(key)`.
- `static string RequestKey(string targetClass, string? targetDef, string? roomClass, string request)` — single key builder; two thin callers (one for `BuildingRequest`, one for `WillieSolverRequestSnapshot`) so the solved-snapshot key and the board key match on the same 4 intrinsic fields. Collisions intentionally merge identical asks onto one row.

All access under the existing `_lock`.

### D2 — Snapshot carries its options (live store only, not persisted)
Add `IReadOnlyList<AdviceOption> Options` (default `[]`) to `WillieSolverSnapshot`. Populate it at the `Record` call from `attempt.Result?.Options ?? []`. **Deliberately not** added to `PlacementSolverReplayOutput` — that record is persisted to the replay corpus, and we keep options out of the corpus to avoid a persisted-shape change. (Per AGENTS.md this is purely additive to the live in-memory store + the live API payloads; no wire/persistence format changes, so **no wipe-and-regen** is needed.)

### D3 — Willie records the board every cycle
In `MinisterOfWillie.RunPlayCycle`, right after `inboundRequests` is computed (line 32, **before** the `switch`), call `solverStore.RecordInbound(Name, board)` where `board` maps each inbound request to `WillieInboundRequest(request, SourceMinisterForRequest(request, activeFlags, cycle.Flag), RequestKey(...))`. This captures the full current request list on **every** path — including cycles whose decision is non-placement (e.g. `power_net_deficit`) and the `Escalate` path — so the tab shows all current requests even when none was solved this tick. In the placement branch, extend the existing `solverStore.Record(...)` snapshot with `Options: attempt.Result?.Options ?? []`.

### D4 — New read-only endpoint `/api/ministers/willie/solver/requests`
`GET /api/ministers/willie/solver/requests` reads `solverStore.RequestBoard("Willie")` and returns:

```
{ minister, requests: [ {
    request,            // full BuildingRequest, snake_case via the existing advice JSON contract
    sourceMinister,
    gameTick,           // from the matched outcome, or null
    capturedAt,         // from the matched outcome, or null
    output,             // { status, noFit, draftable, placementValid, materialsReady, applyReady, trace, errorType, errorMessage } | null
    options             // AdviceOption[] (empty unless status === "options")
} ] }
```

Empty `requests` when Willie has not run yet (board never recorded) → the view shows a "no requests seen yet" empty state. Register the endpoint in `coverage.Register(...)` next to the existing `/solver/latest` registration (AGENTS.md: new endpoints are dashboard-visible). No request body, read-only.

### D5 — New dashboard tab + view (master-detail)
- `scopes.ts`: add `'requests'` to `MinisterViewKey`; add `{ key: 'requests', label: 'Requests' }` to `ministerViews` (after `solver`); insert `'requests'` into `rulesOnlyMinisterViews` after `'solver'` (Willie's enabled set). **Also** extend the `allMinisterViews` exclusion filter to `view !== 'build_queue' && view !== 'solver' && view !== 'requests'` so non-Willie ministers do not get an empty Requests tab (mirrors how `solver`/`build_queue` are Willie-only).
- `ministerViewRegistry.tsx`: import `MinisterRequestsView`; add renderer `requests: ({ scope, systemHealth }) => <MinisterRequestsView scope={scope} systemHealth={systemHealth} />`.
- `semanticIcons.ts`: add `requests: common.construction` to `viewIcons`.
- `api/ministers.ts`: add `fetchSolverRequests(scope, signal)` → `/api/ministers/${scope}/solver/requests`; add types `WillieRequestRow` (reuses the existing TS `BuildingRequest` from `types/advice` for full fields + `output: WillieSolverOutputPayload | null` + `options: AdviceOption[]` + `sourceMinister`/`gameTick`/`capturedAt`) and `WillieRequestBoardPayload { minister; requests: WillieRequestRow[] }`.
- New `components/minister/MinisterRequestsView.tsx`:
  - Willie-only guard (else `EmptyState` "REQUESTS NOT WIRED", matching SolverView/BuildQueueView).
  - Fetch the board via `useAsyncResource`, keyed on `[scope.key, traceKey]` (reuse SolverView's latest-trace key so it refreshes when Willie re-runs).
  - Local `selectedKey` state, default to the first row; reset when the row set changes.
  - **Left sidebar** (`feature-filter-sidebar`-style list of `<button>`s): per request — field icon, request label (`IconizedText`), `source → Willie` eyebrow, priority `StatusPill`, and an outcome status pill (`options` / `no-fit` / `error` / `awaiting`). Active row highlighted via `aria-current`/`active` class.
  - **Right detail**: exhaustive field list for the selected request — `reason`, `target_class`, `target_def`, `room_class`, `capacity_need`, `adjacency`, `power`, `temperature`, `materials_on_hand`, `urgency`, `deadline`, `quantity`, `priority`, `requested_from`, `source_minister`. Then a **Solver outcome** section:
    - `status === 'options'` with options → read-only option cards (footprint thumbnail + `est_materials` + readiness pills), reusing the existing `.build-option-card` / `.blueprint-thumbnail` / `.build-option-readiness` / `.build-option-materials` classes. **No Apply button** — Apply stays owned by the Build Queue tab; this tab is diagnostic. (`// TODO:` note the deliberate omission.)
    - `no_fit` / `error` / `offline` → status pill + the no-fit/error message (reuse SolverView's `statusLabel` phrasing).
    - no outcome → "Awaiting solve — Willie has not run the Placement Solver for this request yet."
  - Empty state when `requests.length === 0`.

### D6 — Extract the footprint thumbnail (small shared refactor)
`BlueprintFootprintThumbnail` is currently a private function in `MinisterBuildQueueView.tsx` (pure, stateless). Extract it (plus its `roleColor`/`compareRole`/`normalizeRole`/`roleOrder` helpers) into `components/minister/BlueprintFootprintThumbnail.tsx` and import it in **both** Build Queue and the new Requests view, so the footprint render does not drift between tabs. This is the only edit to `MinisterBuildQueueView.tsx` (swap inline definition for an import — behavior identical, keeps the working Apply flow untouched).

---

## Blast radius

**Backend (C#)**
- `WillieSolverStore.cs` — add board + per-request map, `RecordInbound`, `RequestBoard`, `RequestKey`, `WillieInboundRequest`, `WillieRequestBoardRow`; add `Options` to `WillieSolverSnapshot`. Existing `Record`/`Latest`/`NotSeen` behavior preserved.
- `MinisterOfWillie.cs` — call `RecordInbound` before the `switch` (all paths); pass `Options` into the existing `Record` snapshot. ~6 lines.
- `MinisterEndpoints.cs` — one new `MapGet` + one `coverage.Register`.

**Backend tests**
- `WillieSolverStoreTests.cs` — board record + join, prune-on-rebind, `RequestKey` matching between full request and reduced snapshot, `Options` round-trip. (Existing `Record_OverwritesLatestSnapshotByMinister` stays green — `_latest` semantics unchanged.)
- `MinisterOfWillieTests.cs` — `RecordInbound` invoked with the full board on a non-placement cycle and on the `Escalate` path; solved request's `Options` captured.
- Endpoint shape test if a `MinisterEndpoints` test pattern exists; otherwise cover via store tests + a manual JSON check in verification.

**Frontend (TS)**
- `scopes.ts` (view key + tab + Willie enable + exclusion filter), `ministerViewRegistry.tsx` (renderer), `semanticIcons.ts` (icon), `api/ministers.ts` (fetch + types).
- New `MinisterRequestsView.tsx`; new `BlueprintFootprintThumbnail.tsx`; import swap in `MinisterBuildQueueView.tsx`.
- `styles.css` — `.willie-requests-view` two-pane grid (mirror `feature-filter-sidebar`: `grid-template-columns: minmax(200px, 240px) minmax(0, 1fr)`), the request-list buttons + active state, and the detail field grid. Option cards reuse existing classes (minimal new CSS).

**Docs**
- `Docs/design/dashboard.md` — new Willie Requests view + `/solver/requests` contract.
- `Docs/design/ministers/construction.md` (Willie is the Construction minister; `Docs/DESIGN.md` routes Willie scope here — there is no `willie.md`) — store now keeps a per-request board + outcome memory (note: live-only, not persisted to the replay corpus).

**No-compat note (AGENTS.md):** all changes are additive to in-memory state and live API payloads. The persisted replay-corpus shape (`PlacementSolverReplayOutput`) is intentionally unchanged, so there is **no wire/persistence format change and no wipe-and-regen**. The `/solver/latest` payload gains an additive `options` field that the existing Solver tab ignores.

---

## Step-by-step

1. `WillieSolverStore`: add `WillieInboundRequest`, `WillieRequestBoardRow`, `Options` on the snapshot, the board + per-request map, `RecordInbound`/`RequestBoard`/`RequestKey`. Unit-test in isolation.
2. `MinisterOfWillie`: record the board every cycle (before the switch); attach `Options` to the solved snapshot. Extend minister tests.
3. `MinisterEndpoints`: add `/api/ministers/willie/solver/requests` + coverage registration.
4. `dotnet test` (backend green); manual `GET /solver/requests` JSON check against a running Host.
5. Frontend: `scopes.ts` + registry + icon + `api/ministers.ts` types/fetch.
6. Build `MinisterRequestsView` (master-detail, all fields + options/error/awaiting); extract `BlueprintFootprintThumbnail` and swap the Build Queue import.
7. `styles.css` two-pane + list styles.
8. Docs (`dashboard.md`, `ministers/willie.md`).
9. `npm.cmd run build`; run RimBob; verify the tab live (see below).

---

## Verification (JSON-first, per standing guidance)

Confirm via the live endpoint + a cabinet run, not only the dashboard render:
- New-colony save; run a cabinet cycle so Chef/Welfare emit building requests aimed at Willie. `GET /api/ministers/willie/solver/requests` → assert `requests[]` lists every inbound building request with **full** fields, that the most-recently-solved request carries `output.status` + (`options[]` or a no-fit/error message), and that not-yet-solved requests have `output: null`.
- Run a cycle whose decision is non-placement (e.g. a power concern) → assert the board still lists the inbound requests (RecordInbound fired on a non-solve path).
- Dashboard: open Willie → **Requests**; confirm the sidebar lists the requests, selecting one shows all its fields, options render as read-only footprint cards when present, and the no-fit/error/awaiting states render otherwise.

---

## Deferred (revisit if it bites)

- **Bounded history per request** (last N solves with a timeline) instead of last-write-only — only if debugging needs the trend.
- **Include labor/item/attention requests** in the list (fields-only, "no solver") — the tab is named generically; current scope is building requests only.
- **Wire Apply into this tab** — currently Apply lives only on Build Queue; revisit if users want to act from the request detail.
- **Surface request-level dedup** if the independent-concerns work (`food-rules-independent-concerns`) makes duplicate inbound requests common; the board's `RequestKey` already merges identical asks onto one row.

---

## Summary (landed 2026-06-03)

**Motivation.** You wanted a new **Willie → Requests** tab: a sidebar listing each request, and a main view showing all of a selected request's fields plus its solver options if generated, or the error message if not. Investigation showed the backend only ever kept the *latest single* solve (overwritten every cycle) and Willie solves *one* request per cycle, so per-request options/error could not be shown without giving the store real per-request memory. We added that memory rather than ship a frontend-only stub where most rows would say "awaiting solve".

**Context.** Extends the already-landed Willie **Solver** tab (`MinisterSolverView`, `/solver/latest`, `WillieSolverStore`) with a sibling **Requests** tab — the Solver tab and `_latest` semantics are untouched. Inbound requests are the same `building_requests` aimed at Willie that the Build Queue "Requested" lane reads. Docs: `Docs/design/dashboard.md`, `Docs/design/ministers/construction.md` (Willie is the Construction minister — there is no `willie.md`; the first Codex pass correctly blocked on that plan bug and Claude fixed the path).

**Scope (shipped).**
- **Backend per-request memory** — `WillieSolverStore` now records the current inbound request **board** every cabinet cycle (`RecordInbound`, full `BuildingRequest` fields) plus a `RequestKey`-keyed **outcome map**; `RecordInbound` prunes the map to the current board to stay bounded. `RequestKey` matches a full `BuildingRequest` and the reduced `WillieSolverRequestSnapshot` on the same four intrinsic fields. `WillieSolverSnapshot` gained `Options` (from `result.Options`), **live-only — deliberately not persisted to `PlacementSolverReplayOutput`/the replay corpus**, so no wire/persistence change and no wipe-and-regen.
- **`MinisterOfWillie`** records the board before the decision `switch`, so the tab lists current requests on every path including non-placement and `Escalate`; the solved snapshot now carries its options.
- **New read-only `GET /api/ministers/willie/solver/requests`** joins board → outcome, registered in `coverage.Register`.
- **Dashboard** — new `requests` view key (Willie-only, excluded from `allMinisterViews`); `MinisterRequestsView` master-detail (sidebar list + all request fields + solver outcome: read-only option footprints, or no-fit/error, or "awaiting solve"); `BlueprintFootprintThumbnail` extracted to its own file and shared with the Build Queue (import swap only). Backend `MinisterRegistry`, its test, and `run-rimbob.ps1` got the matching `requests` entry in their mirrors of Willie's enabled-view list. **No Apply button** — Apply stays on the Build Queue.

**How to verify (human).**
- Dashboard: `http://localhost:5000/?scope=willie&view=requests` — Willie → **Requests** tab. Pick a request in the sidebar; the detail pane shows every field plus the solver outcome.
- Endpoint: `GET http://localhost:5000/api/ministers/willie/solver/requests` — empty `requests: []` until Willie runs; after a cabinet cycle with an inbound build request, each row carries the full request + `output` (`options` / `no_fit` / `error`) or `null` (awaiting). Live-confirmed in the worktree: a Welfare→Willie request returned `output.status: no_fit`, `noFit: ValidationRejected`.
- Commands: `dotnet test Src\RimBob.sln` (549 green); `npm.cmd run build` in `Dashboard`.
- Files: `Src/Ministers/Willie/WillieSolverStore.cs`, `Dashboard/src/components/minister/MinisterRequestsView.tsx`.

**Codex run:** 20260603-005204-willie-requests-dashboard-tab · branch `codex/prompt-20260603-005204-willie-requests-dashboard-tab` · landed commit `ba1c5bc`
