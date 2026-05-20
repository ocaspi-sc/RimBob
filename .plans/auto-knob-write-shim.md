# Auto Knob-Write Shim (Track B) — Plan

> Agent-created plan. **DEFERRED — Auto epic, M7+. Do not build before the
> first per-minister Auto graduation.** This is the *write* half of the
> [`Docs/DESIGN.md`](../Docs/DESIGN.md) decision "Auto execution delegates to the
> game's native automation." The near-term Suggest-only *read* half is a
> separate, independently shippable plan:
> [`set-priority-advice-briefing.md`](set-priority-advice-briefing.md).

---

## 1. Goal

When a minister/advice type graduates from `Suggest` to `Auto`, RimBob writes
**one validated declarative policy knob** (a work-tab priority cell, a stockpile
or grow zone, a drug/food/apparel policy, a schedule block) and lets RimWorld's
own job system do all pawn allocation. RimBob never writes per-pawn jobs.

This is explicitly **not** an HTN planner or a custom assignment solver — see
the reframed [`design/planning.md`](../Docs/design/planning.md) and
[`design/ministers/labor.md`](../Docs/design/ministers/labor.md). The "plan" for an
Auto action is one write plus observation, not a decomposition tree.

## 2. Why this is fenced to M7+

- MVP is Suggest-only; Assisted Apply is the player-confirmed precursor and is
  deliberately restricted to additive/ephemeral *targeted designations*
  (`mark_harvest`/`mark_hunt`/`unforbid`/simple-meal bill), never persistent
  policy knobs (see [`design/advice.md`](../Docs/design/advice.md) "Policy Knobs vs.
  Targeted Designations").
- Persistent policy writes overwrite hand-tuned player intent (high blast
  radius). They return only behind the per-minister Auto dial with explicit
  player consent.
- Building any of this before a concrete Auto candidate exists is speculative.

## 3. Dependencies

- **Track A must land first.** The shim's read-back uses the same Pawn Edit
  Controller GET that [`set-priority-advice-briefing.md`](set-priority-advice-briefing.md)
  introduces. No readback ⇒ no Auto (Assisted Apply doctrine: the backend must
  observe the result).
- A **Labor policy recommender** (thin, rules-first; not a solver) per
  [`design/ministers/labor.md`](../Docs/design/ministers/labor.md) is the only
  minister that recommends the knob. CoS arbitrates contested policy before any
  write.

## 4. Shape when re-engaged (scope decided then, not now)

Recorded so the future slice starts from prior decisions:

- Add `SetPriority` (then `SetStockpileZone`/`DesignateZone`) to
  `AdviceApplyKind` and an Auto write allowlist with validation rules.
- An `AssistedApplyService`-style shim branch, reusing its doctrine exactly:
  validate apply shape → refresh fresh state → staleness/precondition check →
  RIMAPI **one** declarative policy write → refresh → readback via the Track A
  GET → status (`applied` / `already_satisfied` / `stale_advice` /
  `validation_failed` / `rimapi_unavailable` / `rimapi_rejected` /
  `readback_inconclusive`).
- One knob per action. No decomposition. No per-pawn job writes — ever.
- Failed / rejected / no-observed-effect writes produce visible logs/flags, not
  silent retries.

## 5. Re-engagement checklist (from planning.md, restated here)

- [ ] Pick one minister/advice type as the first Auto candidate (stockpile/grow
      zone is the leading guess — small, observable, low blast radius).
- [ ] Identify the single declarative knob it writes; confirm one write
      suffices (if not, the candidate is wrong — not a reason to build HTN).
- [ ] Map the one RIMAPI write endpoint + its Track A read-back.
- [ ] Add the knob to the Auto allowlist with validation + CoS arbitration for
      contested policy.
- [ ] Fixtures/replay for success, rejection, and no-effect cases.
- [ ] Dashboard visibility for active/failed Auto writes + the Auto dial UI
      with a confirmation step.

## 6. Risks & mitigations

- **Overwriting player intent.** Per-minister Auto dial + explicit consent +
  CoS arbitration + one-knob-per-action + readback.
- **Premature build.** This plan authorizes no code before M7; it exists only
  to preserve durable intent.
- **Scope creep into a planner.** The re-engagement checklist explicitly
  rejects multi-step decomposition unless a candidate proves one write cannot
  express it.

## 7. Open questions

- [ ] First Auto knob candidate (stockpile/grow zone?).
- [ ] "No observed effect" timeout given the game executes asynchronously at
      its own job cadence.
- [ ] Does any realistic candidate need multi-step decomposition, or does one
      knob write always suffice?
- [ ] Bulletin-board scope: it queues policy-change requests for Labor, not
      per-pawn tickets (see [`design/communication.md`](../Docs/design/communication.md)).
