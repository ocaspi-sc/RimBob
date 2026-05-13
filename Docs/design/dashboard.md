# RimAI — Advisor Dashboard

> **Living document.** See `CLAUDE.md` for update rules.
> Slice: M0 (skeleton) → M1 (Agenda tab) → M2 (RAG/prompt inspection) → M3 (Food feeder alerts + briefing inspector) → M5 (feedback) → M7 (autonomy).

---

## Purpose

The dashboard is the player's view into RimAI. It is a **read-and-react** surface, not a control surface — the colony is still controlled in RimWorld itself. The dashboard renders advisor memos, captures the player's reaction, and exposes the briefing + decision log so the player (or a developer) can see *why* an advisor said what it said.

Runs alongside the game in a browser window — typical use is on a second monitor or in a half-screen split.

---

## Stack

- **React 18 + TypeScript**, built with **Vite**.
- Lives at `Dashboard/` in the repo (see [`architecture.md`](architecture.md)).
- Production build output is bundled into Host's `Static/` and served as static assets.
- Development uses Vite dev server with a proxy to Host's `/api/*`.
- No state-management library to start — `useState` + a small EventSource hook is enough at MVP scope.

---

## Auth posture

**Localhost-only.** Host binds `127.0.0.1:<port>` and rejects requests with non-loopback `Host:` headers. No auth tokens, no CORS allowance for remote origins. Documented as a hard constraint so we don't accidentally bind `0.0.0.0` and expose advice + briefing data on the network.

If we ever want remote access, that is an explicit future ticket with its own auth design (token + TLS at minimum).

---

## HTTP + SSE contract

All endpoints live under `/api/`. Host is the only producer; dashboard is the only consumer.

### `GET /api/health`
Liveness probe. Returns `{ status: "ok", version: "..." }`.

### `GET /api/advice/stream`  *(SSE)*
The advice feed. Server-Sent Events stream of agenda and `AdviceItem` JSON payloads.

```
event: advice
id: <advice_id>
data: { ...AdviceItem }

event: feedback
id: <feedback_id>
data: { ...FeedbackEvent }      // echoes player feedback for multi-tab sync

event: ping
data: {}                        // every 15s; keeps connection alive
```

On connect, the server replays the current agenda and active unexpired advice items so a fresh page load shows current state. Older advice history is deferred until the decision-log work.

### `POST /api/advice/{id}/feedback`
Body:
```json
{ "action": "Accept" | "Dismiss" | "Modify",
  "note": "optional player note",
  "modified_actions": [ /* SuggestedAction[], present only when action == Modify */ ] }
```
Writes a `FeedbackEvent` to the decision log. Echoes back over SSE for other open tabs.

### `GET /api/briefings/{minister}/latest`
Returns the most recent `IBriefing` for a minister so the briefing-inspector tab can render the source data behind any memo. M3 implements `mayor` and `food`.

### `GET /api/decisions?minister=&since=`
Returns recent decision-log records (with feedback joined) for the decision-log tab.

### `GET /api/agenda/latest`
Returns the current `MayorAgenda` JSON. Used on page load and by the briefing-inspector.

### `GET /api/agenda/history?since=<tick>`
Returns `MayorAgenda` snapshots since `tick`, one per in-game day. Retained for 30 in-game days.

### `POST /api/agenda/refresh`
Demand-trigger from the dashboard "Refresh" button. Runs `IngestionDispatcher.RefreshAllAsync` then `Mayor.RunPlayCycle`, broadcasts the new agenda over SSE, returns `{ "refreshed": true }`. The endpoint is fire-and-acknowledge — the caller does not get the new agenda in the response body; it arrives via the existing SSE stream so all open dashboard tabs stay in sync.

### `GET /api/colony/snapshot`
Returns the latest `MayorBriefing` (the same data the Mayor sees on each turn). The dashboard sidebar polls this every 5s to render dry colony telemetry — date, season, colonists, mood, food, wealth, power, threat, weather, research. Camel-case JSON (ASP.NET Core default). With this in place the agenda body stays interpretive: the Mayor doesn't need to repeat raw numbers the sidebar already shows.

