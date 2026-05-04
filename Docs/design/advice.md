# RimAI — Advice: Schema, Lifecycle, Feedback

> **Living document.** See `CLAUDE.md` for update rules.
> Slice: M1 (schema + emission) → M2 (feedback lifecycle) → M3+ (implicit-feedback inference).

---

## Why this doc exists

Under the assisted-gameplay pivot ([`../DESIGN.md`](../DESIGN.md)), every minister's output is an `AdviceItem`, and the player's reaction is the primary training signal. This doc defines:

1. The `AdviceItem` schema.
2. The Accept / Dismiss / Modify lifecycle.
3. The implicit-feedback strategy (state-watching when the player doesn't click anything).
4. How both feedback channels feed back into [`evaluation.md`](evaluation.md) refinement.
5. The autonomy dial — present in the type system from MVP, only `Suggest` honored until M7.

---

## `AdviceItem` schema

```jsonc
{
  "id":              "advice_2026-05-03T14:02:11Z_mayor_001",
  "minister":        "Mayor",
  "advice_type":     "daily_digest",          // closed enum, per minister
  "severity":        "Medium",                // Low | Medium | High | Critical
  "title":           "End of Day 12 — Food window closing, defense slack",
  "body":            "Markdown body. 2-6 short paragraphs.",
  "rationale":       "Why these were the most important items today.",
  "suggested_actions": [
    { "kind": "designate_zone", "what": "growing zone, ~8x8, fertile soil south of kitchen" },
    { "kind": "build",          "what": "two more sandbag sections covering the east approach" }
  ],
  "citations":       ["strategic-plan-y1-y2.md#fall-checklist"],
  "briefing_ref":    { "minister": "Mayor", "version": 142, "hash": "sha256:..." },
  "issued_at":       "2026-05-03T14:02:11Z",
  "issued_in_game_tick": "Y1Q3D12H06",
  "expires_at":      "2026-05-04T14:02:11Z",
  "expires_on_game_state": null,              // optional predicate; see "Expiry" below
  "supersedes":      null,                    // id of previous advice this replaces, if any
  "autonomy_at_issue": "Suggest"              // value of the dial when this was emitted
}
```

### `severity`
Mirrors `FlagSeverity` in [`communication.md`](communication.md). Drives dashboard visual treatment and (post-M5) whether the advice surfaces as a tactical-alert outside the daily cadence.

### `advice_type`
Closed enum **per minister.** This is the unit the future autonomy dial graduates one at a time. E.g. Agriculture's `food_security`, `harvest_now`, `expand_zone`, `hunt`, `trade_food_surplus`. Adding a new `advice_type` is a deliberate design step (matches "adding a new HTN compound" in the deferred world).

### `suggested_actions[].kind`
Closed enum **across all ministers**. Each `kind` is a category that an `Auto`-graduated minister will eventually wire to an HTN primitive. MVP catalogue:

| Kind | Meaning (MVP: text-only) |
|---|---|
| `designate_zone` | Create / resize / move a zone (growing, stockpile, dumping, ...) |
| `build` | Place a blueprint (structure, furniture, defense). |
| `set_priority` | Adjust per-pawn work priorities. |
| `set_schedule` | Adjust per-pawn schedule slots. |
| `draft` | Draft / undraft a colonist or group. |
| `forbid` | Forbid / unforbid items or buildings. |
| `research` | Set or change the research target. |
| `trade` | Initiate / accept a trade or caravan. |
| `note` | Free-form advisory text with no structured kind (use sparingly). |

Add new kinds by extending this table — every addition makes future Auto graduations possible for that category.

### `briefing_ref`
The exact briefing snapshot the advice was based on. The dashboard's briefing-inspector tab uses this to show the player the source data behind a memo. Refinement uses it to reproduce escalations.

### `expires_at` / `expires_on_game_state`
- `expires_at` — wall-clock TTL, default 24h for daily memos, 4h for tactical alerts.
- `expires_on_game_state` — optional structured predicate over briefing fields. E.g. `{ "field": "ActiveThreats", "op": "==", "value": false }` makes a raid-response alert auto-archive once the raid resolves. M5+.

A memo about a raid that already happened must auto-archive — staleness is corrosive to trust.

### `supersedes`
When an advisor replaces an earlier still-active advice (e.g. a midday revision of the morning's defense alert), `supersedes` carries the old id. The dashboard collapses the chain into the newest version with the older versions in a history sub-view.

### `autonomy_at_issue`
The autonomy mode the issuing minister was in when the advice was emitted. Always `Suggest` in MVP. Recorded so future `Auto`-mode advice items can be distinguished in the log.

---

## Lifecycle

```
        ┌────────────────────────────────────────────────────┐
        │                                                    │
emit ──►Active──► Accepted   ─────────► Archived (kept in log)
        │   │                                                
        │   ├──►  Dismissed  ─────────► Archived
        │   │                                                
        │   ├──►  Modified   ─────────► Archived (with diff)
        │   │                                                
        │   ├──►  Superseded ─────────► Archived (link to successor)
        │   │                                                
        │   └──►  Expired    ─────────► Archived (no explicit feedback)
        │                                                    
        └─► flagged for refinement when the right cluster forms
```

State transitions are emitted as `FeedbackEvent`s and written to the decision log. Archived advice stays queryable indefinitely.

---

## `FeedbackEvent` schema

```jsonc
{
  "advice_id":       "advice_...",
  "action":          "Accept | Dismiss | Modify | Expired | Superseded",
  "note":            "optional player-written note",
  "modified_actions": [ /* SuggestedAction[]; only when action == Modify */ ],
  "issued_at":       "2026-05-03T14:08:23Z",
  "issued_in_game_tick": "Y1Q3D12H07",
  "implicit_signals": {                  // populated even on Accept/Dismiss; see below
    "memo_opened": true,
    "memo_visible_seconds": 47,
    "briefing_inspected": false
  }
}
```

---

## Explicit feedback (Accept / Dismiss / Modify)

Surfaced in the dashboard as three buttons on every memo card.

- **Accept** — "I read this and I'll act on it." Optional note.
- **Dismiss** — "I read this and I'm not acting on it." Note recommended; dismissal-with-note is the highest-value training signal because it tells the system *why* the advice was wrong.
- **Modify** — "Mostly right, but with these changes." Player edits `suggested_actions` text in a modal; edited actions are recorded in `modified_actions`.

The modified actions are **not executed** in MVP. They become input to refinement: when the same advice type is consistently modified the same way, refinement proposes a rule change that produces the modified shape directly.

---

## Implicit feedback (state-diff fallback)

When the player ignores the buttons (the common case for many advice items), we infer engagement from game state.

### Strategy

For each emitted advice item:

1. **Snapshot relevant briefing fields at issuance.** "Relevant" = the fields its `suggested_actions` would plausibly affect, mapped per `kind`:
   - `designate_zone` → zone counts and tiles by type
   - `build` → building counts by def
   - `set_priority` → per-pawn work priority for the work types named
   - `draft` → drafted-pawn ids
   - etc.
2. **At advice expiry, snapshot the same fields.**
3. **Score overlap.** If post-state moved in the direction the advice suggested, infer "followed (likely)." If it moved opposite, "rejected (likely)." If it didn't move, "ignored."
4. **Record as a low-confidence `FeedbackEvent`** with `action: "Expired"` and an `implicit_signals.inferred` block.

### Limitations (acknowledged, not solved)

- Confounding: the player may take an action for unrelated reasons.
- Resolution: many `kind`s are too coarse to map cleanly (a generic "build sandbags" advice can't easily be matched to which sandbag the player placed).
- Timing: the player may act *days* after a memo, beyond its expiry.

These are real but tolerable for a *fallback* signal. The explicit signal is the load-bearing one.

### MVP stub (M2)

Just snapshot + diff a small whitelist of fields (food zones, total wealth, drafted-pawn count). Score is binary: "moved in advice direction" / "didn't." Full per-`kind` mapping comes M3+ as feeder ministers come online.

---

## How feedback feeds refinement

[`evaluation.md`](evaluation.md) Loop 1 (rule promotion) gains a new clustering dimension: **same `advice_type` + same explicit feedback action.**

Examples:
- 8 `expand_zone` advice items all Accepted with no modification → strong promotion candidate; the rule that would have produced them can be promoted.
- 6 `hunt` advice items all Dismissed with notes mentioning "winter, nothing to hunt" → the rule that should *suppress* `hunt` advice in winter is the candidate.
- 5 `food_security` advice items all Modified to add a "build a freezer" action → the rule (or prompt) should learn to include freezer suggestions when food + summer + no freezer.

Implicit "ignored" signals are weighted lower than explicit Dismiss; "followed (likely)" is weighted lower than explicit Accept. Confidence thresholds for promotion are set in [`evaluation.md`](evaluation.md) approval gates.

---

## Autonomy dial

```csharp
public enum AutonomyMode { Off, Suggest, Auto }

// Persisted per (Minister, AdviceType) pair, defaulting to Suggest.
public record AutonomySetting(string Minister, string AdviceType, AutonomyMode Mode);
```

- **Off** — minister doesn't emit this advice type at all. Useful for muting noisy advisors.
- **Suggest** *(MVP default)* — emits `AdviceItem`s; player decides.
- **Auto** *(M7+)* — emits `AdviceItem`s **and** decomposes them via the (re-engaged) HTN planner into RIMAPI writes. The player still sees the memo, but it appears with an "auto-applied" badge. Player can revert to `Suggest` per-pair at any time.

In MVP the only valid value is `Suggest`. The dashboard's autonomy panel shows the dial as informational; PUT is accepted but values other than `Suggest` are clamped back. The contract exists so M7 doesn't reshape it.

---

## Open questions

- [ ] How should we surface low-confidence implicit feedback in the dashboard so the player can correct it? (E.g. "We thought you acted on yesterday's food memo — was that right?" prompts.)
- [ ] Should `Modify` create a *new* advice item (versioned successor) or just attach the modification to the original?
- [ ] How do we handle conflicting advice from two ministers in the same memo cycle? (CoS arbitrates pre-Mayor, but for tactical alerts CoS hasn't run yet.)
- [ ] Per-minister advice-type catalogues — define exact enums per minister in their respective docs.
- [ ] When a memo references a colonist who dies before expiry, should the memo auto-archive? (Probably yes; encode as `expires_on_game_state`.)
- [ ] Granularity of `kind`: too coarse loses Auto-mapping fidelity, too fine bloats the catalogue. Calibrate as feeders come online.
