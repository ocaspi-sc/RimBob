# Advice / Flag Schema Landing — Plan

> Implementation plan. Lands **code** in RimBob. Implements the schema designed in
> [`basie-advice-schema.md`](basie-advice-schema.md) (advice/output side) +
> [`basie-request-taxonomy.md`](basie-request-taxonomy.md) (request/flag side).
> Sliced by risk — **gimp one slice at a time**, keep build/tests green between.

---

## 0. Goal / why landable now

Core-contract cleanup, independent of the Willie (Construction) minister, the
Placement Solver, and the briefing. Touches `Src/Common/Advice`,
`Src/Common/Ministers/AgentFlag.cs`, and downstream consumers (Food, LLM
normalizers, Mayor flag digest, Assisted Apply, dashboard mirrors). Doing it
before more ministers depend on the old shape avoids a bigger migration later.

Design source of record stays the two anchors; exact C# lives in source after landing.

---

## 1. Slices (gimp independently, in order)

### S1 — additive types + `AdviceAction` cleanup  *(SAFE — gimp first)*

No behavior change. Adds dormant types + removes two unused fields.

- `Src/Common/Advice/AdviceItem.cs`: add `Options : IReadOnlyList<AdviceOption>?` (nullable, default null).
- New records (`Src/Common/Advice/`): `AdviceOption`, `BlueprintGroup`, `BlueprintAsset`, `MapCell`, `MaterialEstimate`. (Unused until the Placement Solver + fork exist — dormant scaffolding.)
- `Src/Common/Advice/AdviceAction.cs`: **remove** `Icon` and `Reason`.
- `Src/LlmGateway/AdviceActionNormalizer.cs`, `AdviceResponseNormalizer.cs`: stop emitting `icon`/`reason`; tolerant parser **ignores** legacy `icon`/`reason` keys (don't reject).
- `Src/Ministers/Food/Rules.cs`: drop `icon`/`reason` from `AdviceAction` construction.
- Dashboard: `Dashboard/src/types/advice.ts` (remove icon/reason on the action type; add optional `options`), `MinisterAdviceView.tsx` (stop rendering action reason/icon; `options` render is a no-op while null).
- Tests: `Src/Tests/LLM/AdviceNormalizationTests.cs`, `Src/Tests/Food/FoodRulesTests.cs` — update expectations.

Risk: **low.**

### S2 — flag typed request arrays  *(FOUNDATIONAL)*

Replaces the generic `requests[]` with typed arrays; makes Food emit a typed
`building_request` (the freezer ask Willie will later read).

- New records (`Src/Common/Advice/`): `BuildingRequest`, `LaborRequest`, `ItemRequest`, `AttentionRequest`; enums `BuildingClass`, `RoomClass`; small value types (`CapacityNeed`, `AdjacencyHint`, `PowerNeed`, `TempNeed`, `Urgency`, `Deadline`). Per [`basie-request-taxonomy.md`](basie-request-taxonomy.md) §1.
- `Src/Common/Ministers/AgentFlag.cs`: replace `Requests : IReadOnlyList<ResourceRequest>?` with four nullable arrays — `BuildingRequests`, `LaborRequests`, `ItemRequests`, `Attention`.
- **Retire** `ResourceRequest`, `ResourceRequestKind`, `IconRef` (`Src/Common/Advice/ResourceRequest.cs`) from the flag path.
- `Src/LlmGateway/ResourceRequestNormalizer.cs`: rewrite to emit the typed arrays (or split per-array); update `AdviceResponseNormalizer.cs` wiring.
- `Src/Ministers/Food/Rules.cs`: Food flag emission → emit `building_request` / `labor_request` instead of generic `ResourceRequest`.
- Mayor flag digest: update only if it reads `flag.Requests` (it mainly reads severity/summary — confirm and touch if needed).
- Dashboard: `Dashboard/src/types/advice.ts` (flag `requests[]` → typed arrays), `MinisterAdviceView.tsx` flags section render.
- Tests: `Src/Tests/Food/FoodRulesTests.cs`, `Src/Tests/LLM/AdviceNormalizationTests.cs`, any flag tests.

Decision applied: `requested_from` stays a **string** (not a `MinisterRef` enum) for now.

Risk: **medium** — Food emit + normalizers + Mayor consume + dashboard.

### S3 — `AdviceActionApply` per-kind split  *(DEFER — touches live Assisted Apply)*

- `Src/Common/Advice/AdviceAction.cs`: `AdviceActionApply` → STJ-polymorphic base (`[JsonPolymorphic]`/`[JsonDerivedType]`, discriminator `kind`) + 5 derived: `MarkHarvestAreaApply`, `MarkHuntAreaApply`, `UnforbidThingsApply`, `UpsertProductionBillApply`, **`PlaceBlueprintGroupApply` (new)**. Extend `AdviceApplyKind` with `place_blueprint_group`.
- `Src/ApiHost/AssistedApplyService.cs`, `Endpoints/AdviceApplyEndpoints.cs`, `Program.cs` (poly registration), `Endpoints/SystemEndpoints.cs`: read the new payloads.
- `Src/Ministers/Food/Rules.cs` + `Src/StateStore/Derivations/FoodBriefingDerivation.cs`: update apply construction.
- `place_blueprint_group` executor = **validate-only stub** until the fork blueprint-group endpoints land.
- Dashboard apply handling; Tests: `AssistedApplyServiceTests`, `ReplayCorpusWriterTests`.

Risk: **higher** — live M4.5 feature. Bundle with the RIMAPI fork blueprint-group work.

---

## 2. Keep-green (every slice)

- Sync worktree with `master` before any verification build (AGENTS GIT rule).
- `dotnet build Src/RimBob.sln`
- `dotnet test Src/Tests/RimBob.Tests.csproj`
- `npm.cmd run build` from `Dashboard`
- `.\run-rimbob.ps1` then smoke-check Host `/api/system/health`.

---

## 3. Recommended gimp order

1. **S1** — safe, mechanical; gets the cleanup + dormant types in.
2. **S2** — the contract Willie depends on; Food starts emitting typed `building_request`s.
3. **S3** — last, bundled with the fork blueprint-group endpoints.

---

## 4. Decided / open

- `requested_from` = string (decided).
- S3 apply encoding = STJ `[JsonPolymorphic]` (advice-schema Q4 — confirm at S3).
- `options[]` / `blueprint_group` land **dormant** in S1; no consumer until the Placement Solver + fork exist.

## 5. Out of scope (separate plans)

Placement Solver ([`placement-solver.md`](placement-solver.md)), fork endpoints
([`rimapi-blueprint-groups-and-planning-overlay.md`](rimapi-blueprint-groups-and-planning-overlay.md)),
Willie minister, Construction briefing.
