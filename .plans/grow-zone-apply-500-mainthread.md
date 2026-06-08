# Grow-Zone Apply 500 — Off-Main-Thread Zone Mutation

**Status:** DIAGNOSED, not started. Root cause proven live on 2026-06-07.
**Owner:** RIMAPI mod (primary) + Willie/AssistedApply (secondary).
**Scope:** Fix the intermittent `500 Internal Server Error` on `POST api/v1/map/zone/growing` that the player hits when clicking Willie's grow-zone Apply, then harden RimBob so the next such failure is self-diagnosing and the now-async create reads back correctly.

---

## Symptom

Clicking Apply on a Willie grow-zone option surfaces:

```
RIMAPI HTTP error at api/v1/map/zone/growing: 500 Internal Server Error
```

S5 (`8448e9e feat(willie): apply grow-zone placements`) is the apply path. The S5 RimBob code is correct — this is a RIMAPI mod bug plus two RimBob robustness gaps.

---

## Root cause (proven)

`MapService.CreateGrowingZone` (`C:/dev/RIMAPI-for-RimBob/Source/RIMAPI/RimworldRestApi/Services/MapService.cs:31-54`) calls `FarmHelper.CreateGrowingZone`, which mutates live game state —

```csharp
var zone = new Zone_Growing(map.zoneManager);
map.zoneManager.RegisterZone(zone);
foreach (var cell in cells) { ... zone.AddCell(cell); }
zone.SetPlantDefToGrow(plantDef);
```

— **directly on the HTTP listener thread, with no main-thread marshaling.** Every other RIMAPI write marshals game-state mutation onto RimWorld's main thread via `LongEventHandler.ExecuteWhenFinished(() => { ... })` (e.g. the drop-pod write at `MapService.cs:456`, plus `WindowService`, `OverlayService`, `MapHelper:1596`). `CreateGrowingZone` is the odd one out.

RimWorld game state is single-threaded. Concurrency outcome:

- **Game paused** → main thread idle → off-thread `zoneManager` mutation does not race → `200`.
- **Game running (unpaused)** → main thread is ticking maps / zones / regions while the listener thread mutates `zoneManager` → race (collection-modified / null mid-tick) → unhandled throw → `500`.

The controller (`MapController.cs:185`) has no try/catch, so the throw becomes a bare 500.

### Live evidence (2026-06-07, RIMAPI `localhost:8765`, map id 0, `is_paused: true`)

| Probe | Payload | Result |
|---|---|---|
| 1-cell | `Plant_Rice` rect `(5,5)-(5,5)` | **200** — created "Growing zone 2" id 3 |
| user's option 1 | `Plant_Rice` rect `119,121-124,126` (36 cells) | **200** — created "Growing zone 3" id 4 |

Both succeeded **because the game was paused.** The exact rect the player's Apply used returns 200 when paused, 500 when the world is ticking. That is the race signature.

Also confirmed ruled-out: wire shape is correct (`map_id`/`plant_def`/`point_a`/`point_b` snake_case binds via RIMAPI `SnakeCaseContractResolver`); DTO is rect-based (`PointA`/`PointB`), not cell-list (the bruno `Create Growing Zone.yml` fixture sending `Cells[]` is stale); `MapId` mismatch yields 200 "Map not found", not 500; unknown/occupied cells yield 200 Fail, not 500.

### Cleanup debt from diagnosis
Two stray growing zones were created live during diagnosis (no API delete endpoint exists for growing zones — only stockpile): "Growing zone 2" (1 cell @ `5,5`) and "Growing zone 3" (36 cells @ `119,121-124,126`). Delete in-game.

---

## Locked Decisions

- Primary fix lives in **RIMAPI**, not RimBob. RimBob's S5 validation/wire/readback design is correct.
- Mirror the **existing** RIMAPI marshaling pattern (`LongEventHandler.ExecuteWhenFinished` + inner try/catch + `LogApi.Error`). Do not invent a new dispatcher.
- Marshaling makes the create **fire-and-forget on a later main-thread pump**, so the endpoint can no longer build the post-create `GrowingZoneDto` synchronously. Return an accepted/ok response; **RimBob owns readback** (it already re-reads zones). Do not block the listener thread waiting on the main thread.
- No-compat: this is a behavior fix, not a wire change. No new fields required on success; keep the response envelope shape.

