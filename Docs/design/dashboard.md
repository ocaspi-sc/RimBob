# RimAI Advisor Dashboard

> **Living document.** Dashboard design changes should be recorded here in the
> same turn they are accepted.
> This doc records dashboard purpose, information architecture, and behavioral
> contracts. Exact routes, payload fields, React component names, and API client
> types live in Host and Dashboard source.

---

## Purpose

The dashboard is the player's read-and-react surface for assisted RimWorld play.
The player reads advice, inspects evidence, and keeps control inside RimWorld.

It is also the primary debugging and inspection surface for RimAI. It should
show what the backend, ministers, prompts, briefings, parsers, traces, and logs
actually produced. Prefer contract names and lightly formatted source data over
heavy UI translation. Raw/debug tabs must preserve captured backend payloads.

Dashboard v2 is a from-scratch React implementation inside the existing
dashboard package. Keep the Vite package, build output, and Host serving model;
treat the previous UI as reference only.

---

## Product Posture

- Game-read-only in v2: no RIMAPI write controls, no autonomy toggles, and no
  feedback/Pushback controls.
- Manual Run buttons may trigger RimAI re-evaluation, never RimWorld writes.
- Localhost-only: Host binds loopback and serves the dashboard plus `/api/*`.
- Dense second-monitor operations console, not a landing page.
- Explanation-first: every recommendation needs an inspection path for prompt,
  briefing, RAG, rules/trace, raw LLM output, and current advice.
- Debug-first: preserve backend contract language except in explicitly
  player-facing advice views.

---

## Stack And Serving

- React + TypeScript, built with Vite in root-level `Dashboard/`.
- Production output is bundled into the Host web root and served by
  `RimAI.Host`.
- Dashboard consumes Host HTTP endpoints and the advice SSE stream.
- Local state plus focused polling/SSE hooks is enough for v2; do not add broad
  state-management infrastructure without a concrete need.

---

## Information Architecture

Dashboard v2 has four stable regions:

- Header: product title, runtime status, and global manual trigger.
- Left rail: inspected scope selector.
- Main workspace: SYSTEM overview, INFO reference, ANALYTICS signals, or
  minister inspector tabs.
- Right sidebar: compact colony facts plus colonist cards.

The header exposes `Run Cabinet Now`. Minister workspaces expose
`Run {Minister} Now` beside the selected minister's last-run time. The selected
view is already visible in the tab bar and should not be repeated beside the run
button. Planned ministers show disabled/not-wired controls.

### Scopes

Live non-minister scopes are SYSTEM, INFO, and ANALYTICS. They must be visually
and functionally distinct:

- SYSTEM is the operations/debug console. It answers whether RimAI, Host,
  RIMAPI, SSE, LLM, logs, traces, and endpoints are working.
- INFO is the reference surface. It explains vocabulary and tells the operator
  where to look; it does not carry live metrics.
- ANALYTICS is the interpreted live-signal surface. It summarizes advice mix,
  colony pressure, SSE health, and candidate future analytics.

SYSTEM and ANALYTICS may read from the same bounded dashboard inputs, but SYSTEM
renders raw/source diagnostics while ANALYTICS renders derived meaning. INFO
stays static/reference-oriented so it does not become either a second SYSTEM
page or a second ANALYTICS page.

Live minister scopes are Mayor and Food. Future minister scopes remain visible
but disabled or marked not wired until backend data exists: Construction,
Defense, Welfare, Medical, Research, Industry, Economy, and Chief of Staff.

The left rail selects the inspected scope, not the view.

### Minister Views

Minister scopes use a fixed top tab bar:

- System Prompt
- Briefing
- RAG
- Rules
- Raw LLM Output
- Advice

Use small emoji cues in scope labels, view labels, player-facing section titles,
and compact metric labels when they improve scan speed. Keep backend/debug
payloads, table headers, and contract field names undecorated.

Large objects use the standard disclosure pattern: a real button header with
`aria-expanded` / `aria-controls`, plus a conditionally rendered panel in normal
document flow.

