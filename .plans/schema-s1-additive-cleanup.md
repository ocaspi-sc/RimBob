# Schema S1 — Additive Types + AdviceAction Cleanup

> Child slice of [`schema-landing.md`](schema-landing.md) (slice **S1** only).
> Design source of record: [`willie-advice-schema.md`](willie-advice-schema.md).
> This is the gimp-able implementation plan for S1. **S2 (flag typed request
> arrays) and S3 (apply per-kind split) are explicitly out of scope.**

## Motivation

The advice/flag schema redesign is being landed slice-by-slice, safest first.
S1 is the low-risk, no-behavior-change slice: add the new (dormant) multi-option
and blueprint-group record types, and remove two dead `AdviceAction` fields
(`icon`, `reason`). Landing it early gets the cleanup in and sets the additive
contract before more ministers depend on the old shape.

## Context

- Core types: `Src/Common/Advice/AdviceItem.cs`, `Src/Common/Advice/AdviceAction.cs`
  (`AdviceAction` carries `icon: IconRef?` and `reason: string?`; `IconRef` is
  declared in `Src/Common/Advice/ResourceRequest.cs` and is still used by
  `ResourceRequest` — do NOT delete `IconRef`, only stop using it on `AdviceAction`).
- Design shapes for the new records: `willie-advice-schema.md` §3 (`AdviceOption`),
  §4 (`BlueprintGroup`, `BlueprintAsset`, `MapCell`, `MaterialEstimate`).
- Consumers that build/parse `AdviceAction`: `Src/LlmGateway/AdviceActionNormalizer.cs`,
  `Src/LlmGateway/AdviceResponseNormalizer.cs`, `Src/Ministers/Food/Rules.cs`.
- Dashboard mirrors: `Dashboard/src/types/advice.ts`,
  `Dashboard/src/components/minister/MinisterAdviceView.tsx`,
  `Dashboard/src/dashboard/semanticIcons.ts` (only if it reads `action.icon`).
- Tests: `Src/Tests/LLM/AdviceNormalizationTests.cs`, `Src/Tests/Food/FoodRulesTests.cs`.
- Repo conventions (AGENTS.md): .NET 9, `sealed record` + `[property: JsonPropertyName(...)]`,
  spell out types (no `var`), pure domain projects take no external deps.

## Scope

In scope:
- `AdviceItem`: add optional `Options : IReadOnlyList<AdviceOption>?` (default null,
  JSON name `options`). Place it in the optional trailing-parameter region so
  positional construction stays valid.
- New records in `Src/Common/Advice/` (mirror the existing record style):
  - `AdviceOption { id, label, summary, blueprint_group: BlueprintGroup, est_materials: IReadOnlyList<MaterialEstimate>, tradeoff_note: string? }`
  - `BlueprintGroup { label, map_id: int, assets: IReadOnlyList<BlueprintAsset> }`
  - `BlueprintAsset { role: string, def_name: string, stuff_def_name: string?, cell: MapCell, rotation: int }`
  - `MapCell { x: int, z: int }`
  - `MaterialEstimate { def_name: string, count: int }`
  - These are **dormant** — nothing constructs them yet. They must compile and serialize.
- `AdviceAction`: remove `Icon` (`IconRef?`) and `Reason` (`string?`) fields.
- `AdviceActionNormalizer.cs`: stop setting `icon`/`reason` when building `AdviceAction`.
- `AdviceResponseNormalizer.cs`: tolerant parsing must **silently ignore** legacy
  `icon`/`reason` keys on action payloads (do not throw / reject).
- `Src/Ministers/Food/Rules.cs`: drop `icon`/`reason` from any `AdviceAction` construction.
- Dashboard: remove `icon`/`reason` from the `AdviceAction` TS type; add optional
  `options?` to the `AdviceItem` TS type; `MinisterAdviceView.tsx` stops rendering
  action `reason`/`icon`; `options` renders nothing while null (no picker UI this slice).
- Update `AdviceNormalizationTests.cs` + `FoodRulesTests.cs` expectations.

Out of scope (do NOT touch):
- S2: `AgentFlag` typed request arrays, `ResourceRequest`/`ResourceRequestKind` retirement.
- S3: `AdviceActionApply` per-kind split, `place_blueprint_group`, Assisted Apply path.
- Placement Solver, RIMAPI fork, Willie minister, briefing.
- Do not add an options picker UI or any rendering for the new dormant types.

