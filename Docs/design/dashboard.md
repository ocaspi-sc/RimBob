# RimBob Advisor Dashboard

> **Living document.** Dashboard design changes should be recorded here in the
> same turn they are accepted.
> This doc records dashboard purpose, information architecture, and behavioral
> contracts. Exact routes, payload fields, React component names, and API client
> types live in Host and Dashboard source.

---

## Purpose

The dashboard is the player's read-and-react surface for assisted RimWorld play.
The player reads advice, inspects evidence, and keeps control inside RimWorld.

It is also the primary debugging and inspection surface for RimBob. It should
show what the backend, ministers, prompts, briefings, parsers, traces, and logs
actually produced. Prefer contract names and lightly formatted source data over
heavy UI translation. Raw/debug tabs must preserve captured backend payloads.

Dashboard v2 is a from-scratch React implementation inside the existing
dashboard package. Keep the Vite package, build output, and Host serving model;
treat the previous UI as reference only.

---

## Product Posture

- Inspect-first in v2: no autonomy toggles and no feedback/Pushback controls.
  Game writes appear only as Assisted Apply buttons on backend-allowlisted
  advice actions, each requiring an explicit player click. Current allowlisted
  controls are safe food-stack unforbid, validated harvest designation, and the
  single-workbench simple-meal bill upsert.
- Manual Run buttons may trigger RimBob re-evaluation, never RimWorld writes.
  Assisted Apply controls are separate from Run controls.
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
  `RimBob.Host`.
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

Live non-minister scopes are SYSTEM, INFO, ANALYTICS, and DEV BLOG. They must be
visually and functionally distinct:

- SYSTEM is the operations/debug console. It answers whether RimBob, Host,
  RIMAPI, SSE, LLM, logs, traces, and endpoints are working.
- INFO is the reference surface. It explains vocabulary and tells the operator
  where to look; it does not carry live metrics.
- ANALYTICS is the interpreted live-signal surface. It summarizes advice mix,
  colony pressure, SSE health, and candidate future analytics.
- DEV BLOG is the repository-history surface. It reads the local Git `master`
  history, turns commits into topic timelines, churn/LOC charts, pie summaries,
  largest-commit callouts, and editorial suggestions.

SYSTEM, ANALYTICS, and DEV BLOG may read from bounded dashboard inputs, but each
keeps a separate role: SYSTEM renders raw/source diagnostics, ANALYTICS renders
derived live meaning, and DEV BLOG renders repository-history meaning. INFO
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

Use explicit game icons from the Host icon gateway in scope labels, view labels,
section titles, field labels, compact metric labels, and obvious entity rows
when they improve scan speed. Generic emoji are fallback-only. Icons annotate
contract names; they must not replace, rename, or mutate backend field names.
Player-facing advice prose may also receive deterministic inline icon cues for
curated game terms, but the dashboard must preserve the original source text.
This is presentation only: raw LLM output, JSON inspectors, backend payloads,
and stored advice contracts are not rewritten to include those cues.

Large objects use the standard disclosure pattern: a real button header with
`aria-expanded` / `aria-controls`, plus a conditionally rendered panel in normal
document flow.

The dashboard persists the last selected scope and view in browser storage.
Stored values are validated against known registries and fall back safely when
stale.

Dashboard links may include `scope` and `view` query parameters so the local
Windows launcher can jump directly to a console scope or minister inspection
view. Query values are validated against the same registries as stored
selection and fall back safely when stale.

When adding, removing, or renaming top-level dashboard scopes or minister views,
update the local launcher tray menu in the same change. Individual panels do not
need tray commands unless they are promoted into a first-class scope or minister
view.

Panel registries are frontend implementation details. Do not render registry ids
or the full registered view list inside the normal minister workspace.

### Dynamic Debug Surfaces

Rules, RAG, Raw LLM Output, endpoint coverage, traces, logs, and unknown future
minister data should render through shared inspector primitives where practical.
The inspector layer may infer useful display from payload shape: summary fields,
tables for uniform arrays, object sections, and YAML-like source trees backed by
the original JSON payload. Field labels may include semantic icon cues before or
after the contract name, but raw/debug data keeps the original field names.

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
- Test inventory metadata: declared xUnit `[Fact]` / `[Theory]` counts grouped
  by `Src/Tests` category. This is source inventory, not a pass/fail test run
  result.
- Icon cache metadata: local cache counts, byte totals, warm summary, skipped
  candidates, bounded failure samples, and the full cached PNG inventory
  rendered as the actual cached icons.
