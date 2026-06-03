# Delete `concern` — rules emit actions + flags only

**Status:** PLANNED (not started). Design decision locked this session.
**Owner:** cross-cutting (Advice core + Chef/Welfare/Willie + LLM gateway + Dashboard).
**Scope:** delete the `concern` concept entirely. `AdviceItem.Concern` field, the three per-minister concern enums, the LLM `allowed_concerns` contract, the chain-model concern disambiguator, diagnostics `Concern`, dashboard mirrors, tests, replay corpus.

**Supersedes the premise of** `concern-code-rename` (Tasks.md:29, `advice_type`→`concern`) — that rename is now moot; this removes the field it renamed.

---

## Decision + rationale

Discussion conclusion: `concern` does not earn its keep.

- **Dial unit → action-kind, not concern.** Autonomy graduates Off/Suggest/Auto on what *executes* = the action (+ its Apply handle + validation). Assisted Apply already allowlists by **action kind + validation**; concern plays zero role. The "Auto a whole concern bundle" story is illusory — builds/knobs inside a concern are gated out of Auto until M7+, so Auto only ever touches the **designation** action inside any concern. A dial finer than action-kind buys nothing.
- **Concern is already just a label in code.** Only behavioral consumer is `FoodChainModelBuilder` (chain-viz step grouping). No dial, no supersession (ids key on `trace`), no pushback clustering reads it. Everything else displays it.
- **Cost is real:** three bespoke per-minister enums to maintain, dead values never emitted (`ManageButcherBills`, `TradeForFood`, `RecoverFromFoodEvent`, `StoragePlacement`, `FireRisk`, `BaseLayout`…), and a redundant third name alongside `trace` + `title`.

**Locked:** full delete, no compat code, wipe-and-regen persisted snapshots (house style — matches `normalized-game-time`, `welfare-rules-slice-b`). Autonomy dial is re-specced to **per minister + action-kind**; build sub-grouping (when builds go Auto, M7+) keys on `BuildingClass`, not concern.

---

## The one hard problem: chain-model disambiguation

