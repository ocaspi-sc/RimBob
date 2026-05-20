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

> **Reframed.** RimWorld already *is* the execution engine. Its work-priority
> grid, job-givers, zones, bills, policies, and schedules continuously allocate
> pawns. The Auto epic does not rebuild that. See the
> [`DESIGN.md`](../DESIGN.md) decision log entry "Auto execution delegates to
> the game's native automation."

The original framing — an HTN planner that decomposes advice into a tree of
per-pawn primitive writes — is superseded. At Auto graduation, RimBob writes the
game's *declarative automation knobs* and lets RimWorld's own AI execute. The
"plan" for almost every Auto action is therefore one validated knob write plus
observation, not a decomposition tree.

What remains of this doc is a small **Auto apply shim**, not a planner:

- Validate the knob write against fresh state-store facts.
- Issue exactly one declarative write (or targeted designation) per action.
- Observe the resulting state-store delta to confirm the knob took effect.
- Re-evaluate next cycle instead of forward-simulating multiple days.

Genuine multi-step decomposition is only worth adding later if a concrete Auto
candidate proves a single knob write cannot express it; until then, do not build
an HTN engine.

---

## Vocabulary

- **Knob write:** one declarative change to the game's automation surface — a
  work-priority cell, zone, bill, policy, or schedule block. RimWorld's job
  system, not RimBob, turns it into pawn jobs.
- **Targeted designation:** a one-off spatial write that is not a policy knob —
  `mark_harvest`, `mark_hunt`, `place_blueprint`. Additive and ephemeral.
- **Observed effect:** the state-store delta proving the knob/designation took
  (e.g. work-priority readback, blueprint appears, designated cells change).
- **Compound action (rare):** an outcome no single knob expresses. Out of scope
  until a concrete Auto candidate proves it; not a reason to pre-build HTN.

Exact interfaces should be designed when Auto work starts, using the codebase at
that time as source of truth.

---

## Design Boundaries

- No Auto shim work in MVP.
- No autonomous RIMAPI writes before per-minister Auto graduation.
- MVP Assisted Apply is the player-confirmed precursor: one allowlisted,
  non-pawn, non-policy operation at a time.
- RimBob never writes per-pawn jobs. Pawn allocation is the game's; Auto only
  writes the policy knobs that steer it, and only Labor recommends those.
- The shim operates on state-store facts and re-evaluates when facts change; it
  does not forward-simulate multiple days.
- One knob write or designation per action; no decomposition tree by default.
- Failed, rejected, or no-effect knob writes must produce observable
  logs/flags rather than silent retries.

---

## Failure Model

The Auto shim must explicitly handle:

- Preconditions no longer true at write time.
- RIMAPI write rejected or unavailable.
- Knob write had no observed effect (state-store delta never appeared).
- Two ministers recommend conflicting policy for the same knob — CoS arbitrates
  before the write; the shim does not merge contested policy.
- A higher-priority flag supersedes the pending write.

The response should be logged, visible to dashboard/system inspection, and
available to refinement.

---

## Re-Engagement Checklist

Before implementing Auto shim code:

- [ ] Pick one minister/advice type as the first Auto candidate.
- [ ] Identify the single declarative knob (or designation) it would write.
- [ ] Map that one RIMAPI write endpoint and its state-store read-back.
- [ ] Confirm the knob is expressible as one write — if not, the candidate is
      wrong, not a reason to build HTN.
- [ ] Add the knob to the Auto write allowlist with validation rules.
- [ ] Add fixtures/replay records for success, rejection, and no-effect cases.
- [ ] Add dashboard visibility for active/failed Auto writes.

---

## Open Questions

- [ ] Which knob is the first Auto candidate (stockpile/grow zone is the
      leading guess — small, observable, low blast radius)?
- [ ] How long after a knob write should "no observed effect" be declared,
      given the game executes asynchronously at its own job cadence?
- [ ] Does any realistic Auto candidate actually need multi-step decomposition,
      or does one knob write always suffice?
- [ ] Where does an Auto knob write read back from — a dedicated Pawn Edit
      Controller GET, or inferred from existing aggregates?
