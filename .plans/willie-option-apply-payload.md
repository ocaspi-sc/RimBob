# Willie Option Apply Payload — real `place_blueprint_group` round-trip

> **Backend wiring plan.** Lands **RimBob** code (C#: `Src/`). Makes the Build
> Queue **Apply** button a real player-click round-trip: emit a per-option
> `place_blueprint_group` action payload **and** replace the refusing executor
> stub with a live RIMAPI **validate → place → readback**.
>
> **No RIMAPI fork work.** The fork already exposes
> `POST /api/v1/builder/blueprint-group/place`
> ([`BuilderController.cs:69`](../../RIMAPI-for-RimBob/Source/RIMAPI/RimworldRestApi/BaseControllers/BuilderController.cs)).
> Two RimBob-side stubs ("…when the Apply path lands") are **stale** — this slice
> retires them. Frontend is already wired; no Dashboard logic change needed.

---

## 0. What already exists vs. what's stale

This is the milestone-exit consent step from
[`willie-meta-plan.md`](willie-meta-plan.md) §1: *player clicks Apply → fork
validate → place*. Everything around it landed; only the Apply leg is stubbed.

**Already wired (do not rebuild):**
- **Apply DTO** — `PlaceBlueprintGroupApply` (S3) +
  `AdviceApplyKind.PlaceBlueprintGroup`
  ([`AdviceAction.cs:110`](../Src/Common/Advice/AdviceAction.cs), enum `:143`).
- **Frontend matcher** — `findBlueprintAction`
  ([`MinisterBuildQueueView.tsx:384`](../Dashboard/src/components/minister/MinisterBuildQueueView.tsx))
  hunts `item.actions[]` for a `place_blueprint_group` apply whose
  `blueprint_group` matches the option (`sameBlueprintGroup` `:396`). When it
  finds one the **Apply button auto-enables** and round-trips through
  `applyAdviceAction(item.id, actionIndex)` → `POST /api/advice/{id}/actions/{i}/apply`.
  The current `"No place_blueprint_group action payload is attached to this option
  yet."` disabled state (`:193`) is the symptom we are clearing — **no Dashboard
  change required**, it lights up the moment the backend emits the action.
- **Asset → DTO mapper** — `RimApiPlacementProbe.ToDto(BlueprintAsset)`
  ([`RimApiPlacementProbe.cs:52`](../Src/GameStateSync/RimApiPlacementProbe.cs))
  already maps a `BlueprintGroup` into `BlueprintGroupValidateRequestDto.Items`
  for the solver's validate call. The place path reuses it.
- **RIMAPI place endpoint** — `POST /api/v1/builder/blueprint-group/place`.
  Request `BlueprintGroupPlaceRequestDto` = the validate request **+** `placement_order`
  (string) **+** `require_all` (bool?). Result `BlueprintGroupPlaceResultDto`:
  `status`, `require_all`, `placement_order`, `items[]`
  (`index`, `item`, `status`, `placed`, `thing_id`, `reason`, `validate`),
  `cost[]`, `validate`
  ([`BuilderDtos.cs:92`/`:127`/`:138`](../../RIMAPI-for-RimBob/Source/RIMAPI/RimworldRestApi/Models/BuilderDtos.cs)).

**Stale / missing (this slice fixes):**
1. **Emission gap.** `MinisterOfWillie.EnrichAdviceItem`
   ([`MinisterOfWillie.cs:174`](../Src/Ministers/Willie/MinisterOfWillie.cs))
   rewrites `item.Options` from the solver result but **never touches
   `item.Actions`**. The freezer item (`willie_freezer_request_active`) ships one
   text-only `PlaceBlueprint` action ([`Rules.cs:128`](../Src/Ministers/Willie/Rules.cs)),
   so the frontend matcher finds nothing.
2. **Client wrapper missing.** `RimApiClient` has validate only; place is a TODO
   ([`RimApiClient.cs:346`](../Src/GameStateSync/RimApiClient.cs)). Place
   request/result DTOs are a TODO too
   ([`BlueprintGroupDto.cs:69`](../Src/GameStateSync/Dtos/BlueprintGroupDto.cs)).
3. **Executor refuses.** `AssistedApplyService` routes the kind to
   `BlueprintGroupNotExecutable` → `validation_failed` "not executable until the
   RIMAPI blueprint endpoints land"
   ([`AssistedApplyService.cs:80`/`:474`](../Src/ApiHost/AssistedApplyService.cs)).

> **Coupling note.** Gap 1 alone is a *regression in honesty*: emitting a payload
> while the executor still refuses turns the honest disabled "Apply not wired"
> into an enabled button that fails on click. So OA2 (emit) and OA3 (executor)
> land **together**; do not ship OA2 without OA3.

---

## 1. Slices

### OA1 — RimBob place client + DTOs
- In [`BlueprintGroupDto.cs`](../Src/GameStateSync/Dtos/BlueprintGroupDto.cs)
  (retire the `:69` TODO): add `BlueprintGroupPlaceRequestDto` (extends/embeds the
  validate request + `placement_order`, `require_all`), `BlueprintGroupPlaceResultDto`,
  and `BlueprintGroupPlaceItemResultDto`, mirroring the RIMAPI shape above with
  `snake_case` `JsonPropertyName`s.
- In [`RimApiClient.cs`](../Src/GameStateSync/RimApiClient.cs) (retire the `:346`
  TODO): add `PostBlueprintGroupPlaceAsync` → `PostEnvelopedAsync` to
  `api/v1/builder/blueprint-group/place`.
- Extend the placement port: add `PlaceAsync(BlueprintGroup, placementOrder,
  requireAll, ct)` to `RimApiPlacementProbe`
  ([`RimApiPlacementProbe.cs`](../Src/GameStateSync/RimApiPlacementProbe.cs)),
  reusing `ToDto(BlueprintAsset)`. Decide: extend `IPlacementValidator` vs. a new
  `IPlacementPlacer` port — **lean new port** so the solver's read-only validator
  contract stays write-free.
- **Tests:** mirror
  [`RimApiClientBlueprintGroupTests.cs`](../Src/Tests/Ingestion/RimApiClientBlueprintGroupTests.cs)
  — posts `snake_case`, parses the enveloped result, throws on envelope failure.
- Risk: low (mechanical mirror of the landed validate wrapper).

### OA2 — Emit per-option `place_blueprint_group` actions
- In `EnrichAdviceItem`
  ([`MinisterOfWillie.cs:174`](../Src/Ministers/Willie/MinisterOfWillie.cs)): for
  each validated `AdviceOption`, append one
  `AdviceAction(AdviceActionKind.PlaceBlueprint, instruction, Owner: "Willie",
  Apply: new PlaceBlueprintGroupApply(Label, TargetSummary, MapId:
  option.BlueprintGroup.MapId, BlueprintGroup: option.BlueprintGroup, AssetCount:
  option.BlueprintGroup.Assets.Count))` to `item.Actions`, **alongside** the
  existing prose action. `Label`/`TargetSummary` drive the button text + tooltip
  (`buttonLabel` reads `apply.label`, `MinisterBuildQueueView.tsx:412`).
- The frontend pairs each option card to its action by `sameBlueprintGroup`
  (map_id + label-or-asset-fingerprint), so per-option `Label` should be
  option-unique (reuse `option.Label`).
- Keep emission unconditional (every emitted option gets an Apply action); the
  executor re-validates live, and readiness pills already signal not-ready. Do
  **not** gate emission on `apply_ready` — that would hide the button exactly when
  the player wants to see why.
- **Tests:** `EnrichAdviceItem` attaches one apply-bearing action per option; the
  attached `blueprint_group` equals the option's; multi-option items attach
  multiple, each matchable.
- Risk: low–medium (the option↔action pairing contract is the only subtlety).

### OA3 — Real group-place executor
Replace `BlueprintGroupNotExecutable`
([`AssistedApplyService.cs:474`](../Src/ApiHost/AssistedApplyService.cs)) with
`ApplyBlueprintGroupAsync`, following the existing apply template
(`ApplyProductionBillAsync` is the closest precedent — refresh, map check, RIMAPI
call, readback, typed response):
1. **Cap gate** — reject if `asset_count > AssistedApplyLimits.MaxBlueprintGroupAssets`
   (new const, ~64 per meta §5) → `validation_failed`.
2. **Refresh-for-validation** (`RefreshForValidationAsync`) + stale/expiry +
   `apply.MapId == state.Map.Value.Id` (reuse existing helpers).
3. **RIMAPI group validate** (existing `PostBlueprintGroupValidateAsync`) as a
   pre-place gate: if `!can_place_all` → `stale_advice` with the first failing
   item's `reason`.
4. **RIMAPI group place** (`PostBlueprintGroupPlaceAsync`, `require_all` +
   `placement_order` per §2) wrapped in the same
   `IsRimApiUnavailable`/`RimApiException` → `rimapi_unavailable`/`rimapi_rejected`
   handling every other apply uses.
5. **Readback** (`RefreshForReadbackAsync`); response `readback` carries placed
   count + per-asset `{index, placed, thing_id, reason}`.
6. **Status map** → existing vocabulary: all placed → `applied`; already on map →
   `already_satisfied`; partial under best-effort → `readback_inconclusive`
   (until a richer status exists); else `rimapi_rejected`/`validation_failed`.
   `ShouldClearAppliedAction` already clears the action on `applied`/`already_satisfied`.
- Add `MaxBlueprintGroupAssets` to `AssistedApplyLimits`
  ([`AdviceAction.cs:146`](../Src/Common/Advice/AdviceAction.cs)).
- **Tests:** extend
  [`AssistedApplyServiceTests.cs`](../Src/Tests/ApiHost/AssistedApplyServiceTests.cs)
  with a stub RIMAPI for placed / validate-rejected / rimapi-unavailable / stale /
  over-cap. Serialization round-trip in
  [`AdviceActionApplySerializationTests.cs`](../Src/Tests/Coordination/AdviceActionApplySerializationTests.cs)
  already covers the apply DTO; add the place **result** DTO if it crosses a wire.
- Risk: medium (the live write + status mapping is the real new behavior).

### OA4 — Verify + close the honesty gap
- No Dashboard logic change expected. Confirm: button enables, label reads the
  option's `apply.label`, success shows placed count, expiry/stale still disable.
- If any "unsupported Apply" copy remains literally in the UI, reconcile it; the
  current code already degrades by data, so this is a read-through, not a rewrite.
- Flip the [`Tasks.md`](../Tasks.md) `willie-option-apply-payload` todo + refresh
  meta-plan §4 `DASH` / §6 wording (the Apply leg is no longer stubbed).
- Risk: low.

---

## 2. Decision — atomicity, order, cap (meta-plan §5)

The RIMAPI place request exposes `require_all` + `placement_order`, which **are**
the meta-plan §5 open decision ("group atomicity on partial fresh-state failure +
`MaxBlueprintGroupAssets`"). This plan resolves them with defaults; flag if you
want the other lean:

- **`require_all` = `true` (atomic) for v1.** A half-placed freezer (floor + 3
  walls, no cooler) is worse than none, and the UI has no per-asset partial
  renderer yet. Meta §5's "best-effort place + per-asset report" is the eventual
  target — defer it to a follow-up once the Build Queue can show partial state.
- **`placement_order` = deterministic `floor → wall → door → cooler/fixture`** —
  matches the frontend role layering (`roleOrder`, `MinisterBuildQueueView.tsx:500`)
  and avoids door-before-wall / fixture-before-floor validation failures.
- **`MaxBlueprintGroupAssets` ≈ 64** (meta §5 lean). A freezer shell is well
  under; the cap guards a pathological solver group.

---

## 3. Out of scope
- **RIMAPI fork changes** — the place endpoint already exists; this is RimBob-only.
- **Non-freezer options** — only the freezer path attaches `options[]` today
  (meta §6 "Non-freezer → solver wiring" is a separate slice). The Apply machinery
  is option-class-agnostic, so non-freezer options inherit it for free once they
  attach.
- **Best-effort / partial-place UI** — deferred with `require_all=true` (§2).
- **Planning-overlay (Cap B) placement** — unrelated write path.

## 4. Verification
- A live freezer `building_request` → Willie Build Queue **Proposed** card shows an
  enabled **Apply**; clicking it round-trips and the freezer blueprint group
  appears on the map (readback shows placed count / thing ids).
- Validate-gate honesty: when the footprint is blocked at click time, Apply returns
  `stale_advice` with the blocking reason, not a half-place.
- Over-cap / unavailable / expired paths return the matching status and never
  write.
- No regression to the other four allowlisted apply kinds (harvest / hunt /
  unforbid / bill) or to Briefing / Rules / Advice tabs.

## 5. HumanTodo capture (already in Tasks.md — relink to this plan)

```
- [ ] willie-option-apply-payload [2026-05-29] #assisted #apply #willie Emit per-option `place_blueprint_group` action apply payloads in MinisterOfWillie.EnrichAdviceItem AND land the real group-place executor (RimApiClient PostBlueprintGroupPlaceAsync + place DTOs + AssistedApplyService validate→place→readback), retiring the BlueprintGroupNotExecutable stub. RIMAPI place endpoint already exists; RimBob-only. Frontend auto-enables (no Dashboard change). Defaults: require_all=true, floor→wall→door order, MaxBlueprintGroupAssets≈64 (meta §5). [plan](.plans/willie-option-apply-payload.md)
```
