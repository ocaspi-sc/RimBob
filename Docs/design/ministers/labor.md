# Minister of Labor - Minister Design

> **DEFERRED - Auto epic.** Not built in MVP. No minister writes pawn policy
> until Auto re-engages. See [`DESIGN.md`](../../DESIGN.md) decision "Auto
> execution delegates to the game's native automation."

> **Living document.** See `AGENTS.md` for update rules.

---

## What we know now

Labor recommends **work-system policy** — work priorities, zones, bills,
drug/food/apparel policies, schedules. RimWorld's own job-giver allocates pawns
to specific jobs; Labor (and RimBob in general) never writes per-pawn jobs.

In Suggest mode, other ministers may request work-type-qualified labor on
flags; that is player advice only. In Auto mode, those requests become inputs
to Labor's policy recommendation. They never become per-pawn job writes from
anyone.

The bulletin board/request queue
([`communication.md`](../communication.md)) lives in Coordination; Labor is the
only actor that turns its entries into work-system policy writes.

## What we are not deciding now

The briefing shape, recommendation rules, allowlisted knobs, conflict
arbitration with player-set priorities, and policy-quality metrics are designed
when Auto is actually picked up — not pre-built against hypotheticals.
