# RIMAPI Grow-Zone Create: Resolve Plant Def on Main Thread

**Status:** PLANNED - design only; do not implement until the user says to execute it.
**Repo:** `C:\dev\RIMAPI-for-RimBob` (the RIMAPI mod fork — **not** RimBob). Codex implements here.
**Owner:** RIMAPI map write path.
**Scope:** Fix `POST /api/v1/map/zone/growing` returning `500 "Invalid plant definition: Plant_Rice"` for a valid def, by moving the plant-def resolution off the HTTP listener thread onto the game main thread.

---

## Motivation

RimBob's live proof of the grow-zone `create_growing_zone` Assisted Apply hit:

```
RIMAPI HTTP error at api/v1/map/zone/growing: 500 Internal Server Error: Invalid plant definition: Plant_Rice
```

`Plant_Rice` is the correct vanilla RimWorld defName, and the whole RimBob side (Chef `designate_zone_req`, Willie `create_growing_zone` option, `AssistedApplyService` → `CreateGrowZoneAsync`) is proven correct up to the wire. The write is blocked entirely by this RIMAPI-side validation bug.

---

## Root Cause (confirmed)

`MapService.CreateGrowingZone` (`Source/RIMAPI/RimworldRestApi/Services/MapService.cs:44-51`) resolves the plant def **synchronously on the HTTP listener thread**:

```csharp
var plantDefName = request.PlantDef;
var plantDef = string.IsNullOrWhiteSpace(plantDefName)
    ? null
    : DefDatabase<ThingDef>.GetNamedSilentFail(plantDefName);
if (plantDef == null || plantDef.plant == null)
    return ApiResult.Fail($"Invalid plant definition: {plantDefName}");
```

`DefDatabase<ThingDef>.GetNamedSilentFail` returns null on the listener thread even for a valid, loaded def. Evidence this is thread context — not a missing def or a binding bug:

- `GET /api/v1/def/all` resolves `Plant_Rice` fine (it marshals to the main thread via `LongEventHandler.ExecuteWhenFinished` in `GameDataService`).
- `BuilderService` blueprint-place resolves `ThingDef` via the same `GetNamedSilentFail` **inside its main-thread spawn block** and works (`BuilderService.cs:111/127/133`).
- Request binding is fine: `map_id`/`point_a`/`point_b` bound (their validations passed) and the error echoes `Plant_Rice`, so `PlantDef` bound too.
- Running DLL is current (`build_commit_sha 3996023`, RimWorld `1.6.4633`).

Commit `700ad68 fix(map): marshal grow-zone create onto main thread` marshaled the zone **mutation** to the main thread but deliberately "hoist[ed] cheap validation (… plant def) synchronously" — that hoist is the defect. Every other def resolution in the mod runs on the main thread; this is the only one that does not.

---

## Approach

Edit `MapService.CreateGrowingZone` only:

1. Keep the cheap, non-DefDatabase synchronous checks: map exists, `point_a`/`point_b` present, and `plant_def` is a non-empty string. These do not touch `DefDatabase` and can stay on the listener thread.
2. **Remove** the synchronous `DefDatabase<ThingDef>.GetNamedSilentFail` resolution and its `ApiResult.Fail("Invalid plant definition…")` return.
3. Let the plant-def resolution happen on the main thread inside the existing `LongEventHandler.ExecuteWhenFinished` block. `FarmHelper.CreateGrowingZone` already resolves the def and null-guards there (`FarmHelper.cs:166-168`, returns null and deletes the half-built zone on an invalid def). On null, the existing `catch`/`LogApi.Error` path logs it.
4. Keep the optimistic `ApiResult.Ok()` return. Per `700ad68`, the RimBob client checks only the success envelope and does its own zone readback, so a genuinely invalid def surfaces as a readback miss on the RimBob side plus a RIMAPI log line — no silent corruption.

Net change is a few lines in one method. Do not change `FarmHelper`, the DTO, the return type, or the RimBob side.

---

## Scope / Non-Goals

- **In:** `Source/RIMAPI/RimworldRestApi/Services/MapService.cs` `CreateGrowingZone` only.
- **Out:** `FarmHelper.CreateGrowingZone`, `CreateGrowingZoneRequestDto`, controller, RimBob repo, any other endpoint.
- Do not reintroduce a listener-thread `DefDatabase` lookup anywhere.
- Do not change the `ApiResult` (no `GrowingZoneDto`) contract `700ad68` established.

---

## Verification

- Build the mod for the live target: `dotnet build` the RIMAPI solution/project for the `1.6` assembly (the running RimWorld is `1.6.4633`, loading `1.6/Assemblies/RIMAPI.dll`). Build must be clean.
- The mod has no meaningful unit coverage for DefDatabase/main-thread behavior; verification is build + live retry.
- **Live retry (requires RimWorld restart to load the rebuilt DLL):** with a colony open, drive a RimBob grow-zone apply (or the bruno `Create Growing Zone` request) for `Plant_Rice` over a valid rect → expect success, no `Invalid plant definition`, and the new zone visible in `GET /api/v1/map/zones?map_id=0` / RimBob readback.

---

## Open Questions

- Should the cheap sync path still reject an obviously-empty `plant_def` with a clear 400 (keep the `IsNullOrWhiteSpace` guard), while deferring real def validity to the main thread? (Plan assumes yes.)
- Is a synchronous main-thread "resolve-and-wait" helper preferable long-term (so the API can return a real invalid-def error instead of optimistic Ok)? Out of scope for this fix; note if the mod already has one.
