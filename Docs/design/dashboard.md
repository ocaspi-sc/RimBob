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
  controls are safe food-stack unforbid, validated harvest designation,
  validated low-risk hunt designation, and the single-workbench simple-meal bill
  upsert.
- Manual Run buttons may trigger RimBob re-evaluation, never RimWorld writes.
  Assisted Apply controls are separate from Run controls.
- Localhost-only: Host binds loopback and serves the dashboard plus `/api/*`.
- Dense second-monitor operations console, not a landing page.
- Explanation-first: every recommendation needs an inspection path for prompt,
  briefing, RAG, rules/trace, raw LLM output, and current advice.
- Debug-first: preserve backend contract language except in explicitly
  player-facing advice views.
- Latest advice stays visible: expired or stale persisted minister advice is
  still rendered for inspection and labeled as stale rather than replaced by an
  empty state.

---

## Stack And Serving

- React + TypeScript, built with Vite in root-level `Dashboard/`.
- Production output is bundled into the Host web root and served by
  `RimBob.Host`.
- Dashboard consumes Host HTTP endpoints and the advice SSE stream.
- Local state plus focused polling/SSE hooks is enough for v2; do not add broad
  state-management infrastructure without a concrete need.
- When the Host/API is down, automatic HTTP polling and SSE reconnects back off
  with capped quiet retries. The dashboard should preserve the last visible
  state and recover when RimBob returns without spamming failed localhost
  requests.

---

## Information Architecture

Dashboard v2 has four stable regions:

- Header: product title, runtime status, and global manual trigger.
- Left rail: inspected scope selector.
- Main workspace: SYSTEM, INFO, ANALYTICS, and DEV BLOG local tabs, or
  minister inspector tabs.
- Right sidebar: compact colony facts plus colonist cards.

The header exposes the running RimBob version before transient status pills:
monotonic running build version, build datetime, build revision, dashboard
asset fingerprint, and the served Host root/process tooltip. This is the first
stale-host/stale-asset check because it answers which code and UI bundle the
player is actually reading. The header status pills distinguish separate
surfaces: `Host API` is the latest `/api/status` HTTP poll, `SSE` is the
browser `EventSource` for `/api/advice/stream`, and RIMAPI/LLM/Mayor are current
only while `Host API` is live. If the Host API poll fails after a previous
success, the header must show `stale` and render dependent RIMAPI/LLM/Mayor
values as `last ...`; if no Host API response is reachable, those dependent
values are `unknown`, not inferred from cached data. `LLM configured` means a
Gemini key exists; provider success/failure comes from the latest raw-output
status. `Mayor loaded` means no Mayor run is active and a Mayor snapshot is
loaded; the snapshot version belongs in the tooltip or SYSTEM details, not the
compact label. The dashboard asset marker should read as a state such as
`UI loaded`; bundle hashes belong in the tooltip. Every header marker/chip
should carry a terse explanatory tooltip with the current/last value. Avoid
normal header tooltips that are just route names, implementation URLs, or raw
field names; those details belong in SYSTEM/debug panels.
The header also exposes `Run Cabinet Now`. Minister workspaces expose
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
  history, turns commits into topic timelines, churn/LOC charts, area/topic pie
  summaries, and editorial suggestions.

SYSTEM, ANALYTICS, and DEV BLOG may read from bounded dashboard inputs, but each
keeps a separate role: SYSTEM renders raw/source diagnostics, ANALYTICS renders
derived live meaning, and DEV BLOG renders repository-history meaning. INFO
stays static/reference-oriented so it does not become either a second SYSTEM
page or a second ANALYTICS page.

Live minister scopes are Mayor and Chef (`food` key). Future minister scopes remain visible
but disabled or marked not wired until backend data exists: Construction,
Defense, Welfare, Medical, Research, Industry, Economy, and Chief of Staff.

The left rail selects the inspected scope, not the view.

Non-minister console scopes use a shallow local tab bar:

