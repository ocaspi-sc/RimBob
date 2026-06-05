# Plan: Fix "Mark hunt" apply — designate exact animals by id (drop the area-purity gate)

## Motivation

User report (verbatim): *"'Mark Hunt' apply button doesn't work. it shows this obscure message instead: 'Hunt area now contains unsafe or non-wild animals.'"*

The Chef minister emits a **Mark hunt** advice action (kind `mark_hunt_area`) listing the exact wild-animal `TargetIds` to hunt. Pressing **Apply** frequently fails with `stale_advice` → "Hunt area now contains unsafe or non-wild animals." (and its sibling message "Hunt area now contains extra animals not covered by this advice."). Hunt apply is effectively unreliable: the message is opaque, and the user did nothing wrong.

## Root cause

The apply path designates a **rectangular area**, not the specific animals, so it has to defend that rect against contamination — and RimWorld animals roam constantly, so the rect gets contaminated between advice generation and the Apply click.

- `ApplyHuntAsync` sends a rect to RIMAPI — [Src/ApiHost/AssistedApplyService.cs:374](Src/ApiHost/AssistedApplyService.cs):
  ```csharp
  await rimApi.DesignateAreaAsync(apply.MapId, "Hunt", apply.Rect.X1, apply.Rect.Z1, apply.Rect.X2, apply.Rect.Z2, ct);
  ```
  RIMAPI's area-hunt marks **every** wild animal whose cell falls in the rect — [OrderService.cs:72](../RIMAPI-for-RimBob/Source/RIMAPI/RimworldRestApi/Services/OrderService.cs) iterates cells and designates each `Pawn` with `RaceProps.Animal && Faction != Faction.OfPlayer`. It can't be told "only these animals."

- Because the designation is indiscriminate, the apply layer guards the rect with a **whole-rect purity gate** — [Src/ApiHost/AssistedApplyService.cs:350](Src/ApiHost/AssistedApplyService.cs):
  ```csharp
  IReadOnlyList<AnimalRecord> animalsInRect = state.Animals.Value.Animals
      .Where(animal => IsInside(apply.Rect, animal.Position)).ToList();
  if (animalsInRect.Any(animal => !FoodHuntSafety.IsLowRiskTarget(animal, state.AnimalDefs.Value)))
      return Response("stale_advice", "Hunt area now contains unsafe or non-wild animals.", ...);   // line 354
  IReadOnlyList<AnimalRecord> eligibleInRect = animalsInRect.Where(... IsLowRiskTarget ...).ToList();
  if (eligibleInRect.Any(animal => !targetIds.Contains(animal.Id)))
      return Response("stale_advice", "Hunt area now contains extra animals not covered by this advice.", ...);   // line 360
  ```
  These two gates reject the apply if **anything** other than the exact target set is standing in the rect: a predator, a wounded/bonded wild animal, or even a *different healthy wild animal not in the advice*. (Tame pets are already skipped mod-side by `Faction != OfPlayer`, so the practical contaminant is a wild animal — most damagingly a predator the gate is right to refuse hunting via an area drag.)

- The rect is the **bounding box** of the target animals at briefing-derivation time — [Src/StateStore/Derivations/FoodBriefingDerivation.cs:568](Src/StateStore/Derivations/FoodBriefingDerivation.cs) (`RectFor(selected.Select(a => a.Position))`), bounded only by `MaxHuntRectArea = 120` cells ([Src/Common/Advice/AdviceAction.cs:163](Src/Common/Advice/AdviceAction.cs)). 120 cells is large (e.g. 12×10). Apply also re-refreshes live state first (`RefreshForValidationAsync`, [:343](Src/ApiHost/AssistedApplyService.cs)), so it validates against the *latest* positions — by which time any roaming wild animal that wandered into that box trips the gate.

Net: the button "doesn't work" not because of a narrow code defect but because **area designation is the wrong primitive for "hunt these specific animals."** The advice already carries the exact animal ids (`apply.TargetIds` / `AnimalIds`, built at [Rules.cs:953](Src/Ministers/Food/Rules.cs)), but the area path can't use them, so it falls back to a fragile bounding-box purity tax.

## Fix

Designate the **exact target animals by id**, the same way unforbid already targets exact thing ids. Add a per-animal hunt endpoint to the RIMAPI fork, point `ApplyHuntAsync` at it, and delete the two rect-contamination gates. A predator or stray wild animal wandering through the bounding box becomes irrelevant — we never touch it. The risk policy stays entirely RimBob-side (`FoodHuntSafety`): RimBob sends only low-risk wild target ids, so predators are never in the request; the mod stays a dumb executor with a wild-animal sanity guard.