The dashboard persists the last selected scope and view in browser storage.
Stored values are validated against known registries and fall back safely when
stale.

Panel registries are frontend implementation details. Do not render registry ids
or the full registered view list inside the normal minister workspace.

### Dynamic Debug Surfaces

Rules, RAG, Raw LLM Output, endpoint coverage, traces, logs, and unknown future
minister data should render through shared inspector primitives where practical.
The inspector layer may infer useful display from payload shape: summary fields,
tables for uniform arrays, object sections, and JSON/tree fallback.

Player-facing views stay curated. Advice, Mayor Agenda, and the colony sidebar
may keep hand-shaped layouts because they are read during play.

JSON inspectors should preserve open/closed state across polling or SSE
re-renders when the payload content has not changed.

### SYSTEM View

SYSTEM is not a minister and does not show minister tabs. It starts as one
overview page with compact panels. Split it later only if the overview becomes
too large.

SYSTEM should visually read as operations: runtime state, endpoint names,
diagnostic labels, traces, logs, raw health, and explicit failure/degraded
states.

SYSTEM owns:

- Runtime and Mayor/cabinet run state.
- RIMAPI reachability.
- SSE diagnostics.
- LLM health from actual request/parse status, not only key configuration.
- LLM usage placeholders.
- RAG health and corpus/cache placeholders.
- Endpoint and data coverage markers.
- RIMAPI integration snapshot: cached upstream endpoint denominator, active
  reads, represented client methods, deferred write stubs, and missing
  high-priority endpoints.
- Icon cache metadata: local cache counts, byte totals, warm summary, skipped
  candidates, and bounded failure samples.
- Recent event/advice timeline.
- Log and replay-corpus metadata.

SYSTEM panels default collapsed. Panel headers should carry enough count/status
metadata that the page reads as a compact operations index before the operator
opens a specific diagnostic surface.

### INFO View

INFO is not a minister and does not show minister tabs or manual run controls.
It should help the player/operator understand the dashboard language without
leaving the console.

INFO should visually read as reference: glossary cards, plain-language
descriptions, and a compact "where to look" guide. It should not show live
runtime metrics, colony pressure analytics, advice mix analytics, or SSE health
tables.

INFO owns:

- Important buzzwords: a compact dictionary for RimAI terms such as Agenda,
  AdviceItem, Briefing, Flag, RAG, rules path, LLM escalation, and replay
  corpus.
- RimWorld signals: a compact dictionary for terms that commonly affect advice,
  such as food days, work type, skill, downed, wealth pressure, and power net.
- Scope guide: when to use SYSTEM, ANALYTICS, and minister inspection views.

### ANALYTICS View

ANALYTICS is not a minister and does not show minister tabs or manual run
controls. It is the home for derived live readouts that are useful during play
but are not raw debug surfaces.

ANALYTICS owns:

- Live session analytics derived from existing dashboard state and
  `/api/system/health`.
- SSE live-feed health summary: client state, reconnects, event counts, last
  event age, last event id/type, server connection count, and server-side stream
  errors.
- Colony pressure analytics derived from `/api/colony/snapshot`.
- Advice analytics derived from the active advice feed: priority mix, minister
  mix, resource-request kinds, and suggested-action kinds.
- Analytics candidates that are worth adding when backend data exists, such as
  run freshness, escalation ratio, telemetry coverage, feedback funnel, and
  repeat issue heatmaps.

ANALYTICS should prefer derived readouts from already-bounded dashboard inputs.
Do not add broad backend endpoints just to fill ANALYTICS unless the same data
is useful for SYSTEM or minister inspection surfaces too.

### Right Sidebar

The right sidebar is always-on colony context independent of selected scope.

Render compact facts first, then one small panel per colonist: date/tick/season,
colonist count, mood, medical/downed/dead signals, food days, wealth, power,
threat, weather, research, and colonist cards where available.

If a sidebar poll fails after a successful snapshot, keep rendering the last
snapshot and show a compact stale/error note. Do not replace the whole sidebar
with a failure panel unless no snapshot has ever loaded in this page session.

---

