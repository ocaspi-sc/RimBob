# Chef: surface forbidden meals in the rules path + fix the LLM flag contract

> Minister-refine pass on **Chef** (Food). Two independent, high-confidence, corpus-backed findings from the 2026-05-31 subagent run on a fresh colony. Proposal-first; land only on explicit request. Primary slice is **S1** (the "Chef only wants a stockpile zone" bug); **S2** is a separate harness fix that can land in any order.

## Motivation

On a new colony Chef emitted a single advice item — "Food stockpile categories need verification" with one `set_stockpile_zone` action — while 57 packaged survival meals sat **forbidden** on the map (a huge day-1 buffer nobody can eat). The deterministic LLM-free rules path produced the degenerate output the player saw; the subagent LLM path produced the right plan (unforbid the meals, forage, plant rice, request a kitchen) only because it reasoned over `UnforbidTargets`. The rules path — which runs every normal cycle — structurally cannot reach that conclusion. Separately, the subagent's well-formed cross-minister flag was silently dropped on ingestion (flag_count 0 → 0 → 1 across three corrections) because the live flag contract is under-specified in the system prompt and the normalizer drops malformed flags without surfacing a warning.

## Evidence (replay corpus + live)

Scan of `%LOCALAPPDATA%\RimBob\logs\replay\{chef,food}-*.jsonl` (8 files, 65 records):

- `nutrition_signal_gap` selected **5×**; **5/5** of those briefings carried `unforbid_targets` meal stacks; **0/5** emitted an `unforbid` action.
- **8/8** records that had `unforbid_targets` meals had an **empty** `unclassified_food_items` — the exact field the forbidden-meal rule logic reads.
- `emergency_food_flag` selected **36×**; its `ForbiddenMealCount` also reads `unclassified_food_items`, so its unforbid action/request is effectively dead code on real data.
- Live this session: 3 manual-endpoint POSTs recorded; ingestion response `flag_count` was `0, 0, 1` — the flag only landed after I added the `AgentFlag` envelope and corrected `target_class` to a valid `BuildingClass`.

## Context (current code)

- `nutrition_signal_gap` branch emits only a `SetStockpileZone` action + a `Stockpile` building flag and never consults `UnforbidTargets`: [`Rules.cs:18-33`](../Src/Ministers/Food/Rules.cs).
- The unforbid action exists but is gated on the wrong field. `ForbiddenMealCount`, `FirstForbiddenFoodDef`, `ForbiddenMealActionText`, `ForbiddenMealLabel` all read `briefing.UnclassifiedFoodItems`: [`Rules.cs:622-626`, `Rules.cs:1149-1176`](../Src/Ministers/Food/Rules.cs). They feed `EmergencyActions`/`EmergencyRequests`: [`Rules.cs:721-772`, `Rules.cs:628-719`](../Src/Ministers/Food/Rules.cs).
- The Assisted-Apply builder already uses the right source — `UnforbidApply` reads `briefing.UnforbidTargets.Where(Kind=="meal")`: [`Rules.cs:978-1003`](../Src/Ministers/Food/Rules.cs).
- Field provenance proves the mismatch: `UnforbidTargets = DeriveUnforbidTargets(s)` is derived live from forbidden `StoredResources`/`Things` and classified by `FoodItemClassifier.FoodKindLabel`, whereas `UnclassifiedFoodItems = food.UnclassifiedFoodItems` is a passthrough from the upstream RIMAPI food aggregate (empty on this colony): [`FoodBriefingDerivation.cs:82-83`, `:196-237`](../Src/StateStore/Derivations/FoodBriefingDerivation.cs).
- `FoodUnforbidTarget` carries `Id, Def, Label, Count, Kind, Source, Position` — every field the forbidden-meal helpers need.
- Design already mandates the desired behavior; only the code is behind: [`Docs/design/ministers/food.md:170-179`](../Docs/design/ministers/food.md) ("recommend an unforbid action for forbidden meals before broader stockpile-visibility advice").
- LLM flag contract gap: system prompt describes flags as bare request arrays and only "building requests must include target_class": [`food.system.md:20`](../Src/LlmGateway/prompts/food.system.md). But `AgentFlag` requires positional `id, source_minister, severity, domain, summary` ([`AgentFlag.cs:10-32`](../Src/Common/Ministers/AgentFlag.cs)) and `BuildingRequest.TargetClass` is a `SnakeCaseLowerEnumConverter<BuildingClass>` enum ([`FlagRequests.cs:9-19`, `:128-153`](../Src/Common/Advice/FlagRequests.cs)). `NormalizeFlag` deserializes the whole flag, and on failure falls back to `ReadString(node)`, which returns null for an object node → the flag is dropped with **no** warning: [`AdviceResponseNormalizer.cs:119-157`](../Src/LlmGateway/AdviceResponseNormalizer.cs). The manual endpoint reports `style_warnings: []` and `accepted: true`, so the loss is invisible.

