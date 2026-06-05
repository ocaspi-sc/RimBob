# Dashboard HOME Screen

## Goal

Add a HOME/main screen that is the dashboard's landing scope and an at-a-glance live command summary of RimBob: pipeline vitals plus one card per live minister showing that minister's **bottom-line advice summary** and **rules summary**. Move the global `Run Cabinet Now` button off the header onto HOME, and add a new `Run Cabinet (Rules Only)` button beside it that runs every wired rules-capable minister's deterministic path with no LLM calls.

## Current State

### Scopes / layout
- `Dashboard/src/dashboard/scopes.ts` defines `ScopeKey`/`ScopeKind` and `scopeConfigs`. Console scopes are `system`, `info`, `analytics`, `dev_blog`; minister scopes are `mayor`, `food` (Chef), `willie`, `welfare` (live) plus planned ones. There is no `home` scope.
- `Dashboard/src/App.tsx` branches on `activeScope.kind` to render `SystemOverview` / `InfoOverview` / `AnalyticsOverview` / `DevBlogOverview`, else the minister workspace. The console overviews own their own `ViewTabs`.
- `Dashboard/src/hooks/useDashboardSelection.ts` defaults the stored scope to `system` and persists scope/view in `localStorage`; honors `?scope=&view=` deep links validated against the registries.
- `Dashboard/src/components/layout/ScopeRail.tsx` renders Console and Cabinet groups from `scopeConfigs`.
- `run-rimbob.ps1` mirrors the scope/view registry in `$dashboardConsoleScopes` / `$dashboardMinisterScopes` for the tray deep-link menu.

### Run Cabinet button
- `Dashboard/src/components/layout/DashboardHeader.tsx` renders the global `Run Cabinet Now` button in `.header-controls` and takes `onTriggerCabinet` / `triggerDisabled` / `triggerPending` / `triggerError` props.
- `Dashboard/src/hooks/useManualTriggers.ts` owns `triggerCabinetNow()`, the one-active-trigger guard (`TriggerTarget = 'cabinet' | '{scope}:rules' | '{scope}:llm'`), and the shared `cabinetRunDialog` state.
- `Dashboard/src/api/cabinet.ts` → `triggerCabinet(runId)` posts `POST /api/cabinet/trigger`.
- `Dashboard/src/components/layout/CabinetRunDialog.tsx` is mounted globally in `App.tsx`; header hardcodes the eyebrow `Manual cabinet run`.

### Backend cabinet run
- `Src/ApiHost/Endpoints/CabinetEndpoints.cs` maps `POST /api/cabinet/trigger` → `CabinetCycle.TriggerCabinetAsync(ct, runId)` and the per-minister `trigger/rules` + `trigger/llm`.
- `Src/Coordination/CabinetCycle.cs`:
  - `TriggerCabinetAsync` starts a run-log (`runLogs.StartRun(runId, "cabinet", trigger)`), calls `RunCycleAsync(PlayCycleContext.ManualTrigger, ...)`, then `CompleteRun`/`FailRun`.
  - `RunCycleAsync` refreshes live state (with restored-snapshot fallback), then iterates `registry.CabinetMinisters` in `CabinetOrder` (Chef 10, Welfare 12, Willie 15, Mayor 20), passing the **same `cycle`** (and thus its `RunMode`) into each `RunResolvedMinisterAsync`. After each minister it runs Willie build-request follow-ups in `RulesOnly`/`FlagFired`, deduped via `ministersRunAsRequestFollowUp`.
- `Src/Common/Ministers/PlayCycleContext.cs` has `MinisterRunMode { RulesFirst, RulesOnly, ForceLlm }` and static contexts `ManualTrigger` (RulesFirst), `ManualRulesOnly` (RulesOnly, payload `dashboard:rules`), `ManualForceLlm`.
- `Src/Coordination/MinisterRegistry.cs`: `CabinetMinisters` are the descriptors with a `CabinetOrder`. `CanRunRules` is **true** for `food`, `welfare`, `willie` and **false** for `mayor` (LLM-only).
- Minister `RunMode` handling:
  - `Chef` (`Src/Ministers/Food/Chef.cs`) honors `RulesOnly`: if rules return a lone escalation it publishes the unresolved state summary and stops **before** any LLM call.
  - `MinisterOfWelfare` and `MinisterOfWillie` have **no LLM path at all** — inherently rules-only regardless of `RunMode`.
  - `Mayor` (`Src/Ministers/Mayor/Mayor.cs`) **ignores `RunMode`** and always calls the LLM (or the manual-response file). So a rules-only cabinet run must **not** run Mayor.