- Latest minister traces: trigger, status, rules/LLM path, rule trace,
  escalation reason, emitted counts, and failure detail when available.
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

- Important buzzwords: a compact dictionary for RimBob terms such as Agenda,
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

### DEV BLOG View

DEV BLOG is not a minister and does not show minister tabs or manual run
controls. It is a developer/editorial surface for understanding how the project
has changed over time.

DEV BLOG owns:

- Read-only analytics over every commit reachable from local Git `master`.
- Topic-tag timelines derived from commit subjects and touched paths.
- Commit-size histogram, cumulative net LOC growth, largest-commit callouts,
  and pie/donut summaries by area, author, and topic.
- Creative suggestions for release-note lanes, follow-up checks, and future
  archaeology views.

The scope should stay bounded and local. It may shell out to Git from Host, but
it must not mutate the repository, stage files, or inspect uncommitted worktree
state.

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
- Read-only developer Git-history analytics for the DEV BLOG scope.
- Read-only icon gateway and cache status.
- Developer-only manual raw LLM ingestion for ministers that expose the
  capability while provider quota is a practical blocker.
- Player-confirmed Assisted Apply execution for allowlisted advice actions.

All observability endpoints are read-only unless explicitly named as a manual
RimBob re-evaluation trigger. They never mutate game state. Assisted Apply is a
separate action endpoint family, not an observability endpoint.

`/api/system/health` owns dashboard-visible metadata for known log and
diagnostic artifacts. It should expose bounded metadata such as location,
patterns, counts, byte totals, latest write time, and recent-file summaries, not
raw log file contents or full replay payloads.

Icon cache metadata follows the same rule. SYSTEM may show counts, byte totals,
kind totals, last warm result, skipped count, bounded failure samples, and the
complete cached-file list from `var/icons/`, including relative path, kind,
def/id, size, write time, and public Host URL when one exists. It should not
expose or inline image bytes. The visible cache inventory should render as a
compact grouped set of wrapping rows of the actual cached Host icons, not as a
file table. Groups should be derived from stable cache metadata and def-name
patterns so new warmed icons land in a useful place without hand-maintained
panel entries. Failure samples from the last warm run should not be presented
as current missing-icon errors when the same kind/id is now present in the
cached-file inventory.

The same health payload may expose test inventory metadata for SYSTEM. Count
declared xUnit test methods by source category so the dashboard can answer
"what coverage areas exist?" without claiming the suite just executed.

The same health payload may expose a RIMAPI integration snapshot for SYSTEM.
This is coverage of upstream RimWorld mod endpoints, separate from Host
`/api/*` coverage. It is a declared inventory of current RimBob wiring, not a
live-discovered upstream truth source. It should distinguish active reads,
represented-but-not-refreshed client methods, deferred write stubs, and missing
high-priority endpoint groups, and should be updated when `RimApiClient` or
`RefreshAllAsync` wiring changes.

### Manual Triggers

Manual triggers are RimBob evaluation controls, not game controls:

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
should remain a `Suggest`-mode evaluation control and must not call RIMAPI write
endpoints.

### Icon Rendering

Dashboard icons are read-only Host URLs backed by the local runtime cache. The
cache is populated from the player's running game and is not committed.

The dashboard may render an icon only when it has an explicit source:

- `icon` refs carried by `actions[]` or flag `requests[]`.
- Pawn ids for lazy portrait URLs.
- Known def-name fields from structured payloads, such as crop/material/resource
  dictionaries.
- Bounded semantic cue maps for curated dashboard surfaces, keyed by contract
  fields such as scope, view, section, advice action kind, agenda category, and
  INFO glossary tag.

Do not infer icons from prose in raw/debug views, titles, bodies, reasons, or
instructions. Mayor's player-facing Agenda cards may use a small deterministic
domain cue for priority text and may strip leading LLM-emitted emoji/symbols in
the rendered card; Raw LLM Output and JSON inspectors keep the source text
unchanged. If the explicit icon fails or is missing, render a stable-size
fallback without resizing the row or card.

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

Raw LLM Output is a latest-capture inspector, not proof that the latest
minister run used the LLM. Compare the capture timestamp with the latest
minister trace. If a newer run completed without a newer raw response, mark the
raw output as stale and explain that the latest run did not record an LLM
response, commonly because it stayed on the rules path. When trace path details
are available, name the concrete path (`rules`, `llm`, `llm_failed`), rule
trace, escalation reason, or failure text instead of falling back to a generic
possibility statement. If the latest run did not use the LLM but an earlier raw
LLM capture exists, keep showing that previous capture with the stale warning.
Only render the "no raw output yet" empty state when no prior raw response is
available from the current Host process or replay corpus.

