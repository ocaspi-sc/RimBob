# Schema S2 — Flag Typed Request Arrays

> Child slice of [`schema-landing.md`](schema-landing.md) (slice **S2** only).
> Design source of record: [`willie-request-taxonomy.md`](willie-request-taxonomy.md)
> (§1 = record/array shapes + enums; §3 = requester→array mapping).
> Builds on landed **S1** (commit `7d0818c`: dormant advice records added,
> `AdviceAction.icon/reason` removed). **S3 (AdviceActionApply per-kind split) is
> out of scope.**

## Motivation

Replace the single generic `AgentFlag.Requests` (a wide `ResourceRequest` record
discriminated by a `kind` enum, mostly-null fields) with **typed request arrays**.
The emitting LLM produces these — "put building needs in `building_requests`" is
far more reliable than a `kind` discriminator + remembering which nullable fields
are legal. Also lets `building_request` get rich enough to express "a freezer for
~200 food near the kitchen by day 15" — which the shared record never could.
Lands the request contract Willie will later consume.

## Context

- `Src/Common/Ministers/AgentFlag.cs`: `Requests : IReadOnlyList<ResourceRequest>?`.
- `Src/Common/Advice/ResourceRequest.cs`: `ResourceRequest`, `ResourceRequestKind`
  (Labor/Tile/Item/Building/Bill/StockpileSpace/Attention/TradeCapacity), `IconRef`.
  After S1, `IconRef` is used **only** by `ResourceRequest` — so retiring
  `ResourceRequest` removes its last user; delete `IconRef` too.
- Emitters/parsers: `Src/Ministers/Food/Rules.cs` (Food emits flags + requests),
  `Src/LlmGateway/ResourceRequestNormalizer.cs`, `Src/LlmGateway/AdviceResponseNormalizer.cs`,
  `Src/LlmGateway/prompts/food.system.md` (if it documents the request schema for the LLM).
- Mayor flag consumption: check whether the Mayor digest reads `flag.Requests`
  (it mostly reads severity/summary — touch only if it reads requests).
- Dashboard: `Dashboard/src/types/advice.ts` (flag `requests[]` type),
  `Dashboard/src/components/minister/MinisterAdviceView.tsx` (flags render),
  `Dashboard/src/types/system.ts` / ANALYTICS "resource-request kinds" if it reads them.
- Exact field lists + enum members + the requester→array mapping: follow
  `willie-request-taxonomy.md` §1 and §3 verbatim as the design contract.
- Repo conventions: .NET 9, `sealed record` + `[property: JsonPropertyName(...)]`,
  spell out types, snake_case JSON matching existing records.

## Scope

In scope:
- New records in `Src/Common/Advice/` per taxonomy §1:
  - `BuildingRequest` (the rich centerpiece — §1a fields: `request`, `reason`,
    `target_class`, `target_def?`, `room_class?`, `capacity_need?`, `adjacency?`,
    `power?`, `temperature?`, `materials_on_hand?`, `urgency?`, `deadline?`,
    `quantity?`, `priority?`, `requested_from?`).
  - `LaborRequest` (§1b: `request`, `reason`, `work_type: WorkType`, `skill?`,
    `quantity?`, `priority?`, `requested_from?`).
  - `ItemRequest` (§1c: `request`, `reason`, `item_def?`, `quantity?`, `priority?`, `requested_from?`).
  - `AttentionRequest` (§1d: `request`, `reason`, `priority?`, `requested_from?`).
  - Enums: `BuildingClass`, `RoomClass` (members per §1a). Value types:
    `CapacityNeed`, `AdjacencyHint`, `PowerNeed`, `TempNeed`, `Urgency`, `Deadline`.
- `AgentFlag.cs`: replace `Requests` with four nullable arrays —
  `BuildingRequests`, `LaborRequests`, `ItemRequests`, `Attention` (JSON
  `building_requests` / `labor_requests` / `item_requests` / `attention`).
- **Delete** `ResourceRequest`, `ResourceRequestKind`, `IconRef` (`ResourceRequest.cs`).
- `ResourceRequestNormalizer.cs`: rewrite to normalize the typed arrays from LLM
  output; tolerant repair path may still accept a legacy generic `requests[]` and
  map each entry into the correct typed array by its old `kind` (per taxonomy §1
  migration table). Wire into `AdviceResponseNormalizer.cs`.
- `Src/Ministers/Food/Rules.cs`: Food's deterministic flag emission → emit
  `building_request` (freezer/cooler/storage) and `labor_request` (work-type) into
  the new arrays instead of generic `ResourceRequest`.
- `food.system.md`: if it documents the request schema for the LLM, update it to the typed arrays.
- Mayor flag digest: update only if it reads `flag.Requests`.
- Dashboard: flag-request TS types → typed arrays; `MinisterAdviceView` flags
  section renders the typed arrays; fix any analytics that read request kinds.
- Tests: `Src/Tests/Food/FoodRulesTests.cs`, `Src/Tests/LLM/AdviceNormalizationTests.cs`,
  any flag/normalizer tests — update to the typed arrays.

