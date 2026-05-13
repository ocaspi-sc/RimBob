# RimAI — Advice: Schema, Lifecycle, Feedback

> **Living document.** See `CLAUDE.md` for update rules.
> Slice: M1 (schema + emission) → M5 (Accept / Dismiss / Pushback lifecycle, minister-owned pushback lists) → M6 (refinement consumes pushbacks).
>
> **Roadmap re-eval (2026-05-08):** Feedback was M2; it's now M5 — landing just before M6 consumes it. RAG (was M4) is now M2 because the Mayor's output needs to be worth reacting to before we build UI on top of it. **Modify** is renamed **Pushback**: the player tells the minister *why he's wrong* in natural language, instead of editing `suggested_actions` text. **Implicit-feedback inference is dropped from the MVP** — the explicit Pushback channel is load-bearing; implicit signals were always a fallback and the cost wasn't justified.

---

## Why this doc exists

Under the assisted-gameplay pivot ([`../DESIGN.md`](../DESIGN.md)), every minister's output is an `AdviceItem`, and the player's reaction is the primary training signal. This doc defines:

1. The `AdviceItem` schema.
2. The Accept / Dismiss / Pushback lifecycle.
3. The minister-owned pushback list — each minister persists its own corrections, scoped to itself.
4. How pushbacks feed back into [`evaluation.md`](evaluation.md) refinement.
5. The autonomy dial — present in the type system from MVP, only `Suggest` honored until M7.

---

## `AdviceItem` schema

```jsonc
{
  "id":              "advice_2026-05-03T14:02:11Z_mayor_001",
  "minister":        "Mayor",
  "advice_type":     "daily_digest",          // closed enum, per minister
  "severity":        "medium",                // low | medium | high | critical
  "priority_score":  7,                       // 1-10, first-class ranking within/alongside severity
  "title":           "End of Day 12 — Food window closing, defense slack",
  "body":            "Markdown body. 2-6 short paragraphs.",
  "rationale":       "Why these were the most important items today.",
  "resource_requests": [
    { "kind": "labor", "what": "2 cooking-capable colonists", "why": "meal stock below 2 days", "quantity": 2, "priority": "high", "work_type": "Cook", "skill": "Cooking", "requested_from": "Labor" },
    { "kind": "building", "what": "1 cooler", "why": "freezer warming above safe temperature" }
  ],
  "suggested_actions": [
    { "kind": "designate_zone", "what": "growing zone, ~8x8, fertile soil south of kitchen" },
    { "kind": "build",          "what": "two more sandbag sections covering the east approach" }
  ],
  "guide_citations":       ["strategic-plan-y1-y2.md#fall-checklist"],
  "briefing_ref":    { "minister": "Mayor", "version": 142, "hash": "sha256:..." },
  "issued_at":       "2026-05-03T14:02:11Z",
  "issued_in_game_tick": "Y1Q3D12H06",
  "expires_at":      "2026-05-04T14:02:11Z",
  "expires_on_game_state": null,              // optional predicate; see "Expiry" below
  "supersedes":      null,                    // id of previous advice this replaces, if any
  "autonomy_at_issue": "suggest"              // value of the dial when this was emitted
}
```

### `severity`
Mirrors `FlagSeverity` in [`communication.md`](communication.md). Severity is semantic routing, not only UI decoration: the Mayor and CoS use it when deciding what enters the digest, what interrupts the player, and what can preempt other priorities. Rules may compute severity dynamically from live state.

### `priority_score`
Integer 1-10. This is a first-class output on every `AdviceItem`, used to rank advice within and across severity tiers. `severity` answers "how dangerous or interruptive is this?" while `priority_score` answers "how high should the player put this on today's list?" A High/6 can be urgent but bounded; a Medium/9 can be very important but not preemptive.

### `advice_type`
Closed enum **per minister.** This is the unit the future autonomy dial graduates one at a time. E.g. Food's `food_security`, `harvest_now`, `expand_zone`, `hunt`, `trade_food_surplus`. Adding a new `advice_type` is a deliberate design step (matches "adding a new HTN compound" in the deferred world).

