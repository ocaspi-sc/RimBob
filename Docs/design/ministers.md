# RimAI — Minister Design

> **Living document.** See `CLAUDE.md` for update rules.

---

## The universal minister shape

Every minister — from Labor to Mayor — has the same structure. What differs is the escalation rate and the domain.

```
Minister
├── Briefing          focused state snapshot, ~500 tokens, computed by state store
├── Rules layer       deterministic code; handles majority of decisions
├── LLM call          judgment; invoked only on escalation
├── Decision log      every decision recorded with inputs, path, outcome
└── Refinement        same minister, different context; reviews log, promotes rules
```

### Two operational modes

**Play mode** (live game, suggest-only):
```
Briefing version changes OR relevant flag fires OR scheduled wakeup fires
  → Rules.Evaluate(briefing)
  → RulesResult.Decision(advice, flags)              →  emit to AdviceBus + flag channel
  → RulesResult.Decision(... ScheduledWakeup = ...)  →  also registers a future wakeup
  → RulesResult.Escalate(reason)                     →  LLM call → emit to AdviceBus + flag channel
```

**Wakeup triggers:** `BriefingChanged` | `FlagFired(flag)` | `Heartbeat` | `ScheduledWakeupFired(payload)`

The Orchestrator passes the trigger into `RunPlayCycle` so ministers can inspect why they were woken (e.g. a rules branch that only runs on `ScheduledWakeupFired`).

> **Deferred (Auto epic):** when a minister graduates to `Auto` for some advice type, that decision path additionally produces HTN goals which feed the planner / Labor / RIMAPI writes. Until then, output is `AdviceItem`s only.

**Refinement** (async, between sessions or on-demand):
```
RunRefinement()
  → Read own pushback list (Src/Cabinet/<Minister>/Pushbacks/)
  → Read own escalation history (rule misfires, LLM call patterns)
  → Cluster pushbacks by theme (recurring corrections in the player's words)
  → Propose Rules.cs / prompt changes (code-gen with tools)
  → Run proposals against fixture suite
  → Gate on approval (human in v1; auto above confidence threshold post-MVP)
  → Promote approved changes to Rules.cs  ← "rule promotion"
```

Each minister **owns its own pushback list** — the player's natural-language explanations of why that minister was wrong. Pushbacks are scoped: the Mayor doesn't see Agriculture's pushbacks. They flow into the issuing minister's next prompt as "recent player corrections" (M5) and serve as the refinement corpus (M6). Implicit state-diff is *not* part of MVP — see [`advice.md`](advice.md).

Refinement IS the minister. Not a separate agent — the same minister in a different mode, with different tools available (code read/write, fixture runner) and different context (its pushback list instead of a briefing).

---

## IMinisterRules — the shared interface

All ministers implement the same interface. This is the contract refinement works against, the test harness targets, and the planner depends on.

```csharp
public interface IMinisterRules<TBriefing>
{
    RulesResult Evaluate(TBriefing briefing, ColonyContext context);
}

public abstract class RulesResult { }

public class Decision : RulesResult
{
    public List<AdviceItem>   Advice          { get; init; }  // see design/advice.md
    public List<AgentFlag>    Flags           { get; init; }
    public string             Trace           { get; init; }  // which rule fired
    public ScheduledWakeup?   ScheduledWakeup { get; init; }  // optional; see below
}

public class Escalate : RulesResult
{
    public string Reason       { get; init; }  // why rules couldn't decide
    public object Context      { get; init; }  // additional context for LLM
}
```

Same shape. Different implementations per minister.

---

## LLM output schema

When the LLM is called (escalation path), it returns one or more `AdviceItem`s:

```jsonc
{
  "advice": [
    {
      "advice_type": "food_security",   // closed enum, per minister
      "severity":    "High",            // Low | Medium | High | Critical
      "title":       "Food situation tightening — start a second growing zone",
      "body":        "Days-of-food has dropped to 22 from 31 yesterday...",
      "rationale":   "rice matures in 8d, cold snap in 14d",
      "suggested_actions": [
        { "kind": "designate_zone", "what": "growing zone, ~8x8, fertile soil south of kitchen" },
        { "kind": "set_priority",   "what": "raise Plants priority for Hannah and Ben" }
      ],
      "expires_in_in_game_hours": 24
    }
  ],
  "flags": [ /* AgentFlag[] */ ],
  "scheduled_wakeup": {              // optional; omit or null = no wakeup
    "fire_in_hours": 192,            // real-time hours (8 in-game days ≈ 192h at default speed)
    "payload": "crop_maturity_check" // opaque string; passed back verbatim when wakeup fires
  },
  "notes": "free-form rationale, logged not parsed in v1"
}
```

Rules for the LLM:
- `advice_type` is a **closed enum per minister**. The LLM picks from the list; no free-form advice types. (This is the unit the future autonomy dial graduates one at a time.)
- `suggested_actions` are advisory text — they are *not* executed in MVP, only rendered. Their `kind` is a closed enum so future Auto graduation can wire each kind to an HTN primitive.
- The `notes` field is the upgrade seam. When a note pattern repeats and the LLM consistently writes the same advice off it, refinement promotes it into a rule.
- Minister LLMs never name colonists, specify blueprints, or choose methods. Those would matter under Auto; under Suggest, the player decides.
- **`scheduled_wakeup` rules:** at most one pending wakeup per minister. A newer emission supersedes the older (logged as a supersession event). The payload is an opaque string — logged but never parsed by the system. It is the minister's note to itself. `fire_in_hours` is real-time; tick-mapping to in-game speed is deferred. When a wakeup fires, the minister runs its normal evaluation cycle (rules first); the payload arrives in the `WakeupTrigger` and is available to rules that choose to inspect it. A wakeup does not bypass the rules layer. Rules may also emit a `ScheduledWakeup` directly from `Decision` without escalating.
- Use structured output (bullets, key-value)
- Use emojis

