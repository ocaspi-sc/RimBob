# RIMAPI Blueprint Groups + Planning Overlay — Plan

> Agent-created plan. **All work lands in the RIMAPI**
> (`C:\dev\RIMAPI-for-RimBob`, repo `ocaspi-sc/RIMAPI-for-RimBob`), not this
> repo. RimBob integrates over HTTP only (no linking; GPL-3.0 posture).
>
> **Builds on** [`rimapi-blueprint-placement-endpoint.md`](rimapi-blueprint-placement-endpoint.md)
> — the single-asset `validate` / `place` / `read` triplet. That plan is the
> per-item primitive; this plan composes it into multi-asset **groups** and adds
> a **planning-overlay** workflow.

---

## 0. Why a second plan

The single-asset plan deliberately scoped out "multi-asset / area placement."
Willie's (Minister of Construction) target chain needs more:

- A freezer / functional room = **walls + door + floor + contained buildings**
  (cooler, shelves). In RimWorld both rooms and the buildings inside them are
  placed via blueprints. A useful Willie suggestion is a *set*, not one cell.
- North-star chain: Food requests a freezer → Willie suggests 3 layout options →
  player picks one in the dashboard → the chosen blueprint **group** is placed.
  That commit places a whole group at once.
- Future "Willie plans a new base / major expansion" idea (HumanTodo): blueprints
  laid as a **planning overlay** the player commits region-by-region, not
  auto-built.

This depends on the current MVP posture: **suggest + player-confirmed apply**
(see `Docs/DESIGN.md`). A group placement is still exactly one player Apply
click — it is not `Auto`.

---

## 1. Capability A — Blueprint groups (near-term, the freezer chain)

A **group** = an ordered list of single-asset placements (the §3 primitive from
the base plan) treated as one logical unit. Field names should align with the
`AdviceOption.blueprint_group` schema from the Willie advice-schema step
(`.plans/willie-advice-schema.md`, in flight) — confirm before coding.

### A.1 `POST /api/v1/builder/blueprint-group/validate` — dry-run

Request: `{ map_id, items: [ {def_name, stuff_def_name?, cell:{x,z}, rotation}, ... ] }`.

Per item, run the base plan §3.1 `CanPlaceBlueprintAt` dry-run. Response:
per-item `can_place` / `reason` / `occupies_cells` / `cost`, plus group-level
`can_place_all`, aggregate `cost`, and **intra-group overlap detection** (two
items claiming the same cell → reject with the conflicting item indices). No
mutation under any path.

### A.2 `POST /api/v1/builder/blueprint-group/place` — commit a group

Request: same `items` + a `placement_order` policy. Default order:
terrain/floor → walls → doors → other buildings, so the room shell exists before
its contents. Re-validate each item server-side (do not trust the caller).

**Partial-failure policy (open question, §4):**
- (a) **all-or-nothing** — validate all, then place all; abort if any required
  item fails. Recommended for room shells: a half-placed freezer is useless.
- (b) best-effort with a per-item result report.

Recommend (a) behind a `require_all` flag defaulting `true`. Idempotent per item
(reuse base plan §3.2 `already_present` checks). Response: per-item
`{status, thing_id, reason}` + group `status`.

### A.3 Read

Reuse the base plan's `GET /api/v1/map/blueprints`. No new read endpoint —
group membership is a RimBob-side concept; RIMAPI only sees individual
blueprints/frames.

---

## 2. Capability B — Planning overlay (future; the "plan a new base" idea)

Goal: Willie lays out a base/expansion **outline** the player reviews and commits
region-by-region, instead of auto-building everything.

**Key open question — mechanism.** RimWorld blueprints are **not natively
forbiddable or suspendable**; a placed `Blueprint_Build` gets built once Construct
labor + materials exist. So "place blueprints and forbid them" needs one of:

- **(b1) Vanilla Plan designator** — `Designator_Plan` paints cost-free colored
  planning cells that pawns ignore. This *is* RimWorld's native planning overlay.
  Willie paints Plan cells for the outline, then converts a player-chosen region
  into a real blueprint group (Capability A) on commit. Cleanest, mod-free.
- **(b2) "More Planning" / planner mod** — richer overlay, adds a mod dependency.
  Guides reference it (`Docs/guides/Base/more-planning-mod.md`,
  `Docs/guides/Base/rimworld-planner.md`).
- **(b3) Suspended blueprints** — investigate whether a placed `Blueprint_Build`
  can be flagged so the job-giver skips it. Likely needs a custom flag; least
  RimWorld-native.

**Recommend (b1):** Plan-designator overlay for the outline + Capability-A group
placement on commit. RIMAPI endpoints needed when scheduled: `POST /builder/plan`
(paint plan cells), `DELETE /builder/plan` (clear), `GET /map/plan` (read plan
cells). Defer detailed DTOs until the base-planning feature is scheduled.

---

## 3. Files touched in RIMAPI

Same areas as the base plan: `BaseControllers/BuilderController.cs`,
`Services/BuilderService.cs` (+ `Services/Interfaces/IBuilderService.cs`),
`Models/BuilderDtos.cs` (add group DTOs). Capability B adds plan-designator
service/DTOs when scheduled. `AutoRouteRegistry` discovers `*Controller` methods
by reflection — no DI wiring changes.

---

## 4. Open questions

- [ ] Group partial-failure policy: all-or-nothing vs best-effort (recommend
      all-or-nothing for room shells via `require_all`).
- [ ] Placement order: is a fixed floor→walls→doors→buildings order enough, or
      does the caller need explicit per-item ordering?
- [ ] Planning-overlay mechanism: confirm the vanilla `Designator_Plan` API
      surface on the 1.5 / 1.6 Krafs refs; decide b1 vs b2 vs b3.
- [ ] Does `blueprint_group` carry relative or absolute cells? (Advice-schema
      step decides; RIMAPI takes absolute cells per the base plan.)
- [ ] Should validate-group return aggregate **work-ticks** for feasibility
      scoring (extends the base plan §9 open question)?

---

## 5. Threading / verification / build

Identical to the base plan: HTTP requests enqueue and drain on the main game
thread via `RIMAPI_GameComponent.ProcessServerQueues()`; build both `RIMWORLD_1_5`
and `RIMWORLD_1_6` configs; RimWorld must restart/mod-reload to pick up the
assembly; RimBob has no compile-time dependency.

Verify (manual in-game, curl/Postman):
- validate-group on a clear room footprint → all `can_place`, overlap detection
  catches a deliberate self-overlap;
- place-group → room shell + cooler appear in the correct order;
- re-POST the same group → all `already_present`, no duplicates;
- (Capability B, when built) plan cells paint and clear with no resource cost.