---

## Slice 1 — RIMAPI: marshal grow-zone creation onto the main thread (root cause)

In `MapService.CreateGrowingZone`, keep the cheap up-front validation on the listener thread (map lookup, `PointA/PointB` null guard, plant-def existence) returning `ApiResult.Fail` synchronously, then wrap the **zone mutation** (`FarmHelper.CreateGrowingZone` body) in `LongEventHandler.ExecuteWhenFinished(() => { try { ... } catch (Exception ex) { LogApi.Error(...); } })`, matching `MapService.cs:456-509`.

Return contract options:
- **(A, preferred)** Return `ApiResult.Ok()` (accepted) immediately after queueing; RimBob reads back the zone itself. Simplest, matches the drop-pod precedent, removes the synchronous readback that currently runs off-thread (`GetGrowingZoneById` → `GetZoneFertility`/`GetSoilType` also touch grids off-thread today).
- **(B)** Marshal and block the listener thread on a completion handle to still return the `GrowingZoneDto`. More code, reintroduces a wait; only do this if a caller other than RimBob needs the synchronous body.

Pick A unless a second consumer needs the inline DTO.

Move the **whole** state-touching path (including `GetGrowingZoneById` readback if kept) inside the marshaled block — today even the post-create fertility/soil reads run off-thread.

Verify: with the game **unpaused**, repeated `POST api/v1/map/zone/growing` over growable unzoned rects return success and create exactly one zone each, no 500 across N attempts. Paused behavior unchanged.

---

## Slice 2 — RimBob: stop swallowing the RIMAPI error body (diagnosability)

The player saw `500 Internal Server Error` with no cause because `RimApiClient.EnsureWriteAcceptedAsync` raises `RimApiException` from status alone and drops the response body. RIMAPI returns a JSON envelope on 500 (`{"success":false,"errors":["Internal server error: <message>"],...}` — confirmed live on the zones GET). 

Fix: in `EnsureWriteAcceptedAsync` (`Src/GameStateSync/RimApiClient.cs`), read the response content on failure and include any `errors[]`/message in the thrown `RimApiException`. Then `AssistedApplyService.ApplyCreateGrowingZoneAsync`'s `catch (RimApiException ex) → Response("rimapi_rejected", ex.Message, …)` surfaces the real reason in `apply_result`, dashboard-visible. Applies to all write endpoints, not just grow-zone.

Tests: a 500 with an `errors[]` body yields a `RimApiException` whose message contains the server text; existing write tests still pass.

---

## Slice 3 — RimBob: fix grow-zone readback (confirmed always-inconclusive bug + timing)

**Confirmed root cause (2026-06-07, code-traced).** `AssistedApplyService.HasMatchingGrowingZone` matches only via `zone.Cells ⊆ target` or `zone.Bounds ⊇ rect`. But `MapAggregateMapper.FromZones` derives `MapZoneRecord.Cells`/`Bounds`/`PlantDef` entirely from `ZoneDto.Cells`, and the live `GET /api/v1/map/zones` returns **no `cells`, no `bounds`, no `plant_def`** in the list (only `id`/`cells_count`/`label`/`type` — the mapper itself falls back to `CellsCount`, so the omission is by design). Therefore `HasMatchingGrowingZone` returns `false` for every freshly created zone → **readback returns `readback_inconclusive` even when the write succeeded** (proven: paused-game writes return 200 and create the zone, yet RimBob could never confirm it). This bites *now*, independent of Slice 1.

**Part A (independent — the real fix):** stop matching on unavailable cells/bounds. Snapshot the set of existing growing-zone ids (type `GrowingZone`/`Zone_Growing`) during the pre-write validation refresh; after the post-write `RefreshForReadbackAsync`, treat success as "a growing zone id is present now that wasn't before" — optionally tightened by `CellCount == apply.TargetCount`. Optionally confirm plant def via the per-zone growing read / farm summary (`crop_types[].zone_id` carries `plant_def_name`), but do not depend on `/map/zones` carrying `plant_def`. Keep `already_satisfied` semantics working off the same id/count signal rather than cell coverage.