- `Src/Coordination/CabinetRunLogStore.cs` is scope-agnostic: `StartRun(runId, scope, trigger)` accepts any `scope` string and publishes `RunChanged`. `Src/ApiHost/Endpoints/AgendaStreamEndpoint.cs` subscribes to `RunChanged` and emits the `cabinet_run` SSE event for any run. `CabinetRunStepSnapshot.Status` is a free-form string the dialog renders generically.

### Data available to HOME (no new reads needed)
- `useAdviceFeed` exposes `feed.activeAdvice: AdviceItem[]`, `feed.stateSummaries: Record<minister,string>`, `feed.flags`, `feed.chains`, and `agenda: MayorAgenda | null`.
- `/api/system/health` (`SystemHealth`) exposes `runtime` vitals (`rimapi_reachable`, `rimworld`, `colony_state_origin`, `active_advice_count`, `active_flag_count`, `*_briefing_version`, `mayor_*`), `llm.status`, and `traces: MinisterTrace[]` (latest per-minister `path`/`ruleFired`/`escalationReason`/`adviceCount`/`flagCount`/`status`/timestamps).
- `Dashboard/src/dashboard/selectors.ts` already has reusable `isScopeMinister`, `valueForScope`, and `formatLastRun(scope, health)`.
- Colony pressure facts come from `/api/colony/snapshot` (already polled in `App.tsx` and rendered by the always-on `ColonySidebar`).

## Product Contract

- HOME is a new live console scope, listed first in the left-rail Console group, and is the **default** landing scope (replacing SYSTEM as the boot default). Existing stored/deep-link selections still win.
- HOME has no minister tabs. It is one dense overview view (`overview`).
- HOME shows two button-triggered RimBob evaluation controls (never RimWorld writes):
  - `Run Cabinet Now` — unchanged behavior; the same control that lived in the header.
  - `Run Cabinet (Rules Only)` — runs each wired **rules-capable** cabinet minister (Chef, Welfare, Willie) in `RulesOnly` mode and **skips the Mayor** (LLM-only synthesis). No LLM calls happen. Willie build-request follow-ups (already rules-only) still run.
- While **either** cabinet run is in flight, both HOME buttons are disabled/busy, and both are disabled when the Host API is not live. Both reuse the existing global run-step dialog and `cabinet_run` SSE reconciliation; the dialog labels a rules-only run distinctly.
- The header keeps the brand block, version chips, and the status pills (`Host API`, `RimWorld`, `RIMAPI`, `LLM`, `SSE`, `Mayor`). Only the cabinet trigger control leaves the header.
- HOME vitals band (live, derived from already-polled inputs — `/api/status`, `/api/system/health`, the SSE feed, `/api/colony/snapshot`):
  - Connectivity/pipeline tiles: Host API, RIMAPI, RimWorld, LLM, SSE, Mayor (same tone semantics as the header pills), plus `colony_state_origin` (live vs restored/stale).
  - Counts: active advice, active flags, agenda version, cabinet last-run time/result.
  - A few colony-pressure highlights that are genuinely "vital": game date, colonist count, food days, threat, mood. (The full colonist cards stay in the right sidebar; HOME does not re-implement them.)
- HOME minister grid — one card per **live** minister (`mayor`, `food`, `welfare`, `willie`), each card:
  - Header: minister emoji + display label + last-run label (`formatLastRun`).
  - **Bottom-line advice summary**:
    - Mayor: `agenda.posture.summary` plus the top active `short_term` priority text (falls back to "no agenda yet").
    - Feeders: highest-priority active `AdviceItem.title` for that minister (with its first action `instruction` as subtext); if none, the first line of `state_summary`; else an explicit "no active advice" state.
  - **Rules summary**: from the minister's latest `SystemHealth.traces` entry — path (`rules`/`llm`/`llm_failed`), `ruleFired` or `escalationReason`, advice/flag counts, and status tone. Explicit "no trace yet" / "not wired" states for missing data.
  - Clicking a card selects that minister scope (`selection.selectScope(key)`), landing on its Advice view.
  - Planned ministers are **not** shown on HOME (HOME is the live summary; the rail still lists planned scopes).
