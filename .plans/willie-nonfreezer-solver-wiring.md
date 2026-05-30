# Willie Non-Freezer Solver Wiring — generalize the orchestrator gate beyond freezer

## Summary

The Placement Solver already has class-generic breadth (placement-solver-3 added
hospital / bedroom / workshop / storage templates and a `RoomClass` alias map),
but `MinisterOfWillie` only ever *invokes* it for the freezer path. Every gate is
hardcoded to `freezer_request_active` / `IsFreezingBuildRequest` /
`willie_freezer_request_active`, and the notes say "kitchen anchor" / "freezer
drafts". This plan generalizes the orchestrator so any building-request class —
and Willie's own missing-room decisions (kitchen / hospital / storage) — drive
the solver and attach validated options, not freezer-only.

## Sequencing dependency

This slice **shares files with `willie-freezer-apply-readiness`** and must land
**after** it. Both edit `MinisterOfWillie.cs` in the same methods
(`TrySolvePlacementAsync`, `EnrichAdviceItem`/`EnrichFreezerAdvice`, `NoteFor`).
Generalizing Enrich must inherit that slice's `materials_ready` / `apply_ready`
wiring, or the readiness fix would be re-broken for non-freezer options. Land
freezer-apply-readiness first, then this on top. (`rimapi-building-detail-read`
is independent of both and can run in parallel.)

## Current Evidence

**Gate 1 — solver only runs for freezer.**
`Src/Ministers/Willie/MinisterOfWillie.cs:128-136` (`ShouldRunPlacementSolver`):
```csharp
freezerRequest = inboundRequests.FirstOrDefault(Rules.IsFreezingBuildRequest)!;
return string.Equals(trace, "freezer_request_active", ...) && freezerRequest is not null;
```
Only fires on the `freezer_request_active` trace and only finds a *freezing*
request.

**Gate 2 — Enrich matches one hardcoded advice id.**
`MinisterOfWillie.cs:160-172` (`EnrichFreezerAdvice` / `IsFreezerAdvice`):
```csharp
string.Equals(item.Id, "willie_freezer_request_active", ...)
```
Even if the solver ran for another class, options would not attach — the enricher
only recognizes the freezer advice id.

**Gate 3 — notes are freezer-worded.**
`MinisterOfWillie.cs:224-243` (`NoteFor` / `NoFitNote`): "validated freezer
option", "no kitchen anchor", "no freezer drafts", "buildable footprint near the
kitchen". Wrong for a hospital/bedroom/storage request.

**Gate 4 — Rules only emits a freezer request trace.**
`Src/Ministers/Willie/Rules.cs:85-99`: the only inbound-`BuildingRequest` rule is
`freezer_request_active`, guarded by `IsFreezingBuildRequest` (`:370-373`). A
non-freezer inbound Willie `BuildingRequest` (kitchen/hospital/workshop/storage)
falls through to `maintain_build_program` and never reaches the solver.

**Gate 5 — missing-room decisions are prose-only.**
`Rules.cs:101-133` (`kitchen_missing`, `hospital_missing`, `storage_room_missing`)
call `MissingRoomDecision` (`:157-178`), which emits a prose `place_blueprint`
action with the explicit TODO (`:175`): "replace prose-only place_blueprint
actions with PlacementSolver options once Solver1 owns room footprints." Solver1+
now owns those footprints, so this is the wiring gap.

**Already-generic pieces (no change needed):**
- `PlacementSpec.FromBuildingRequest` (`PlacementSpec.cs:21-37`) maps
  TargetClass/RoomClass/CapacityNeed/Adjacency/Power/Temperature generically.
- `IPlacementSolver.SolveAsync` + templates resolve by `RoomClass` already.

## Root Cause (one line)

The solver is class-generic, but `MinisterOfWillie`'s invoke gate, advice-id
matcher, and note text — plus `Rules.cs`'s sole inbound rule — are all hardcoded
to the freezer case, so no other class ever reaches `SolveAsync`.

