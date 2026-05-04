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
  → Read own decision log
  → Identify escalation patterns, rule misfires
  → Read player FeedbackEvents (Accept | Dismiss | Modify) joined to advice ids
  → Identify advice consistently Dismissed or consistently Modified the same way
  → Propose Rules.cs / prompt changes (code-gen with tools)
  → Run proposals against fixture suite
  → Gate on approval (human in v1; auto above confidence threshold post-MVP)
  → Promote approved changes to Rules.cs  ← "rule promotion"
```

Player feedback is the **primary** training signal under suggest-only. Implicit state-diff (did the colony state evolve in a way consistent with the advice?) is a fallback when feedback is absent. See [`advice.md`](advice.md).

Refinement IS the minister. Not a separate agent — the same minister in a different mode, with different tools available (code read/write, fixture runner) and different context (decision log instead of briefing).

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

See [`advice.md`](advice.md) for the full `AdviceItem` schema and feedback lifecycle.

---

## The rules layer in practice

The rules layer is a C# class per minister. Pure, no I/O, deterministic.

**It should handle the common case.** Rough escalation rate targets:

| Minister | Target escalation rate | Rationale |
|---|---|---|
| Labor | < 5% | Assignment is a solved optimization problem |
| Agriculture | ~20% | Seasonal decisions, crop choices need judgment |
| Construction | ~30% | Build program needs judgment; order is mechanical |
| Welfare | ~40% | Mood interventions often formulaic; crises need judgment |
| Defense | ~60% | Raid response is highly context-dependent |
| Chief of Staff | ~70% | Arbitration is judgment by definition |
| Mayor | ~85% | The role is judgment |

These are initial targets. The improve loop drives them down over time.

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

```
Tools available in refinement (not in play mode):
- ReadFile(path)         — read Rules.cs and related source
- WriteFile(path, code)  — propose a Rules.cs change
- RunFixtures(suite)     — run the fixture test suite, return pass/fail + diffs
- ReadDecisionLog(n)     — last n decisions with outcomes
- ReadEscalationLog(n)   — last n escalations with LLM outputs
```

### Typical refinement session flow

1. Read last N escalations where outcome was eventually observed.
2. Group by `escalation_reason`. Find clusters of ≥5 with consistent LLM output.
3. For each cluster: draft a rule that would have matched + produced the same output.
4. Write proposed Rules.cs change.
5. Run fixtures. If pass rate drops, revise. If pass rate is stable or improves, surface for approval.
6. Human reviews diff + fixture results → approve or reject with note.
7. On approval, Rules.cs is updated. The approved rule is logged as a promotion event.

### Approval gates (v1)

All rule promotions require human approval in v1. The system surfaces:
- The escalation pattern (N examples)
- The proposed rule (diff)
- Fixture results (before/after)
- The human approves or rejects with a note

Auto-approve (post-MVP): available when the fixture suite covers ≥50 scenarios for the minister and the proposed rule improves pass rate. Requires explicit opt-in per minister.

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

Player Accept / Modify events on shipped advice are also raw material for new fixtures — see `fixture-gen` in [`evaluation.md`](evaluation.md).

Fixtures run on every CI push. A rule change that breaks a fixture is a regression.

---

## Open questions

- [ ] How is refinement triggered? Manually via `/refine` slash command? Nightly? After N escalations?
- [ ] Where are refinement session transcripts logged for audit?
- [ ] Should fixture generation be automated (a `fixture-gen` Claude skill)?
- [ ] Define the confidence threshold for post-MVP auto-approve