`FoodChainModelBuilder.FoodChainActionTargets` ([FoodChainModelBuilder.cs:277](../Src/Common/Briefings/FoodChainModelBuilder.cs#L277)) uses concern to split same-action-kind into different chain steps. Audit of every concern dependency there:

| Ambiguity | Today keys on | Replacement (no concern) |
| --- | --- | --- |
| `mark_harvest` → `grow.harvest` vs `forage.harvest` | `concern == wildharvest` | `Apply.Label` = "Mark forage" vs "Mark harvest" (set by `HarvestApply(_, "wild"\|"crop")`), fallback instruction contains "forage" |
| `production_bill` → `cook` vs `hunt.butcher` | `concern == managebutcherbills` | instruction/recipe contains "butcher" (cook bills say "simple meal") |
| `place_blueprint` → `store`/`hunt.butcher`/`cook` | concern + `IsColdStorageBlueprint` | **already** has instruction sniff (`freezer`/`cooler`→store; "butcher"→butcher; else cook) — just drop the concern checks |
| `AddConcern(...)` seeding steps from the concern label alone | concern | **delete** — every step is already reachable from `AddAction` (MarkHarvest→harvest, MarkHunt→hunt, DesignateZone→grow.zone, ProductionBill→cook, PlaceBlueprint(freezer)/SetStockpileZone→store). Chain now derives purely from actions. |

Net: chain steps derive from **actions only**. The two genuinely ambiguous pairs (forage-vs-crop, cook-vs-butcher) are distinguishable from data the rule already writes (`Apply.Label`, instruction text) — same sniff pattern the builder already uses for blueprints. **No new field required.** (If sniffing proves brittle in verification, fallback is one optional `string? Step` hint on `AdviceAction` set by the emitting rule — action-grain, not a per-minister enum. Prefer not to add it.)

---

## LLM contract changes

`AdviceResponseNormalizer.NormalizeAdvice` ([AdviceResponseNormalizer.cs:72](../Src/LlmGateway/AdviceResponseNormalizer.cs#L72)) bakes concern into two things:

- **Advice id:** `{domain}_llm_{concern}_{tick}_{idx}` → drop concern segment → `{domain}_llm_{tick}_{idx}`.
- **Title fallback:** `HumanizeIdentifier(rawConcern)` → fallback to first action instruction, else `"{Minister} advice"`.

Remove `DefaultConcern` from `LlmAdviceNormalizationContext`, the `node["concern"]` read, and the snake-case concern plumbing. `FoodLlmResponseParser.cs:176` non-empty-concern validation → drop. `PromptBuilder` `AllowedConcerns` ([PromptBuilder.cs:84](../Src/LlmGateway/PromptBuilder.cs#L84)) → remove from the prompt model + builder. `food.system.md:12` "Use only concern values from allowed_concerns" → delete the line. Unknown inbound `concern` key is ignored by default System.Text.Json — no explicit tolerance code.

---

## Blast radius (33 files, ~279 occurrences — mostly mechanical)

**Core / delete**
- `Src/Common/Advice/AdviceItem.cs` — remove `Concern` field.
- `Src/Common/Advice/FoodConcern.cs`, `WelfareConcern.cs`, `WillieConcern.cs` — **delete files** (enums). Keep the `FoodLlmResponse` record in FoodConcern.cs — relocate it to its own file.
- `Src/Common/Ministers/IMinisterRules.cs` — drop `Concern` from `RuleEmittedAdviceTrace`; drop it in `WithEmissions`.

**Minister rules (drop concern arg + emit no concern; rename misnomers)**
- `Src/Ministers/Food/Rules.cs` — `ConcernFor` drops `FoodConcern type` + concern string; `DeterministicConcerns`/`ConcernEmission`/`WithConcernEmissions` → rename neutral (`RuleEmission`, `EmitAdvice`, `WithRuleEmissions`). Advice built without `Concern:`.
- `Src/Ministers/Welfare/Rules.cs`, `Src/Ministers/Willie/Rules.cs` — same.

**LLM gateway** — `AdviceResponseNormalizer.cs`, `FoodLlmResponseParser.cs`, `PromptBuilder.cs`, `prompts/food.system.md` (+ check `welfare`/`willie` system prompts for concern instructions).

**Chain model** — `Src/Common/Briefings/FoodChainModelBuilder.cs` — rewrite `From`/`AddConcern`/`AddAction`/`AddBlueprintTarget` per the table above.

**Dashboard** — `types/advice.ts:314`, `types/system.ts:221` (drop `concern`); `MinisterAdviceView.tsx:590,618` (eyebrow → title); `MinisterBuildQueueView.tsx:213,261` (group label → title); `MinisterRulesView.tsx` (drop `concern` columns); `AnalyticsOverview.tsx:28` ("by minister and concern" → "by minister"); `semanticIcons.ts:157`.

**Tests (heavy)** — `FoodRulesTests` (58 hits), `WelfareRulesTests` (50), `WillieRulesTests`, `MinisterOfWillieTests`, `MinisterOfWelfareTests`, `FoodMinisterTests` (`concern=="food_security"` → assert `trace`/`title`), `LlmClientTests`, `AdviceNormalizationTests`, `FoodPromptTests` (`allowed_concerns`), `MinisterOutputStoreTests`, `AdviceBusTests`, corpus reader/writer/recorder tests. **Delete** `WelfareConcernTests.cs`, `WillieConcernTests.cs` (dedicated enum tests).

**Regenerate** — `Src/Tests/Food/Fixtures/replay-corpus/food-rules-history.jsonl` (2 cases) via the existing recorder; `FoodReplayCorpusTests` (drop concern equality).

**Docs** — `Docs/design/advice.md` (delete "Concern" section; rewrite "Autonomy Dial" to per-minister + action-kind; note chain steps derive from actions), `ministers.md`, `ministers/food.md`, per-minister concern catalogues, `DESIGN.md` if it references concern.

---

## Step-by-step

1. **Core field + enums:** remove `AdviceItem.Concern`; delete the 3 enum files (relocate `FoodLlmResponse`); drop `RuleEmittedAdviceTrace.Concern`.
2. **Rules:** strip concern from Food/Welfare/Willie emission factories; rename `Concern*` helpers neutral. Advice now = trace-keyed id + title + actions + flags.
3. **Chain model:** rewrite to derive steps from actions only (Apply.Label + instruction sniff for the 2 ambiguous pairs).
4. **LLM:** drop `allowed_concerns` (prompt model + builder + system prompt), concern read/default/id/title-fallback in normalizer, non-empty validation in parser; new LLM advice id scheme.
5. **Dashboard:** drop `concern` from types + 5 components; regroup build-queue/advice eyebrow by title; analytics by minister.
6. **Tests:** rewrite assertions to trace/title; delete the 2 concern-enum test files; regen replay corpus (2 cases).
7. **Docs:** advice.md/ministers.md/food.md + autonomy-dial respec.
8. `dotnet build` + `dotnet test`; dashboard `tsc`/`vite build`; then live JSON verification.

Lands **atomically** (record-field removal won't compile half-done). Could split **A: backend+LLM+chain+tests** / **B: dashboard** for review, but JSON shape change makes single landing cleaner; recommend one Codex worktree.

---

## Verification (JSON-first, per standing guidance)

Confirm via cabinet run + replay/advice JSON, not the dashboard screenshot:
- New-colony cabinet cycle → Chef advice snapshot JSON has **no `concern` key**, ids are `chef_<trace>`, multiple priority-sorted cards intact.
- **Chain model unchanged:** assert the same `steps[]` set (grow.harvest / forage.harvest / hunt.hunt / grow.zone / cook / store) for a multi-concern new-colony tick — the action-derived steps must match the pre-change concern-derived steps. This is the regression risk.
- LLM path (subagent/manual): emit advice with no concern; confirm normalizer produces valid ids + titles, drops nothing.
- Welfare + Willie advice snapshots: no `concern` key, titles/flags intact.

---

## Deferred / non-goals

- **Autonomy dial implementation** stays M7+. This only re-specs its *unit* (action-kind) in docs; no dial code now.
- **`AdviceAction.Step` hint** — only if chain sniffing proves brittle in verification. Prefer deriving from existing Apply.Label/instruction.
- **Mayor** out of scope — emits `MayorAgenda`, not `AdviceItem`; no concern.

---

## Summary (landed 2026-06-03)

**Motivation.** We re-examined whether the `concern` concept earns its keep and concluded it does not. The autonomy dial — concern's headline justification — graduates trust on what *executes* (the action + its Apply handle + validation), which is already how Assisted Apply gates. The "Auto a whole concern bundle" story is illusory: builds/knobs inside a concern stay gated out of Auto until M7+, so Auto only ever touches the designation action inside any concern, and a dial finer than action-kind buys nothing. In code, `concern` was already just a label — the only behavioral consumer was `FoodChainModelBuilder` step grouping; no dial, supersession (ids key on `trace`), or pushback path read it. So we deleted it: rules emit **actions + flags** only.

**Context.** Discussion across this session; supersedes the premise of `concern-code-rename` (Tasks.md). Design decisions recorded in `Docs/design/advice.md` (Concern section removed; Autonomy Dial re-specced to per-minister + action-kind) and `Docs/DESIGN.md`. Related follow-up captured: `minister-rules-table-refactor` (table-driven rules), which depends on this landing first.

**Scope (shipped).**
- Removed `AdviceItem.Concern`; deleted the three per-minister enums (`FoodConcern`/`WelfareConcern`/`WillieConcern`), relocating the `FoodLlmResponse` record to its own file.
- Stripped concern from Food/Welfare/Willie rule emission; dropped diagnostics `Concern` (`IMinisterRules`).
- Rewrote `FoodChainModelBuilder` to derive chain steps from **actions only** (action-kind + `Apply.Label` + instruction sniff for forage-vs-crop and cook-vs-butcher). No new `AdviceAction` field added.
- Removed the LLM `allowed_concerns` contract end to end: `PromptBuilder` model+builder, `food.system.md`, `FoodLlmResponseParser` non-empty check, `AdviceResponseNormalizer` concern read/default/id-segment/title-fallback. New LLM advice id `{domain}_llm_{tick}_{idx}`.
- Dashboard: dropped `concern` from `types/advice.ts` + `types/system.ts`, 5 minister/analytics components, and renamed `concern`→`signal` CSS/interfaces.
- Tests/fixtures/docs: rewrote assertions to trace/title, deleted the two concern-enum test files, regenerated `food-rules-history.jsonl`, renamed `multi-concern.json`→`multi-rule.json`, updated current design docs/roadmap/guides. No compat code; wipe-and-regen.
- **Not done (deferred):** historical `.plans/*` and old `Tasks.md` entries left as planning artifacts.

**How to verify (human).**
  - Dashboard: any minister Advice view — cards show title eyebrow (no concern label); Rules table has no `concern` column; Analytics groups by minister.
  - Commands: `dotnet test Src\RimBob.sln` (547/547); `cd Dashboard; npm.cmd run build`.
  - Live: run a cabinet cycle → Chef advice snapshot JSON has no `concern` key, ids are `chef_<trace>`, chain `steps[]` unchanged (`grow.harvest`/`forage.harvest`/`hunt.hunt`/`hunt.butcher`/`grow.zone`/`cook`/`store`).
  - Files to glance at: `Src/Common/Advice/AdviceItem.cs`, `Src/Common/Briefings/FoodChainModelBuilder.cs`, `Docs/design/advice.md`.

**Codex run:** 20260603-005702-delete-concern · branch `codex/prompt-20260603-005702-delete-concern` · landed commit `e62591c`
