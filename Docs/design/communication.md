# RimBob - Inter-Minister Communication

> **Living document.** See `AGENTS.md` for update rules.
> This doc records coordination semantics. Exact flag records, context records,
> and endpoint/log fields live in source and tests.

---

## The Rule

**Ministers do not talk to each other directly.**

All coordination uses shared, observable channels:

1. **Flag channel** - ministers publish needs, incidents, and escalations for
   CoS/Mayor synthesis.
2. **Bulletin board** - deferred Auto-epic labor/request channel, re-engaged at
   M7 with Labor.

No bilateral messaging, no minister-to-minister method calls, and no shared
mutable state between ministers. If two ministers need frequent direct
coordination, that may mean the boundary is wrong.

---

## Flag Channel

Flags are how ministers signal needs upward and sideways. They are not player
advice by themselves; they are routing and arbitration inputs.

M3 runtime bridge: before a separate CoS loop exists, Food publishes active
flags and the Mayor reads active Medium+ flags during the same cabinet cycle.
This is a Mayor-side bridge, not direct minister communication.

Design-level flag fields:

- Stable id for dedupe/supersession.
- Source minister.
- Severity.
- Domain/summary/detail.
- Optional resource requests.
- Expiry/supersession metadata.

Exact shape lives in `Src/Common/Ministers/AgentFlag.cs`.

Flag resource requests use the same semantics as advice resource requests:
advisory needs, not allocation authority.

### Severity Tiers

| Tier | Meaning | Typical handling |
|---|---|---|
| Critical | Immediate colony/colonist danger | Preempt normal flow |
| High | Important tactical or near-term issue | CoS/Mayor-side bridge |
| Medium | Digest-worthy pressure | Batched into Mayor synthesis |
| Low | Background opportunity or cleanup | Deferred until capacity exists |

### Severity Calibration

- The emitting minister self-rates.
- CoS may downgrade with a logged reason; it cannot upgrade.
- A minister upgrades by emitting a new higher-severity flag, preserving audit
  history.
- Flag severity distribution per minister should be visible during refinement.

### Lifecycle

Flags move through active, resolved, superseded, or expired states. Same-minister
same-issue updates should replace or supersede rather than spam the active view.

---

## Cross-Domain Context In Briefings

Ministers cannot read each other's briefings. A minister's own briefing may
include summarized cross-domain facts that affect its decisions.

Example: Food does not need raid details; it may only need an `active threat`
signal to avoid planting advice during combat.

These facts are computed by the state store as derived views, not exchanged by
ministers.

---

## Chief Of Staff

CoS is the arbitration layer for conflicts and duplicate framing.

CoS can:

- Dedupe overlapping flags.
- Choose which framing leads the Mayor's memo.
- Downgrade flags with reasons.
- Decide whether something becomes a tactical alert or normal digest input.
- Surface strategic tensions to the Mayor.

CoS does not produce routine player advice, issue labor requests, mutate the
Agenda, or call RIMAPI.

The first implementation can be a Mayor-side deterministic helper while feeder
volume is low. Split into a separate loop only when traffic justifies it.

The original CoS role of bulletin-board arbitration is deferred with the Auto
epic.

---

## Mayor To Ministers: Agenda Broadcast

The Mayor's direction to ministers is a read-only broadcast through the current
Agenda. Ministers read relevant posture and cabinet-direction context when they
next evaluate.

This is not a message and does not fire a wake event. The Mayor is the only
writer of the Agenda.

See [`agenda.md`](agenda.md).

---

## What Does Not Exist

- Direct calls from one minister to another.
- Shared mutable minister-owned data structures.
- A pub/sub chat bus between ministers.
- A ministry chat room.

These create hidden coupling. Flags, briefings, and Agenda broadcast are enough
for the current `Suggest`-mode design.

---

## Open Questions

- [ ] Should flag retrieval be strict priority, FIFO with priority gates, or
      priority with aging?
- [ ] What exact dedupe key should active flags use?
- [ ] How long are flag histories retained for refinement?
- [ ] When does CoS need to split from Mayor-side helper into its own loop?
