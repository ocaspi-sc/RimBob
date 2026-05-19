# Dashboard v2 From-Scratch Plan

## Summary

Build Dashboard v2 as a new React implementation inside the existing dashboard package. Keep the Vite/package/build/Host wiring, but treat the previous dashboard UI as reference material only. The new app source tree owns its own shell, registries, layout, CSS, and components.

The dashboard remains inspect-first and `Suggest` mode. It exposes what RimBob knows, what it recommended, why a minister woke up, what data was missing, and how the runtime is behaving. It may show Assisted Apply only for backend-allowlisted advice actions, and it does not show Pushback or feedback controls in v2.

## Core Information Architecture

### Left Rail: Scope Selector

The left rail selects who or what is being inspected.

- `SYSTEM` for runtime, LLM, RAG, SSE, logs, endpoint coverage, and recent events.
- `Mayor` for the strategic Agenda and Mayor prompt/briefing/RAG/rules evidence.
- `Food` for active feeder advice and Food briefing evidence.
- Future ministers remain visible but clearly disabled until wired.

Each scope has a small emoji marker for recognition. The emoji is presentation only; it does not affect backend payloads.

### Minister Tabs

Minister scopes show a fixed top tab bar:

- `System Prompt`
- `Briefing`
- `RAG`
- `Rules`
- `Advice`

Each tab is a structured inspector with readable sections, not a raw JSON dump. Large objects use the standard disclosure pattern: a real `<button>` with `aria-expanded` / `aria-controls`, and a normally flowed panel.

### SYSTEM Scope

SYSTEM is not a minister. It starts as one overview surface rather than using minister tabs. If it grows too large, split it later into `Runtime`, `LLM`, `RAG`, and `Logs`.

SYSTEM should show:

- Runtime status, RIMAPI reachability, Mayor run state, last success/error.
- SSE diagnostics: connection state, active/total connections, last event, event count, error/reconnect information.
- LLM health: provider/configured status, last success/error, usage placeholders until token/cost data is exposed.
- RAG health: enabled/disabled status, guide corpus path, guide/chunk/cache status when exposed.
- Endpoint/data coverage: `available`, `missing`, `failed`, `unsupported`, `stale`.
- Recent event/advice timeline for cabinet/game events that influenced advice.
- Bounded log pointers and future recent-log output.

### Right Sidebar

The right sidebar stays independent of selected scope. It is compact colony context, not another debug tab.

- Date, tick, season, food, mood, power, threat, research, wealth when available.
- One small panel per colonist using `MayorBriefing.Colonists.Pawns`.
- Warning treatment for downed/dead pawns, break risk, low food, negative power, and active threat.

## External Dashboard Insights Folded In

### SYSTEM SSE Diagnostics

The external dashboard made transport status obvious. V2 should make SSE observable directly: connected/disconnected state, last event type/id/time, event counts, last error, and connection totals. This belongs in SYSTEM because advice freshness depends on the stream.

### Endpoint And Data Coverage

The dashboard should distinguish "empty because nothing happened" from "empty because we did not expose the data." Each inspector can render a coverage row:

- `available`: data is exposed and fresh enough to render.
- `missing`: known needed data has no endpoint yet.
- `failed`: endpoint exists but errored.
- `unsupported`: explicitly not part of the current slice.
- `stale`: endpoint returned data older than expected.

This is especially important for RAG, Rules, logs, and future ministers.

### Recent Event And Advice Timeline

The user needs to know what changed before a recommendation. V2 should keep a compact timeline fed by SSE events, agenda updates, advice items, refreshes, and later game/cabinet events. The first implementation can derive this from current browser-side feed events; backend trace/log endpoints can enrich it later.

### Debug Tables Only Where They Help

Dense filterable tables are appropriate for debug surfaces, not for the primary advice screen. Use tables for Rules traces, RAG chunks/citations, active flags, resource requests, endpoint coverage, and recent logs. Keep Advice and Briefing readable and sectioned.

### RIMAPI Coverage Checklist

External RIMAPI endpoint coverage should become a telemetry-gap checklist, not a UI dependency. Use it to answer which read surfaces power briefings, which are not yet used, and which future Auto/write endpoints remain intentionally out of scope.

### Defer Image Caching

Pawn/item image caching can improve recognition later, but it is not part of core v2. First ship the inspection architecture, data coverage, and reliable text surfaces.

### Control Affordances

V2 is a read-and-react dashboard by default. Do not add broad RIMAPI write buttons, direct controls, autonomy toggles, or feedback controls. The exception is Assisted Apply: a backend-approved Apply button may appear on one allowlisted advice step when validation, result tracing, and read-back are wired.

## Interfaces

### Existing Endpoints Used First

- `GET /api/status`
- `GET /api/advice/stream`
- `GET /api/agenda/latest`
- `GET /api/briefings/mayor/latest`
- `GET /api/briefings/food/latest`
- `GET /api/mayor/prompt`
- `GET /api/ministers/{minister}/prompt` for Mayor and Food prompt introspection.
- `GET /api/colony/snapshot`

### Read-Only Introspection Endpoints

Initial v2 adds or plans these surfaces:

- `GET /api/ministers`
- `GET /api/ministers/{minister}/prompt`
- `GET /api/ministers/{minister}/trace/latest`
- `GET /api/system/health`

Planned next:

- `GET /api/ministers/{minister}/rag/latest`
- `GET /api/system/logs/recent`

