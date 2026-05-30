# Willie Solver Tab — expose the Placement Solver trace + a dedicated dashboard view

## Summary

Willie's Placement Solver produces rich diagnostics every cycle — selected rule,
per-generator draft attempts with scoring metrics, diversity dedupe reasons, the
`NoFitReason` enum, and the four-stage readiness ladder — but almost none of it
reaches the dashboard. Today only `AdviceItem.options[]` + per-option readiness
pills surface (Build Queue "Proposed" lane); the no-fit reason leaks **only** as
prose appended to the advice `Rationale`, and the whole `PlacementTrace`
(generators, scores, diversity, room-reuse) is invisible.

Crucially, the data **already exists serialized**: `PlacementSolverReplayOutput`
(`Src/Ministers/Willie/MinisterOfWillie.cs:364-398`) captures Status, NoFit, all
four readiness states, and the full `PlacementTrace`, and is persisted to the
replay JSONL each cycle (`MinisterOfWillie.cs:43,60-61`, `OutputKind:"placement_solver"`).
The gap is **live exposure** — there is no endpoint serving the latest solver
output — plus the dashboard tab to render it.

This slice (1) adds an in-memory latest-only solver store + a read endpoint that
serves the already-built `PlacementSolverReplayOutput`, and (2) adds a Willie-only
"Solver" dashboard tab. The tab ships in phases: **v1** (solve summary + pipeline
funnel + readiness ladder), with **v2** (draft trace + score breakdown) and **v3**
(anchor resolution + spec echo) as follow-on sections reading the same payload.

Success: `GET /api/ministers/willie/solver/latest` returns the most recent solve;
the Solver tab funnel highlights the exact stage a no-fit died at
(`NoAnchors`→anchors, `NoDrafts`→drafts, `HardGateRejected`→gates,
`NoReachablePath`→reachable, `ValidationRejected`→validated), and the readiness
ladder matches the freezer snapshot's `materials_ready`/`apply_ready`.

## Current Evidence

**Solver output is fully serialized but not live-exposed.**
`PlacementSolverReplayOutput.FromResult` (`MinisterOfWillie.cs:375-385`) carries:
```
Status ("options" | "no_fit" | "error"), NoFit?, Draftable, PlacementValid,
MaterialsReady, ApplyReady, Trace (PlacementTrace), ErrorType?, ErrorMessage?
```
`PlacementTrace` (`PlacementResult.cs:14-25`): `SelectedRule`, `Drafts[]`
(`PlacementDraftTrace`: `GeneratorId`, `AnchorRoomId`, `Status`, `Reason?`,
`Metrics[]` (`MetricValue`: Id, RawValue?, Unit?, Normalized, Weight, Contribution,
Better), `DiversityReason?`), `Notes[]`. `NoFitReason` enum
(`PlacementResult.cs:43-50`): `NoAnchors`, `NoDrafts`, `HardGateRejected`,
`NoReachablePath`, `ValidationRejected`.

Built in `RunPlayCycle` (`MinisterOfWillie.cs:33-44`,
`placementReplayOutput = attempt.ReplayOutput`) and persisted to replay only
(`:49-61`). **No DI store, no endpoint** serves it for the live dashboard.

**Dashboard surfaces a thin slice today.**
- `MinisterBuildQueueView.tsx:71-79` "Proposed" lane renders `options[]` +
  readiness pills (`:227-234`) — the only solver output a user sees.
- No-fit reason is appended to `Rationale` as prose by `NoteFor`/`NoFitNote`
  (`MinisterOfWillie.cs:239-263`); the dashboard never parses it structurally.
- `Dashboard/src/types` has **no** `no_fit` / `placement_trace` / `drafts` field
  (grep confirms only `system.ts:292 traces`).

**Endpoint + store precedents to mirror.**
- Diagnostic read endpoints: `/api/ministers/food/crop-math/latest`
  (`MinisterEndpoints.cs:140-155`) and `/api/ministers/food/hunt-risk/latest`
  (`:157-207`) — compute/return a typed snapshot, register a coverage row
  (`:39-48`).