**Part B (depends on Slice 1 — timing):** once Slice 1 makes the create fire-and-forget on a later main-thread pump, the new-zone id may not be visible on the first readback refresh. Add a brief bounded poll (e.g. up to ~3 refreshes / short delay) before concluding; if still absent, return `readback_inconclusive` with "creation queued; not yet confirmed" wording rather than implying failure. Scope the poll to the grow-zone path.

Tests: post-write state with a new `GrowingZone` id (not present pre-write) returns `applied`; unchanged zone set returns `readback_inconclusive`; (Part B) a readback that misses first then hits on a later refresh returns `applied`. Do not regress other applies.

---

## Slice 4 — RimBob: S5 minor review fixes (from `8448e9e` review)

Independent of the threading work; pure quality/robustness on the landed S5 code.

- **Non-rectangular option silently un-appliable.** `MinisterOfWillie.GrowingZoneApplyActionForOption` returns `null` when `rect.Area != cells.Count` (holes / L-shape), so the option still renders but shows no Apply and no reason. Latent today (solver emits solid rects) but confusing if the solver evolves. Add a visible note on the option/advice body ("non-rectangular region — place manually") instead of a silent drop. Matches the rect-only `CreateGrowZoneAsync` contract.
- **Empty-crop existing zone over-matches.** `AssistedApplyService.HasMatchingGrowingZone` treats a growing zone whose `PlantDef` is null/blank as matching **any** `plant_def`, returning `already_satisfied` even when the requested crop differs — a misleading message. Either require plant-def equality, or make the message state that an unset-crop zone already covers the cells.
- **Readback always-inconclusive — CONFIRMED real bug, promoted to Slice 3.** (Was flagged here as "verify"; code-tracing confirmed it. See Slice 3 Part A.)
- **Missing reject-path tests.** Add focused `AssistedApplyServiceTests` cases against the existing `MinimalRefreshHandler`: `NotGrowable`, `RectOutsideTerrain`, `AlreadySatisfied`, `TargetCount != Rect.Area`, over-`MaxGrowingZoneCells`, and advice-expired (`StaleAdviceIfExpired`). Core happy-path / occupied / rejected / serialization are already covered.

---

## Slice 5 — RIMAPI: auto-increment version + report build commit SHA

Independent of the threading fix, but a **direct verification aid for Slice 1**: the whole 500 diagnosis was slowed by not knowing which `RIMAPI.dll` was running (source clean, dll dated Jun 5). A version+SHA readout makes "is the fixed build actually loaded?" a single GET.

Reuse the **existing** `GET /api/v1/version` (`GameController.cs:31`) — do not add a route. Today it returns `VersionDto { Version(unset), RimWorldVersion, ModVersion(from About.xml ModMetaData), ApiVersion }`. The csproj carries a manual `<Version>1.9.0</Version>` (`RimApi.csproj:9`).

- **Capture build identity at compile time.** Add an MSBuild step (in `RimApi.csproj`, runs before compile) that captures `git rev-parse --short HEAD`, a dirty flag, `git rev-list --count HEAD` (monotonic build number, no stored state), and build UTC timestamp, emitting them as a generated `BuildInfo` constants class (or assembly metadata attributes) compiled into `RIMAPI.dll`. Make it resilient when git is absent (fallback `"unknown"`), and don't fail the build.
- **Increase version each build.** Derive `InformationalVersion = {Version}+{commitCount}.{shortSha}{-dirty?}` so it changes whenever HEAD moves; the build timestamp distinguishes byte-identical-source rebuilds. Leave About.xml `ModVersion` as the human-facing release string.
- **Surface it.** Extend `VersionDto` with `BuildCommitSha`, `BuildNumber`, `BuildTimestamp` (and populate the currently-unused `Version` from the informational version); fill them in `GetVersion` from `BuildInfo`. The `/api/v1/version` response cache (5 min) is fine.

Reconciles the existing `rimapi-version-endpoint` Tasks inbox item — this slice is its detailed form.

Tests/verify: build twice across two commits and confirm `GET /api/v1/version` reports the new `build_commit_sha`/`build_number`; absent-git build still compiles with `"unknown"`.

---

## Sequencing & independent slices

You asked which slices are independent. By dependency:

