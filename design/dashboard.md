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

### `GET /api/autonomy` / `PUT /api/autonomy`
Reads/writes the per-minister autonomy dial. In MVP, every value is `Suggest` and `PUT` is a no-op that returns 200; the endpoint exists so the dashboard can render the panel and the contract is set for M7.

```json
{ "Mayor": "Suggest", "Agriculture": "Suggest", "Defense": "Suggest" }
```

---

## Layout (MVP)

```
┌────────────────────────────────────────────────────────────────┐
│  RimAI                                  [colony name + tick]  │
├────────────┬───────────────────────────────────────────────────┤
│  Tabs      │  Memo Feed (default)                              │
│  • Memos   │   ┌─────────────────────────────────────────────┐ │
│  • Briefing│   │ End of Day 12 — Food window closing         │ │
│  • Log     │   │ Mayor · Medium · 14:02                      │ │
│  • Autonomy│   │ <body, markdown>                            │ │
│            │   │ Suggested:                                   │ │
│            │   │  • designate growing zone south of kitchen   │ │
│            │   │  • raise Plants priority                     │ │
│            │   │ [Accept] [Modify] [Dismiss]                  │ │
│            │   └─────────────────────────────────────────────┘ │
│            │   ┌── older memo ──────────────────────────────┐ │
│            │   │ ...                                         │ │
└────────────┴───────────────────────────────────────────────────┘
```

### Tabs (MVP)
- **Memos** — feed, newest first. M1 ships this.
- **Briefing** — pick a minister, see the latest briefing JSON pretty-printed. M1 ships this.
- **Log** — recent decision-log records with feedback events joined. M2 ships this.
- **Autonomy** — per-minister dial. Read-only display in MVP; PUT is wired but every value stays `Suggest`. M2 ships the panel; the dial gets a real switch only at M7.

### Tactical alerts (M5)
When a `tactical_alert` advice arrives (severity ≥ High), the memo feed surfaces it with a distinct visual treatment (top of feed, sticky for some interval, severity-color border). It does not interrupt the daily-digest cadence; it sits alongside.

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
