# RimAI — Advisor Dashboard

> **Living document.** See `CLAUDE.md` for update rules.
> Slice: M0 (skeleton) → M1 (memo feed) → M2 (feedback) → M5 (tactical alerts) → M6 (autonomy panel readonly).

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
The advice feed. Server-Sent Events stream of `AdviceItem` JSON payloads.

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

On connect, the server replays the last 24h of unexpired advice items so a fresh page load shows current state. Older advice is fetched on demand from `/api/advice?from=...`.

### `POST /api/advice/{id}/feedback`
Body:
```json
{ "action": "Accept" | "Dismiss" | "Modify",
  "note": "optional player note",
  "modified_actions": [ /* SuggestedAction[], present only when action == Modify */ ] }
```
Writes a `FeedbackEvent` to the decision log. Echoes back over SSE for other open tabs.

### `GET /api/briefings/{minister}/latest`
Returns the most recent `IBriefing` for a minister so the briefing-inspector tab can render the source data behind any memo.

### `GET /api/decisions?minister=&since=`
Returns recent decision-log records (with feedback joined) for the decision-log tab.

### `GET /api/agenda/latest`
Returns the current `MayorAgenda` JSON. Used on page load and by the briefing-inspector.

### `GET /api/agenda/history?since=<tick>`
Returns `MayorAgenda` snapshots since `tick`, one per in-game day. Retained for 30 in-game days.

### `POST /api/agenda/{version}/priority/{rank}/feedback`
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
{ "Mayor": "Suggest", "Agriculture": "Suggest", "Defense": "Suggest" }
```

---

## Layout (MVP)

```
┌──────────────────────────────────────────────────────────────────┐
│  RimAI                                    [colony name + tick]  │
├────────────┬─────────────────────────────────────────────────────┤
│  Tabs      │  Agenda (default)                                   │
│  • Agenda  │  Posture: Consolidation · Defensive                 │
│  • Alerts  │  State of the Union                                  │
│  • Briefing│  Six colonists healthy; mood 72%. Food 18d vs        │
│  • Log     │  winter in 20 — tight. Wealth 85k tracking. East    │
│  • Autonomy│  wall gap untested. Stable, one time-pressure.       │
│            │  ─                                                   │
│            │  What changed                                        │
│            │  "Winter approaching — repositioned food as P1."    │
│            │  ┌──────────────────────────────────────── NEW ──┐  │
│  • Log     │  │ 1 · Food · Establish second growing zone       │  │
│  • Autonomy│  │   before winter                                │  │
│            │  │   · designate growing zone, ~8×8, south of    │  │
│            │  │     kitchen                                    │  │
│            │  │   · raise Plants priority on Hannah and Ben    │  │
│            │  │   [Accept] [Modify] [Dismiss]                  │  │
│            │  └────────────────────────────────────────────────┘  │
│            │  ┌─────────────────────────────────────────────────┐ │
│            │  │ 2 · Defense · Patch east-wall gap ...           │ │
│            │  └─────────────────────────────────────────────────┘ │
│            │  Long-term goals                                     │
│            │  ● winter_prep    active    this quadrum             │
│            │  ○ base_expansion deferred  year 2                   │
└────────────┴─────────────────────────────────────────────────────┘
```

### Tabs (MVP)
- **Agenda** — Mayor's living plan; short-term priorities and long-term goals. **Default tab.** M1 ships this.
- **Alerts** — feeder-minister `AdviceItem`s and Mayor tactical alerts (M5+). Empty in M1; placeholder tab acceptable.
- **Briefing** — pick a minister, see the latest briefing JSON pretty-printed. M1 ships this.
- **Log** — recent decision-log records with feedback events joined. M2 ships this.
- **Autonomy** — per-minister dial. Read-only display in MVP; PUT is wired but every value stays `Suggest`. M2 ships the panel; the dial gets a real switch only at M7.

### Agenda tab detail
- Header: posture badges, then `state_of_the_union` paragraph (the "where we are" briefing), then `update_notes` one-liner (the "what changed" delta).
- Short-term priorities: numbered list of priority cards, each with domain, summary, suggested actions, and feedback buttons.
- Delta badges: `NEW` for priorities added since the player's last view; `UPDATED` for changed items; `DONE` / `DEFERRED` for items that closed (shown for one day, then moved to history).
- Long-term goals: compact list with status badges.
- "Agenda history" link → inline changelog of past versions (if we ship it; see open questions).

### Tactical alerts (M5)
When a `tactical_alert` advice arrives (severity ≥ High), it surfaces in the **Alerts** tab with a distinct visual treatment (top of list, sticky for some interval, severity-colour border). A badge on the Alerts tab nav item signals unread alerts.

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
- [ ] Theme: light/dark toggle vs. follow-system.