See [`advice.md`](advice.md) for the full `AdviceItem` schema and feedback lifecycle.

---

## The rules layer in practice

The rules layer is a C# class per minister. Pure, no I/O, deterministic.

**It should handle the common case.** Escalation rate varies by minister — Labor and Agriculture are mostly mechanical, Mayor and CoS are mostly judgment. Targets get calibrated per minister once it's shipped; the improve loop drives them down over time.

**Authoring rules:** write the most important rule first (the one that handles the highest-frequency case), then the second most important, etc. The Evaluate method tries rules in priority order and returns on first match. An unmatched briefing escalates.

**Keeping rules honest:** every rule has a name (string constant). Decision log records which rule fired. Bad outcomes with the same rule name → that rule is a candidate for revision.

---

## Escalation triggers

Rules should escalate when:
- No rule matches the current briefing state
- Competing goals are within a small margin and context would break the tie
- An unusual event type is detected (enum value not covered by rules)
- A guide passage is likely to change the decision (tagged in rule: `RequiresGuideKnowledge = true`)

Rules should NOT escalate for:
- Simple threshold checks with known response (food < 30 days → food_security goal, always)
- Routine maintenance decisions (harvest when mature crops exist)
- Anything that has been wrong < 5% of the time across 20+ logged instances

---

## Refinement — detail

### What refinement has access to

Refinement reads the decision and escalation logs and can edit `Rules.cs`, prompts, and fixtures. For code-shaped work it can shell out to **Claude Code** by writing a prompt to `.plans/<minister>-<task>.md` — see [`evaluation.md`](evaluation.md) for the full tooling story. Fixtures are the regression net.

### Typical refinement session flow

Read recent escalations with observed outcomes, cluster by reason, draft a rule that would have matched, run fixtures, surface diff + fixture results for human approval. Promotions are logged.

Auto-approve is post-MVP and per-minister opt-in; specifics deferred until the fixture suite is mature.

---

## Fixture testing

Each minister has a fixture suite: a set of canned briefings with known-good expected outputs.

```
Tests/
└── Agriculture/
    └── Fixtures/
        ├── food-shortage-day-40.json        # briefing + expected goals
        ├── fall-harvest-window.json
        ├── cold-snap-imminent.json
        ├── surplus-with-trader-approaching.json
        └── first-devilstrand-decision.json
```

**Fixture format:**
```json
{
  "description": "Food shortage on day 40, fall, trader approaching in 3 days",
  "briefing": { ... },
  "expected_advice": ["food_security", "trade_food_surplus"],
  "expected_top_advice_type": "food_security",
  "expected_severity_min": "High",
  "should_escalate": false,
  "notes": "Rules should handle this; trade is secondary to food security"
}
```

Player Accept events and Pushback entries on shipped advice are also raw material for new fixtures — see `fixture-gen` in [`evaluation.md`](evaluation.md).

Fixtures run on every CI push. A rule change that breaks a fixture is a regression.

---

## Zone ownership

Zones (stockpile, growing, dumping, home, allowed) are owned by the minister whose domain they serve. No dedicated Minister of Zoning — zones are means to other ministers' ends, not a domain of their own.

| Zone type | Owning minister | Notes |
|---|---|---|
| Food stockpile | Agriculture | Co-located with freezer; minister knows food quantities and spoilage risk |
| Material / component stockpile | Construction | Co-located with workshops; minister knows material flow and build queue |
| Ammo / weapon stockpile | Defense | Near killbox or armoury |
| Medicine stockpile | Welfare | Near hospital; minister tracks medical supply chain |
| Growing zone | Agriculture | Placement, size, crop assignment |
| Dumping zone | Construction | Rock chunks, corpses, waste — base hygiene |
| Home zone | Mayor / CoS | Colony-wide; no minister claims it |
| Allowed zone | Mayor / CoS | Colony-wide; no minister claims it |

**In Suggest mode:** conflicting zone advice from two ministers surfaces as two cards on the dashboard. The player resolves it. No system-level arbitration needed.

**At M7 (Auto graduation):** when zone suggestions can be auto-applied via RIMAPI, the CoS gets a zone-conflict resolution rule. Last-write-wins is not acceptable; CoS arbitrates by domain priority for contested tiles: Defense > Agriculture > Construction > Welfare.

**Layout efficiency** (pawn travel distance, zone placement relative to workstations) is owned by **Construction** as an extension of its `RoomProgram` brief — not a new minister. See `design/ministers/construction.md`.

---

## Open questions

- [ ] How is refinement triggered? Manually via `/refine` slash command? Nightly? After N escalations?
- [ ] Where are refinement session transcripts logged for audit?
- [ ] Should fixture generation be automated (a `fixture-gen` Claude skill)?
- [ ] Define the confidence threshold for post-MVP auto-approve