- Latest-only in-memory stores: `MinisterTraceStore` (served at
  `/api/ministers/{minister}/trace/latest`, `:410-445`) and `RawLlmOutputStore`
  — simple `Latest(label)` stores, DI singletons. **Not** `MinisterOutputStore`,
  which is a durable schema-versioned snapshot store (`MinisterOutputStore.cs:16`
  `SnapshotSchemaVersion`, load switch `:102-126`) — adding a third output kind
  there would force a schema bump + load-path change.

**Tab registration mechanism** (`Dashboard/src/dashboard/scopes.ts`):
`MinisterViewKey` union (`:19`), `ministerViews[]` (`:76-85`), per-scope
`enabledViews`; Willie uses `rulesOnlyMinisterViews` (`:90`,
`['briefing','build_queue','rules','advice']`); `allMinisterViews` (`:87-89`)
**filters out** `build_queue` so only Willie shows it. Renderers in
`ministerViewRegistry.tsx:36-71`. Build Queue is the precedent for a Willie-only
tab that fetches its own data (`MinisterBuildQueueView.tsx:38,44` guard
`scope.key !== 'willie'`).

## Root Cause (one line)

The solver already serializes its full trace (`PlacementSolverReplayOutput`) to
replay, but nothing holds the latest in memory or serves it over HTTP, and there
is no dashboard view to render it — so the solver pipeline is a black box beyond
`options[]` + a prose no-fit note.

## Scope

### Part A — Latest-only solver store (backend)

- **New `Src/Coordination/WillieSolverStore.cs`** (mirror `MinisterTraceStore`):
  a DI singleton holding the latest `WillieSolverSnapshot` per minister label
  (Willie-only in practice, but key by label for symmetry). Methods:
  `Record(WillieSolverSnapshot)` and `Latest(string label)`. Thread-safe
  (`lock`), latest-only (no history).
- **`WillieSolverSnapshot`** record = the already-built
  `PlacementSolverReplayOutput` **plus** live context the replay output lacks:
  driving request summary (`Request`, `TargetClass`, `RoomClass?`,
  `RequestedFrom`, `Priority?`), `GameTick`, `CapturedAt`. Reuse
  `PlacementSolverReplayOutput` verbatim as a nested field — do **not** re-model
  the trace.
- **`MinisterOfWillie` (`:10-19` ctor, `:33-44` RunPlayCycle)** — inject
  `WillieSolverStore solverStore`. In the solver branch, right after
  `placementReplayOutput = attempt.ReplayOutput` (`:43`), call
  `solverStore.Record(new WillieSolverSnapshot(Name, placementRequest-summary,
  briefing.GameTick, DateTimeOffset.UtcNow, attempt.ReplayOutput))`. Only writes
  when the solver actually ran (`TryGetPlacementRequest` true, `:35`); latest
  solve persists until the next one. No write on escalate / non-solver cycles.

### Part B — Read endpoint (backend)

- **`Src/ApiHost/Endpoints/MinisterEndpoints.cs`** — add
  `GET /api/ministers/willie/solver/latest` (mirror `crop-math/latest`
  `:140-155`): resolve the Willie descriptor, read `solverStore.Latest("Willie")`,
  return it, or return a typed placeholder (`Status:"not_seen_yet"`) when no solve
  has happened since Host startup (mirror the trace endpoint's placeholder
  `:421-441`). Register a coverage row beside the others (`:39` pattern):
  `"/api/ministers/willie/solver/latest","available","Latest Willie Placement
  Solver outcome: selected rule, per-generator draft trace + scores, no-fit
  reason, and the draftable/placement/materials/apply readiness ladder."`
- **`Src/ApiHost/Program.cs`** — register `WillieSolverStore` as a singleton
  (next to `MinisterTraceStore` / `RawLlmOutputStore`); confirm `MinisterOfWillie`
  resolves it via DI.

### Part C — Solver tab registration (frontend)

- **`Dashboard/src/dashboard/scopes.ts`** — add `'solver'` to `MinisterViewKey`
  (`:19`); add `{ key: 'solver', label: 'Solver' }` to `ministerViews` (`:76-85`,
  after `build_queue`); add `'solver'` to `rulesOnlyMinisterViews` (`:90`) so
  Willie shows it; add `'solver'` to the `allMinisterViews` filter (`:87-89`,
  alongside `build_queue`) so other ministers do **not** get a Willie-only tab.