- Coverage/data-state honesty: every HOME tile/card distinguishes `available` / `missing` / `stale` / `failed` per the dashboard Data Coverage rules (e.g., stale when Host API is stale, missing when no trace exists yet).

## Backend Plan

1. **`PlayCycleContext`** (`Src/Common/Ministers/PlayCycleContext.cs`): add a static `ManualCabinetRulesOnly` = `new(PlayCycleTrigger.ManualTrigger, WakeupPayload: "dashboard:cabinet_rules", RunMode: MinisterRunMode.RulesOnly)`. Distinct payload from per-minister `ManualRulesOnly` for trace/replay provenance.
2. **`CabinetCycle`** (`Src/Coordination/CabinetCycle.cs`):
   - Add `public async Task<CabinetTriggerResult> TriggerCabinetRulesOnlyAsync(CancellationToken ct, string? runId = null)` mirroring `TriggerCabinetAsync` but: `runLogs.StartRun(runId, "cabinet_rules", ...)` and calling the cycle runner in rules-only mode. Return scope `"cabinet_rules"`.
   - Generalize `RunCycleAsync` to accept a `bool rulesOnlyCabinet = false` (keeps the existing `ManualTrigger`/`CabinetRefresh`/bootstrap callers running the full set). When `rulesOnlyCabinet`:
     - Iterate only `registry.CabinetMinisters.Where(d => d.CanRunRules)` (Chef, Welfare, Willie), passing the rules-only `cycle` (so `RunMode` flows to each minister). The existing Willie follow-up + `ministersRunAsRequestFollowUp` dedup is unchanged.
     - For each excluded cabinet minister (`!CanRunRules`, i.e. Mayor), when `cabinetRunId is not null` record a **skipped** run-log step (`minister_<key>`, detail "Skipped — LLM-only minister excluded from a rules-only cabinet run.") so the dialog and SYSTEM surface explain the absence rather than silently dropping it.
   - The public `TriggerCabinetRulesOnlyAsync` passes `PlayCycleContext.ManualCabinetRulesOnly` and `rulesOnlyCabinet: true`.
3. **`CabinetRunLogStore`** (`Src/Coordination/CabinetRunLogStore.cs`): add a `SkipStep(runId, key, label, kind, detail, minister)` helper that records a step with `Status: "skipped"` (completed-at = now, duration 0) and publishes. This keeps `CompleteStep`'s "completed" status semantics intact.
4. **`CabinetEndpoints`** (`Src/ApiHost/Endpoints/CabinetEndpoints.cs`):
   - Map `POST /api/cabinet/trigger/rules` → reads the optional `{ run_id }` body (reuse `ReadTriggerRequestAsync`), calls `cabinet.TriggerCabinetRulesOnlyAsync(ct, runId)`, maps expected operational errors via `ManualTriggerErrorResults.TryMap`, and returns `{ triggered, scope, trigger, run_mode = "rules_only", state_source, used_restored_snapshot, run_log }`.
   - Register endpoint coverage text for the new route (rules-only cabinet trigger; suggest-only; no RIMAPI writes; Mayor excluded).
   - No legacy alias and no `mode` query branch on the existing endpoint — a clean second route per the dashboard doc's "do not keep legacy trigger aliases" rule.

## Frontend Plan