Out of scope (do NOT touch):
- S3: `AdviceActionApply` / `AdviceApplyKind` / Assisted Apply path.
- The S1 dormant records (`AdviceOption`/`BlueprintGroup`/etc.) and `AdviceAction`
  beyond what compiles — already landed.
- Placement Solver, RIMAPI fork, Willie minister, briefing.
- Do not invent a `MinisterRef` enum — `requested_from` stays a **string**.

## Approach

1. Add the value types + enums, then the four request records (taxonomy §1).
2. Swap `AgentFlag.Requests` → the four arrays.
3. Delete `ResourceRequest.cs` (record + kind enum + `IconRef`); fix every reference.
4. Rewrite `ResourceRequestNormalizer` to the typed arrays + legacy-generic repair; wire it in.
5. Map Food's current `ResourceRequest` emissions to the right typed array (taxonomy §3).
6. Update `food.system.md` request schema if present.
7. Update Mayor digest only if it reads requests; update dashboard types + render + analytics.
8. Update tests. Run Verification; fix until green; self-review for leftover `ResourceRequest` refs.

## Verification

- `dotnet build Src/RimBob.sln`  → clean.
- `dotnet test Src/Tests/RimBob.Tests.csproj`  → all green.
- `npm.cmd run build` from `Dashboard`  → TS compiles.
- `.\run-rimbob.ps1` (non-5000 `-ListenUrl`), check `/api/system/health` reachable + dashboard serves; stop the Host after.

Pass: all four green; Food still emits flags (now with typed request arrays), dashboard renders them.

## Where to see it (dashboard)

Food minister → Advice tab → the flags section: cross-minister requests now render
grouped by typed array (building / labor / item / attention) instead of a single
`kind`-tagged list. ANALYTICS "resource-request kinds" reflects the new shape.
Induce a Food freezer/cooler need to see a `building_request` populated.

## Open questions

- `requested_from` = string (decided — do not enum it now).
- `BuildingClass` / `RoomClass` membership: use the taxonomy §1a proposed set; if a
  Food emission needs a class not listed, add it and note it (don't block).
- `StockpileSpace` (old kind) folds into `building_requests` with
  `target_class: stockpile`/`shelf` (taxonomy §1 migration). `Tile`/`Bill`/`TradeCapacity` → `attention`.
- Multiple entries per array on one flag are allowed (a flag may carry several building_requests).
- If Food's LLM path doesn't actually emit requests today, keep the LLM/prompt
  changes minimal (deterministic rules emission is the must-have); note what you skipped.

---

## Summary (landed 2026-05-22)

**Motivation.** Replace the single generic `AgentFlag.Requests` (wide
`ResourceRequest` discriminated by `kind`, mostly-null fields) with typed request
arrays. The emitting LLM produces these reliably, and `building_request` can now
express a rich ask ("freezer for ~200 food near kitchen by day 15"). Lands the
cross-minister request contract Willie will later consume.

**Context.** Design source: [`willie-request-taxonomy.md`](willie-request-taxonomy.md)
§1 (shapes) + §3 (requester→array mapping). Builds on landed S1 (`7d0818c`).
`ResourceRequest` fully retired (its last user after S1).

**Scope (shipped).**
- New `Src/Common/Advice/FlagRequests.cs`: `BuildingRequest` (rich), `LaborRequest`,
  `ItemRequest`, `AttentionRequest` + enums `BuildingClass`/`RoomClass` + value
  types (`CapacityNeed`, `AdjacencyHint`, `PowerNeed`, `TempNeed`, `Urgency`, `Deadline`).
- `AgentFlag.Requests` → four nullable arrays (`building_requests`/`labor_requests`/
  `item_requests`/`attention`).
- Deleted `ResourceRequest`, `ResourceRequestKind`, `IconRef` (C#). No refs remain.
- Request normalizer rewritten to typed arrays + legacy-generic repair path.
- Food rules emit typed arrays (old-kind→array per taxonomy §3); `food.system.md`
  minimal update; `AdviceSnapshotPolicy` request-count touched; dashboard flag
  types/render + the legacy `AdviceItem.resource_requests` TS field removed; tests.
- `requested_from` stays a string. S3 untouched.

**How to verify (human).**
- Dashboard: Food → Advice → flags section — cross-minister requests render grouped
  by typed array (building/labor/item/attention). Induce a freezer/cooler need to
  see a `building_request`.
- Commands: `dotnet build Src/RimBob.sln`; `dotnet test Src/Tests/RimBob.Tests.csproj` (317/317);
  `npm.cmd run build` (Dashboard); `.\run-rimbob.ps1` → `/api/system/health`.
- Files: `Src/Common/Ministers/AgentFlag.cs`, `Src/Common/Advice/FlagRequests.cs`, `Src/Ministers/Food/Rules.cs`.

**Codex run:** `20260522-161835-schema-s2-flag-request-arrays` · branch
`codex/prompt-20260522-161835-schema-s2-flag-request-arrays` · verifier Adherent: yes ·
landed commit `8676d59d6969e2e3f215988c7fe1c83244bcbf2f`.
