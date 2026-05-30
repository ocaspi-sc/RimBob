# Willie Freezer Apply Readiness — unblock `apply_ready` and resolve `materials_ready`

## Summary

The Willie freezer advice item (`willie_freezer_request_active`) carries 2 validated
placement options but the Apply button remains locked: `apply_ready=blocked` and
`materials_ready=unknown`. Both states are hardcoded stubs placed intentionally
during the `willie-option-apply-payload` slice (commit `5dd05f5`). This plan
identifies the two independent root causes and describes the edits to make
`materials_ready` compute from live stock and `apply_ready` follow from it.

## Current Evidence

**Live snapshot** (`GET http://localhost:5000/api/ministers/willie/snapshot`,
build `edd17b90`, map_id=0):

```
Rationale: "…Placement solver: 2 validated options; materials_ready=unknown,
apply_ready=blocked."
```

Both options:
```json
"readiness": {
  "draftable": "ready",
  "placement_valid": "ready",
  "materials_ready": "unknown",
  "apply_ready": "blocked"
}
```

Both options carry real `est_materials`:
```
BlocksGranite:90, ComponentIndustrial:3, Steel:106, WoodLog:25
```

### Code paths

**Gate 1 — `apply_ready` hardcoded Blocked:**

`Src\Ministers\Willie\PlacementSolver.cs:174–182`
```csharp
notes.Add("group_place_apply_out_of_scope");
return new PlacementResult(
    ...
    ApplyReady: PlacementReadiness.Blocked);   // ← unconditional
```

The comment `group_place_apply_out_of_scope` is a deliberate deferral note added
when the apply executor was not yet wired. The executor (`AssistedApplyService.
ApplyBlueprintGroupAsync`) **has since landed** in `AssistedApplyService.cs:479–603`
with a full validate → place → readback cycle. The note and the hardcoded
`Blocked` are now stale.

**Gate 2 — `materials_ready` always Unknown:**

`Src\Ministers\Willie\PlacementSolver.cs:363–379` (`MaterialReadiness` overload):
```csharp
if (cost.Count == 0) return PlacementReadiness.Ready;
if (spec.MaterialsOnHand.Count == 0) return PlacementReadiness.Unknown;  // ← hits here
```

`spec.MaterialsOnHand` comes from `PlacementSpec.FromBuildingRequest`
(`Src\Ministers\Willie\PlacementSpec.cs:32`):
```csharp
MaterialsOnHand: request.MaterialsOnHand ?? [],
```

The source `BuildingRequest` is built by `Food.Rules.FreezerSupportRequests`
(`Src\Ministers\Food\Rules.cs:284–298`) with no `MaterialsOnHand` argument
(the parameter is optional / nullable, so it defaults to `null`). The solver
therefore receives an empty list and always returns `Unknown`.

**Stock data exists but is not wired in:**

`ColonyState.StoredResources.Value.CountByDef` is a `Dictionary<string, int>`
(def → non-forbidden StackCount sum) built by `MapAggregateMapper.FromStoredResources`
(`Src\StateStore\Ingestion\MapAggregateMapper.cs:187–190`). It is already populated
at every ingestion cycle and used by `AssistedApplyService` for other apply kinds.
`PlacementSolver` is injected via DI but currently receives no `ColonyState`
reference and no material inventory.

**`apply_ready` logic:**

`MinisterOfWillie.EnrichAdviceItem` (`Src\Ministers\Willie\MinisterOfWillie.cs:183–192`)
stamps every option with the *same* top-level `PlacementResult.ApplyReady` value.
There is no per-option override: all options get `Blocked` today because the
`PlacementResult` carries a single `ApplyReady` field that the solver sets
unconditionally.

## Root Cause (one line)

`apply_ready` is hardcoded `Blocked` (`PlacementSolver.cs:182`) and `materials_ready`
is always `Unknown` because `PlacementSpec.MaterialsOnHand` is never populated
(Food `Rules.cs:298` emits the freezer `BuildingRequest` without stock data, and the
solver has no path to `ColonyState`).

## Scope

### Part A — Remove the `apply_ready` hardcode and derive it from `materials_ready`

- **`Src\Ministers\Willie\PlacementSolver.cs`**  
  In the success path (line 174–182): remove `notes.Add("group_place_apply_out_of_scope")`.
  Replace `ApplyReady: PlacementReadiness.Blocked` with a derived value:
  `applyReady = materialsReady is Ready or Unknown ? Ready : Blocked`.  
  Rationale: `Unknown` means "can't check stock" (not "definitely short"), so the
  Apply button should be enabled for the player to attempt; the live validate gate
  in `AssistedApplyService` is the real safety net. `Blocked` (materials definitively
  short) keeps Apply locked.

  The `NoFit` path (`PlacementSolver.cs:407–412`) legitimately keeps
  `ApplyReady: Blocked` — no options were validated, so there is nothing to place.
  Leave it unchanged.

### Part B — Feed real stock into `MaterialReadiness`

Two sub-options; **Option B1 is preferred** (simpler, no solver DI change):

**Option B1 — Populate `MaterialsOnHand` in the `BuildingRequest`**