## Scope

### S1 — Rules read forbidden meals from `UnforbidTargets` (the fix; primary)

- Repoint the forbidden-meal helpers in `Rules.cs` from `briefing.UnclassifiedFoodItems` to `briefing.UnforbidTargets.Where(Kind=="meal")`: `ForbiddenMealCount` (sum `Count`), `ForbiddenMealLabel` (first target `Label`), `ForbiddenMealActionText` (distinct `Position` strings, top 3), and replace `FirstForbiddenFoodDef` with the first meal target's `Def`. This is a source repoint to the already-populated derived field, not a compat path; `UnforbidApply` already uses this source so the action payload and the advice text stay consistent.
- Extract one small helper, e.g. `ForbiddenMealUnforbidAction(briefing)` returning the `Unforbid` `AdviceAction` (text + `UnforbidApply` payload) and `ForbiddenMealItemRequest(briefing, priority)` returning the `ItemRequest`, so both branches reuse identical wording.
- `nutrition_signal_gap` branch ([`Rules.cs:20-33`](../Src/Ministers/Food/Rules.cs)): when `UnforbidTargets` has meal entries, prepend the `Unforbid` action **before** the existing `SetStockpileZone` action, and add the forbidden-meal `ItemRequest` to the flag's requests. Keep concern `ManageFoodStockpile` and trace `nutrition_signal_gap` unchanged. Priority stays `Medium` (no proven shortage) — see Open questions.
- `EmergencyActions`/`EmergencyRequests` automatically start working on real data once the helpers are repointed (no further edit), closing the dead-code path for the 36 emergency cases.
- No change to `FoodBriefingDerivation` and no change to the briefing wire shape. `UnclassifiedFoodItems` stays on the briefing for LLM use; Rules just stop depending on it.

### S2 — LLM flag contract: stop silent drops (independent harness fix)

- `food.system.md`: tighten the flags rule to (a) require the `AgentFlag` envelope on every flag — `id`, `severity` (low|medium|high|critical), `domain` ("food"), `summary` — in addition to the request arrays; (b) enumerate the allowed snake_case `target_class` tokens (`freezer, cooler, production_bench, stockpile, shelf, …`) and `work_type` tokens; (c) state that a flag missing the envelope or using an unknown enum value is dropped. Also repoint the line-30 clause from `UnclassifiedFoodItems` to `UnforbidTargets` so the LLM path names the same source as S1.
- `AdviceResponseNormalizer.NormalizeFlag`: when a flag node is a non-empty object but does not yield a usable strict `AgentFlag` (no `Id` after deserialize, or deserialize threw on an enum), count it as a dropped flag instead of silently returning null. Thread a `DroppedFlagCount` (or warning list) out through `NormalizedAdviceResponse` → `FoodLlmParseResult` so the manual endpoint can include it in the response and `style_warnings`. This makes a real Gemini run surface the same problem the player would otherwise never see.

### S3 — Tests / fixtures