### `resource_requests[].kind`
Closed enum across all ministers. A resource request states what the minister needs in order to resolve the problem it is describing; it is not itself an executed action. MVP draft catalogue:

| Kind | Meaning |
|---|---|
| `labor` | More pawn time/capacity in a work area or skill band. |
| `tile` | Reserved map area, zone footprint, or placement space. |
| `item` | Consumable or held item (medicine, components, shells, textiles, etc.). |
| `building` | A built asset or workstation (cooler, hospital bed, fabrication bench, turret, etc.). |
| `bill` | A production/configuration bill or stock target. |
| `stockpile_space` | Filtered storage capacity in a relevant zone. |
| `attention` | Priority from another subsystem or from the player. |
| `trade_capacity` | Buying/selling/caravan bandwidth. |

Each request carries `kind`, `what`, and short `why` text. It may also carry `quantity`, `priority`, `requested_from`, `work_type`, and `skill` when the emitter can state them cleanly. These fields are still advisory in MVP: they describe need, not allocation.

Labor requests must name a RimWorld work-tab type when possible. "Labor capacity" is not acceptable output by itself; use concrete phrasing such as `work_type: "Cook"`, `skill: "Cooking"` or `work_type: "PlantCut"`, `skill: "Plants"`.

### `suggested_actions[].kind`
Closed enum **across all ministers**. Each `kind` is a category that an `Auto`-graduated minister will eventually wire to an HTN primitive. These are the outward "do X" recommendations, distinct from `resource_requests[]` which describe prerequisites or needs. MVP catalogue:

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

For M3 feeder alerts, rules should prefer stable same-issue ids when there is no explicit history chain yet. Re-emitting `food_emergency_food_flag` replaces the active card for that issue instead of creating another duplicate alert. Later decision-log work can preserve each emission while the active dashboard remains deduplicated.

### `autonomy_at_issue`
The autonomy mode the issuing minister was in when the advice was emitted. Always `Suggest` in MVP. Recorded so future `Auto`-mode advice items can be distinguished in the log.

---

## Lifecycle

```
        ┌────────────────────────────────────────────────────┐
        │                                                    │
emit ──►Active──► Accepted   ─────────► Archived
        │   │                                                
        │   ├──►  Dismissed  ─────────► Archived
        │   │                                                
        │   ├──►  Pushback   ─────────► Archived + appended to issuing
        │   │                          minister's pushback list
        │   ├──►  Superseded ─────────► Archived (link to successor)
        │   │                                                
        │   └──►  Expired    ─────────► Archived (no explicit feedback)
        │                                                    
        └─► clustered by the issuing minister at refinement time (M6)
```

State transitions are emitted as `FeedbackEvent`s. Pushbacks additionally append to the **issuing minister's own pushback list** (see below). Archived advice stays queryable indefinitely.

---

## `FeedbackEvent` schema

```jsonc
{
  "advice_id":       "advice_...",
  "minister":        "Mayor",                  // issuing minister; pushbacks route to its list
  "action":          "Accept | Dismiss | Pushback | Expired | Superseded",
  "note":            "optional short note (Accept/Dismiss)",
  "pushback_text":   "natural-language explanation of why the minister was wrong",
  "issued_at":       "2026-05-03T14:08:23Z",
  "issued_in_game_tick": "Y1Q3D12H07"
}
```

`pushback_text` is only present when `action == "Pushback"`. `note` and `pushback_text` are mutually exclusive — use whichever the action requires.

---

## Explicit feedback (Accept / Dismiss / Pushback)

Surfaced in the dashboard as three buttons on every memo / agenda item.

- **Accept** — "I read this and I'll act on it." Optional short note.
- **Dismiss** — "I read this and I'm not acting on it." Optional short note.
- **Pushback** — "You're wrong, and here's why." Free-text natural-language explanation entered in a modal. The textarea prompt is literally *"Tell the minister why he's wrong."* This replaces the older "Modify" action — instead of asking the player to edit suggested-action text, we ask them to teach the minister.