- **`Src\StateStore\Derivations\WillieBriefingDerivation.cs`** (or wherever the
  Willie briefing is derived): no change needed — the briefing does not carry
  construction-material stock today, and adding it here would bloat the briefing
  shape unnecessarily.

- **`Src\Ministers\Willie\MinisterOfWillie.cs` — `TrySolvePlacementAsync`
  (line 138)**:  
  Before calling `PlacementSpec.FromBuildingRequest(request)`, read
  `colonyState.StoredResources.Value.CountByDef` (already injected as
  `colonyState` at line 16) and build the `MaterialsOnHand` list:
  `countByDef.Select(kvp => new MaterialHint(kvp.Key, kvp.Value)).ToList()`.  
  Pass this list into the request (or directly into `PlacementSpec`) so the solver
  receives it.  
  The cleanest extension: add an optional `materialsOnHand` parameter to
  `PlacementSpec.FromBuildingRequest` or construct `PlacementSpec` directly in
  `TrySolvePlacementAsync` with the stock override. Either approach avoids a DI
  change to `PlacementSolver`.

- **`Src\Common\Advice\FlagRequests.cs`** — no change; `BuildingRequest.MaterialsOnHand`
  already exists as a nullable field.

**Option B2 — Inject `ColonyState` into `PlacementSolver`** (alternative, not preferred)  
Inject `ColonyState` into `PlacementSolver` via the constructor and read
`StoredResources.Value.CountByDef` directly inside `MaterialReadiness`. This is
heavier (requires updating the DI registration and the solver test harness) and
couples the solver to `ColonyState` when the minister already has the data.

### Part C — Update the Rationale note format (minor)

- **`Src\Ministers\Willie\MinisterOfWillie.cs` — `NoteFor` (line 224–229)**:  
  The Rationale suffix already prints `materials_ready=...` and `apply_ready=...`
  from `PlacementResult`; no format change needed. Once Parts A and B land the
  values will change from `unknown/blocked` to `unknown/ready` (if stock read fails
  or stock is zero) or `ready/ready` (if materials are sufficient), or
  `blocked/blocked` (if stock is definitively short for every option). No edit
  required here unless the phrasing should distinguish "unknown=can't check" from
  "unknown=not yet computed".

## Test Plan

- **`Src\Tests\Willie\PlacementSolverTests.cs`**  
  - Add a case: solver returns options + `materialsReady=Unknown` (empty stock list
    from spec) → `applyReady=Ready` (no longer `Blocked`).  
  - Add a case: solver returns options + `materialsReady=Blocked` (spec has stock
    that is definitively short for all options) → `applyReady=Blocked`.  
  - Add a case: solver returns options + `materialsReady=Ready` → `applyReady=Ready`.  
  - Verify existing `NoFit` cases still produce `applyReady=Blocked`.

- **`Src\Tests\Willie\MinisterOfWillieTests.cs`**  
  - Existing test at line 53 asserts `materials_ready = "ready"` with a
    `MaterialsReady: PlacementReadiness.Ready` fixture; confirm it stays green.  
  - Add a test: `colonyState.StoredResources` carries `BlocksGranite:100`; the
    spec's `MaterialsOnHand` is populated from it; `materialsReady` reflects the
    comparison against `est_materials`.

- **Live verification (after rebuild + host restart)**  
  - `GET /api/ministers/willie/snapshot` → `rationale` suffix shows
    `materials_ready=ready` (or `unknown` if stock is zero/empty) and
    `apply_ready=ready` for the freezer options.  
  - Clicking Apply in the Build Queue dashboard for one of the two Starter freezer
    options triggers `POST /api/advice/willie_freezer_request_active/actions/{i}/apply`
    and returns `{"status":"applied", ...}` or `{"status":"stale_advice", ...}`
    (if the cells are now occupied), not `validation_failed`.

## Out of Scope

- RIMAPI / RIMAPI fork changes — the place endpoint already exists and the executor
  (`AssistedApplyService.ApplyBlueprintGroupAsync`) is already wired.
- Dashboard / frontend changes — the Apply button logic reads `apply.apply_ready`
  from the action payload, not the option's readiness field; once the executor
  returns `"applied"` the UI updates correctly with no frontend change.
- Per-option `apply_ready` override — all options today share the same
  `PlacementResult`-level readiness value; per-option granularity (e.g. option A
  has stock, option B does not) is a future enhancement.
- Non-freezer option types — the `applyReady` derivation logic is in `PlacementSolver`
  and applies generically once non-freezer options are wired; no extra work needed.
- `PlacementResult.NoFit` path readiness values — legitimately blocked; leave as-is.

## Assumptions

- `ColonyState.StoredResources.Value.CountByDef` is populated by the time
  `MinisterOfWillie.TrySolvePlacementAsync` runs (it is refreshed every ingestion
  cycle; the minister runs after ingestion).
- The `est_materials` `def_name` values produced by `FreezerTemplate` match the
  keys in `CountByDef` case-insensitively (already ensured by `MaterialReadiness`'s
  `OrdinalIgnoreCase` comparison at `PlacementSolver.cs:373`).
- The apply executor (`AssistedApplyService.ApplyBlueprintGroupAsync`) is the live
  safety net; `apply_ready=Ready` does not bypass it — the executor still runs
  validate → place → readback on every click.