### `GET /api/status`
Server health + Mayor run state. Polled by the topbar status pill. Shape:
```json
{
  "server": "ok",
  "rimapi_reachable": true,
  "llm_configured":   true,
  "briefing_version": 7,
  "agenda_version":   3,
  "mayor_running":    false,
  "mayor_started_at":   "2026-05-08T17:20:21Z",
  "mayor_completed_at": "2026-05-08T17:20:36Z",
  "mayor_last_error":   null
}
```
`mayor_running` flips `true` for the duration of a `RunPlayCycle` so the dashboard can show a live spinner; `mayor_last_error` carries the most recent failure text without scraping logs.

### `GET /api/mayor/prompt`
Introspection: returns `{ system, user }` — the exact two strings `PromptBuilder` would hand to Gemini for the *next* Mayor call given the current briefing + previous agenda. Used by the Briefing tab (M1+) and developer tooling. Not cached — recomputed per call.

### `POST /api/agenda/{version}/priority/{rank}/feedback`  *(M2)*
Accepts / dismisses / modifies an individual short-term priority item. Same `FeedbackEvent` body as `/api/advice/{id}/feedback`; recorded with `source: "agenda_priority"`.

### New SSE event type on `GET /api/advice/stream`
```
event: agenda_update
id: <version>
data: { ...full MayorAgenda }
```
On connect, the server replays the current Agenda (if one exists) as a single `agenda_update` event before the live feed begins. Idle connections receive `event: ping\ndata: {}\n\n` every 15s.

### `GET /api/autonomy` / `PUT /api/autonomy`
Reads/writes the per-minister autonomy dial. In MVP, every value is `Suggest` and `PUT` is a no-op that returns 200; the endpoint exists so the dashboard can render the panel and the contract is set for M7.

```json
{ "Mayor": "Suggest", "Food": "Suggest", "Defense": "Suggest" }
```

---

## Layout (MVP)

Dashboard presentation is a **dark command-center console**. The default desktop view should fit inside one browser viewport: a top status bar, left boxed tab rail, central scrollable agenda console, and right boxed telemetry console. The page itself should avoid whole-window scrolling on normal desktop sizes; overflow belongs inside the agenda and telemetry boxes so the dashboard remains usable next to RimWorld.

Visual conventions:
- Dark theme only for MVP; no light/dark toggle until there is an explicit design reason.
- Use repeated boxed modules with squared, technical styling: compact borders, status lamps, corner accents, dense telemetry rows, and restrained cyan / amber / green / red accents.
- Keep information dense and scan-friendly. Avoid marketing-page spacing, large empty hero areas, and soft card-heavy layouts.
- Keep vertical padding tight enough for split-screen play. Prefer compact rows and module spacing over airy panels.
- Text must wrap inside its module instead of widening the dashboard. Narrow screens can stack modules, but desktop should keep the command-center composition.
- Do not show scrollbars unless the content actually overflows. Decorative panel accents must stay inside their boxes so they do not create phantom scrollbars.

```
┌─ Topbar ───────────────────────────────────────────────────────────────┐
│  RimWorld Advisory Cabinet                                             │
│  RimAI Command           ● Agenda feed live · Poll 5s · [ Refresh ]    │
├─ Tab rail ────┬─ Main console ───────────────────────┬─ Sidebar ──────┤
│ ▸ Agenda      │  Agenda                v142 · Y1Q3D12│  Colony        │
│   Alerts (M5) │  $ Consolidation · ⚔ Defensive       │  ─ Date        │
│   Briefing    │  "Hold wealth, shore up food …"      │   12 Jul Y1    │
│   Log (M2)    │  ─                                   │   Winter 20d   │
│   Autonomy    │  State of the Union                  │  ─ People  6   │
│               │   Food          Food 18d vs 20…       │  ─ Mood   72%  │
│               │   🛡️ Defense      East wall gap, …  │  ─ Food   18d  │
│               │   ❤️ Welfare      Mood 72%, no risk  │  ─ Wealth 85k  │
│               │   🔨 Construction Freezer running    │  ─ Power +120W │
│               │   💰 Treasury     Wealth ~85k        │  ─ Threat: no  │
│               │   🔬 Research     Mid-microelec      │  ─ Weather -2°C│
│               │  ─                                   │  ─ Research 60%│
│               │  What changed                        │                │
│               │  "Winter arrives in ~20 days. …"     │  briefing v142 │
│               │  ─                                   │  tick 612,400  │
│               │  Short-term · 3 active               │                │
│               │  ① Establish growing zone … [NEW]   │                │
│               │    [Accept][Modify][Dismiss]         │                │
│               │  ② Patch east-wall gap …             │                │
│               │  ③ Review schedules …                │                │
│               │  ─                                   │                │
│               │  Long-term · 2                       │                │
│               │   ● Winter prep before day 30        │                │
│               │   ○ Base expansion → Y2              │                │
└───────────────┴──────────────────────────────────────┴────────────────┘
```

