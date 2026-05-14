# Chief of Staff - Minister Design

> **Living document.** See `AGENTS.md` for update rules.
> CoS is the cabinet arbitration role. This doc records responsibilities and
> boundaries, not final schemas or rule code.

---

## Role

Chief of Staff sits between feeder ministers and the Mayor. Its job is to make
cabinet pressure coherent before it reaches the player.

CoS can:

- Dedupe overlapping flags.
- Choose lead framing when multiple ministers describe the same issue.
- Downgrade inflated flags with a logged reason.
- Decide whether a flag becomes a tactical alert or normal Mayor input.
- Surface strategic tensions to the Mayor.

CoS does not issue labor requests, produce routine player advice, mutate the
Agenda, or call RIMAPI.

---

## Implementation Posture

Keep CoS split in the design language, but do not overbuild runtime structure
while feeder volume is low. The first pass may be a Mayor-side deterministic
helper. A separate loop/process is justified only when active flag traffic makes
the helper hard to reason about.

---

## Rules And Escalation

Rules should handle mechanical arbitration:

- Critical threat flags preempt normal work.
- Single high-priority flags need little arbitration.
- Low flags defer behind high/critical pressure.
- Same-source duplicate flags should update/supersede rather than stack.

Escalate when multiple serious flags compete, when two ministers should share a
single player-facing framing, or when priority depends on Mayor posture.

---

## Open Questions / TODO

- [ ] Define first CoS resolution actions when implementation starts.
- [ ] Decide whether CoS needs its own briefing or only the active flag set plus
      Mayor posture.
- [ ] Define three-way contention handling.
- [ ] Decide whether ignored Low flags age upward.
