# Minister of Labor - Minister Design

> **DEFERRED - Auto epic.** Not built in MVP.
> The assisted-gameplay pivot cuts pawn allocation from suggest-only scope. No
> minister touches RIMAPI pawn writes until M7+ re-engages Auto.

> **Living document.** See `AGENTS.md` for update rules.
> This doc records the Labor boundary. Do not implement until the first feeder
> minister is a real Auto candidate.

---

## Domain

Labor owns pawn allocation once Auto exists:

- Work-priority changes.
- Forced jobs when supported and explicitly allowed.
- Schedule enforcement where it affects assignment execution.
- Clearing cross-minister labor requests.
- Skill-to-task matching and workload balance.

The bulletin board/request queue lives in Coordination. Labor is the only actor
that reads execution requests and turns them into pawn-allocation writes.

---

## Core Rule

Only Labor touches pawn allocation.

Other ministers may request work-type-qualified labor in advice or flags. In
Suggest mode that is player advice only. In Auto mode, requests become inputs to
Labor; they do not let Food, Defense, Construction, or Welfare assign pawns
directly.

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

## Solver Direction

Labor needs an assignment solver, not an HTN tree. The first implementation
should be simple, inspectable, and benchmarked against replay/fixtures before
complex optimization.

Do not pre-specify the final algorithm. The refinement loop should identify
where the simple solver fails and justify upgrades with evidence.

---

## Success Metrics

- Critical requests are assigned quickly once Auto is enabled.
- High-priority requests do not starve.
- Skill gaps are surfaced before they become crises.
- Pawn allocation writes are never issued by non-Labor ministers.
- Solver changes are replayed before promotion.

---

## Open Questions / TODO

- [ ] Partial fulfillment: assign available pawns or defer the whole request?
- [ ] How does Labor account for caravans and temporary pawn absence?
- [ ] Do prisoners or animals ever enter Labor's assignment model?
- [ ] Boundary between Welfare schedules and Labor execution.
- [ ] What instrumentation proves the initial solver is failing?
