# Minister of Labor - Minister Design

> **DEFERRED - Auto epic.** Not built in MVP.
> The assisted-gameplay pivot cuts pawn-policy writes from `Suggest` mode and
> Assisted Apply. No minister writes pawn policy until M7+ re-engages Auto.

> **Reframed.** Labor is **not** an assignment solver. RimWorld's work-priority
> grid + job-givers already solve pawn allocation. Labor recommends *policy*
> (priorities, zones, bills, drug/food/apparel policies, schedules); the game
> executes. See the [`DESIGN.md`](../../DESIGN.md) decision log entry "Auto
> execution delegates to the game's native automation."

> **Living document.** See `AGENTS.md` for update rules.
> This doc records the Labor boundary. Do not implement until the first feeder
> minister is a real Auto candidate.

---

## Domain

Labor owns work-system **policy recommendation** once Auto exists. It never
writes per-pawn jobs — the game's job-giver does that.

- Work-priority configuration (which pawn, which work type, what priority).
- Zone membership where it gates labor (e.g. growing/allowed zones).
- Schedule blocks where they change what work happens when.
- Drug/food/apparel policy assignment.
- Reading cross-minister labor requests and turning them into the smallest
  policy change that lets the game's solver satisfy them.
- Surfacing skill gaps and workload imbalance the policy cannot fix.

Forced/queued individual jobs are explicitly **out of scope**: that is
re-implementing the game's solver. If a request cannot be met by a policy knob,
Labor reports the gap; it does not micromanage pawns.

The bulletin board/request queue lives in Coordination. Labor is the only actor
that reads those requests and turns them into work-system policy writes.

---

## Core Rule

RimWorld allocates pawns. Only Labor recommends or writes the work-system
**policy** that steers it.

Other ministers may request work-type-qualified labor in advice or flags. In
Suggest mode that is player advice only. In Auto mode, requests become inputs to
Labor's policy recommendation; they do not let Food, Defense, Construction, or
Welfare write priorities/zones/schedules directly, and they never become per-pawn
job writes from anyone.

---

## Future Briefing Direction

When Labor is re-engaged, its briefing should answer:

- Which pawns are available and qualified for each work type?
- Which requests are open, blocked, deferred, or stuck?
- Which skills are missing or over-constrained?
- Which pawns are unavailable due to health, schedule, combat, caravan, or other
  state?
- Which assignments would conflict with higher-priority colony needs?

Exact fields and solver inputs should be designed when the Auto slice starts.

---

## Policy Recommendation, Not A Solver

Labor needs **no** custom assignment solver and no HTN tree. RimWorld's
work-priority system *is* the solver — it already matches idle pawns to the
highest-priority work they can do, every tick, accounting for skills, distance,
reachability, and availability. Rebuilding that is wasted, fragile work.

Labor's job is the policy that *configures* the game's solver: sensible
priority numbers per pawn/work type, zone membership, schedule blocks. The hard
problem is recommendation *quality* (the same rules-first → LLM-escalation shape
as every minister), not optimization math.

First implementation should be a small rules layer over the same briefing
shape, benchmarked against replay/fixtures. Do not pre-build optimization; the
refinement loop justifies any added complexity with evidence.

---

## Success Metrics

- Critical labor requests are met by a policy change quickly once Auto is on.
- A policy recommendation does not starve other high-priority work.
- Skill gaps are surfaced before they become crises (policy cannot conjure a
  skill the colony lacks — say so).
- No per-pawn job writes are ever issued, by Labor or anyone.
- Only Labor-recommended policy reaches the Auto write allowlist.
- Policy-recommendation changes are replayed before promotion.

---

## Open Questions / TODO

- [ ] How aggressively should Labor rewrite player-set priorities vs. nudge
      minimally? (player intent in the work grid is high blast radius.)
- [ ] How does Labor account for caravans and temporary pawn absence when the
      game's solver already excludes absent pawns?
- [ ] Do prisoners or animals (work assignments, allowed-area) enter Labor's
      policy model, or stay with Welfare/Defense?
- [ ] Boundary between Welfare *wanting* a schedule/policy change and Labor
      owning the policy write.
- [ ] What instrumentation proves a policy recommendation (not "the solver") is
      failing — e.g. a request stays open N cycles after the policy change?