- `Src/Tests/Food/Fixtures/`: add a briefing fixture matching the live case — `EstimatedDaysOfFood: null`, `UnclassifiedFoodUnits: 57`, `UnclassifiedFoodItems: []`, `UnforbidTargets`: meal stacks summing 57. New `FoodRulesTests` case: assert the `nutrition_signal_gap` advice now leads with an `Unforbid` action carrying an `UnforbidThingsApply` payload (12 targets / 57 count) ordered before `set_stockpile_zone`, and the flag carries the forbidden-meal `ItemRequest`. Regression: a `nutrition_signal_gap` briefing with empty `UnforbidTargets` still produces today's single `set_stockpile_zone` advice unchanged.
- `Src/Tests/LLM/`: a response whose flag has request arrays but no envelope, and one with an invalid `target_class`, both assert the new dropped-flag diagnostic fires; a well-formed flag (post-prompt shape) lands with `flag_count == 1`.

## Approach (Codex order)

1. S1 helpers repoint + extract the shared unforbid action/item-request helpers; build green.
2. S1 `nutrition_signal_gap` branch wiring; add fixture + rules tests (S3 first half); run the Food test slice.
3. S2 prompt edit + normalizer dropped-flag diagnostic + endpoint surfacing; add LLM tests (S3 second half).
4. Verification; append a before/after advice diff to this plan.

## Before/after (representative — today's live briefing; label `fixture-backed`, run on land)

- BEFORE — rules/`nutrition_signal_gap`: `[medium] manage_food_stockpile` "Food stockpile categories need verification"; actions: [`set_stockpile_zone`]; flag building_request: stockpile visibility. The 57 forbidden survival meals are never mentioned.
- AFTER — rules/`nutrition_signal_gap` (same trigger/concern/priority): actions: [`unforbid` "Unforbid 57 packaged survival meals across 12 stacks at (127,120), (125,120), (127,118); then let haulers bring them into the food stockpile" (UnforbidThingsApply, 12 targets / 57 count), `set_stockpile_zone` (unchanged)]; flag now also carries an `item_request` for the 57 forbidden meals. Matches both the LLM path and `food.md`.
- Non-target cases unchanged: `nutrition_signal_gap` with no `UnforbidTargets` meals is byte-identical to today; the repoint additionally revives the forbidden-meal action/request in the 36 `emergency_food_flag` cases that currently emit nothing for it.

## Verification

- `dotnet test Src\Tests\RimBob.Tests.csproj --filter FullyQualifiedName~RimBob.Tests.Food` green incl. the new fixture case; LLM flag-drop tests green.
- Full `dotnet test Src/RimBob.sln` if the normalizer/parse-result contract is touched (S2).
- Live: `.\run-rimbob.ps1`, manual-trigger Chef on the current colony, confirm the rules-path advice now leads with the unforbid action; then a manual LLM POST with an enveloped flag lands `flag_count == 1` with no dropped-flag warning.

## Where to see it (dashboard)

Chef → Advice (`/api/ministers/food/snapshot`): the `nutrition_signal_gap` card now shows the unforbid action first. Chef → Infographics unaffected. The flag's `item_request`/`building_request` surfaces in active flags and the Willie Build Queue → Requested lane. The S2 dropped-flag count appears in the manual-LLM ingestion response and `style_warnings`.

## Out of scope / follow-ups