- **Independent — start any order, parallelizable:**
  - **Slice 1** — RIMAPI main-thread marshal (root cause). *RIMAPI repo.*
  - **Slice 2** — RimBob surface the 500 body. *RimBob repo.*
  - **Slice 3 Part A** — RimBob readback matching fix (new-zone-id detection; confirmed bug, bites now). *RimBob repo.*
  - **Slice 4** — RimBob S5 minor fixes. *RimBob repo.* (its sub-items are also mutually independent)
  - **Slice 5** — RIMAPI version + build SHA. *RIMAPI repo.*
- **Dependent:**
  - **Slice 3 Part B** — RimBob readback eventual-consistency poll — *depends on Slice 1* (only matters once the create is fire-and-forget). Land after/with Slice 1.

Grouped by repo for batching: **RIMAPI** = {1, 5} (independent of each other); **RimBob** = {2, 3A, 4} independent now, {3B} after 1.

Soft ordering hint (not a hard dep): do **Slice 5 first** so you can confirm the **Slice 1** rebuild is the one RimWorld loaded.

RIMAPI changes (1, 5) require a mod rebuild + RimWorld reload to take effect (`RIMAPI.dll` is loaded by the running game).

**RimWorld is currently closed** (user, 2026-06-07) — all *code* work can proceed; the *live* verification steps below are deferred until RimWorld is relaunched (use the run-from-worktree flow on its own port).

---

## Verification (live)

With the game **unpaused** and a real colony:
1. Run a cabinet cycle so Willie emits grow-zone options (`GET /api/ministers/willie/zone-requests` → `status: options`).
2. Click Apply on an option in the dashboard.
3. Confirm `apply_result` is `applied` and `GET api/v1/map/zones?map_id=<live>` shows the new growing zone with the right plant def and rect.
4. Repeat several times unpaused to confirm the race is gone (pre-fix this intermittently 500s).

Per MEMORY "verify with JSON first": confirm via the zone-requests / zones JSON, not just the dashboard card.

---

## Non-Goals

- No change to S5 validation semantics (growability/occupancy/staleness gating stays).
- No grow-zone **delete** endpoint (separate request; would also ease diagnosis cleanup).
- No wider audit of every RIMAPI write for main-thread safety in this slice (grow-zone only); flag others if found.
- No switch to cell-list payload; rect (`PointA`/`PointB`) is the live DTO contract.

---

## Summary — RimBob slices 2, 3A, 4 (landed 2026-06-08)

**Motivation.** Clicking Willie's grow-zone Apply returned a bare `500 Internal Server Error` with no cause, and even a *successful* create could never be confirmed. The root-cause 500 fix is a RIMAPI-repo change (Slice 1, separate); this slice ships the three independent **RimBob-side** hardening pieces so the failure is legible and the readback is correct.

**Context.** Builds on S5 (`8448e9e`). Root cause + full plan above. Slice 1 (RIMAPI main-thread marshal), Slice 3 Part B (eventual-consistency poll, depends on Slice 1) and Slice 5 (RIMAPI version+SHA) were follow-ups — see the independence table and landed sections below.

**Scope (shipped).**
- **Slice 2** — `RimApiClient.EnsureWriteAcceptedAsync` now reads the failed response body and folds the RIMAPI `errors[]`/message into the `RimApiException`, so `apply_result` shows the real cause (all write endpoints; robust to empty/non-JSON body).
- **Slice 3A** — `AssistedApplyService` grow-zone readback no longer matches on `zone.Cells`/`Bounds` (the live `/map/zones` returns neither). Success = a `GrowingZone` id present post-write that was absent pre-write, tightened by `CellCount == TargetCount`; `already_satisfied` uses the same plant-def + count signal. No poll added (that is Part B). Applied result now carries `zone_id`.
- **Slice 4** — `MinisterOfWillie.GrowingZoneApplyActionForOption` keeps non-rectangular options visible with a manual-placement note instead of a silent `null`; `HasMatchingGrowingZone` no longer over-matches a blank-`PlantDef` zone; six reject-path tests added (NotGrowable, RectOutsideTerrain, AlreadySatisfied, TargetCount≠Rect.Area, over-cap, advice-expired).