Initial v2 endpoints are read-only. Assisted Apply endpoints are added only with the documented action lifecycle.

## Frontend Strategy

### Source Architecture

Keep package/build infrastructure and replace the app implementation:

```text
Dashboard/src/
  App.tsx
  api/
    client.ts
  dashboard/
    scopes.ts
    panelRegistry.ts
  hooks/
    useAdviceFeed.ts
    useAsyncResource.ts
    usePollingResource.ts
  components/
    layout/
    minister/
    shared/
    system/
  types/
```

The app uses registries instead of ad hoc conditionals:

- `ScopeConfig` for SYSTEM, Mayor, Food, and future ministers.
- `MinisterViewConfig` for the fixed minister tabs.
- `PanelConfig` for SYSTEM panels and coverage-driven surfaces.

This gives modular panels while preserving a fixed, opinionated layout.

### State Model

```ts
type ScopeKey = 'system' | 'mayor' | 'food' | 'construction' | 'defense' | 'welfare' | 'medical' | 'research' | 'industry' | 'economy' | 'chief_of_staff';
type MinisterViewKey = 'prompt' | 'briefing' | 'rag' | 'rules' | 'advice';

const [selectedScope, setSelectedScope] = useState<ScopeKey>('system');
const [selectedView, setSelectedView] = useState<MinisterViewKey>('advice');
```

`selectedView` applies only to minister scopes. SYSTEM renders its own overview.

### Fetching

Use simple hooks and local state. No state-management library is needed.

- Poll `/api/status`, `/api/colony/snapshot`, `/api/system/health`, and current minister surfaces.
- Subscribe to `/api/advice/stream` once.
- Derive recent timeline entries from SSE events until backend event logs exist.
- Render explicit degraded states when an endpoint is missing or not exposed yet.

### Visual Direction

Dashboard v2 is a dense dark operations console:

- Left rail = who/what scope.
- Top tabs = inspection lens.
- Center = evidence and output.
- Right = live colony facts.

Use compact rows, clear headings, status pills, restrained accent colors, and stable overflow regions. Avoid landing-page composition, decorative cards, oversized type, and a one-hue palette.

## Backend Strategy

### Minister Trace

Expose the last run trace per minister so the Rules tab can answer "why did RimBob say this now?"

Trace should include:

- Trigger (`StartupBootstrap`, `CabinetRefresh`, `FlagFired`, `Heartbeat`, `ScheduledWakeupFired`).
- Started/completed time.
- Success/failure.
- Advice count emitted.
- Flags emitted.
- Error text when failed.
- Briefing version or game tick when available.

The first implementation can use an in-memory recent-run store. Decision-log-backed history can come later.

### SYSTEM Health

Expose a bounded operational summary:

- Status fields currently available from `/api/status`.
- SSE diagnostics.
- LLM configured/provider placeholders.
- RAG configured/corpus status where available.
- Log paths.
- Endpoint coverage markers.
- Recent minister traces.

### Logs

Do not stream arbitrary files into the UI. A later endpoint should return a bounded recent tail from known Host/decision logs with limits.

## Implementation Order

1. Update this plan to state v2 is from scratch and the current UI is reference only.
2. Create a clean v2 `src/` architecture while preserving package/build config.
3. Build the shell: header, scope rail, SYSTEM overview, minister tabs, main panel, colony sidebar.
4. Wire Mayor and Food using existing endpoints.
5. Add SYSTEM diagnostics placeholders and data coverage markers.
6. Remove Pushback/feedback controls from v2.
7. Add backend introspection endpoints incrementally, starting with minister trace and SYSTEM health.
8. Reconcile `Docs/design/dashboard.md` after the v2 direction is accepted.

## Acceptance Criteria

- Selecting SYSTEM shows system health, not minister tabs.
- Selecting a minister shows `System Prompt`, `Briefing`, `RAG`, `Rules`, and `Advice`.
- Mayor and Food render useful surfaces from current endpoints.
- Future ministers are visible but clearly not wired.
- Trigger/rules visibility has a first-class home under `Rules`.
- Endpoint and data coverage states are explicit.
- SSE diagnostics are visible under SYSTEM.
- Recent event/advice timeline is visible under SYSTEM and/or Advice.
- Pushback/feedback controls are absent; broad write controls are absent except documented Assisted Apply.
- Right sidebar shows compact colony facts plus colonist cards.
- The layout stays dense, readable, and stable on desktop and narrow viewports.

## Test Plan

- `npm.cmd run build` from `Dashboard`.
- `dotnet build Src/RimBob.sln --no-restore`.
- Verify Host still serves the built dashboard.
- Browser-check desktop and narrow viewport layout.
- Confirm SYSTEM does not show minister tabs.
- Confirm Mayor/Food render useful views from current endpoints.
- Confirm missing backend capabilities render explicit "not exposed yet" or degraded coverage states.
- Confirm no broad write/control UI exists; Assisted Apply appears only on backend-approved actions when that lifecycle is wired.

## Assumptions

- "From scratch" means new React/CSS/source architecture while keeping Vite/package/build wiring.
- Existing dashboard components are reference only; useful behavior may be reimplemented, not carried forward as architecture.
- Dashboard v2 remains inspect-first and `Suggest` mode; Assisted Apply is manual and allowlisted, not Auto.
- External `rimapi-dashboard` remains inspiration/reference only, not a dependency.