- SYSTEM: Runtime, Connectivity, Storage, Coverage, Events.
- INFO: Overview, Glossary, Contracts, Data Sources.
- ANALYTICS: Session, Colony, Advice, SSE, Candidates.
- DEV BLOG: Features, Churn, Commits, Topics, Suggestions.

Console tabs are first-class dashboard views for URL/storage validation and
launcher deep links, but they do not change cabinet ownership. Keep the top
tab layer shallow; nested tool navigation should use controls such as filters,
not a second tab system.

### Minister Views

Minister scopes use a fixed top tab bar:

- System Prompt
- Briefing
- RAG
- Rules
- Raw LLM Output
- Infographics
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

The minister Rules view should expose a compact all-rules table when the backend
trace provides one. Group the catalog by outcome with semantic rule/outcome icon
cues, and keep each row showing the rule name, outcome, condition summary,
output action, and live evidence/reason so non-selected rules are inspectable
without reading source.

Player-facing views stay curated. Advice, Mayor Agenda, and the colony sidebar
may keep hand-shaped layouts because they are read during play.

JSON inspectors should preserve open/closed state across polling or SSE
re-renders when the payload content has not changed.

### SYSTEM View

SYSTEM is not a minister and does not show minister tabs. It uses local tabs to
separate dense operational data: runtime identity, connectivity/provider state,
storage/cache/log metadata, endpoint/test coverage, and event/trace timelines.

SYSTEM should visually read as operations: runtime state, endpoint names,
diagnostic labels, traces, logs, raw health, and explicit failure/degraded
states.

SYSTEM owns:

- Runtime and Mayor/cabinet run state, including the full Host executable path
  so operators can see which checkout is actually serving the dashboard.
- Persistent runtime storage metadata, including the stable data root, unified
  minister output store, latest ColonyState snapshot, and RAG embedding cache
  paths.
- RIMAPI reachability.
- SSE diagnostics.
- LLM health from actual request/parse status, not only key configuration.
- LLM usage placeholders.
- RAG health and corpus/cache placeholders.
- Endpoint and data coverage markers.
- RIMAPI integration snapshot: cached upstream endpoint denominator, active
  reads, represented client methods, deferred write stubs, and missing
  high-priority endpoints.
- Colony state snapshot metadata: latest curated snapshot path, capture time,
  age, load/save errors, and whether current state is live or restored/stale.
- Test inventory metadata: declared xUnit `[Fact]` / `[Theory]` counts grouped
  by `Src/Tests` category. This is source inventory, not a pass/fail test run
  result.
- Icon cache metadata: local cache counts, byte totals, warm job lifecycle,
  skipped candidates, deferred candidates, bounded failure samples, and per-def
  warm state. The full usable cached PNG inventory belongs behind the explicit
  icon-cache debug request, then renders as actual cached icons. Upstream
  placeholder red-X PNGs are invalid cache artifacts: they should be treated as
  missing/unusable, not displayed as real game art.
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
- Data-source guide: which dashboard surfaces are live, persisted/restored,
  SSE/session buffered, or static reference copy.

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
- Feature-tag timelines derived from the Features taxonomy.
- Commit-size histogram, cumulative net LOC growth, and pie/donut summaries by
  area and topic.
- A Features tab: a long structured feature inventory that is not grouped by day
  and does not focus on individual commits. It should dedupe commits across old
  visible lanes, render one flat score-sorted list, and filter that list through
  tag chips. Former subsystem names such as Food, Mayor, Dashboard, Icons,
  Execution, Persistence, Infra, Design, Agent Ops, and Advice are tags, not
  sections or nested tabs. Cross-cutting concepts such as Visualization,
  Refactor, Agents, RAG, Rules, RIMAPI, Persistence, and Icon Gateway are also
  tags. Use longer human-readable feature names with semantic icons and tag
  emojis. The feature-tag timeline is the first reading surface in this tab,
  followed by a left-side vertical tag rail and the filtered feature list. The
  same tag chips control both the visible feature list and the feature-tag
  timeline chart in this tab. Tag filtering is single-select: one selected tag
  filters the list and draws that tag's chart series; when no tag is selected,
  the chart may show the highest-scoring tags as an overview. Show each
  feature's estimated score as raw deterministic commit scope plus a visible
  commit-count effort bonus, so a feature spread across many commits ranks
  heavier than the same raw scope in one commit. The
  tab itself is the feature inventory, so avoid redundant wrapper panels or
  repeated "Feature inventory" headings. Tooltips can keep raw score math,
  source commit count, file count, material areas, and supporting areas for
  audit. Prefer smaller, interesting feature rollups over broad buckets such as
  "food chain modeling."