- **`Dashboard/src/dashboard/ministerViewRegistry.tsx`** — add a `solver:`
  renderer (`:36-71`): `({ scope }) => <MinisterSolverView scope={scope} />`
  (fetches its own data, like Briefing/Build Queue).
- **`Dashboard/src/api/ministers.ts`** — add
  `fetchSolver(scope, signal) => readJson('/api/ministers/${scope}/solver/latest')`
  + a `WillieSolverPayload` interface mirroring `WillieSolverSnapshot`
  (status, noFit, draftable/placementValid/materialsReady/applyReady,
  trace{selectedRule, drafts[], notes[]}, errorType/message, request context).

### Part D — Solver view component, v1 (frontend)

- **New `Dashboard/src/components/minister/MinisterSolverView.tsx`** — Willie-only
  guard (mirror `MinisterBuildQueueView.tsx:44`); `useAsyncResource(fetchSolver)`;
  loading / error / `not_seen_yet` empty states. v1 sections:
  - **(1) Solve summary header** — driving request (target class/def, room class,
    source minister) + `trace.selectedRule` + outcome badge:
    `"N options"` (Status `options`) or `"no-fit: <reason>"` (Status `no_fit`) or
    `"error: <type>"` (Status `error`).
  - **(2) Pipeline funnel** — six labeled stages
    `anchors → drafts → hard-gate → reachable → validated → options`. Render each
    as a node; when `Status==="no_fit"`, map `noFit` to the killing stage and mark
    it failed, upstream passed, downstream not-reached:
    `NoAnchors`→stage 1, `NoDrafts`→2, `HardGateRejected`→3, `NoReachablePath`→4,
    `ValidationRejected`→5; `options` ⇒ all six pass. (Survivor counts per stage
    are a v2 enrichment from `trace.drafts`; v1 may show pass/fail only.)
  - **(6) Readiness ladder** — four-gate strip
    `Draftable → PlacementValid → MaterialsReady → ApplyReady` reusing the
    readiness tone logic already in `MinisterBuildQueueView.tsx:462-468`
    (extract/share `readinessTone`).
- **CSS** — add Solver-tab styles next to the existing
  `willie-briefing-hud` / `build-queue` styles (same stylesheet).

### Part E — v2 / v3 sections (follow-on, same payload)

Documented now, built after v1 lands; no new backend needed (data already in
`trace`):
- **v2 (4) Draft trace table** — one row per `trace.drafts[]`: `GeneratorId`,
  `AnchorRoomId`, `Status`, `Reason`, `DiversityReason`. Reuse `DynamicTable`.
- **v2 (5) Score breakdown** — per surviving draft, `Metrics[]` bars:
  `Id`, `RawValue`+`Unit`, `Normalized`×`Weight`=`Contribution`, `Better` arrow.
- **v3 (3) Anchor resolution** — join `trace.drafts[].AnchorRoomId` against the
  briefing `anchorInventory.anchors` (already fetched by Briefing) to show which
  anchors the solver resolved (surfaces the multifunction Kitchen-from-stove
  anchor). No new field needed.
- **v3 (7) Spec echo** — the `PlacementSpec` inputs (TargetClass, RoomClass,
  CapacityNeed, Adjacency, Power, Temperature, MaterialsOnHand). **Not** in the
  replay output today; defer with a small add of `Spec` to
  `PlacementSolverReplayOutput.FromResult` (or carry it on `WillieSolverSnapshot`,
  which already has the driving request). Decide at v3.
- **v3 (8) Notes / raw** — `trace.notes[]` + a raw-JSON `JsonTree` fallback.

### Part F — Static snapshot export

- The static export (`web/Snapshot/pages/willie/*.html`, `f1276cd`/`f793161`) is
  **generated** by the export tooling enumerating enabled views, not hand-written.
  Once the `solver` view is registered (Part C) and a live solve is captured,
  refresh the export so `web/Snapshot/pages/willie/solver.html` is produced. No
  manual HTML authoring.

### Unchanged on purpose (verify, do not edit)

- `PlacementSolverReplayOutput` / `PlacementTrace` / `MetricValue` shapes — reused
  verbatim as the wire contract; the solver already emits them.
- Replay persistence (`MinisterOfWillie.cs:49-61`) — keeps recording
  `placement_solver` to JSONL unchanged; the new store is an additive live mirror.
