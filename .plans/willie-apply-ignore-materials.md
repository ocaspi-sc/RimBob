# Willie Apply Ignores Materials — decouple `apply_ready` from `materials_ready`

> **Decision reversal.** Placing a blueprint group costs **zero materials** in
> RimWorld — pawns haul materials to the blueprint *after* it exists, then build
> the frame. The current solver gates the **Apply** (place-blueprint) action on
> `materials_ready`, so a colony that is short a material can no longer drop the
> blueprint it needs to queue the haul+build. This slice makes `apply_ready`
> follow `placement_valid` (footprint validated), independent of stock.

## Problem (live, observed)

Willie Build Queue → Proposed renders 2 freezer option cards, but the Apply button
reads **"Apply not wired"** and is disabled.

`GET /api/ministers/willie/snapshot` (live, commit `5548c6b`):
- advice `willie_building_request_active`, 2 options, rationale:
  *"Placement solver: 2 validated options; materials_ready=blocked, apply_ready=blocked."*
- the item's lone action is the fallback prose `place_blueprint` with `apply: null`
  — no `place_blueprint_group` action attached.

Chain:
1. `MaterialReadiness` → `Blocked` (colony short ≥1 of BlocksGranite 90 /
   ComponentIndustrial 3 / Steel 106 / WoodLog 25; Steel is fine at 1801).
2. Solver derives `ApplyReady = Blocked` from materials
   ([`PlacementSolver.cs:174-176`](../Src/Ministers/Willie/PlacementSolver.cs)).
3. `EnrichAdviceItem` only attaches the per-option `place_blueprint_group` action
   when `ApplyReady != Blocked`
   ([`MinisterOfWillie.cs:207-214`](../Src/Ministers/Willie/MinisterOfWillie.cs)) →
   action dropped.
4. Frontend `findBlueprintAction` finds no `place_blueprint_group` action →
   `actionMatch = null` → label "Apply not wired", button disabled
   ([`MinisterBuildQueueView.tsx:192-195,384-394,408-413`](../Dashboard/src/components/minister/MinisterBuildQueueView.tsx)).

## Why the gate is wrong

The Apply executor `AssistedApplyService.ApplyBlueprintGroupAsync`
([`AssistedApplyService.cs:483-561`](../Src/ApiHost/AssistedApplyService.cs)) runs
RIMAPI `builder/blueprint-group/validate` then `builder/blueprint-group/place`.
Both operate on **blueprints**:
- `validate` checks `can_place_all` — footprint / overlap / terrain only, **no
  material check** ([`AssistedApplyService.cs:509,522`](../Src/ApiHost/AssistedApplyService.cs)).
- `place` drops blueprints; materials are hauled afterward by colonists.

So enabling Apply while materials are short is **safe** and **correct** — the
validate gate is the real safety net and it does not look at stock. A material
shortage is advisory ("pawns will haul when available"), not a placement blocker.

## Supersedes / reverses

- **Reverses** [`placement-solver.md:166`](placement-solver.md): *"Material
  shortage should block `apply_ready`…"* — that line conflated *can-build-now*
  with *can-place-blueprint*. Its own next sentence (`:167-168`) already wanted to
  **show** the option when `placement_valid` but not `materials_ready`; this slice
  finishes that thought by also un-gating Apply.
- **Overrides** [`willie-freezer-apply-readiness.md:100-109`](willie-freezer-apply-readiness.md)
  Part A, which set `applyReady = materialsReady is Ready or Unknown ? Ready :
  Blocked`. New rule: `apply_ready = placement_valid`, materials-independent.
- `materials_ready` itself is **unchanged** — still computed from live stock and
  still surfaced as the readiness pill + rationale suffix. Only its influence on
  `apply_ready` is removed.

## Scope