- Commit size in DEV BLOG is a deterministic scope score, not raw LOC. Prefer
  Git word-diff/token counts when available so a one-word change in a long line
  stays small; then include bounded file/area weight and discount generated
  artifacts and sync merges.
- Dashboard tag scoring means dashboard-owned work. Dashboard files that only
  mirror another area's contract change are supporting metadata (`reflected:
  Dashboard`), not Dashboard feature score.
- Creative suggestions for release-note lanes, follow-up checks, and future
  archaeology views.

The scope should stay bounded and local. It may shell out to Git from Host, but
it must not mutate the repository, stage files, or inspect uncommitted worktree
state. The Host caches the parsed report in process by repository root and
`master` head: fresh requests return without shelling out, expired requests
validate the current local `master` commit, and only a head change should rerun
the full Git history scan. The frontend should also memoize derived feature
rollups so tag changes do not rebuild the whole feature inventory.

### Right Sidebar

The right sidebar is always-on colony context independent of selected scope.

Render compact facts first, then one small panel per colonist: date/tick/season,
colonist count, mood, medical/downed/dead signals, food days, wealth, power,
threat, weather, research, and colonist cards where available.

Date rendering should use the backend's normalized game-time model: show the
colony-relative label (`Y1 D6, Aprimay 5, 14h` style), total elapsed days when
useful, the current tick, and season/quadrum context. Do not promote RIMAPI's
raw `5500` calendar year in player-facing sidebar or advice labels except in
raw/debug inspectors.

Mayor agenda stamps read `updated_game_date`; advice cards and inspector JSON
read `issued_game_date` plus tick fields for expiry math. The dashboard should
show normalized day deltas and total days, not raw tick arithmetic, wherever a
player-facing freshness label is needed.

If a sidebar poll fails after a successful snapshot, keep rendering the last
snapshot and show a compact stale/error note. Do not replace the whole sidebar
with a failure panel unless no snapshot has ever loaded in this page session.
If Host is serving restored ColonyState while RIMAPI is unreachable, show a
compact stale snapshot chip near the sidebar footer.

---

## Data Contracts

The dashboard talks only to bounded Host APIs. Exact route lists and payload
types live in `Src/ApiHost/Endpoints/`, `Dashboard/src/api/`, and
`Dashboard/src/types/`.

Design-level endpoint families:

- Runtime/status and system health.
- Advice SSE stream.
- Manual cabinet/minister triggers.
- Typed minister snapshot reads for the Mayor and feeder active advice.
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

Each dashboard page renders a compact endpoint footer at the bottom of the main
workspace. It lists the Host HTTP endpoints and live stream endpoints observed
while that page is active, including method, status, and latest query duration;
slow requests are visually promoted so stale or heavy pages are obvious during
play.

Prompt inspector endpoints should preserve the exact `{ system, user }` payload
shape for dashboard consumers, but may cache the rebuilt prompt briefly by
minister and source-state version. An explicit refresh parameter should force a
fresh rebuild for debugging when the operator needs to re-run retrieval.

`/api/system/health` owns dashboard-visible metadata for known log and
diagnostic artifacts. It should expose bounded metadata such as location,
patterns, counts, byte totals, latest write time, and recent-file summaries, not
raw log file contents or full replay payloads. The default logs root is stable
machine-local storage under LocalAppData (`RimBob/logs`), not the active
repository or worktree; `RimBob:LogsRoot` may override it. Serilog logs,
structured decision logs, replay corpus records, Mayor prompt dumps, and manual
fallback files should stay under that same root.

`/api/system/health` also owns the running RimBob version contract: monotonic
running build version, build datetime, assembly build revision, dashboard asset
fingerprint, reload token, process start time, and per-process instance id. The
running build version is generated into Host assembly metadata during each Host
build and increments its patch component locally from `0.0.1000`, so rebuilding
the same commit still changes the visible running version. The dashboard treats
the reload token as the hard-refresh key. A changed token means the served Host
build or dashboard bundle changed and the browser tab should reload; a changed
process instance alone is just a reconnect and must not trigger cabinet
regeneration.

The same payload owns persistent runtime data paths. It should expose the
stable data root plus the resolved minister output root, latest ColonyState
snapshot, and RAG embedding cache paths so SYSTEM can prove those artifacts are
not forking per branch or worktree. The default data root is stable
machine-local storage under LocalAppData (`RimBob`), not the active repository
or worktree; `RimBob:DataRoot` may override it. Relative child paths, including
`RimBob:Rag:CacheRoot`, resolve under that data root.

SYSTEM should also show per-minister output metadata from the unified store:
minister key, output kind, generation, persisted time, path, and load/flush
state. This is the dashboard-visible proof that the last good Mayor Agenda and
feeder advice snapshots were reloaded rather than regenerated.

Minister Advice views render the latest persisted feeder snapshot, including
expired advice. Advice freshness labels should use game-tick expiry when the
payload provides it and should render normalized game-day deltas rather than
raw tick math. Apply controls for expired advice should stay visible but
disabled or fail with a stale-advice result after backend validation.

Icon cache metadata follows the same rule. SYSTEM may show counts, byte totals,
kind totals, warm job state (`idle`, `running`, `completed`, or `failed`), live
job progress, last warm result, skipped count, deferred count, and bounded
failure samples from `/api/system/health` or the default icon-cache status
request. The complete cached-file list is an explicit debug payload requested
from `/api/icons/cache/status?includeFiles=true`; it includes relative path,
kind, def/id, size, write time, and public Host URL when one exists. The default
icon cache root is stable machine-local storage under LocalAppData
(`RimBob/icons`), not the active repository or worktree; `RimBob:IconCacheRoot`
may override it. Relative overrides resolve under the same stable machine-local
RimBob root. It should not expose or inline image bytes. The visible cache
inventory should render as a compact grouped set of wrapping rows of the actual
cached Host icons, not as a file table. Groups should be derived from stable
cache metadata and def-name patterns so new warmed icons land in a useful place
without hand-maintained panel entries.

The static warm runs as a Host-owned background job, not as request-bound work.
The POST that starts warming returns immediately with the current job state;
client disconnects must not cancel an in-flight warm. The manifest is
incremental and mergeable: persisted PNG successes remain accounted for across
partial runs, later failed-only warm attempts, and Host restarts. Status samples
are derived from per-def state plus the current cached-file inventory, not a
frozen end-of-run list; a def that is now present on disk must not keep showing
as a current missing-icon error.

The static warm fetches each candidate with conservative pacing and bounded
retry. Transient RIMAPI failures and `result: "missing"` icon responses are
tracked separately so the operator can tell under-load/no-asset failures from
transport failures. Deterministic key errors fail fast. A failure sample
therefore reflects a candidate that stayed unresolved across all attempts, not
a one-off upstream hiccup. Faction icons depend on a loaded world; when no
world is loaded, faction warming is `deferred`, not `failed`.

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
cache is populated from the player's running game, is not committed, and should
not fork per worktree unless an operator explicitly configures a worktree-local
`RimBob:IconCacheRoot`.
Terrain images are cached as dashboard-sized thumbnails, not full source
textures, because terrain endpoints can return large tile textures that are not
useful at icon scale.

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

- Host ready: current running RimBob version plus Host instance identity,
  emitted on SSE connect so an already-open dashboard can reload after the
  served build or dashboard asset fingerprint changes.
- Agenda update: full current Mayor agenda.
- Advice snapshot: authoritative active advice set, either global or
  minister-scoped. Feeder snapshots may include whole-minister `state_summary`
  and deterministic `chain` data; global replay snapshots may include the
  corresponding per-minister dictionaries.
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

Chef's Briefing view should also show deterministic crop-candidate math as a
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
Rules trace/replay diagnostics should also include emitted action provenance so
the operator can see which rule or LLM-after-escalation path produced each
action row. Keep that provenance in trace/replay diagnostics; raw `actions[]`
remains the player-facing action contract.

### Infographics

Infographics renders visual, deterministic readouts derived from minister
snapshot data. It is for whole-minister models that are easier to read as a
diagram than as advice text or raw JSON.

Chef's Infographics view renders backend `chain` data as one merged work-order
diagram. Grow, forage, and hunt enter as separate colored routes, then merge
into the shared Harvest/Butcher -> Storage -> Cook -> Fridge path. Every step
is a small icon-led box with short factual subtext; crop waiting belongs under
Plant rather than as its own node. State color semantics are stable across the
diagram: green means covered/current state, blue means the current advice
emitted an action, gray means idle/future capacity, and red means a blocked
prerequisite.

### Advice

Mayor Advice renders the Agenda as the Mayor's player-facing output. Feeder
minister Advice renders the minister's current-state summary first, then active
`AdviceItem`s sorted by priority. For Chef, this summary is a deterministic
briefing-derived labelled summary, not LLM prose. The Advice view may render it
as a compact table with lightweight icon-database cues because it is
player-facing; raw and debug views preserve the original `state_summary` text.
It should name concrete food stores, growing areas/crop progress, acquisition
opportunities, hunt-risk posture, kitchen/storage/freezer signals, and
confidence gaps before the action cards. Hunt-risk posture in Current State is
a compact briefing-derived line, not the full diagnostics payload; the full
type-level breakdown remains under `/api/ministers/food/hunt-risk/latest`.
Cards show rationale, concrete advice actions, citations, issue id or supersession
when available, and coverage gaps. No feedback buttons are shown in v2.

Feeder Advice views also render the latest `AgentFlag`s emitted with that
minister snapshot. Flags are not action duplicates: they carry routing pressure
and `requests[]` such as labor, building, bill, tile, or freezer needs that
should remain visible even when the player-facing `actions[]` list avoids a
high-blast-radius policy knob.

If an action carries a backend-approved executable handle, the Advice view may show
an Apply control on that action. Apply controls must be visually distinct from
feedback, disabled when state is stale or validation fails, and followed by a
compact result state that links to the SYSTEM/trace evidence for the attempted
write and read-back.

An Apply control renders only in the scope of the minister that **emitted** the
action. A requesting minister shows its outbound `requests[]` but never another
minister's executable handle — e.g. Chef requests a freezer, but the
`place_blueprint` Apply lives in Construction's (Willie's) scope, because
Construction emits the placement action.

The dashboard does not cache Agenda documents in browser storage. Stale agenda
and feeder-advice recovery comes from the Host-owned unified minister output
store. On a fresh runtime with no persisted minister output at all, Host
initializes a labeled bootstrap Agenda before serving the dashboard. Rebuilds
reload persisted snapshots and do not fire the cabinet just to populate the UI.
The no-output empty state is reserved for initialization/storage failure or
intentionally disabled output storage.

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
  summaries, such as the Chef current-state table.
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

- [ ] Which console tabs should get direct toolbar affordances once their
      diagnostics become routine operator workflows?
- [ ] Which decision-log fields should become durable trace history versus
      in-memory recent-run state?
- [ ] Should browser notifications be scoped to tactical alerts only?