- `MinisterBuildQueueView` "Proposed" lane — still renders `options[]`; the Solver
  tab explains *why* options exist or don't, it does not replace the apply UI.

## Decisions

- **Reuse `PlacementSolverReplayOutput` as the wire contract.** The heavy data
  (drafts, scores, diversity, no-fit) is already serialized; this slice adds
  exposure + UI only, no re-modeling of solver internals.
- **In-memory latest-only `WillieSolverStore`** (mirror `MinisterTraceStore`), not
  a new `MinisterOutputStore` output kind — avoids a `SnapshotSchemaVersion` bump
  and the durable load switch.
- **Solver tab is Willie-only**, registered exactly like Build Queue (filtered out
  of `allMinisterViews`).
- **Phase the tab.** v1 (summary + funnel + readiness) is the highest insight per
  byte and renders against the full payload; v2/v3 add draft-trace/score and
  anchor/spec sections from the same already-present `trace`.
- **Funnel stage names mirror `NoFitReason` exactly** so the enum is the single
  source of truth for "where placement died."

## Test Plan

- **`Src/Tests/Willie/MinisterOfWillieTests.cs`** — after a solver cycle (existing
  freezer fixture), `WillieSolverStore.Latest("Willie")` returns a snapshot whose
  nested `PlacementSolverReplayOutput` matches the solve (`Status`, `NoFit`,
  readiness, `Trace.SelectedRule`); no write on an escalate / non-solver cycle.
- **New `Src/Tests/.../WillieSolverStoreTests.cs`** — latest-only overwrite;
  `Latest` for an unknown label returns null.
- **`Src/Tests/.../MinisterEndpoints`** (or host integration test) — endpoint
  returns the stored snapshot after a solve; returns the `not_seen_yet`
  placeholder before any solve; coverage row registered.
- **Frontend** (`Dashboard` vitest) — `MinisterSolverView` renders: Willie-only
  guard for non-Willie scope; funnel marks the correct failed stage for each
  `NoFitReason`; all-pass funnel for `options`; readiness ladder tones; error
  state for `Status:"error"`.
- Build: `dotnet build Src/ApiHost/RimBob.Host.csproj -c Debug` (stop any live
  Host first — running hosts lock the DLLs) + `dotnet test
  Src/Tests/RimBob.Tests.csproj` + `npm --prefix Dashboard run build`.

## Live Verification (after land + rebuild + Host restart)

- Trigger / observe a freezer solve. `GET /api/ministers/willie/solver/latest`
  returns `Status:"options"` (or `no_fit` with a `noFit` reason) plus the full
  `trace`.
- Willie → **Solver** tab: summary shows the freezer request + selected rule;
  funnel shows all six stages passed (options) **or** highlights the stage the
  `noFit` reason names; readiness ladder matches the freezer snapshot's
  `materials_ready` / `apply_ready` (cross-check
  `GET /api/ministers/willie/snapshot`).
- Per the verify-JSON-first norm: confirm via the endpoint JSON + replay corpus
  `placement_solver` output before trusting the rendered tab.

## Out of Scope

- New solver logic, generators, templates, or scoring — render-only of existing
  trace.
- Replacing the Build Queue Proposed/apply UI — the Solver tab is diagnostic.
- v2/v3 sections beyond stubs (draft table, score bars, anchor join, spec echo) —
  enumerated in Part E, built after v1.
- Adding `PlacementSpec` to the wire — deferred to v3 (spec echo).
- Non-freezer solver coverage — independent (`willie-nonfreezer-solver-wiring`);
  this tab renders whatever class solved, so it benefits automatically once that
  slice lands.
- Historical solve timeline / multi-solve retention — latest-only for now.

## Assumptions

- `PlacementSolverReplayOutput` serializes cleanly over the existing JSON pipeline
  (it is plain records; already written to replay JSONL).
- `briefing.GameTick` is available in `RunPlayCycle` for the snapshot stamp
  (it is — used elsewhere in the briefing).
- The static-snapshot export tooling auto-discovers registered minister views; if
  it hard-codes the view list, Part F adds `solver` to that list.
- `willie-nonfreezer-solver-wiring` may land before or after this; the tab does
  not depend on it (freezer alone exercises every section).
