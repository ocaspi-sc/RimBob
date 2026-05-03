# RimAI — Planning: HTN

> ⚠️ **DEFERRED — Auto epic.** Not built in MVP.
> Under the assisted-gameplay pivot ([`../DESIGN.md`](../DESIGN.md)), ministers emit `AdviceItem`s instead of HTN goals; there is no planner consumer in `Suggest`-only mode. The HTN engine and primitive contract land at M7 when the first minister graduates from `Suggest` to `Auto`.
> This document is preserved verbatim so M7 design starts from where the original work left off, not from scratch.

---

> **Living document.** See `CLAUDE.md` for update rules.

---

## Overview

The HTN planner decomposes minister goals into primitive tasks. It is shared infrastructure — ministers register their own domains, the engine is neutral.

The bulletin board (work assignment to colonists) is a separate concern owned by Labor, not the planner. See [`design/ministers/labor.md`](ministers/labor.md).

---

## HTN — Hierarchical Task Network

### Vocabulary

- **Primitive task** — directly executable. Either a RIMAPI write call or a `PostLaborRequest` to the bulletin board.
- **Compound task** — a named outcome with multiple methods. E.g. `EnsureFoodSecurity`.
- **Method** — a precondition (lambda over briefing) + an ordered list of subtasks. The planner picks the first method whose preconditions hold.

Planning = recursive expansion: take the top-level compound (from the LLM's top-priority `goal_id`), find a matching method, expand subtasks, recurse until all are primitive.

### Domain registration

Each minister registers its domain at startup. The planner engine is shared; the domains are per-minister.

```csharp
planner.RegisterDomain(new AgricultureDomain());
planner.RegisterDomain(new DefenseDomain());
// etc.
```

A domain is a collection of compound tasks + their methods. It references its minister's briefing type.

### World-state representation

Preconditions are lambdas over the minister's current briefing. **No forward simulation.** Methods operate on what is observably true now; the world updates between planner runs. Ministers' plans are small (1–3 primitives typically); re-planning on briefing change is cheap.

Forward simulation belongs at the Mayor/CoS level for multi-day strategic trades, not in per-minister planners.

### When the planner runs

- After LLM call updates goals.
- After a flag fires.
- After a primitive completes or fails.
- Hourly heartbeat as safety net.

### Plan persistence

**Fresh plan each run.** No patching across runs. Compounds are small; a stale plan is more dangerous than a recomputed one.

### Example domain (Agriculture)

```
Compound: EnsureFoodSecurity
├── Method "already_secure"
│   pre:  daysOfFood >= target
│   tasks: []
├── Method "harvest_now"
│   pre:  matureCropTiles > 0 AND plantsLaborAvailable
│   tasks: [PostLaborRequest(Plants, Harvest, matureZones)]
├── Method "expand_zone"
│   pre:  hasGrowableSoil AND season.allowsPlanting AND constructionLaborAvailable
│   tasks: [ChooseCrop, DesignateZone(RIMAPI), PostLaborRequest(Plants, Sow, newZone)]
├── Method "hunt"
│   pre:  huntableAnimalsNearby AND shootingLaborAvailable
│   tasks: [PostLaborRequest(Hunting, Hunt, targets)]
└── Method "emergency_trade_flag"
    pre:  silverAboveThreshold AND traderApproaching
    tasks: [EmitFlag(FlagSeverity.High, "request_food_trade")]
```

---

## Primitive contract

Every primitive declares two things:

```csharp
public interface IPrimitive
{
    Task Execute();
    ObservabilityContract Observable { get; }
}

public class ObservabilityContract
{
    public string BriefingField { get; }    // e.g. "DaysOfFoodRemaining"
    public Predicate<object> SuccessWhen { get; }
    public TimeSpan Timeout { get; }
}
```

`WatchedPrimitive` wraps every primitive. On execute, it registers a watcher. If `SuccessWhen` is not true within `Timeout`, the primitive is marked stuck.

### Failure modes

| Mode | Behaviour |
|---|---|
| Precondition false at plan time | Backtrack, try sibling method |
| Primitive stuck (timeout, no observable effect) | Raise flag, replan with cooldown on failed method |
| RIMAPI write rejected | Log, raise High flag, fall back to next method |

Stuck-primitive detection is a first-class concern, not an afterthought. It is the most common silent failure in agent systems that talk to a slow world.

---

## Open questions

- [ ] How are compound tasks versioned? (If Agriculture adds a new method mid-playthrough, does the in-progress plan replan?)
- [ ] Priority inheritance: if a High request spawns a Critical sub-request, does the sub-request inherit Critical?
- [ ] Stuck timeout values — need calibration against real RIMAPI response times