## Data Contracts

The dashboard talks only to bounded Host APIs. Exact route lists and payload
types live in `Src/ApiHost/Endpoints/`, `Dashboard/src/api/`, and
`Dashboard/src/types/`.

Design-level endpoint families:

- Runtime/status and system health.
- Advice SSE stream.
- Manual cabinet/minister triggers.
- Agenda and active advice reads.
- Latest minister prompt, briefing, RAG, trace, and raw LLM output.
- Colony snapshot/sidebar data.
- Bounded log and replay-corpus metadata.
- Read-only icon gateway and cache status.
- Developer-only manual raw LLM ingestion for Food while provider quota is a
  practical blocker.

All observability endpoints are read-only unless explicitly named as a manual
RimAI re-evaluation trigger. They never mutate game state.

`/api/system/health` owns dashboard-visible metadata for known log and
diagnostic artifacts. It should expose bounded metadata such as location,
patterns, counts, byte totals, latest write time, and recent-file summaries, not
raw log file contents or full replay payloads.

Icon cache metadata follows the same rule. SYSTEM may show counts, byte totals,
kind totals, last warm result, skipped count, and bounded failure samples from
`var/icons/`; it should not expose or inline image bytes.

The same health payload may expose a RIMAPI integration snapshot for SYSTEM.
This is coverage of upstream RimWorld mod endpoints, separate from Host
`/api/*` coverage. It is a declared inventory of current RimAI wiring, not a
live-discovered upstream truth source. It should distinguish active reads,
represented-but-not-refreshed client methods, deferred write stubs, and missing
high-priority endpoint groups, and should be updated when `RimApiClient` or
`RefreshAllAsync` wiring changes.

### Manual Triggers

Manual triggers are RimAI evaluation controls, not game controls:

- Cabinet trigger: refresh live state, then run wired live ministers in the
  dependency order.
- Minister trigger: refresh live state, then run only the selected wired
  minister.
- Do not keep legacy trigger aliases unless a current dashboard or script
  consumer requires them.

Expected operational failures should be translated before they reach the
player. If RIMAPI is not listening, manual triggers report "RimWorld is not
running" with a short recovery instruction; the dashboard shows that problem
detail directly instead of route names, HTTP status codes, or generic internal
server errors. Logs remain the place for stack traces and low-level diagnostics.

Manual trigger traces must be visible in the dashboard. The current trigger
should remain suggest-only and must not call RIMAPI write endpoints.

### Icon Rendering

Dashboard icons are read-only Host URLs backed by the local runtime cache. The
cache is populated from the player's running game and is not committed.

The dashboard may render an icon only when it has an explicit source:

- `icon` refs carried by `resource_requests[]` or `suggested_actions[]`.
- Pawn ids for lazy portrait URLs.
- Known def-name fields from structured payloads, such as crop/material/resource
  dictionaries.

Do not infer icons from prose in titles, bodies, reasons, instructions, or raw
LLM text. If the explicit icon fails or is missing, render a stable-size fallback
without resizing the row or card.

### Advice SSE

The advice stream is the live source for agenda and active-advice events.
SYSTEM should show detailed connection state, event counts, last event metadata,
and recent errors. ANALYTICS should summarize operator-level health signals so
the player can answer whether the live feed is healthy without reading raw
diagnostics. INFO should only explain what SSE means and where to inspect it.

Design event types:

- Agenda update: full current Mayor agenda.
- Advice snapshot: authoritative active advice set, either global or
  minister-scoped.
- Single advice item: retained for compatibility and event timelines; snapshots
  are authoritative for removing stale cards.

---

## Minister View Detail

### System Prompt

Shows the exact prompt material available for the selected minister. Future
ministers should use generalized read-only prompt inspection when wired. Until a
minister exposes prompt data, render a clear not-exposed state.

### Raw LLM Output

Shows captured provider output before schema parsing, tolerant repair,
normalization, or advice rendering. This is a developer/debug view.

Do not rename fields, paraphrase values, collapse keys, or replace the captured
payload in this tab. Syntax highlighting, indentation, and copy controls are
acceptable. Schema translation belongs in parser/normalizer code and in the
Advice view, not in raw-output inspection.