Pushback is the highest-value signal in the system. It's the player's correction in their own words; refinement reads pushbacks to find recurring themes and propose rule changes.

---

## Minister-owned pushback list

Each minister **owns and persists its own pushback list**. Pushbacks are scoped — the Mayor doesn't see Food's pushbacks; Food doesn't see Defense's. Each minister learns from its own corrections.

### Storage

- Location: `Src/Cabinet/<Minister>/Pushbacks/` (durable, kept across sessions).
- Format: append-only JSON-lines file, one entry per pushback, with the originating `advice_id`, full advice payload reference (so the minister can see what it said), the player's `pushback_text`, and the in-game tick.
- Retention: indefinite. Pushbacks are training data — pruning is a deliberate decision (post-M6).

### Two roles

1. **Inline correction (M5)** — the last N pushbacks (capped by age + relevance) are injected into the issuing minister's next prompt as a "recent player corrections" section. The minister reads them as part of its context and adjusts subsequent advice. No code change required to benefit; the LLM does the lifting.
2. **Refinement input (M6)** — the full pushback list is the corpus that the refinement loop clusters over to propose `Rules.cs` changes. See [`evaluation.md`](evaluation.md).

### Why scoped to one minister

- Encapsulation: each minister is responsible for its own learning. Mixing pushbacks across ministers would require routing logic that hasn't earned its keep.
- Prompt budget: only the issuing minister needs its own corrections in-context.
- Refinement clarity: clustering is simpler when the corpus is homogeneous.

If a pushback is ever relevant to multiple ministers, the player can pushback again on the relevant memo when each minister surfaces it. We do not de-duplicate across ministers.

---

## Implicit feedback — dropped from MVP

> *Removed in the 2026-05-08 re-eval.* The earlier plan included a state-diff implicit-feedback collector (snapshot relevant briefing fields at issuance, diff at expiry, score overlap with `suggested_actions`). It's been cut: the explicit Pushback channel is load-bearing, implicit signals were always a fallback, and the kind-to-field mapping was speculative. If a need re-emerges post-M6 we'll add it then with real data on what's missing.

---

## How feedback feeds refinement

[`evaluation.md`](evaluation.md) Loop 1 (rule promotion) clusters over a single minister's **pushback list**:

- 6 pushbacks on `hunt` advice in winter all saying variations of "nothing to hunt this season" → candidate rule: suppress `hunt` advice when `Season == Winter && WildAnimalCount < threshold`.
- 5 pushbacks on `food_security` advice all mentioning "we already have a freezer" → candidate prompt change: include `HasFreezer` in the briefing and tell the minister not to suggest one when present.
- Accept-without-note streaks reinforce that the rule that produced them is working — used to *confirm* a rule rather than promote a new one.

Confidence thresholds for promotion are set in [`evaluation.md`](evaluation.md) approval gates.

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

- [ ] Pushback selection for the prompt: how do we cap "last N corrections" — by age, by relevance to the current briefing fields, by token budget, or a mix? Calibrate once we have real pushbacks.
- [ ] Pushback expiry / pruning: do pushbacks ever lose relevance (e.g. a winter-specific correction once it's spring), or do they always go in? Assume always-in until M6 shows otherwise.
- [ ] How do we handle conflicting advice from two ministers in the same memo cycle? (CoS arbitrates pre-Mayor, but for tactical alerts CoS hasn't run yet.)
- [ ] Per-minister advice-type catalogues — define exact enums per minister in their respective docs.
- [ ] When a memo references a colonist who dies before expiry, should the memo auto-archive? (Probably yes; encode as `expires_on_game_state`.)
- [ ] Granularity of `kind`: too coarse loses Auto-mapping fidelity, too fine bloats the catalogue. Calibrate as feeders come online.