Developer fallback ingestion is an observability and quota workaround only. The
existing `run-minister-using-subagent` skill owns the operator workflow:
copy exact prompt inputs, generate JSON outside Gemini, and post the raw output
to the selected minister's manual ingestion endpoint. Support is
capability-scoped by minister; it does not call RIMAPI write endpoints and does
not imply support for unwired ministers.

### Briefing

Shows the latest minister briefing grouped into readable sections rather than
dumping raw JSON as the only view. Include raw/source inspection where useful,
but keep the primary view scannable.

Food's Briefing view should also show deterministic crop-candidate math as a
compact inspector panel when the backend exposes it. The panel is read-only and
exists to make crop choice, season fit, fertility, storage modifiers, and
classification confidence inspectable without digging through prompt JSON.

### RAG

Shows retrieval status, guide context, citations, snippets, and cache/embedding
status when exposed. Until a minister has dedicated RAG data, render explicit
degraded or not-exposed coverage states.

### Rules

The Rules view answers: "Why did RimBob say this now?"

Show trigger, rules-vs-LLM path, selected rule or escalation reason, matched
signals, suppressed lower-priority candidates, relevant flag or wakeup payload,
emitted advice/flags, briefing version/tick when available, and last error.

### Advice

Mayor Advice renders the Agenda as the Mayor's player-facing output. Feeder
minister Advice renders the minister's current-state summary first, then active
`AdviceItem`s sorted by priority. For Food, this summary is a deterministic
briefing-derived labelled summary, not LLM prose. The Advice view may render it
as a compact table with lightweight icon-database cues because it is
player-facing; raw and debug views preserve the original `state_summary` text.
It should name
concrete food stores, growing areas/crop progress, acquisition opportunities,
kitchen/storage/freezer signals, and confidence gaps before the action cards.
Cards show rationale, concrete advice actions, citations, issue id or supersession
when available, and coverage gaps. No feedback buttons are shown in v2.

If an action carries a backend-approved executable handle, the Advice view may show
an Apply control on that action. Apply controls must be visually distinct from
feedback, disabled when state is stale or validation fails, and followed by a
compact result state that links to the SYSTEM/trace evidence for the attempted
write and read-back.

The dashboard does not cache Agenda documents in browser storage. Stale agenda
recovery comes from Host-owned durable agenda storage. On a fresh runtime with
no stored Agenda, Host initializes a labeled bootstrap Agenda before serving the
dashboard. The no-agenda empty state is reserved for initialization/storage
failure or intentionally disabled agenda storage.

Advice actions are the actionable reading surface and should default open when an
advice card mounts, including after browser refresh or a new active-advice
snapshot. Raw/debug views should still expose legacy normalization metadata when
old `resource_requests[]` or `suggested_actions[]` payloads were repaired into
actions.

These rows may render real game icons only from explicit `icon` refs. The
dashboard does not fuzzy-match reason or instruction text to asset names.

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
- Use compact tables for debug surfaces and explicitly curated player-facing
  summaries, such as the Food current-state table.
- Debug tables use contract names by default. Friendly aliases are allowed only
  when they clarify a stable contract and do not hide the backend field.
- Advice action cards render `kind` plus `instruction`, with quantity, owner,
  work/skill, and reason as secondary detail; do not introduce generic `what`
  labels in the dashboard or new raw minister output.
- Avoid decorative hero sections, oversized empty cards, and one-note color
  themes.

---

## Deferred

- Feedback/Pushback UI returns only when the feedback lifecycle is actively
  wired.
- Autonomy controls return only with M7+ autonomy work.
- Broad RIMAPI write/control surfaces remain deferred; Assisted Apply is the
  only MVP exception and is limited to backend-allowlisted advice actions.
- Hard-case icon variants such as stuff colors, crop growth stages, styles,
  rotations, motes/projectiles, and per-instance art remain deferred.
- Hard-case action controls such as broad bill editing, schedules, pawn
  assignment, zones, medical/prisoner operations, and combat commands remain out
  of scope for MVP.

---

## Open Questions

- [ ] Should SYSTEM split into multiple tabs after log and RAG health surfaces
      mature?
- [ ] Which decision-log fields should become durable trace history versus
      in-memory recent-run state?
- [ ] Should browser notifications be scoped to tactical alerts only?