## Scope

### Part A — Generalize the orchestrator gate

- **`MinisterOfWillie.ShouldRunPlacementSolver` (:128-136)** — replace the
  freezer-only predicate with: "the selected decision is a solver-eligible
  building-request trace AND a matching inbound `BuildingRequest` exists." Return
  the matched request (rename `freezerRequest` → `request`). Eligible traces:
  the generalized inbound rule (Part D) plus the missing-room traces (Part E).
  Carry the `RoomClass`/`BuildingClass` so the note layer can word itself.

- **`RunPlayCycle` solver block (:35-40)** — rename `EnrichFreezerAdvice(...)` →
  `EnrichSolverAdvice(...)`; pass the advice id that drove the solver (the
  decision's emitted advice id) so Enrich can target the right item generically.

### Part B — Generalize Enrich

- **`EnrichFreezerAdvice` / `IsFreezerAdvice` (:160-172)** → `EnrichSolverAdvice` /
  match by the **driving advice id** (passed in from `RunPlayCycle`) instead of
  the literal `"willie_freezer_request_active"`. `EnrichAdviceItem` (:174-204)
  stays as-is — it is already class-agnostic — and **keeps the
  `materials_ready`/`apply_ready` wiring landed by `willie-freezer-apply-readiness`.**

### Part C — Generalize the notes

- **`NoteFor` (:224-233)** — keep the option-count / readiness suffix; it is
  already generic.
- **`NoFitNote` (:235-243)** — reword per the request's target class rather than
  hardcoding "kitchen"/"freezer". Take the resolved `RoomClass`/anchor class as a
  parameter and interpolate: `NoAnchors → "no {anchorClass} anchor is available"`,
  `NoDrafts → "no {roomClass} drafts were generated"`,
  `NoReachablePath → "no walkable route to a {anchorClass} anchor"`,
  `ValidationRejected → "no buildable footprint near the {anchorClass} passed fork
  validation"`. Freezer keeps its current wording by passing kitchen/freezer.

### Part D — Generalize the inbound Rules path

- **`Rules.cs:85-99`** — replace the freezer-only `freezer_request_active` rule
  with a general inbound rule that fires for **any** inbound Willie
  `BuildingRequest`, e.g. `building_request_active`, selecting the highest-priority
  request. Keep freezer wording when `IsFreezingBuildRequest(request)` is true;
  otherwise word it from the request's target class. The advice id becomes
  `willie_building_request_active` (or keep a per-class id — decide in
  implementation; the orchestrator now matches by the **emitted id**, so either
  works as long as `ShouldRunPlacementSolver` and `EnrichSolverAdvice` agree).
  - Update `RuleMatches` (`:342-344`) and `AllRuleEvaluations` (`:289`) trace rows
    to the generalized rule.
  - `IsFreezingBuildRequest` (`:370-373`) stays — it now only controls *wording*,
    not *gating*.

### Part E — Wire missing-room decisions into the solver

- **`Rules.cs` `MissingRoomDecision` (:157-178)** — these decisions
  (`kitchen_missing` / `hospital_missing` / `storage_room_missing`) should become
  solver-eligible traces. Two options:
  - **E1 (preferred):** have `MissingRoomDecision` synthesize an internal
    `BuildingRequest` for the missing `RoomClass` (no external flag needed) and
    mark the trace solver-eligible so `ShouldRunPlacementSolver` runs the solver
    for it. Remove the prose-only TODO action once options attach.
  - **E2:** leave Rules prose-only and have the orchestrator synthesize the
    request from the missing-room trace. (Heavier in the orchestrator; E1 keeps
    request-building in Rules where the room class is known.)
- This replaces the `:175` TODO. Keep the prose action as a fallback only when the
  solver returns no-fit.

### Part F — DI / construction

- No new constructor dependency: `MinisterOfWillie` already injects
  `placementSolver` + `colonyState`; `TrySolvePlacementAsync` already takes a
  generic `BuildingRequest`. Rename its `request` parameter doc/log text away from
  "freezer".

## Test Plan

- **`Src/Tests/Willie/MinisterOfWillieTests.cs`**
  - New: an inbound **non-freezer** Willie `BuildingRequest` (e.g. workshop) with
    a solver stub returning options → advice carries the options + apply actions
    (proves the gate, enrich, and notes generalized).
  - New: a `kitchen_missing` decision with a solver stub returning options → the
    kitchen advice carries options instead of prose-only (Part E).
  - Existing freezer test stays green (freezer is now one case of the general path).
  - No-fit path for a non-freezer class → `NoFitNote` reads the right class
    wording, not "kitchen"/"freezer".

- **`Src/Tests/Willie/...Rules`** (WillieRules tests) — the generalized inbound
  rule selects for a non-freezer request; missing-room traces are marked
  solver-eligible; freezer wording still applies when the request is freezing.

- **Live (after rebuild + host restart)** — inject or observe a non-freezer Willie
  building_request (or trigger a missing-kitchen state) and confirm
  `GET /api/ministers/willie/snapshot` attaches validated options + apply actions
  to the non-freezer advice; replay `willie-*.jsonl` shows
  `output_kind:"placement_solver"` with `status:"options"` for a non-freezer trace.

## Out of Scope

- The `materials_ready`/`apply_ready` derivation itself — owned by
  `willie-freezer-apply-readiness` (this slice inherits it).
- New solver templates / new `RoomClass` breadth — placement-solver-3 already
  shipped the templates; this is pure orchestrator/Rules wiring.
- Multi-request fan-out (running the solver for *several* inbound requests in one
  cycle) — keep the current single-selected-decision model; pick the
  highest-priority request. Fan-out is a follow-up.
- Building-condition reads (`rimapi-building-detail-read`) — independent.

## Assumptions

- `willie-freezer-apply-readiness` has landed (readiness wiring present in
  `EnrichAdviceItem` / `PlacementSolver`).
- The solver returns a stable `RoomClass`/anchor class on its result (or it can be
  taken from the driving `PlacementSpec`) so the note layer can word no-fit
  messages per class. If the result does not currently expose the resolved anchor
  class, thread it through `PlacementSolveAttempt` from the `spec`.
- Non-freezer templates (kitchen/hospital/workshop/storage/bedroom) produce
  validated options against the live map; if a class has no template the solver
  returns `NoDrafts` and the prose fallback (Part E) still shows.

## Summary — Landed `8d66711` (2026-05-30)

`feat(willie): generalize solver wiring`. Landed **after** `willie-freezer-apply-readiness`
(`f721618`) as sequenced — `MinisterOfWillie.cs` collision avoided, readiness wiring inherited.

- `MinisterOfWillie.cs` (+112): solver gate, advice-id matcher, and no-fit notes
  generalized off the freezer hardcodes (Parts A–C).
- `Rules.cs` (+207): freezer-only inbound rule generalized to any building-request
  class; missing-room decisions (kitchen/hospital/storage) now drive the solver
  instead of prose-only (Parts D–E), retiring the `:175` TODO.
- **Extra beyond plan (expected):** new `KitchenTemplate.cs` + `RoomTemplateSet`
  wiring + `Program.cs` DI — the plan *assumed* a kitchen template existed; it did
  not, so Codex added one (matches the "no template → NoDrafts" assumption).
- Tests: `WillieRulesTests` (+55), `MinisterOfWillieTests` (+85),
  `RoomTemplateBreadthTests`, `RoomTemplateSetTests`. Suite green at land.

Not re-verified live this closeout — confirm a non-freezer trace attaches options
via willie snapshot + replay `output_kind:"placement_solver" status:"options"`.

Follow-up surfaced post-land: `willie-solver-tab` (expose `PlacementSolverReplayOutput`
on an endpoint + a Solver dashboard tab) — separate plan, not part of this slice.
