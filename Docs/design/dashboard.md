# RimAI Advisor Dashboard

> Living document. Dashboard design changes should be recorded here in the same turn they are accepted.

## Purpose

The dashboard is the player's view into RimAI. It is a read-and-react surface for assisted RimWorld play: the player reads the cabinet's advice, inspects the evidence behind it, and keeps control inside RimWorld.

Dashboard v2 is a from-scratch React implementation inside the existing dashboard package. Keep the Vite package, build output, and Host serving model; treat the previous dashboard UI as reference material only.

## Product Posture

- Read-only in v2: no RIMAPI write controls, no autonomy toggles, and no feedback/Pushback controls.
- Localhost-only: Host binds loopback and serves the dashboard plus `/api/*`.
- Dense second-monitor operations console, not a landing page.
- Explanation-first: every recommendation should have a visible place for prompt, briefing, RAG, rules/trigger trace, and current advice.

## Stack And Serving

- React 18 + TypeScript, built with Vite in `Src/Dashboard`.
- Production output is bundled into `Src/ApiHost/wwwroot` and served by `RimAI.Host`.
- Dashboard consumes Host HTTP endpoints and the advice SSE stream.
- No frontend state-management library is required; local state plus small polling/SSE hooks is enough for v2.

## Information Architecture

### Shell

Dashboard v2 uses four stable regions:

- Header: product title, runtime status, refresh/build context where exposed.
- Left rail: scope selector.
- Main workspace: SYSTEM overview or minister inspector tabs.
- Right sidebar: compact colony facts plus colonist cards.

### Left Rail: Scopes

The left rail selects the inspected scope, not the view.

Live scopes:

- SYSTEM
- Mayor
- Food

Visible future scopes:

- Construction
- Defense
- Welfare
- Medical
- Research
- Industry
- Economy
- Chief of Staff

Each scope may show a small emoji marker. Future scopes are disabled or marked "not wired" until backend data exists.

### Minister Views

Minister scopes use the fixed top tab bar:

- System Prompt
- Briefing
- RAG
- Rules
- Advice

Each view renders structured sections. Large objects use the standard disclosure pattern: a real button header with `aria-expanded` / `aria-controls`, plus a conditionally rendered panel in normal document flow.

Panel registries are frontend implementation details. Do not render registry ids or the full registered view list inside the normal minister workspace; the selected tab already provides that orientation.

### SYSTEM View

SYSTEM is not a minister and does not show minister tabs. It starts as one overview page with compact panels. Split it later into `Runtime`, `LLM`, `RAG`, and `Logs` only if the overview becomes too large.

SYSTEM owns:

- Runtime status and Mayor run state.
- RIMAPI reachability.
- SSE diagnostics: connection state, active/total connections, event count, last event, last error.
- LLM health and usage placeholders.
- RAG health and corpus/cache placeholders.
- Endpoint and data coverage markers.
- Recent event/advice timeline.
- Log paths and future bounded log tail.

### Right Sidebar

The right sidebar is always-on colony context independent of selected scope.

Render compact facts first, then one small panel per colonist:

- Date, tick, season.
- Colonist count, mood, break risk, medical/downed/dead signals.
- Food days, wealth, power, threat, weather, research where available.
- Colonist cards: name, mood, health, hunger, current job, top skill, downed/dead state.

## Data Contracts

### Current Endpoints

Dashboard v2 initially uses:

- `GET /api/status`
- `GET /api/advice/stream`
- `GET /api/agenda/latest`
- `GET /api/briefings/mayor/latest`
- `GET /api/briefings/food/latest`
- `GET /api/mayor/prompt`
- `GET /api/ministers/{minister}/prompt` for Mayor and Food prompt introspection.
- `GET /api/colony/snapshot`

### V2 Introspection Endpoints

Dashboard v2 adds or plans read-only introspection:

