# RimAI — The Mayor's Agenda

> **Living document.** See `CLAUDE.md` for update rules.
> Slice: M1 (Agenda replaces daily_digest as Mayor's advice output) → M3 (minister_direction consumed by feeder ministers) → M5 (tactical alerts join Alerts tab).

---

## What the Agenda is

The Agenda is a **living planning document** the Mayor maintains and updates at the end of every turn (once per in-game day). It is not a one-shot memo — it persists across turns and evolves as the colony's situation changes.

In MVP, the Agenda **is** the Mayor's advice. There is no separate `daily_digest` `AdviceItem`. The player reads the Agenda the way a real executive reads a ranked to-do list: free-text bullets for short-term priorities and long-term goals, plus a one-liner explaining what changed today.

The Agenda is the **interface between the Mayor and the ministers**: it tells feeder ministers (M3+) where the Mayor wants their attention, so the cabinet stays coherent without direct inter-minister messaging.

---

## Schema

Each bullet is a free-text string. The Mayor writes what it wants; there is no structured action-kind catalogue, no domain tag, no deadline field.

```jsonc
{
  "version":              142,          // increments every update
  "updated_in_game_tick": "Y1Q3D12",

  "posture": {
    "economic": "consolidation",        // growth | consolidation | survival
    "military": "defensive",            // defensive | offensive | neutral
    "summary":  "Hold wealth, shore up food before winter. No expansion this quadrum."
  },

  "state_of_the_union": "Six colonists, all healthy; mood is steady at 72%. Food covers 18 days against a winter that arrives in 20 — tight but not red. Wealth has plateaued near 85k; raid points are tracking, not racing. Power is comfortable, the freezer is running, the east wall has a known gap that hasn't been tested. Research is mid-microelectronics. Overall: stable colony with one obvious time-pressure (food vs. winter) and one quiet liability (the wall).",

  "update_notes": "Winter arrives in ~20 days. Moved food to top. Defense is calm — east wall still needs patching but is not urgent.",

  "short_term": [
    { "id": "st_1", "text": "Establish a second growing zone before winter — food covers 18 days, winter arrives in ~20.", "status": "active" },
    { "id": "st_2", "text": "Patch the east-wall gap before the next raid window.", "status": "active" },
    { "id": "st_3", "text": "Review colonist schedules for the cold snap — anyone on long outdoor shifts is a break risk.", "status": "active" }
  ],

  "long_term": [
    { "id": "lt_1", "text": "Complete winter prep (food, heat, schedules) before day 30.", "status": "active" },
    { "id": "lt_2", "text": "Base expansion deferred to year 2, after wealth velocity recovers.", "status": "deferred" }
  ],

  "minister_direction": {
    "Agriculture": "Focus on winter-prep angles: growing adequacy, stockpile, freezer capacity. Hold expansion.",
    "Defense":     "Light posture. Note the east-wall gap; patrol scheduling.",
    "Welfare":     "Watch mood through the cold snap. Flag anyone near a mental break."
  }
}
```

### Field notes

**`version`** — monotonically incrementing integer. The dashboard diffs consecutive versions to show delta badges.

**`state_of_the_union`** — narrative paragraph the Mayor writes describing where the colony stands *right now*: people, food, defense, wealth, mood, research — whatever matters this turn. This is the "where are we" passage; `update_notes` is the "what changed" delta. Target ~100–200 words. Kept fresh each turn even on quiet days.

**`update_notes`** — one or two sentences the Mayor writes explaining what changed since the previous version. The player's daily delta briefing. Kept short (~50–100 words).

**`posture`** — structured strategic stance. Ministers read this in their briefing context; it frames the Agenda display header in the dashboard.

**`short_term[].id`** — stable across versions for continuing items. The Mayor's system prompt instructs it to reuse IDs for bullets it carries forward unchanged or with minor edits, and to assign new IDs for genuinely new bullets. This is how the dashboard detects NEW vs UPDATED vs carried-over items.

**`short_term[].status`** — `active | completed | deferred`. The Mayor marks items `completed` when the goal is achieved, `deferred` when pushed or dropped. Completed and deferred bullets stay in the document for one day (so the player sees the change), then move to history.

**`long_term[].status`** — same enum as `short_term`.

**`minister_direction`** — only present for active ministers (M3+). Absent in M1. Plain-English direction injected into each minister's LLM prompt as a prefix. Not parsed by code.

---

## What the Mayor does at turn-end

At the end of each in-game day, the Mayor's LLM call receives:
- Current `MayorAgenda` (the document to update — passed back in full)
- The daily digest inputs (briefing, CoS flags, trend windows — see [`ministers/mayor.md`](ministers/mayor.md))

The LLM outputs a **new complete `MayorAgenda`** — not a diff, just the updated document. The server increments `version`, stores the old version in history, and broadcasts the new one.

The Mayor's turn is the only thing that mutates the Agenda. Ministers never write to it.

---

## Relationship to `AdviceItem`

| Concept | What it is | Who produces it | When |
|---|---|---|---|
| `MayorAgenda` | Living plan; free-text bullets | Mayor | Updated once/day |
| `AdviceItem` | Ephemeral tactical item | Feeder ministers (M3+) | On trigger |
| `tactical_alert` (M5+) | Urgent one-shot alert | Mayor or feeder | On severity spike |

In MVP (M1), only the Agenda exists. The `AdviceItem` schema is preserved as the feeder-minister output format, ready for M3.

The `AdviceBus` gains a new event type: `agenda_update`. The dashboard subscribes to this alongside `advice` events.

---

## Feedback model

Feedback attaches to **individual bullets** (short-term and long-term items), not to the Agenda document as a whole.

The three feedback actions are:
- **Accept** — "I'll act on this."
- **Dismiss** — "I'm not doing this." Note recommended; dismissal-with-note is the highest-value training signal.
- **Modify** — "Almost right, but here's my version." Player edits the bullet text in a modal; the modified text is recorded as `modified_text` on the `FeedbackEvent`.

There is no implicit feedback for Agenda items. Feedback is explicit only.

```jsonc
// FeedbackEvent for an agenda bullet
{
  "source":          "agenda_item",
  "agenda_version":  142,
  "item_id":         "st_1",
  "action":          "Modify",
  "note":            "already have a freezer zone, the issue is harvest timing",
  "modified_text":   "Set growing zone harvest threshold lower so crops don't rot — harvest timing, not new zones.",
  "issued_at":       "2026-05-03T14:08:23Z",
  "issued_in_game_tick": "Y1Q3D12H07"
}
```

Refinement clusters these the same way it clusters `AdviceItem` feedback: dismiss patterns → rule to suppress that bullet shape; modify patterns → rule to produce the modified form directly.

---

## Dashboard: Agenda tab

The Agenda tab is the **primary** dashboard tab in MVP (replaces "Memos").

```
┌──────────────────────────────────────────────────────────────────┐
│  Agenda                        Y1 Q3 D12 · Updated 2 mins ago   │
│  Consolidation · Defensive                                        │
├──────────────────────────────────────────────────────────────────┤
│  State of the Union                                               │
│  Six colonists, all healthy; mood steady at 72%. Food covers 18 │
│  days against a winter in 20 — tight but not red. Wealth ~85k,  │
│  raid points tracking. Power comfortable, freezer running, east │
│  wall has a known gap. Research mid-microelectronics. Stable    │
│  with one time-pressure and one quiet liability.                │
├──────────────────────────────────────────────────────────────────┤
│  What changed                                                     │
│  "Winter arrives in ~20 days. Moved food to top."               │
├──────────────────────────────────────────────────────────────────┤
│  Short-term                                                       │
│  ┌──────────────────────────────────────────────────── NEW ────┐ │
│  │ · Establish a second growing zone before winter — food       │ │
│  │   covers 18 days, winter arrives in ~20.                     │ │
│  │   [Accept] [Modify] [Dismiss]                                │ │
│  └──────────────────────────────────────────────────────────────┘ │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ · Patch the east-wall gap before the next raid window.       │ │
│  │   [Accept] [Modify] [Dismiss]                                │ │
│  └─────────────────────────────────────────────────────────────┘ │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ · Review colonist schedules for the cold snap.              │ │
│  │   [Accept] [Modify] [Dismiss]                                │ │
│  └─────────────────────────────────────────────────────────────┘ │
├──────────────────────────────────────────────────────────────────┤
│  Long-term                                                        │
│  ● Complete winter prep (food, heat, schedules) before day 30.   │
│  ○ Base expansion deferred to year 2.                             │
└──────────────────────────────────────────────────────────────────┘
```

Delta badges on short-term bullets: `NEW` for new IDs; `UPDATED` for carried-forward IDs with changed text; `DONE` / `DEFERRED` for IDs that exited (shown one day then moved to history).

Long-term items display status as icon: `●` active, `○` deferred, `✓` completed. No feedback buttons on long-term items in MVP — they are informational only.

---

## Minister interface (M3+)

When a feeder minister's rules layer or LLM prompt runs, it receives from the briefing context:

```csharp
public class MinisterBriefingContext
{
    public MayorPosture Posture         { get; }  // economic + military stance
    public string?      AgendaDirection { get; }  // minister_direction[ministerName], null in M1
}
```

`AgendaDirection` is injected as a prompt prefix: "The Mayor's current guidance for your domain: `{AgendaDirection}`." The minister may choose not to surface issues that are absent from or actively deprioritised in the Agenda.

This is a read-only broadcast — no callback, no acknowledgment.

---

## API contract

### New SSE event on `GET /api/advice/stream`

```
event: agenda_update
id: <version>
data: { ...full MayorAgenda }
```

The `data` is the full `MayorAgenda` JSON — `version`, `updated_in_game_tick`, `update_notes`, `posture`, `state_of_the_union`, `short_term`, `long_term`, `minister_direction`. SSE `id` mirrors `version` so EventSource resume works.

On connect, the server replays the current Agenda as a single `agenda_update` event before the live feed begins. If no Agenda exists yet (Host just started, no day has rolled), the connection stays open and the dashboard sees its first `agenda_update` when the Mayor's first turn fires.

The server also sends `event: ping\ndata: {}\n\n` every 15 seconds when idle to keep the connection alive through proxies.

### `GET /api/agenda/latest`
Returns the current `MayorAgenda` JSON.

### `GET /api/agenda/history?since=<tick>`
Returns `MayorAgenda` snapshots since `tick`, one per in-game day. Retained for 30 in-game days; older history archived to decision log.

### `POST /api/agenda/{version}/item/{id}/feedback`
Body: same `FeedbackEvent` shape as `/api/advice/{id}/feedback`, with `source: "agenda_item"`, `agenda_version`, `item_id`, and (for Modify) `modified_text` instead of `modified_actions`.

---

## Open questions

- [ ] Should short-term bullets be capped (e.g. 5)? A hard cap forces the Mayor to rank and cut rather than surfacing everything.
- [ ] Should the player be able to manually mark a short-term bullet complete mid-day, or does only the Mayor mark completion on the next turn?
- [ ] Long-term items: feedback buttons in a future milestone, or permanently informational?
- [ ] `minister_direction` is free-text — sufficient for prompt-inject in M3, but if the rules layer needs to parse direction cheaply, may need lightweight structure later.