This spans **two repos** and lands as two slices:

---

## Slice 1 — RIMAPI fork: per-animal hunt endpoint

Repo: `C:\dev\RIMAPI-for-RimBob` (`ocaspi-sc/RIMAPI-for-RimBob`, RimWorld 1.6). Mirror the existing `/api/v1/order/unforbid` thing-id endpoint ([OrderService.UnforbidThings:104](../RIMAPI-for-RimBob/Source/RIMAPI/RimworldRestApi/Services/OrderService.cs)).

New route: `POST /api/v1/order/designate/hunt` accepting `{ map_id, thing_ids[] }`, designating `DesignationDefOf.Hunt` on exactly those animals.

1. **DTOs** — [Models/DesignateRequestDto.cs](../RIMAPI-for-RimBob/Source/RIMAPI/RimworldRestApi/Models/DesignateRequestDto.cs) (alongside `UnforbidThingsRequestDto`/`UnforbidThingsResponseDto`):
   ```csharp
   public class HuntThingsRequestDto { public int MapId { get; set; } public List<string> ThingIds { get; set; } = new(); }
   public class HuntThingsResponseDto {
       public int Requested { get; set; }
       public int Designated { get; set; }        // newly added Hunt designation
       public int AlreadyDesignated { get; set; }  // Hunt already present
       public int NonAnimalTargets { get; set; }   // id resolved but not RaceProps.Animal
       public int PlayerFactionTargets { get; set; } // wild-only guard tripped (tame/colony)
       public int Missing { get; set; }            // id not a spawned pawn on this map
   }
   ```
2. **`IOrderService`** — add `ApiResult<HuntThingsResponseDto> DesignateHuntThings(HuntThingsRequestDto request);` ([Services/Interfaces/IOrderService.cs](../RIMAPI-for-RimBob/Source/RIMAPI/RimworldRestApi/Services/Interfaces/IOrderService.cs)).
3. **`OrderService.DesignateHuntThings`** — validate non-null body, `MaxHuntBatchSize = 12` (mirrors RimBob `MaxHuntTargets`; comment the cross-repo intent), valid map. Resolve each id via spawned animal pawns (`map.mapPawns.AllPawnsSpawned` filtered to `RaceProps.Animal`, indexed by `thingIDNumber`; Codex picks the exact lister). Per id: not found → `Missing`; resolved but not animal → `NonAnimalTargets`; `Faction == Faction.OfPlayer` → `PlayerFactionTargets` (skip — never hunt a colony animal, same guard the area branch uses at [OrderService.cs:79](../RIMAPI-for-RimBob/Source/RIMAPI/RimworldRestApi/Services/OrderService.cs)); else add `Designation(pawn, DesignationDefOf.Hunt)` if `DesignationOn(pawn, DesignationDefOf.Hunt) == null` → `Designated`, otherwise `AlreadyDesignated`.
4. **Lenient result (decision):** return `ApiResult.Ok(response)` with the accounting whenever map + batch size are valid, even if some ids are `Missing`/ineligible — **do not** hard-fail on partial misses the way `UnforbidThings` does. Rationale: RimBob already refreshes live state and pre-filters to present low-risk wild targets immediately before calling, and tolerates a bounded number of vanished targets via `AllowedMissing` (slice 2). Double-gating mod-side would defeat that tolerance and re-introduce all-or-nothing brittleness — the exact failure mode we are removing. Only structural errors (null body, missing map, empty/oversized batch) return `ApiResult.Fail`.
5. **Controller** — add the `[Post("/api/v1/order/designate/hunt")]` handler in [BaseControllers/OrderController.cs](../RIMAPI-for-RimBob/Source/RIMAPI/RimworldRestApi/BaseControllers/OrderController.cs), mirroring the `UnforbidThings` handler.
6. **Mod docs + smoke test** — add the endpoint to `docs/_api_macroses/controllers/OrderController.yml` and a Bruno request under `tests/bruno_api_collection/Order/` next to `Area.yml`. Note the fork's `CHANGELOG.md`.

Mod-side leaves the area endpoint untouched — harvest/mine/deconstruct still use `DesignateArea`. Only hunt gains a per-thing path.

## Slice 2 — RimBob: call the endpoint, delete the rect gates

Repo: `C:\dev\RimBob`. RimBob talks to RIMAPI over HTTP only (no link against the mod — Project Invariant), so this slice compiles and unit-tests against a mocked `RimApiClient` independent of the mod build.