### Scope plumbing
1. **`scopes.ts`**: add `'home'` to `ScopeKey` and `'home'` to `ScopeKind`; add `HomeViewKey = 'overview'` and `homeViews = [{ key: 'overview', label: 'Overview' }]`. Add the `home` config first in `scopeConfigs` (`kind: 'home'`, `status: 'live'`, `enabledViews: ['overview']`). Add `home` branches to `viewsForScopeKind` and `defaultViewForScope`; include `homeViews` in `allDashboardViews`. (`overview` already validates via INFO; that's fine.)
2. **`useDashboardSelection.ts`**: change the default stored scope from `system` to `home` (`readStoredScope` fallback) so a fresh dashboard lands on HOME. Stored/`?scope=` values still take precedence.
3. **`ScopeRail.tsx`**: no code change needed — HOME renders in the Console group automatically. Add a `scopeStatusLabel` case so HOME shows a sensible chip (e.g. `home`).

### Run-cabinet rewiring
4. **`api/cabinet.ts`**: add `triggerCabinetRules(runId, signal?)` → `postJson('/api/cabinet/trigger/rules', signal, { run_id: runId })`.
5. **`useManualTriggers.ts`**:
   - Extend `TriggerTarget` with `'cabinet_rules'`.
   - Add `triggerCabinetRulesOnly()` mirroring `triggerCabinetNow()` (generate `run_id`, seed `cabinetRunDialog` with a `scope: 'cabinet_rules'` run, post via `triggerCabinetRules`, reconcile `run_log`/failure). Reuse the same single-active-trigger guard and shared dialog state.
   - Expose `triggerCabinetRulesOnly` from the hook.
6. **`App.tsx`**:
   - Stop passing cabinet props to `DashboardHeader`.
   - Add an `isHome` branch rendering `<HomeOverview .../>` with: `status.data`, `systemHealth.data`, `snapshot.data`, `feed` (advice/stateSummaries/agenda), `stream`, the cabinet last-run from `feed.cabinetRuns`, both trigger callbacks, a `cabinetBusy = triggers.triggerState.target === 'cabinet' || === 'cabinet_rules'`, per-button pending flags, `triggerError`, `hostApiLive`, and `selection.selectScope`.
   - The global `CabinetRunDialog` stays mounted as-is.
7. **`DashboardHeader.tsx`**: remove the `.header-controls` cabinet button and the `onTriggerCabinet`/`triggerDisabled`/`triggerPending`/`triggerError` props. Keep brand, version chips, and status pills.
8. **`CabinetRunDialog.tsx`**: vary the eyebrow by `run.scope` — `cabinet_rules` → "Manual cabinet run — rules only", else "Manual cabinet run". Add a `.cabinet-run-step.skipped` tone (gray) so skipped Mayor reads clearly.

### HOME component
9. Add `Dashboard/src/components/home/HomeOverview.tsx`:
   - Props: status, health, snapshot, advice (`AdviceItem[]`), stateSummaries, agenda, stream, recent cabinet runs, `onRunCabinet`, `onRunCabinetRules`, `cabinetBusy`, `cabinetPending`, `cabinetRulesPending`, `triggerError`, `hostApiLive`, `onSelectScope`.
   - Sections:
     - **Command bar**: title eyebrow + the two trigger buttons (reuse `.trigger-button` / `.trigger-button.secondary` styling and `SemanticIconCue` like `WorkspaceTitle`), with the trigger-error `role="status"` text. Rules-only button `title`: "Run every wired rules-capable minister's deterministic path; skips the LLM-only Mayor."
     - **Vitals band**: reuse the connectivity/state derivations (see Refactor Suggestion #1) for Host API / RIMAPI / RimWorld / LLM / SSE / Mayor tiles, plus count tiles (active advice, active flags, agenda version, cabinet last-run) and the colony-pressure highlights from `snapshot`. Use `MetricCard`/`StatusPill` primitives already in the shared set.
     - **Minister grid**: map the live minister `scopeConfigs` (kind `minister`, status `live`) to cards. Compute bottom-line advice (`valueForScope` over `stateSummaries`, `advice.filter(isScopeMinister)` sorted by priority, agenda for Mayor) and rules summary (`health.traces.find(isScopeMinister)`), with `formatLastRun` for the timestamp. Each card is a `<button>`/clickable region calling `onSelectScope(scope.key)`.
   - Honor stale/missing/failed states (Host API stale → tiles read "last …" like the header; no trace → "no trace yet"; no health → loading).
10. **`Dashboard/src/styles.css`**: add HOME styles — command bar row, responsive vitals tile grid, and a minister-card grid that matches the dark command-center density rules (no decorative hero, panels own overflow, text wraps).

### Launcher
11. **`run-rimbob.ps1`**: add a `HOME` entry first in `$dashboardConsoleScopes` with a single `Overview`/`overview` view so the tray deep-links to HOME, per the dashboard doc rule to update the tray when scopes change.

## Docs

- **`Docs/design/dashboard.md`**:
  - Information Architecture / Header: the header global trigger moves to HOME; the header keeps title, version chips, and status pills.
  - Add HOME to the live non-minister scopes list and add a short "HOME View" subsection: landing scope, RimBob vitals band, per-live-minister bottom-line advice + rules summary cards that deep-link into the minister scope, and the two cabinet run controls. Note HOME derives from already-bounded inputs (status/health/SSE/colony snapshot) and does not add new broad endpoints.
  - Scopes list + the "Non-minister console scopes use a shallow local tab bar" area: HOME is single-view (Overview), no sub-tabs.
  - Manual Triggers: add the rules-only cabinet trigger — runs wired rules-capable ministers in rules-only mode, **excludes the LLM-only Mayor** (records a skipped step), reuses the run-step dialog + `cabinet_run` SSE, stays suggest-only with no RIMAPI writes. Note both cabinet controls now live on HOME.
- **`Docs/DESIGN.md`**: add a decision-log row only for the durable contract bit (new `POST /api/cabinet/trigger/rules` route + rules-only cabinet semantics excluding Mayor). The HOME UI itself is a dashboard-doc concern, not a DESIGN decision.
- **`Tasks.md`**: linked from the inbox (this plan) and marked landed when it ships.

## Tests And Verification

- **`Src/Tests/Coordination/CabinetCycleTests.cs`** (mirrors existing `FakeMinister`/`BuildCycle` patterns; the real `MinisterRegistry` is used, so `CanRunRules` gating is exercised):
  - `TriggerCabinetRulesOnlyAsync` runs Chef/Welfare/Willie with `RunModes == [RulesOnly]` and `WakeCount == 1` each, and **Mayor `WakeCount == 0`**.
  - The run-log scope is `cabinet_rules`, status `completed`, steps include `minister_food`/`minister_welfare`/`minister_willie` and a `skipped` Mayor step (`minister_mayor`, status `skipped`).
  - A rules-only run where Chef/Welfare publish a Willie `building_request` still runs Willie once (`FlagFired`/`RulesOnly`), not twice (reuses the existing dedup assertion shape).
- **Endpoint test** (if the existing Host endpoint harness makes it cheap; otherwise rely on the coordination tests + a live check): `POST /api/cabinet/trigger/rules` returns `run_mode: "rules_only"`, scope `cabinet_rules`, and a `run_log`.
- **Dashboard**: there is no React component test harness in `Dashboard/src`; rely on `npm.cmd run build` (TypeScript typecheck) plus the live visual check below. Match that existing posture — do not introduce a new test framework in this slice.
- Run `dotnet test` and `npm.cmd run build` in `Dashboard/`.
- Per AGENTS Build/Verification + the launch-from-worktree memory: after landing in the worktree, sync `master`, build, launch RimBob from the worktree on a non-`5000` port, confirm `/api/system/health` shows the worktree `host_process_path`, then in the dashboard: HOME is the default scope; vitals render; minister cards show bottom-line advice + rules summary and deep-link into each minister; `Run Cabinet Now` and `Run Cabinet (Rules Only)` both open the run-step dialog; the rules-only run shows Chef/Welfare/Willie steps + a skipped Mayor step and records no Mayor LLM call (verify via `/api/ministers/mayor/trace/latest` and Raw LLM capture timestamp being unchanged). Give the worktree dashboard URL for visual sign-off.

## Compatibility And State

Ephemeral HTTP/SSE + browser-local UI contract changes only; no persisted game/minister state schema change. No compat code; the new route is additive and the default-scope change is browser-local. If a browser has a stale stored scope/view it already falls back through the validated registries. No legacy trigger alias.

## Refactor Suggestions (surfaced per AGENTS)

1. The header status derivations (`deriveHostApiState`, `deriveRimWorldState`, `deriveRimApiState`, `deriveLlmState`, `deriveMayorState`, `deriveStreamState`) are private to `DashboardHeader.tsx`, but HOME wants the same tiles. Extract them into a shared `Dashboard/src/dashboard/connectivityStatus.ts` (returning `{ label, tone, title }`) consumed by both the header and HOME, instead of duplicating the logic. Keep this small and within this slice if low-risk; otherwise note as a follow-up.
2. `ministerAliases` is duplicated in `scopes.ts` (`displayMinisterName`) and `selectors.ts` (`valueForScope`/`isScopeMinister`). HOME uses both name-mapping and value-mapping; consider consolidating the alias table in one module. Out of scope to fix here, but flag it.

## Out Of Scope

- Any RimWorld/RIMAPI write behavior (HOME triggers stay suggest-only).
- Canceling a running cabinet cycle from HOME or the dialog.
- Changing cabinet run order or the Mayor's run path.
- A rules-only path for the Mayor (Mayor stays LLM-only; it is simply excluded from rules-only runs).
- New backend endpoints to feed HOME beyond the rules-only trigger; HOME reuses status/health/SSE/colony-snapshot.
- Re-implementing the colonist cards on HOME (they remain in the right sidebar).
- Showing planned (not-wired) ministers as HOME cards.