- `GET /api/ministers`
- `GET /api/ministers/{minister}/trace/latest`
- `GET /api/system/health`
- `GET /api/ministers/{minister}/rag/latest`
- `GET /api/system/logs/recent`

These endpoints are observability surfaces only. They do not mutate game state.

### Advice SSE

`GET /api/advice/stream` remains the live stream for agenda and advice events. V2 also treats the stream itself as observable data: SYSTEM should show whether the connection is open, how many events have arrived, the last event type/id/time, and any recent stream error.

SSE event contract:

- `agenda_update`: full current `MayorAgenda`.
- `advice_snapshot`: active `AdviceItem[]` snapshot. `minister: null` means replace the whole active-advice list, used on stream replay/reconnect. `minister: "Food"` means replace only that minister's active cards.
- `advice`: single `AdviceItem`, retained for append-style compatibility and event timelines. Snapshot events are authoritative for removing stale cards.

## Minister View Detail

### System Prompt

Shows the exact prompt material available for the selected minister.

Mayor and Food are backed by `GET /api/ministers/{minister}/prompt`, with `/api/mayor/prompt` retained as a legacy Mayor route. Future ministers should use the same generalized read-only prompt endpoint when added. Until then, render a clear "not exposed yet" state.

### Briefing

Shows the latest minister briefing grouped into readable sections rather than raw JSON.

Mayor sections:

- Overview
- People
- Food and resources
- Infrastructure
- Welfare and threat
- Environment
- Research

Food sections:

- Food status
- Crops
- Wild harvest
- Skills and labor signals
- Infrastructure and storage
- Kitchen and butchery
- Data coverage
- Recent food incidents

### RAG

Shows retrieval status, guide context, citations, snippets, and cache/embedding status when exposed. Until dedicated RAG endpoints exist, Mayor may derive partial context from agenda citations and prompt context; other ministers should render explicit degraded coverage states.

### Rules

The Rules view is the home for triggers and decision path visibility. It should answer: "Why did RimAI say this now?"

Show:

- Last wake trigger.
- Rules versus LLM escalation path when available.
- Rule fired or escalation reason when available.
- Active flag or scheduled wakeup payload when available.
- Advice emitted.
- Flags emitted.
- Briefing version and game tick when available.
- Last error.

### Advice

Mayor Advice renders the Agenda as the Mayor's player-facing output.

Feeder minister Advice renders active `AdviceItem`s sorted by severity, then `priority_score`. Cards show rationale, suggested actions, resource requests, citations, issue id/supersession when available, and coverage gaps. No feedback buttons are shown in v2.

## Data Coverage

Every debug or inspector surface should distinguish empty data from unavailable data:

- `available`: exposed and fresh enough to render.
- `missing`: needed, but no endpoint or field exists yet.
- `failed`: endpoint exists but errored.
- `unsupported`: intentionally out of scope for this slice.
- `stale`: present but older than expected.

Use this especially for RAG, Rules traces, logs, future ministers, token/cost metrics, and endpoint coverage.

## Visual Rules

- Dark command-center UI with dense spacing and clear hierarchy.
- Left rail, main workspace, and sidebar own their overflow; avoid whole-page scrolling on desktop.
- Text wraps within panels.
- Use compact tables only for debug surfaces: Rules, RAG, logs, active flags, resource requests, and endpoint coverage.
- Avoid decorative hero sections, oversized empty cards, and one-note color themes.

## Deferred

- Feedback/Pushback UI returns only when the feedback lifecycle is actively wired.
- Autonomy controls return only with the M7+ autonomy work.
- Pawn/item image caching waits until text-first v2 is stable.
- RIMAPI write/control surfaces remain out of scope for suggest-only MVP.

## Open Questions

- Should SYSTEM split into multiple tabs after log and RAG health surfaces mature?
- Which decision-log fields should become durable trace history versus in-memory recent-run state?
- Should browser notification behavior be scoped to tactical alerts only?