The four main regions are independent boxed panels (`panel-box`): topbar, tab rail, main console, sidebar. The page itself does not scroll on a normal desktop — overflow lives inside the main console and the sidebar.

### Topbar
- **Status pill.** Polls `/api/status` every few seconds; renders a coloured lamp (`status-light` `live` / `standby` / `error`) and a label ("Agenda feed live" / "Awaiting first agenda" / failure text from `mayor_last_error`).
- **Refresh button.** `POST /api/agenda/refresh`. Disabled while a refresh is in flight; the button label flips to "Refreshing…". Useful when the player wants a fresh briefing without waiting for the next in-game-day rollover.
- **Poll cadence indicator.** Shows the sidebar poll interval (5s) so the player knows how stale the telemetry can be.

### Tabs (MVP)
- **Agenda** — Mayor's living plan; short-term priorities and long-term goals. **Default tab.** M1 ships this.
- **Prompt** — ready developer inspector immediately after Agenda. It renders `/api/mayor/prompt` independently of whether an agenda exists, so the prompt/debug view does not fall back to the Mayor uplink waiting state.
- **Alerts** — active feeder-minister `AdviceItem`s. M3 renders Food cards with severity, rationale, suggested actions, and a separate `Resource requests` block. Tactical-alert badges are still M5+.
- **Briefing** — pick Mayor or Food and see the latest briefing JSON pretty-printed. M3 backs this with `/api/briefings/{minister}/latest`.
- **Log** — recent decision-log records with feedback events joined. M2 ships this.
- **Autonomy** — per-minister dial. Read-only display in MVP; PUT is wired but every value stays `Suggest`. M2 ships the panel; the dial gets a real switch only at M7.

Locked tabs render a unified empty-console panel (`MODULE LOCKED · Coming in M{n}`), not a disabled button — keeps the visual mass consistent so the rail doesn't shift width as M2/M5/M7 land.

### Prompt view
- The left rail's **Prompt** tab opens the full-height prompt inspector for the next Mayor call. The Agenda header may also expose a local **Prompt** toggle, but the dedicated tab is the primary entry point and works before the first agenda has been produced.
- `/api/mayor/prompt` remains an exact-string introspection endpoint (`{ system, user }`). Readability is a frontend concern: the dashboard parses the Mayor user message JSON at render time, with a raw decorated text fallback when parsing fails.
- Parsed prompt JSON renders as a readable labeled tree rather than a raw `JSON.stringify` block. Primitive fields become compact key/value rows; arrays render as nested items; empty objects/lists/nulls get explicit placeholders.
- Important prompt keywords map to emoji decorations in the UI (food, defense, welfare, health, people, power, construction, wealth, research, weather, risk, status). These icons are visual decorations only; they are not inserted into the backend prompt payload.
- The parsed `briefing` object is split into collapsible boxed groups using case-insensitive field matching: Overview, People, Food & resources, Infrastructure, Welfare & threat, Environment, Research, and Other briefing fields when new fields arrive.
- All prompt inspector boxes start collapsed.
- Prompt inspector boxes use the standard React disclosure pattern: a `<button>` header with `aria-expanded` / `aria-controls`, and a conditionally rendered panel in normal document flow.
- Prompt inspector boxes stay in normal document flow: opening a box expands its row and pushes every subsequent box downward. The main console owns page-level scrolling; individual large box bodies still cap their height and scroll internally.
- Non-briefing user-message context (`previous_agenda`, `agenda_directives`, `guide_context`) stays available in a separate collapsed **Prompt context** box. If parsing fails, render the raw user message unchanged.
- The system prompt renders as its own collapsible boxed section below the briefing inspector.

