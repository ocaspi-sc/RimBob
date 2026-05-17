# RimBob - Planning / HTN

> **DEFERRED - Auto epic.** Not built in MVP.
> In `Suggest` mode, ministers emit `AdviceItem`s and there is no planner
> consumer. Assisted Apply is a separate player-click path for a single
> allowlisted step, not this planner. Re-engage this doc at M7+ when a minister
> is ready to graduate from `Suggest` to `Auto`.

> **Living document.** See `AGENTS.md` for update rules.
> This doc records planning intent and boundaries, not committed interfaces.

---

## Purpose

The future planner decomposes trusted minister advice actions into executable steps.
It exists only when RimBob is allowed to act autonomously through multi-step
RIMAPI writes.

The planner is shared infrastructure. Ministers own domain methods and advice
semantics; Labor owns pawn assignment; the planner coordinates execution shape.

---

## Vocabulary

- **Compound task:** an outcome that needs decomposition.
- **Method:** one way to satisfy a compound task, gated by current state.
- **Primitive task:** an executable write or a request to another execution
  owner such as Labor.
- **Observed success:** the state-store signal proving a primitive worked.

Exact interfaces should be designed when Auto work starts, using the codebase at
that time as source of truth.

---

## Design Boundaries

- No planner work in MVP.
- No planner-owned or autonomous RIMAPI writes before per-minister Auto
  graduation.
- MVP Assisted Apply is outside the planner: one player-confirmed,
  allowlisted, non-pawn operation at a time.
- No minister except Labor touches pawn allocation.
- Planning should operate on state-store facts and replan when facts change.
- Plans should stay small and inspectable; avoid speculative multi-day forward
  simulation in first Auto slices.
- Failed or stuck primitives must produce observable logs/flags rather than
  silent retries.

---

## Failure Model

The Auto planner must explicitly handle:

- Preconditions no longer true.
- RIMAPI write rejected or unavailable.
- Primitive appears stuck because observed state did not change.
- Competing requests need the same pawn/resource.
- A higher-priority flag interrupts current execution.

The response should be logged, visible to dashboard/system inspection, and
available to refinement.

---

## Re-Engagement Checklist

Before implementing planner code:

- [ ] Pick one minister/advice type as the first Auto candidate.
- [ ] Map required RIMAPI writes and read-back observability.
- [ ] Define the smallest primitive contract needed for that candidate.
- [ ] Define how Labor receives pawn-allocation requests.
- [ ] Add fixtures/replay records for success, rejection, and stuck cases.
- [ ] Add dashboard visibility for active/stuck Auto execution.

---

## Open Questions

- [ ] How are compound/method versions tracked across playthroughs?
- [ ] How does priority inheritance work across sub-requests?
- [ ] What stuck timeout values match real RIMAPI response times?
- [ ] When is forward simulation worth adding, if ever?
