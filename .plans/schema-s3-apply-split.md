# Schema S3 — AdviceActionApply Per-Kind Split

> Child slice of [`schema-landing.md`](schema-landing.md) (slice **S3**).
> Design source: [`willie-advice-schema.md`](willie-advice-schema.md) §2 (refined to a
> single polymorphic hierarchy). Builds on landed **S1** (`7d0818c` — dormant
> `BlueprintGroup`/`BlueprintAsset`/`MapCell` records exist) and **S2** (`8676d59`).
> **Highest-risk slice: touches the live M4.5 Assisted Apply path.**

## Motivation

`AdviceActionApply` is a god-record: one flat type with 11 nullable fields serving
4 apply kinds, so every consumer must guess which fields are live for which kind.
Split it into a `System.Text.Json`-polymorphic hierarchy — shared fields on the
base, per-kind fields on derived types — and add the new `place_blueprint_group`
payload (inert until the RIMAPI fork blueprint-group endpoints exist). This is the
last schema slice; it unblocks the eventual blueprint-placement Apply.

## Context

- `Src/Common/Advice/AdviceAction.cs`: current `AdviceActionApply` (flat record:
  `Kind`, `Label`, `TargetSummary`, `MapId`, `TargetCount`, `Rect?`, `TargetIds?`,
  `ThingIds?`, `ThingTargets?`, `WorkbenchBuildingId?`, `RecipeSelectorKey?`,
  `RepeatMode?`); `AdviceApplyKind` (MarkHarvestArea/MarkHuntArea/UnforbidThings/
  UpsertProductionBill); `AdviceThingApplyTarget`; `AssistedApplyLimits`.
- The 4 existing apply kinds are **live** (M4.5): `AssistedApplyService.cs`,
  `Endpoints/AdviceApplyEndpoints.cs`, `Program.cs` (JSON/DI), `Endpoints/SystemEndpoints.cs`.
- Emitters: `Src/Ministers/Food/Rules.cs`, `Src/StateStore/Derivations/FoodBriefingDerivation.cs`.
- Persistence: advice snapshots (with their apply payloads) are serialized in the
  unified minister output store + replay corpus. A wire-shape change means old
  persisted apply payloads will not round-trip (see Open Questions).
- Dashboard: `Dashboard/src/types/advice.ts` (apply types), the Apply control
  component / `MinisterAdviceView.tsx`.
- `BlueprintGroup`/`BlueprintAsset`/`MapCell`/`MaterialEstimate` already exist (S1, dormant).
- Repo conventions: .NET 9, `sealed record`, snake_case JSON, no `var`.

## Scope

In scope:
- Replace flat `AdviceActionApply` with a polymorphic hierarchy in `AdviceAction.cs`:
  ```
  [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
  [JsonDerivedType(typeof(MarkHarvestAreaApply),      "mark_harvest_area")]
  [JsonDerivedType(typeof(MarkHuntAreaApply),         "mark_hunt_area")]
  [JsonDerivedType(typeof(UnforbidThingsApply),       "unforbid_things")]
  [JsonDerivedType(typeof(UpsertProductionBillApply), "upsert_production_bill")]
  [JsonDerivedType(typeof(PlaceBlueprintGroupApply),  "place_blueprint_group")]   // NEW
  public abstract record AdviceActionApply(string Label, string TargetSummary, int MapId);
  ```
  Derived payloads carry only their own fields:
  - `MarkHarvestAreaApply` / `MarkHuntAreaApply`: `Rect`, `TargetIds`, `TargetCount`.
  - `UnforbidThingsApply`: `ThingIds` (or `ThingTargets`), `TargetCount`.
  - `UpsertProductionBillApply`: `WorkbenchBuildingId`, `RecipeSelectorKey`, `RepeatMode`, `TargetCount`.
  - `PlaceBlueprintGroupApply` (NEW): `BlueprintGroup`, `AssetCount`.
- Keep `AdviceApplyKind` as the discriminator enum; add `PlaceBlueprintGroup`.
- `AssistedApplyService` / `AdviceApplyEndpoints`: switch on the derived types to
  execute the 4 existing kinds **with identical behavior**. `place_blueprint_group`
  is **inert**: no executor (fork endpoints absent), so it is never Apply-eligible —
  exposes no Apply button; if somehow invoked, return a clear "not executable yet".
- `Program.cs`: ensure the polymorphic `JsonSerializerOptions` are wired for Host serialization.
- Food emitters (`Rules.cs`, `FoodBriefingDerivation.cs`): construct the derived
  payload types instead of the flat record.
- Dashboard: apply TS types → discriminated union by `kind`; Apply control reads
  the per-kind shape; render no Apply for `place_blueprint_group`.
- Tests: `AssistedApplyServiceTests`, `ReplayCorpusWriterTests`, any apply/snapshot tests.