1. **`RimApiClient`** — add `DesignateHuntThingsAsync(int mapId, IReadOnlyList<string> animalIds, CancellationToken ct)` posting `{ map_id, thing_ids }` to `api/v1/order/designate/hunt`. Mirror `UnforbidThingsAsync` ([Src/GameStateSync/RimApiClient.cs:568](Src/GameStateSync/RimApiClient.cs)) — same `EnsureWriteAcceptedAsync` handling.
2. **`ApplyHuntAsync`** ([Src/ApiHost/AssistedApplyService.cs:322](Src/ApiHost/AssistedApplyService.cs)) — make target selection id-based and rect-independent:
   - **Delete** the two rect-contamination gates — the `animalsInRect` purity check ([:350-354](Src/ApiHost/AssistedApplyService.cs)) and the extra-animals check ([:356-360](Src/ApiHost/AssistedApplyService.cs)). They exist only to defend area designation; per-id designation makes them obsolete.
   - **Keep** the id-based survival check, sourced directly from live state by id instead of by rect:
     ```csharp
     IReadOnlyList<AnimalRecord> currentTargets = state.Animals.Value.Animals
         .Where(animal => targetIds.Contains(animal.Id)
                       && FoodHuntSafety.IsLowRiskTarget(animal, state.AnimalDefs.Value))
         .ToList();
     int missing = apply.TargetIds.Count - currentTargets.Count;   // died / tamed / fled / turned risky
     if (missing > AllowedMissing(apply.TargetIds.Count))
         return Response("stale_advice", "Too many hunt targets changed since this advice was issued.", ...);
     if (currentTargets.Count == 0)
         return Response("already_satisfied", "No targeted animals are currently available for hunting.", ...);
     ```
     A target that became non-low-risk (wounded below the health floor, now-manhunter, tamed) correctly drops out and counts toward `missing` — we must not hunt a now-risky animal.
   - **Replace** the `DesignateAreaAsync(..., "Hunt", rect)` call ([:374](Src/ApiHost/AssistedApplyService.cs)) with `DesignateHuntThingsAsync(apply.MapId, currentTargets.Select(a => a.Id).ToList(), ct)`.
   - Keep the cheap up-front `validation_failed` guards ([:329-341](Src/ApiHost/AssistedApplyService.cs)): empty targets, duplicate ids, `MaxHuntTargets`. The `Rect.Area` sanity guard stays as advisory payload validation (the rect no longer drives execution but still rides the payload as a UI/location hint and a "batch is local" sanity bound).
   - Keep `RefreshForValidationAsync`/`RefreshForReadbackAsync` and the success payload (`Response("applied", "Hunt designation submitted for {n} animal target(s).", ... rect = apply.Rect)`). `apply.Rect` stays in the response for the dashboard map hint.
3. **Advice payload is unchanged** — `MarkHuntAreaApply` keeps `Rect` + `TargetIds` ([Src/Common/Advice/AdviceAction.cs](Src/Common/Advice/AdviceAction.cs), built at [Rules.cs:936](Src/Ministers/Food/Rules.cs)). No generator change, no schema/wire-persisted change.

## Out of scope / non-goals

- **No change to the area endpoint** (`DesignateArea` / `DesignateAreaAsync`) — harvest, mine, deconstruct keep using it. Only hunt moves to per-thing.
- **Risk policy stays RimBob-side.** Do not add hunt-risk filtering to the mod beyond the existing `RaceProps.Animal && Faction != OfPlayer` wild-animal sanity guard. `FoodHuntSafety` remains the single source of "what is safe to hunt."
- **Do not retune limits** — `MaxHuntTargets` (12), `MaxHuntRectArea` (120), `AllowedMissing` / `MaxMissingTargetFraction` (0.25) are unchanged. The bug is the primitive (area vs id), not the thresholds.
- **No advice-generator change** — target selection, rect derivation, and the `MarkHuntAreaApply` payload are untouched. `Rect` stays (UI hint + locality bound).
- **No persisted-state or replay-corpus regen.** The RimBob→RIMAPI call changes (a live HTTP write), but no persisted snapshot, advice schema, or wire-persisted shape changes.
- **Harvest apply untouched** — that path has its own (separate) readiness predicate; not in scope here.

## Rejected alternatives