- Re-deriving `food.UnclassifiedFoodItems` from live things upstream — we route around it via `UnforbidTargets`; do not introduce a second forbidden-food source.
- Broad LLM parser rewrite or provider swap. S2 only stops silent flag loss and aligns the prompt to the live `AgentFlag`/`BuildingClass` contract (per evaluation calibration guidance: bring the normal harness up to par, don't conclude "use Codex").
- Priority/severity policy beyond the single Open question below.

## Open questions

- Should `nutrition_signal_gap` bump `Medium → High` when a concrete unforbiddable buffer exists? Argument for: there is now an immediate actionable buffer. Argument against: nutrition is still unclassified, so days-of-food is unproven; keep `Medium` and let the unforbid action carry the value. Proposed default: keep `Medium`.
- Confirm `MaxUnforbidTargets` (the `UnforbidApply` cap) and the advice `Count` agree when there are more than the cap of stacks, so the advice text count matches the apply payload count.

---

## Summary (landed 2026-05-31)

**Motivation.** On a fresh colony Chef emitted only "set up a stockpile zone" while 57 packaged survival meals sat forbidden on the map — a huge day-1 buffer the rules path couldn't see. The deterministic rules (which run every normal cycle) structurally missed it; only the LLM path surfaced it, and even then a well-formed cross-minister flag was silently dropped on ingestion. This slice makes the rules path surface the meals and stops the silent flag loss.

**Context.** Minister-refine pass on Chef (Food), evidenced by the 2026-05-31 subagent run + replay-corpus scan (`nutrition_signal_gap` 5×, all with `unforbid_targets` meals, 0 unforbid actions emitted; 8/8 unforbid-meal records had empty `unclassified_food_items`). Root cause: Rules read forbidden meals from the upstream-passthrough `UnclassifiedFoodItems` (empty in practice) instead of the live-derived `UnforbidTargets` (`FoodBriefingDerivation.cs`) that `UnforbidApply` already uses. Design `Docs/design/ministers/food.md:170` already mandated the unforbid-before-visibility behavior; only the code was behind. Related follow-up `restore-minister-outputs-from-corpus` covers persisting flags across restarts.

**Scope (shipped).**
- S1 — `Src/Ministers/Food/Rules.cs`: repointed the forbidden-meal helpers (`ForbiddenMealCount`, `ForbiddenMealLabel`, `ForbiddenMealActionText`, former `FirstForbiddenFoodDef`) from `UnclassifiedFoodItems` to `UnforbidTargets.Where(kind=="meal")` with no compat dual-read; added `ForbiddenMealUnforbidAction` + `ForbiddenMealItemRequest` helpers driven by one capped target set; `nutrition_signal_gap` now prepends the `unforbid` action (with `UnforbidThingsApply` payload) before `set_stockpile_zone` and adds the forbidden-meal `item_request` to its flag. Priority stays `Medium`. `UnforbidApply` refactored to take the precomputed targets so the advice text count and payload `TargetCount` are structurally identical. The emergency path revives automatically.
- S2 — `food.system.md` flag rule now requires the `AgentFlag` envelope (`id`, `severity`, `domain`, `summary`) plus the request arrays, enumerates the valid snake_case `target_class`/`work_type` tokens, and repoints the forbidden-meal clause to `UnforbidTargets`. `AdviceResponseNormalizer.NormalizeFlag` now counts non-empty flag objects that fail the envelope/enum check (`DroppedFlagCount`), threaded through `NormalizedAdviceResponse` → `FoodLlmParseResult` → the manual-LLM endpoint response and `style_warnings`. No fix-up of malformed flags — they are counted and dropped.
- S3 — new fixture `Src/Tests/Food/Fixtures/nutrition-signal-gap-forbidden-meals.json`; `FoodRulesTests` cases for unforbid-first ordering (57 count / 12 targets) and the empty-`UnforbidTargets` regression (single `set_stockpile_zone`); three `LlmClientTests` for missing-envelope, invalid-`target_class`, and well-formed-flag-lands.

**How to verify (human).**
- Dashboard: Chef → Advice (`/api/ministers/food/snapshot`) — the `nutrition_signal_gap` card leads with the unforbid action; the dropped-flag count appears in the manual-LLM ingestion response + `style_warnings`.
- Commands: `dotnet test Src\RimBob.sln` (491 passed) or filtered `--filter FullyQualifiedName~RimBob.Tests.Food`.
- Files: `Src/Ministers/Food/Rules.cs`, `Src/LlmGateway/AdviceResponseNormalizer.cs`, `Src/LlmGateway/FoodLlmResponseParser.cs`, `Src/ApiHost/Endpoints/MinisterEndpoints.cs`, `Src/LlmGateway/prompts/food.system.md`.

**Run note.** Codex's verification attempt ran a live `run-rimbob.ps1` smoke (out of plan scope), which spawned a stray worktree `RimBob.Host.exe` that locked `RimBob.Ministers.dll` and broke its own rebuild; the Codex CLI then panicked before recording a final message/session. The committed work was intact — Claude killed the stray Host and re-ran the full suite green, and the Sonnet verifier returned `Adherent: yes`.

**Codex run:** 20260531-230904-chef-unforbid-in-rules-and-flag-cont · branch `codex/prompt-20260531-230904-chef-unforbid-in-rules-and-flag-cont` · landed commit `2c508c8`