**No legacy/compat code.** Per AGENTS.md ("we don't care about legacy or breaking
changes or compatibility. be brave"): do **not** add a tolerant-fallback parser
for the old flat `AdviceActionApply` payload in the snapshot store or replay
reader. If pre-S3 persisted snapshots fail to load after upgrade, the human wipes
the stale snapshot file and the cabinet regenerates on next cycle. No
compat-only code paths.

Out of scope:
- The RIMAPI fork blueprint-group endpoints + the `place_blueprint_group` executor
  (separate plan: `rimapi-blueprint-groups-and-planning-overlay.md`). S3 lands the
  payload type inert.
- Placement Solver, Willie minister, briefing.
- Any change to the 4 existing kinds' actual apply behavior/allowlist semantics.

## Approach

1. Define the abstract polymorphic base + 5 derived records (reuse landed `BlueprintGroup`).
2. Migrate `AssistedApplyService`/endpoints to pattern-match the derived types; preserve
   each existing kind's validate→write→read-back exactly. Wire `place_blueprint_group` as inert.
3. Confirm Host `JsonSerializerOptions` handle the polymorphism (attributes should suffice).
4. Update Food emitters to the derived types.
5. Update dashboard apply union + control.
6. Update tests. Run Verification; fix until green; self-review for any consumer still
   assuming the flat shape.

## Verification

- `dotnet build Src/RimBob.sln`  → clean.
- `dotnet test Src/Tests/RimBob.Tests.csproj`  → all green.
- `npm.cmd run build` from `Dashboard`  → TS compiles.
- `.\run-rimbob.ps1` (non-5000 `-ListenUrl`): `/api/system/health` reachable; dashboard serves.
  If a live colony is available, exercise one existing Apply (e.g. `mark_harvest`)
  end-to-end and confirm it still validates + writes + reads back. Stop the Host after.

Pass: all green; the 4 live apply kinds behave identically; `place_blueprint_group`
serializes but exposes no Apply.

## Where to see it (dashboard)

Food → Advice: existing Apply buttons (unforbid / mark_harvest / mark_hunt / cook
bill) still work unchanged — this is an internal type restructure, not a behavior
change. No `place_blueprint_group` Apply appears (inert until the fork lands).

## Open questions

- **STJ polymorphism wiring.** Prefer `[JsonPolymorphic]`/`[JsonDerivedType]`
  attributes on the base over manual `JsonSerializerOptions`; confirm the Host's
  serializer (and any source-gen context, if used) honors them.
- **Inert exposure.** Confirm the existing Apply-eligibility gate naturally excludes
  `place_blueprint_group` (no executor) so no Apply button renders.
- **Bundle timing.** S3 may be landed standalone (type inert) OR bundled with the
  fork blueprint-group slice so `place_blueprint_group` ships with a real executor.
  Standalone is fine and lower-risk; the fork slice then only adds the executor.

---

## Summary (landed 2026-05-23)

**Motivation.** Split the `AdviceActionApply` god-record (11 nullable fields
serving 4 apply kinds) into an STJ-polymorphic hierarchy: shared fields on the
base, per-kind fields on derived sealed records. Add the new
`PlaceBlueprintGroupApply` inert (no executor until the RIMAPI fork
blueprint-group endpoints land). Final schema slice.

**Context.** Design source: [`willie-advice-schema.md`](willie-advice-schema.md)
§2. Builds on landed **S1** (`7d0818c`) + **S2** (`8676d59`). Lands per the new
AGENTS.md brave-no-compat expansion (just added): no tolerant-fallback parsers
for the old flat payload — wipe-and-regen on upgrade.

**Scope (shipped).**
- `AdviceActionApply` → abstract `[JsonPolymorphic]` base (`label`,
  `target_summary`, `map_id`) + 5 sealed derived records:
  `MarkHarvestAreaApply`, `MarkHuntAreaApply`, `UnforbidThingsApply`,
  `UpsertProductionBillApply`, **`PlaceBlueprintGroupApply` (NEW, inert)**.
- 4 live apply kinds preserved EXACTLY in behavior — `AssistedApplyService`
  pattern-matches the derived types; identical validate→write→read-back.
- `place_blueprint_group` is **inert**: type serializes; eligibility gate
  excludes it; dashboard suppresses its Apply button.
- Food emitters (`Rules.cs`) updated to construct derived types.
- Dashboard: apply TS types → discriminated union; `isExecutableApply` gate.
- Tests: new `AdviceActionApplySerializationTests`; updated
  `AssistedApplyServiceTests`, `MinisterOutputStoreTests`, `ReplayCorpusWriterTests`,
  `FoodRulesTests`.
- **Zero compat code.** Initial Codex pass mistakenly added a tolerant-fallback
  converter for legacy flat snapshots (in line with the original prompt). The
  verifier flagged it as a violation of the new AGENTS.md brave rule; a Resume
  removed the converter + its test + the `[JsonConverter]` attribute. Re-verify
  Adherent: yes.

**How to verify (human).**
- Dashboard: existing Apply buttons (unforbid / mark_harvest / mark_hunt / cook
  bill) still work unchanged. No Apply renders for `place_blueprint_group`.
- Commands: `dotnet build Src/RimBob.sln`; `dotnet test Src/Tests/RimBob.Tests.csproj` (319/319);
  `npm.cmd run build`; `.\run-rimbob.ps1` → `/api/system/health`.
- Files: `Src/Common/Advice/AdviceAction.cs`, `Src/ApiHost/AssistedApplyService.cs`,
  `Src/Tests/Coordination/AdviceActionApplySerializationTests.cs`.

**Upgrade note.** Pre-S3 persisted minister-output snapshots and replay-corpus
records carry the old flat `AdviceActionApply` shape and **will fail to
deserialize**. Per brave-no-compat: on first Host load after the upgrade, wipe
stale snapshots (delete the affected files under the runtime data/replay roots
if Host throws on load). The cabinet regenerates them on next cycle.

**Codex run:** `20260524-213721-schema-s3-apply-split` · branch
`codex/prompt-20260524-213721-schema-s3-apply-split` · commits `f7bcdaf` (split)
+ `bdadd4d` (compat revert) · verifier Adherent: yes (after revert) ·
landed commit `4927741fcbc27f108595c3fbef72937d745dce83`.