- **Loosen the rect gate** (keep area designation, allow some contaminants): impossible to do safely. Area-hunt marks *every* wild animal in the rect including predators; if we stop refusing a contaminated rect, we start designating predators/non-targets. The gate is correct *given* area designation — the primitive is the problem.
- **Per-cell 1×1 area designations** (one tiny rect per target cell, RimBob-only, no mod change): still racy and unsafe. The target can step off its cell between refresh and the designation tick, and — worst case — a **predator sharing the prey's cell** (exactly the dangerous hunting scenario) would get marked. Reduces but does not remove the contamination class, and adds N HTTP calls. Not robust.
- **RimBob-only fix in general:** there is none that is robust. Targeting exact animals fundamentally requires a per-id designation primitive, which only the mod can provide.

## Tests

RimBob — `AssistedApplyServiceTests` ([Src/Tests/ApiHost/AssistedApplyServiceTests.cs](Src/Tests/ApiHost/AssistedApplyServiceTests.cs)), mock `RimApiClient`:
- **Contaminant in bounding box now succeeds** (the headline regression): live state has a predator and/or an extra healthy wild animal standing inside `apply.Rect`, plus the intended low-risk targets. Assert apply returns `applied` and `DesignateHuntThingsAsync` is called with exactly the target id set — the contaminants are neither refused nor designated. (This replaces the old tests that asserted the "unsafe or non-wild" / "extra animals" rejections.)
- **Target died/tamed/fled** beyond `AllowedMissing` → `stale_advice` ("Too many hunt targets changed…"); within tolerance → `applied` with the surviving ids.
- **Target turned risky** (e.g. health dropped below `MinimumHealthyWildHealth`) drops from `currentTargets` and counts as missing.
- **All targets gone** → `already_satisfied`.
- Up-front guards (empty / duplicate ids / over `MaxHuntTargets`) still → `validation_failed`.
- RIMAPI unavailable / rejected around the designate call still map to `rimapi_unavailable` / `rimapi_rejected`.

RIMAPI fork — service-level test (mirror any existing `UnforbidThings` coverage) for `DesignateHuntThings`: wild animal id → `Designated`; already-designated → `AlreadyDesignated`; colony pet id → `PlayerFactionTargets` (skipped); non-animal / missing id → counted, not designated; result stays `Ok` on partial miss; oversized/empty batch and bad map → `Fail`. Plus the Bruno smoke request.

## Verification (cross-repo, live)

The two slices have a runtime dependency: RimBob's new call only succeeds end-to-end once the fork DLL exposing `/order/designate/hunt` is loaded in RimWorld. Sequence:
1. Land Slice 1 in the fork; rebuild the mod DLL and load it in RimWorld; confirm the route live (Bruno / curl `POST /api/v1/order/designate/hunt`).
2. Land Slice 2 in a RimBob worktree; launch RimBob from that worktree on its own non-`5000` port (per launch-from-worktree practice) pointed at the live fork.
3. In the dashboard, trigger Chef, open a **Mark hunt** advice, and click **Apply** with a predator or stray wild animal deliberately near the target cluster. Confirm: the apply succeeds, the obscure "unsafe or non-wild" message is gone, RimWorld shows Hunt designations on exactly the advised animals (and not on the predator), and the Assisted Apply attempt history / cabinet action history card shows `applied` with the animal count. Verify live state via the apply response + `/api/v1/map/animals` rather than the snapshot export.

## Dashboard reflection

No new panel. The existing Assisted Apply attempt history and the cabinet action history card surface the outcome — they will start showing `applied` ("Hunt designation submitted for N animal targets") where they previously showed `stale_advice`. The success payload still carries `rect` for the map hint.

## Docs

Same-turn doc updates (Documentation Discipline):
- [Docs/design/RimAPI.md](Docs/design/RimAPI.md) — add the `/order/designate/hunt` row to the order endpoint table (near [:208](Docs/design/RimAPI.md)); note it is the per-animal hunt path and that area-hunt is no longer used by RimBob.
- [Docs/design/ministers/food.md](Docs/design/ministers/food.md) — update the hunt-apply description: Mark hunt designates exact animal ids, not a rect; the rect is a UI/locality hint only.
- [Docs/design/advice.md](Docs/design/advice.md) — if it describes hunt apply as an area designation, correct it; keep the Suggest+player-confirmed-apply posture.
- RIMAPI fork: `OrderController.yml` + `CHANGELOG.md` as in Slice 1.

## No compat

No compat code; no wipe-and-regen needed. There is no persisted or wire-persisted shape change — the advice payload (`MarkHuntAreaApply`) is unchanged and the only behavioral change is which live RIMAPI write RimBob issues at apply time. Do not add any `if (area)` fallback or keep the old area-hunt call path "just in case"; delete the rect gates outright.