**How to verify.**
  - Tests: `dotnet test Src\RimBob.sln --filter "kind!=reach"` → green (625/625); focused `~AssistedApplyServiceTests|~MinisterOfWillieTests|~RimApiClientTests` → 101/101. (The 8 unfiltered reds are pre-existing `[Trait("kind","reach")]` `AdviceForNewColony1Tests`, unrelated.)
  - Dashboard (deferred until RIMAPI Slice 1 lands + RimWorld running): click a Willie grow-zone Apply; `apply_result` should be `applied` with `zone_id`, or a `rimapi_rejected` message carrying the RIMAPI error text instead of a bare 500.
  - Files: `Src/GameStateSync/RimApiClient.cs`, `Src/ApiHost/AssistedApplyService.cs`, `Src/Ministers/Willie/MinisterOfWillie.cs`.

**Codex run:** `20260607-215455-grow-zone-apply-500` · branch `codex/prompt-20260607-215455-grow-zone-apply-500` (squashed + removed) · landed commit `12ca7b8`

---

## Slice 1 landed (RIMAPI repo, 2026-06-08)

Root-cause 500 fix landed in `C:\dev\RIMAPI-for-RimBob` master, commit `700ad68` `fix(map): marshal grow-zone create onto main thread`. `MapService.CreateGrowingZone` now validates synchronously (map / point_a-point_b / plant def), queues the zone mutation through `LongEventHandler.ExecuteWhenFinished` (try/catch + `LogApi.Error`), and returns `ApiResult.Ok()` immediately (return type `ApiResult<GrowingZoneDto>` → `ApiResult`; the RimBob client checks only the success envelope and does its own readback). Compile-verified `dotnet build -c Debug` → 0 errors.

**Deploy (still pending):** `dotnet build -c Release-1.6` writes `1.6\Assemblies\RIMAPI.dll` and requires RimWorld closed (the running game locks the dll), then reload the save. **Live verify after deploy:** run a cabinet cycle, click a Willie grow-zone Apply with the game **unpaused**, repeat several times — pre-fix this intermittently 500s; post-fix it should not.

---

## Slice 5 landed (RIMAPI repo, 2026-06-08)

RIMAPI version/build identity landed in `C:\dev\RIMAPI-for-RimBob` master, commit `3996023` `feat(game): expose RIMAPI build metadata`. The existing `GET /api/v1/version` route now populates the previously-empty `version` field and adds `build_commit_sha`, `build_number`, `build_timestamp`, and `build_dirty`; JSON remains snake_case through the existing resolver. `RimApi.csproj` generates `BuildInfo.g.cs` before compile from `git rev-parse --short HEAD`, `git rev-list --count HEAD`, `git status --porcelain --untracked-files=no`, and UTC build time, with `"unknown"`/`0` fallbacks when git metadata is unavailable. The generated assembly informational version uses the same `{Version}+{build_number}.{build_commit_sha}{-dirty?}` string as the endpoint.

**Verification.** `dotnet build Source\RIMAPI\RimApi.csproj -c Debug` and `dotnet build Source\RIMAPI\RimApi.csproj -c Release-1.6` both passed. The post-commit `Release-1.6` generated `BuildInfo` reported `InformationalVersion = "1.9.0+247.3996023"`, `BuildCommitSha = "3996023"`, `BuildNumber = 247`, and `BuildDirty = false`; built DLL SHA256 `0EFB6AF5E70F7A83E87AF3B04710B82D3EF36892E75FCA4632058E7C65C178FA`.

**Live deploy verified.** RimWorld was later closed, the rebuilt `1.6\Assemblies\RIMAPI.dll` was copied over the installed mod DLL at `D:\Games\SteamLibrary\steamapps\common\RimWorld\Mods\RIMAPI-for-RimBob\1.6\Assemblies\RIMAPI.dll`, and RimWorld was restarted. Live `GET http://localhost:8765/api/v1/version` reported `version: "1.9.0+247.3996023"`, `build_commit_sha: "3996023"`, `build_number: 247`, `build_dirty: false`, `mod_version: "1.9.0"`, and `rim_world_version: "1.6.4633"`. Live `GET http://localhost:8765/api/v1/dev/endpoints` reported 182 endpoints and included `/api/v1/version`, `/api/v1/map/zone/growing`, and `/api/v1/order/designate/hunt`; `GET http://localhost:8765/api/v1/game/state` confirmed `program_state: "Playing"`, `map_count: 1`, `colonist_count: 3`, and `is_paused: true`.

**Remaining:** Slice 3B (RimBob readback eventual-consistency poll — only now relevant since create is async).