If no LLM call has happened in the current Host process, render "no raw output
yet" rather than an error. If a request fails before provider text arrives,
surface the failure state so the view explains why no raw response exists.

Developer fallback ingestion for Food is an observability and quota workaround
only. It records and parses pasted raw output through the same backend parser;
it does not call RIMAPI write endpoints and does not imply support for unwired
ministers.

### Briefing

Shows the latest minister briefing grouped into readable sections rather than
dumping raw JSON as the only view. Include raw/source inspection where useful,
but keep the primary view scannable.

### RAG

Shows retrieval status, guide context, citations, snippets, and cache/embedding
status when exposed. Until a minister has dedicated RAG data, render explicit
degraded or not-exposed coverage states.

### Rules

The Rules view answers: "Why did RimAI say this now?"

Show trigger, rules-vs-LLM path, rule fired or escalation reason, relevant flag
or wakeup payload, emitted advice/flags, briefing version/tick when available,
and last error.

### Advice

Mayor Advice renders the Agenda as the Mayor's player-facing output. Feeder
minister Advice renders the minister's current-state summary first, then active
`AdviceItem`s sorted by priority. For Food, this summary is deterministic
briefing-derived state, not LLM prose: it should name concrete food stores,
growing areas/crop progress, kitchen/storage/freezer signals, and confidence
gaps before the action cards. Cards show rationale, suggested actions, resource
requests, citations, issue id or supersession when available, and coverage
gaps. No feedback buttons are shown in v2.

The dashboard does not cache Agenda documents in browser storage. Stale agenda
recovery comes from Host-owned durable agenda storage. On a fresh runtime with
no stored Agenda, Host initializes a labeled bootstrap Agenda before serving the
dashboard. The no-agenda empty state is reserved for initialization/storage
failure or intentionally disabled agenda storage.

Resource requests and suggested actions are actionable reading surfaces and
should default open when an advice card mounts, including after browser refresh
or a new active-advice snapshot. They may use lightweight emoji labels because
they are player-facing action summaries, not raw backend payloads.

These rows may render real game icons only from explicit `icon` refs. The
dashboard does not fuzzy-match request or instruction text to asset names.

---

## Data Coverage

Every debug or inspector surface should distinguish:

- `available`: exposed and fresh enough to render.
- `missing`: needed, but no endpoint or field exists yet.
- `failed`: endpoint exists but errored.
- `unsupported`: intentionally out of scope for this slice.
- `stale`: present but older than expected.

Use this especially for RAG, Rules traces, logs, future ministers, token/cost
metrics, and endpoint coverage.

---

## Visual Rules

- Dark command-center UI with dense spacing and clear hierarchy.
- Left rail, main workspace, and sidebar own their overflow; avoid whole-page
  scrolling on desktop.
- Text wraps within panels.
- Use compact tables only for debug surfaces.
- Debug tables use contract names by default. Friendly aliases are allowed only
  when they clarify a stable contract and do not hide the backend field.
- Resource request tables expose execution-facing fields with enough width for
  `Reason`; avoid catch-all labels such as `Meta`.
- Suggested action cards render `kind` plus `instruction`; do not introduce
  generic `what` labels in the dashboard or new raw minister output.
- Avoid decorative hero sections, oversized empty cards, and one-note color
  themes.

---

## Deferred

- Feedback/Pushback UI returns only when the feedback lifecycle is actively
  wired.
- Autonomy controls return only with M7+ autonomy work.
- Hard-case icon variants such as stuff colors, crop growth stages, styles,
  rotations, motes/projectiles, and per-instance art remain deferred.
- RIMAPI write/control surfaces remain out of scope for suggest-only MVP.

---

## Open Questions

- [ ] Should SYSTEM split into multiple tabs after log and RAG health surfaces
      mature?
- [ ] Which decision-log fields should become durable trace history versus
      in-memory recent-run state?
- [ ] Should browser notifications be scoped to tactical alerts only?