## Approach

1. Add the five new records (one file, e.g. `Src/Common/Advice/AdviceOption.cs`, or
   grouped sensibly) following the existing `sealed record` + `JsonPropertyName` style.
2. Add `Options` to `AdviceItem` in the optional tail; fix any positional construction sites.
3. Remove `Icon`/`Reason` from `AdviceAction`; fix all construction sites (normalizers, Food rules).
4. Make `AdviceResponseNormalizer` tolerant of (ignore) legacy `icon`/`reason` action keys.
5. Update the dashboard TS types + `MinisterAdviceView.tsx` accordingly.
6. Update the two test files; remove assertions on `icon`/`reason`.
7. Run Verification; fix until green; self-review the diff for stray changes.

## Verification

- `dotnet build Src/RimBob.sln`  → builds clean.
- `dotnet test Src/Tests/RimBob.Tests.csproj`  → all green.
- `npm.cmd run build` from `Dashboard`  → TypeScript compiles.
- `.\run-rimbob.ps1` (use a non-5000 `-ListenUrl` if 5000 is busy), then check
  `/api/system/health` is reachable and the dashboard serves. Stop the spawned Host after.

Pass criteria: all four green; no behavior change (Food advice still emits/renders,
minus the removed `reason`/`icon` action lines).

## Where to see it (dashboard)

No new visible surface this slice — it is additive (dormant types) + field removal.
Human check: Food minister Advice cards still render, now without per-action
`reason`/`icon` secondary detail; dashboard builds and serves. The `options[]`
picker UI is a later slice (after the Placement Solver + fork land).

## Open questions

- New-record JSON casing: match the existing `AdviceItem`/`AdviceAction` convention
  (explicit `[property: JsonPropertyName("snake_case")]` per field). If the project
  has a global snake-case enum/converter, follow whatever the existing records do.
- If removing `AdviceAction.Reason`/`Icon` reveals a consumer not listed above,
  fix it to keep the build green and note it in the final message (do not expand
  scope beyond making S1 compile + pass).

---

## Summary (landed 2026-05-22)

**Motivation.** Land the advice/flag schema redesign slice-by-slice, safest
first. S1 is the no-behavior-change slice: add the dormant multi-option +
blueprint-group record types and remove the two dead `AdviceAction` fields
(`icon`, `reason`). First gimp run of the schema work.

**Context.** Design source: [`willie-advice-schema.md`](willie-advice-schema.md).
Parent landing plan: [`schema-landing.md`](schema-landing.md) (S2 = flag typed
request arrays, S3 = apply per-kind split — both deferred). The dormant
`blueprint_group` / `options[]` exist for the future Placement Solver.

**Scope (shipped).**
- `AdviceItem.Options` (nullable) added; new dormant records `AdviceOption`,
  `BlueprintGroup`, `BlueprintAsset`, `MapCell`, `MaterialEstimate`.
- `AdviceAction.Icon` + `.Reason` removed; `AdviceActionNormalizer` no longer
  reads/emits those keys (legacy keys silently dropped — covered by new tests).
- Cascading consumer/test/dashboard updates to keep green (IMinisterRules,
  AdviceSnapshotPolicy, FoodChainModelBuilder, AdviceTextStyleWarnings,
  food.system.md, dashboard advice.ts/system.ts/MinisterAdviceView/
  MinisterRulesView, ~9 test files).
- S2/S3 untouched; no options picker UI; `IconRef` preserved (ResourceRequest still uses it).

**How to verify (human).**
- Dashboard: Food minister → Advice tab; cards render without per-action
  `reason`/`icon` secondary lines. No new picker (options dormant).
- Commands: `dotnet build Src/RimBob.sln`; `dotnet test Src/Tests/RimBob.Tests.csproj` (317/317);
  `npm.cmd run build` (Dashboard); `.\run-rimbob.ps1` → `/api/system/health`.
- Files: `Src/Common/Advice/AdviceItem.cs`, `AdviceAction.cs`, `AdviceOption.cs`.

**Codex run:** `20260522-125209-schema-s1-additive-cleanup` · branch
`codex/prompt-20260522-125209-schema-s1-additive-cleanup` · verifier Adherent: yes ·
landed commit `7d0818ced8ce4e3582d359cd3e7cbd705208672e`.