### S1 — `apply_ready` follows `placement_valid`, not materials
[`Src/Ministers/Willie/PlacementSolver.cs:173-184`](../Src/Ministers/Willie/PlacementSolver.cs)
(success path). Keep `materialsReady` computed (line 173) for the pill. Replace the
materials-derived `applyReady` (174-176) so apply-readiness mirrors placement
validity:
```csharp
PlacementReadiness materialsReady = MaterialReadiness(spec, finalCandidates.Select(c => c.Validation.Cost));
PlacementReadiness placementValid = PlacementReadiness.Ready; // every finalCandidate passed group-validate
return new PlacementResult(
    Options: options,
    Trace: new PlacementTrace("placement_solver", draftTraces, notes),
    NoFit: null,
    Draftable: PlacementReadiness.Ready,
    PlacementValid: placementValid,
    MaterialsReady: materialsReady,
    // Blueprint placement consumes no materials (pawns haul on build); apply-ready
    // tracks footprint validity, not stock. Material shortage stays advisory via
    // MaterialsReady + the rationale suffix.
    ApplyReady: placementValid);
```
(Implementer's discretion on the exact local; the invariant is `ApplyReady ==
PlacementValid` on the success path, with no read of `materialsReady`.)

### S2 — leave the NoFit path Blocked
[`PlacementSolver.cs:398-415`](../Src/Ministers/Willie/PlacementSolver.cs): the
NoFit branch keeps `ApplyReady: Blocked` and `PlacementValid: Blocked` — no
options were validated, nothing to place. **No change.** (Invariant `ApplyReady ==
PlacementValid` already holds: both `Blocked`.)

### S3 — confirm the attach gate now opens
[`MinisterOfWillie.cs:207-214`](../Src/Ministers/Willie/MinisterOfWillie.cs):
`attachApplyActions = options is { Count: > 0 } && result.ApplyReady !=
PlacementReadiness.Blocked`. With S1, success-path `ApplyReady = Ready`, so the
per-option `place_blueprint_group` actions attach whenever options exist. **No code
change required** — the guard stays as a correct belt-and-suspenders (NoFit has no
options anyway). Do **not** widen or simplify it in this slice.

### S4 — flip the unit assertions
- [`Src/Tests/Willie/PlacementSolverTests.cs:78-79`](../Src/Tests/Willie/PlacementSolverTests.cs):
  the materials-short test keeps `MaterialsReady == Blocked` but now asserts
  `ApplyReady == Ready`. Rename the test if its name implies apply-is-blocked.
- Lines 29-31 (Ready/Ready/Ready) and 60-61 (Unknown→Ready) stay green unchanged.
  Line 97 (NoFit `ApplyReady == Blocked`) stays unchanged.
- [`Src/Tests/Willie/MinisterOfWillieTests.cs`](../Src/Tests/Willie/MinisterOfWillieTests.cs):
  grep for any assertion that the `place_blueprint_group` apply action is **absent**
  when materials are short, and flip it to **present**. Any test that placed a
  colony short on freezer stock specifically to assert "Apply not wired" must now
  assert the action attaches with a non-null `PlaceBlueprintGroupApply`.

### S5 — reconcile the reversed design docs (doc-only)
- [`placement-solver.md:166-168`](placement-solver.md): replace the "block
  `apply_ready`" sentence with the corrected rule (apply-ready = placement-valid;
  materials advisory) and a one-line pointer to this plan.
- [`willie-freezer-apply-readiness.md`](willie-freezer-apply-readiness.md): add a
  short "**Superseded (in part)**" note at the top of Part A pointing here — Part B
  (real stock feeding `materials_ready`) still stands; only Part A's
  `apply_ready`-from-materials derivation is replaced.

## Out of scope
- **Frontend changes.** No edit to `MinisterBuildQueueView.tsx`. Once the backend
  attaches the action, the button enables and its label becomes the option label
  ("Starter freezer"). The `materials_ready=blocked` pill staying lit next to an
  **enabled** Apply is intended/advisory — not a bug. A dedicated "short materials,
  placeable anyway" hint/tradeoff note is a separate nicety.
- **`materials_ready` computation / stock feed** — untouched (landed in
  `willie-freezer-apply-readiness` Part B; keep it).
- **Per-option apply granularity** — all options still share the
  `PlacementResult`-level readiness; no per-option override here.
- **RIMAPI / fork** — the validate→place endpoints already exist and are
  materials-free; no fork change.
- **Materials still reported** — Willie may still raise a `material_bottleneck`
  concern elsewhere; this slice does not touch that signalling.

## Assumptions
- Every `finalCandidate` on the success path has passed group-validate (today
  `PlacementValid` is hardcoded `Ready` there); `ApplyReady == PlacementValid`
  preserves that. If a future change makes `PlacementValid` conditional, apply-ready
  correctly follows it.
- The live validate gate in `AssistedApplyService` remains the click-time safety
  net; `apply_ready=Ready` does not bypass it — the executor still runs
  validate → place → readback and rejects if the footprint no longer fits.

## Verification
- **Unit:** `dotnet test` Willie suite green with S4 assertions flipped; the
  materials-short case now yields `MaterialsReady=Blocked` + `ApplyReady=Ready` and
  an attached `place_blueprint_group` action.
- **Live (the original repro):** with a colony short ≥1 freezer material,
  `GET /api/ministers/willie/snapshot` → `willie_building_request_active` carries
  `options[]` **and** a `place_blueprint_group` action (`apply` non-null); rationale
  reads *"materials_ready=blocked, apply_ready=ready"*. Dashboard Build Queue →
  Proposed shows the option with an **enabled** Apply (label = option label, not
  "Apply not wired").
- **Round-trip (throwaway save):** click Apply on the short-stock option → RIMAPI
  `blueprint-group/validate` passes (`can_place_all`), `place` drops the blueprint
  group, readback reports `thing_id`s; the colony then hauls/builds normally.

## Tasks.md
Linked from "Captured by /todo" as `willie-apply-ignore-materials`.
