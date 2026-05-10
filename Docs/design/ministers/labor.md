# Minister of Labor — Minister Design

> ⚠️ **DEFERRED — Auto epic.** Not built in MVP.
> The assisted-gameplay pivot ([`../../DESIGN.md`](../../DESIGN.md)) cuts pawn allocation from MVP scope: in `Suggest`-only mode no minister touches RIMAPI pawn writes, so the assignment solver and bulletin board have no consumer.
> This document is preserved verbatim for the M7 milestone, when the first feeder minister graduates from `Suggest` to `Auto` and needs the execution spine. Do not implement until M7 is on deck.

---

> **Living document.** See `CLAUDE.md` for update rules.
> Slice: post-MVP (M7). Core infrastructure minister for the Auto epic — required before any other minister can graduate to `Auto`.

---

## Domain

Work allocation and skill-to-task matching. Labor runs the bulletin board clearance loop — the board itself lives in the Coordination subsystem, but Labor is the only actor that reads open requests and issues pawn assignments via RIMAPI.

---

## Core rule: only Labor touches pawn allocation

No other minister issues RIMAPI writes that affect pawn work priorities or job assignments. Other ministers post to the bulletin board; Labor assigns.

---

## goal_id enum

```csharp
public enum LaborGoal
{
    ClearBulletinBoard,       // assign all open requests
    RebalanceWorkloads,       // redistribute when skill gaps appear
    HandleSkillGap,           // no pawn has a needed skill — flag upward
    OptimizeShiftCoverage     // 24h coverage for critical work types
}
```

Labor almost always runs `ClearBulletinBoard`. Other goals emerge from the assignment solver surfacing problems.

---

## Briefing

```
LaborBriefing
├── PawnRoster           [name, skills (all work types), passions, current job, health %, schedule slot]
├── BulletinBoard        [open requests by priority, deferred, stuck]
├── LaborUtilisation     [% work hours productive | idle | forced-rest by pawn]
├── SkillGaps            [work types with ≥1 open request and zero capable pawns]
└── ShiftConflicts       [pawns double-assigned or idle during critical work windows]
```

---

## Assignment solver (NOT an HTN)

Labor's core is an assignment solver, not a decision tree. It maps open bulletin-board requests to available pawns.

**v1 — greedy skill-match:**
For each open request ordered by priority:
1. Find free pawns with the required `WorkType`.
2. Score by: `skillLevel × passion_multiplier × (1 / healthPenalty)`.
3. Assign highest scorer. Issue RIMAPI work priority write.
4. If no pawn qualifies → move request to Deferred, emit flag to requester.

**Improve target:**
Labor's refinement will evolve the solver toward optimal assignment — recognising that greedy is suboptimal when requests compete for the same pawn. The improve loop will identify systematic under-utilisation patterns and propose solver changes. We do not pre-specify the target algorithm; Labor discovers it.

---

## Rules layer (target ~95% coverage)

| Rule | Condition | Output |
|---|---|---|
| `clear_critical_first` | Critical requests in Open | Assign before any lower-priority requests |
| `defer_if_no_skill` | Open request with WorkType nobody has | Move to Deferred, flag back to requester |
| `preempt_for_critical` | Critical request arrives AND all pawns assigned | Yank lowest-priority assigned pawn, reassign |
| `rest_enforced` | Pawn health < 40% OR pawn in rest schedule | Do not assign, mark as unavailable |
| `skill_gap_flag` | SkillGap exists for > 1 in-game day | Emit Medium flag to CoS: colony lacks skill X |

**Escalates when (< 5%):**
- Conflicting Critical requests that can't both be satisfied (Defense vs Food both Critical)
- Unusual pawn state (mind-controlled, berserk, wedding) — defer or flag

---

## RIMAPI writes owned

- Work priority overrides (per pawn, per work type)
- Restrict/permit zone assignments
- Force job (direct job override when RIMAPI supports it)
- Schedule assignment

No other minister issues these writes.

---

## Bulletin board management

Labor runs the bulletin board clearance loop. It is triggered by:
- `BulletinBoard.Version` change (new request posted)
- Any assigned request completing or going stuck
- Hourly heartbeat

See [`design/communication.md`](../communication.md) for bulletin board and flag channel details.

---

## Refinement focus areas

1. **Solver optimality:** does greedy produce measurably worse outcomes than optimal? Identify cases where a different assignment would have been better (e.g., the best cook was assigned to hauling while a mediocre one cooked).
2. **Priority calibration:** are Low requests being systematically starved? Is there a pawn who is always idle when there's Low work?
3. **Skill gap surfacing:** how early does Labor detect skill gaps vs when does a crisis actually hit?

---

## Success metrics

- All Critical bulletin-board requests assigned within 1 in-game hour.
- < 5% of High requests deferred for > 4 in-game hours.
- Pawn idle rate < 15% during active work schedule slots.
- Zero cases where a minister's RIMAPI pawn write bypasses the board.

---

## Open questions / TODO

- [ ] Partial fulfillment: if 3 planters requested and only 1 available, assign 1 or defer all? (v1: assign what's available, note partial in request)
- [ ] Caravan pawn-hours: how does Labor account for pawn absence during caravans?
- [ ] Prisoner handling: do prisoners appear in `PawnRoster`? Do they get assigned work?
- [ ] Animals: animal labor (hauling animals) — is that Labor's domain?
- [ ] Shift scheduling: does Labor set sleep/work/joy schedules, or does Welfare? (Propose: Welfare sets the pattern; Labor enforces it within the pattern)
- [ ] When does greedy solver fail badly enough to trigger refinement? Instrument this explicitly.
