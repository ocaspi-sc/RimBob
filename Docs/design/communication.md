# RimAI — Inter-Minister Communication

> **Living document.** See `CLAUDE.md` for update rules.

---

## The rule

**Ministers do not talk to each other directly.**

All coordination is via two shared channels:
1. **Flag channel** — publish observable needs, incidents, and escalations. Consumed by CoS and folded into the Mayor's daily digest.
2. **Bulletin board** — *Deferred (Auto epic).* Was the labor-request channel; no consumer in suggest-only MVP. Re-engaged at M7. See [`ministers/labor.md`](ministers/labor.md).

No bilateral messaging. No shared mutable state between ministers. If two ministers genuinely need to coordinate frequently and directly, that is a signal they should be one minister.

---

## Flag channel

Flags are how ministers signal needs upward and sideways. The CoS consumes them; the Mayor reads a daily digest of Medium flags; Critical flags preempt everything.

M3 runtime status: before a separate CoS loop exists, `FlagChannel` is an in-process active-flag store. Food publishes flags into it; the Mayor reads active Medium+ flags during the same `CabinetCycle` and folds them into the next agenda prompt. This is a Mayor-side bridge, not direct minister-to-minister communication.

### Flag schema

```csharp
public class AgentFlag
{
    public string         Id;                // for cancellation / deduplication
    public string         SourceMinister;    // "Food"
    public FlagSeverity   Severity;          // Critical, High, Medium, Low
    public string         Domain;            // "food", "defense", "medical", ...
    public string         Summary;           // "Food supply below 5 days"
    public ResourceRequest[]? Requests;      // optional resources needed to resolve it
    public string?        Detail;            // rule name, trace, or LLM rationale
    public DateTime?      ExpiresAt;         // auto-expiry
}
```

`Requests` uses the same `ResourceRequest` schema as `AdviceItem.resource_requests[]` in [`advice.md`](advice.md). In MVP the field is advisory only; it lets CoS and Mayor see what a minister needs without granting allocation authority.

### Severity tiers

| Tier | Handled by | Examples | Behaviour |
|---|---|---|---|
| **Critical** | Immediate preemption | Active raid, fire, infestation | Defense can interrupt all other ministers' work |
| **High** | Chief of Staff / Mayor-side bridge in M3 | Colonist downed, food shortage, disease | CoS resolves or escalates to Mayor |
| **Medium** | Mayor daily digest | Research complete, wall breach, mood declining | Batched, not acted on immediately |
| **Low** | Labor queue, when free | Beautification, surplus trade opportunity | Deferred until capacity available |

### Severity calibration

- The emitting minister **self-rates**. This is the most common source of flag-inflation bugs.
- The Chief of Staff may **downgrade** a flag (with reasoning logged). CoS cannot upgrade.
- Ministers cannot upgrade their own past flags — they re-emit a new flag with higher severity, creating an audit trail.
- Flag severity distribution per minister is logged and surfaced during refinement sessions.

### Flag lifecycle

```
Emitted → Active → Resolved (by CoS action or natural expiry)
                 → Superseded (same minister emits updated flag for same issue)
                 → Expired (ExpiresAt passed with no action)
```

---

## Cross-domain context in briefings

Ministers cannot read each other's briefings. But a minister's briefing can include a **context block** with summarized cross-domain facts that affect its decisions.

Example: `FoodBriefing.ActiveThreats: bool` — Food doesn't need to understand the raid; it just needs to know "don't plant right now."

These cross-domain facts are computed by the state store as derived views, not by ministers. They flow through briefing derivations, not through minister-to-minister calls.

---

## Chief of Staff as the arbitration layer

When two ministers emit conflicting flags that would produce contradictory or duplicative advice (both want to write a memo about the same colonist crisis, both think their angle is the right framing), the CoS arbitrates before the Mayor's digest builds.

CoS sees:
- All active flags with their full trace
- Mayor's current posture and `colony_objective`

CoS outputs:
- A priority ruling (which flag's framing leads the Mayor's memo)
- Optionally a downgrade of the losing flag
- Optionally a flag to the Mayor if the conflict represents a strategic tension worth noting in the memo body

CoS does not directly produce advice items, issue labor requests, or write to RIMAPI. It shapes the digest the Mayor receives.

> **Deferred:** the original CoS role of arbitrating bulletin-board priority via Labor is paused with the rest of the Auto epic.

---

## Mayor → ministers: the Agenda

The Mayor's direction to ministers is a **read-only broadcast via the Agenda**. Ministers never receive direct messages from the Mayor; they read the relevant section of the current `MayorAgenda` in their briefing context.

```csharp
public class MinisterBriefingContext
{
    public MayorPosture  Posture          { get; }  // economic + military stance
    public string?       AgendaDirection  { get; }  // cabinet_direction entry for this minister (null in M1)
    public string[]      ShortTermDomains { get; }  // ranked domain list from agenda.short_term
}
```

`AgendaDirection` is the `cabinet_direction[ministerName]` string from the current Agenda, injected as a prefix into the minister's LLM prompt. It tells the minister where the Mayor wants their attention focused this day.

`ShortTermDomains` is a simple ranked list (e.g. `["food","defense","welfare"]`) derived from the Agenda's `short_term` priorities. Ministers use it in their rules layer to rank competing issues without needing to parse the full Agenda.

This is not a message; it does not fire any wake event. Ministers apply posture adjustments to their goal priorities when they next evaluate. The Mayor is the only writer of the Agenda — ministers never mutate it.

→ See [`design/agenda.md`](agenda.md) for the full Agenda schema.

---

## What does NOT exist

- No direct method calls from one minister to another.
- No shared mutable data structures ministers write to.
- No pub/sub event bus between ministers (the AdviceBus is one-way: ministers → Host → dashboard, not minister → minister).
- No "ministry chat room."

These would create hidden coupling, debugging nightmares, and circular dependencies. The flag + board model is sufficient and observable.

---

## Open questions

- [ ] Should the CoS flag channel be FIFO or priority-sorted? Priority-sorted risks starvation of Low flags; FIFO is unfair to Critical. Likely: priority queue with aging (Low flags eventually get promoted if waiting too long).
- [ ] Flag deduplication: if Food emits "food shortage" every tick, should the board suppress duplicates? Propose: same minister, same summary → update existing flag, don't add new one.
- [ ] How long are flags retained in history for improve-mode audit?
