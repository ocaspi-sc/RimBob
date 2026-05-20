# RimBob - Planning

> **DEFERRED - Auto epic.** Not built in MVP. In `Suggest` mode, ministers emit
> `AdviceItem`s and there is no planner consumer. Assisted Apply is a separate
> player-click path for a single allowlisted step. Re-engage this doc when a
> minister is ready to graduate from `Suggest` to `Auto`.

> **Living document.** See `AGENTS.md` for update rules.

---

## What we know now

At Auto graduation, RimBob writes the game's declarative automation knobs —
work-priority cells, zones, bills, drug/food/apparel policies, schedules — and
RimWorld's own job-giver allocates pawns. See [`DESIGN.md`](../DESIGN.md)
decision "Auto execution delegates to the game's native automation."

RimBob never writes per-pawn jobs. Auto only writes the policy knobs that steer
the game's allocation, and only Labor recommends them
([`ministers/labor.md`](ministers/labor.md)).

Targeted designations (`mark_harvest`, `mark_hunt`, `place_blueprint`) are a
separate, additive/ephemeral category and follow the existing Assisted Apply
allowlist doctrine ([`advice.md`](advice.md)).

## What we are not deciding now

The shim shape, vocabulary, failure model, validation/readback contract,
allowlist mechanics, and multi-step handling are all designed when the first
real Auto candidate is picked — not pre-built against hypotheticals.