### Agenda tab detail
- Header card: posture badges (economic `$ growth/consolidation/survival` + military `⚔ defensive/offensive/neutral`), version + tick on the right, posture summary below.
- **State of the Union** — checklist, one row per category present in `state_of_the_union`. Order: food · defense · welfare · construction · treasury · research; unknown keys appear last.
- **What changed** — `update_notes` rendered italic.
- **Short-term priorities** — section header shows "(N active)". Numbered rank circles on each card. Closed items (completed/deferred) collapse into a `<details>` "N closed this turn" disclosure beneath the active list.
- **Long-term goals** — compact bordered list with status icons (`●` active, `○` deferred, `✓` completed) and a status pill on the right.
- **Minister direction** — only renders when non-empty (M3+).
- Delta badges: `NEW` for priorities added since the player's last view; `UPDATED` for changed items; `DONE` / `DEFERRED` for items that closed.

### Sidebar (Colony telemetry)
- Polls `/api/colony/snapshot` every 5s (configurable via `SnapshotPollMs` constant).
- Groups: Date, People, Mood, Food, Wealth, Power, Threat, Weather, Research.
- Group and row labels can carry decorative emoji icons in the UI. They are marked `aria-hidden` and do not change the telemetry payload or accessible label text.
- Telemetry rows should stay dense: use compact spacing and two-column row grids when the sidebar width allows, falling back to normal wrapping on narrow layouts.
- Warning rows render in red (`telemetry-row.warn`) for: low food (< 7d), downed/sick colonists, break-risk count, negative net power, active raid.
- Footer line shows `briefing v{n} · tick {gameTick}` so the player can correlate sidebar values with the current agenda version.

### Feeder alerts (M3) and tactical alerts (M5)
M3 Alerts is the active feeder memo surface. It renders Food `AdviceItem`s from SSE `event: advice`, replayed on page load by `AdviceBus.ActiveAdvice()`. High/Critical visual treatment exists, but unread badges, tactical-alert stickiness, and Mayor-authored `tactical_alert` advice remain M5+.

Alerts is also a debugging surface. The dashboard should render the advice payload it receives, including imperfect resource requests, instead of silently filtering or rewriting minister output. Quality gates for vague advice belong in rules, prompts, and response normalization.

---

## Modify modal

When the player clicks **Modify** on a memo:
1. Show the memo's `suggested_actions` as an editable list.
2. Player can edit `what` text, delete actions, or add new ones (free text only — no enum picker in MVP).
3. On save, `POST /api/advice/{id}/feedback` with `action: "Modify"` and `modified_actions: [...]`.
4. The memo card updates in place to show the modified version with a "modified by you" badge.

The modified actions are not executed — they're recorded. Refinement reads them as training signal: "for this briefing shape, the player rewrites our advice this way."

---

## Implicit-feedback notes (M2 stub, full in M3+)

Even when the player ignores the buttons, the dashboard contributes by reporting **engagement signals** to Host:
- Was the memo opened/expanded?
- How long was it visible?
- Was the briefing-inspector opened from this memo?

These are weak signals but cheap to collect; documented in [`advice.md`](advice.md) under implicit feedback.

---

## Open questions

- [ ] Should the dashboard auto-open in the player's browser on Host startup, or stay click-to-open from a tray icon / log line?
- [ ] Notification on new memo: in-page only, or OS-level (browser notification API)?
- [ ] Memo expiry UX: hide expired memos, gray them out, or move to a "history" pane?
- [ ] Mobile/small-screen layout — needed?
- [x] Theme: dark command-center console for MVP; no light/dark toggle yet.
