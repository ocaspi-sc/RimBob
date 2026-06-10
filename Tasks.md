# RimBob - Tasks

> Single task inbox and execution board for RimBob.
> Big milestone sequencing belongs in `Docs/ROADMAP.md`.
> `/todo` entries go under "Captured by /todo"; promote serious items into the execution board when they become active work.
> Every checkbox line starts with a unique one-word identifier immediately after the checkbox.
> Lifecycle: completed `/todo` captures leave the active list after they are promoted or shipped; stale or duplicate captures are deleted.

---

## Captured by /todo

<!-- entries go here -->
- [ ] dashboard-devblog [2026-06-09] #idea #ux Add a dashboard devblog surface listing major milestones (dashboard v2, first freezer landed, etc).
- [ ] live-farm-gate-fix [2026-06-09] #test #debt Reinstate the reverted crop-types→plant-count fix (ex-`225ff4a`) in `LiveTestHelpers.CountFarmPlants` + `LiveHostSmokeTests`/`LiveRimApiContractTests` as its own micro-slice so `kind!=reach` stays green when live RIMAPI returns `total_plants:0`. Sharpens the live-decouple bullet of `newcolony-test-polish`. [plan](.plans/new-colony-1-reach-fixes.md)
- [ ] milestone-nc1 [2026-06-09] #milestone #index #chef #welfare #willie #mayor #apply #dashboard #test **NC1 vertical-slice milestone index.** Evidence-backed status of the 8 criteria, ordered remaining slices (S1 Chef calibration → S2 Willie heater/cooler → S3 apply chain → S4 Mayor-minimal → S5 dashboard declutter → S6 test polish), stale-checkbox corrections, and the two unscoped design questions (Mayor minimal, dashboard important-views). Verified 2026-06-09: NC1 non-reach 12/0 green, 3 reach red (food-buffer split, growing-capacity demote, far-hunt demote). [plan](.plans/milestone-nc1.md)
- [ ] newcolony-test-polish [2026-06-09] #test #debt Harden new-colony-1 advice tests: assert semantics/formulas not incidental fixture floats; add precondition guards so tests can't go silently toothless (cf. `ForageApplyVerdictIsReady` `4dbdcda`); decouple live-farm tests (`LiveHostSmokeTests`, `LiveRimApiContractTests`) from a flapping RIMAPI so the `kind!=reach` gate stays green when RIMAPI returns `total_plants:0`; fix `IconCacheServiceTests.StartWarm` background-timing flake. [plan](.plans/new-colony-1-reach-fixes.md)
- [ ] grow-zone-apply-500 [2026-06-07] #bug #rimapi #willie #zones #apply #threading Make grow-zone Apply truthful end-to-end. Root-cause history is preserved in the plan; current next action is RIMAPI Slice 6: marshal-and-wait on the main thread, resolve `plant_def` there instead of via listener-thread `DefDatabase`, and return a real `GrowingZoneDto` or truthful `Fail`. Then RimBob Slice 7 trusts returned `zone_id` and deletes heuristic/poll readback. [plan](.plans/grow-zone-apply-500-mainthread.md)
- [ ] state-rooted-replay-corpus [2026-06-05] #food #chef #test #apply #integration **Add the apply-verdict step the new-colony suite skips (close the `ebfaef5` seam).** **Dependency LANDED:** `advice-for-new-colony-1` (`560f73a`) already built the state-rooted harness (`ColonyStateSnapshotStore.LoadAsync→RestoreInto→BriefingCache→Rules.Evaluate`, real LFS snapshot w/ 25 forage `is_harvestable:true` below 0.85) and `Chef_NewColony1_ForagesNearestBerries` already covers `state→briefing→advice` for forage. **Remaining gap = the back half:** that suite never invokes `AssistedApplyService`, so the apply-side predicate (where `ebfaef5` lived) stays untested — generation emits the right `TargetIds`, nothing asserts apply *accepts* them. Scope now shrunk to: (1) extract pure `AssessHarvest(state, apply)` verdict from `ApplyHarvestAsync` (no HTTP, reuses `PlantHarvest.IsReady`, behaviour-identical); (2) assert `AssessHarvest(scenario.State, forageApply).Outcome == Ready` on the landed scenario = direct `ebfaef5` regression. No new corpus/runner/capture-helper/fixtures (all landed), harvest-only, no wire/schema change. Co-touches `AssistedApplyService.cs` with `hunt-apply-per-animal` (different method, trivial). [plan](.plans/state-rooted-replay-corpus.md)
- [ ] null-stamp-crash [2026-06-05] #bug #llm-gateway #advice **`NullReferenceException` in `SortAdvice` when manual LLM output posted without `stamp` field.** `IsCompleteStrictAdvice` passes null-Stamp item → `NormalizeStrictAdvice` returns it → `SortAdvice` hits `Stamp.IssuedAt` → NRE → 500. Only crashes with 2+ advice items (.NET 9 skips `ThenBy` key for single-element sort). Fix: add `advice.Stamp is not null` to `IsCompleteStrictAdvice` guard so null-stamp items fall to fallback path. [plan](.plans/null-stamp-crash-fix.md)
- [ ] forage-apply-readiness [2026-06-05] #food #chef #apply #bug **"Apply Mark Forage" always fails with "Too many harvest targets are no longer ready in the original area."** Root cause: generator and assisted-apply validator use different harvest-readiness predicates. Generator (`FoodBriefingDerivation.IsHarvestReady`, [:491](.plans/forage-apply-harvestable-readiness.md)) = `IsHarvestable ?? Growth>=0.85`, so forageable wild plants (berries/agave/healroot) harvestable below 0.85 growth become advice `TargetIds`. Validator (`AssistedApplyService.ApplyHarvestAsync`, [:271](.plans/forage-apply-harvestable-readiness.md)) gates on **`Growth>=0.85f` only**, ignoring `IsHarvestable` → every forage target counts not-ready → `stale > AllowedMissing` → `stale_advice`. Crop harvest mostly escapes (mature ≈1.0). Fix: extract one shared `PlantHarvest.IsReady(plant)` in `RimBob.Core.Aggregates`, use it on both sides. No RIMAPI/wire/payload/corpus change. [plan](.plans/forage-apply-harvestable-readiness.md)
- [ ] hunt-apply-per-animal [2026-06-05] #food #chef #apply #bug #rimapi **"Mark Hunt" apply fails with obscure "Hunt area now contains unsafe or non-wild animals."** Root cause: hunt apply designates a **rect area** (`DesignateAreaAsync(..,"Hunt",rect)`, [AssistedApplyService.cs:374](.plans/mark-hunt-apply-per-animal-designation.md)), and RIMAPI area-hunt marks *every* wild animal in the rect — so the apply layer guards the 120-cell bounding box with a whole-rect purity gate ([:350-360](.plans/mark-hunt-apply-per-animal-designation.md)) that rejects if any predator/stray wild animal wandered in. Animals roam constantly → contaminated rect → reject. The advice already carries exact `TargetIds` but the area primitive can't use them. **Fix (2 slices, 2 repos):** S1 RIMAPI fork — new `POST /order/designate/hunt` per-thing endpoint mirroring `/order/unforbid` (designate `DesignationDefOf.Hunt` on exact animal ids, wild-only guard, lenient accounting). S2 RimBob — `DesignateHuntThingsAsync` client + rewrite `ApplyHuntAsync` to designate exact ids and **delete** the two rect-contamination gates (keep id-based `AllowedMissing` survival check). Risk policy stays RimBob-side; no advice/schema/persistence change; no corpus regen. [plan](.plans/mark-hunt-apply-per-animal-designation.md)
- [ ] landing-helper-stop-host [2026-06-04] #ops #git #host Stop the matching Host before landing-helper build/test when `-RestartHost` is set, so master landings do not fail on locked ApiHost DLLs.
- [ ] dashboard-rule-card [2026-06-04] #dashboard #rules #ministers #ui **Rule Card — visualize one rule-as-data row.** One reusable `RuleCard`, rendered **one-per-row** (full-width horizontal band), that **replaces** the all-rules table section on the Rules tab of **every** minister (Chef/Welfare/Willie + future) — all emit the same `RuleEvaluationTrace` via the shared `MinisterRuleTableEvaluator`. Kills the 1040px horizontal-scroll `DynamicTable` ([MinisterRulesView.tsx:159](.plans/dashboard-rule-card.md)); 3-col internal grid (Identity / Evidence / Emits) fills width with no overflow, `not_matched` rows render thin. **Wire fields are NOT enough:** `outputAction` is a lossy `"requests: 1"` summary and `conditions`===`reason`, so the card shows **real emitted decisions** — advice title, typed requests with their **game-sprite icon** (`iconForFieldValue('target_def',..)`), routing owner, priority, escalate reason. That detail already exists per-rule at `BuildTrace` ([:101](.plans/dashboard-rule-card.md)) but is discarded; **Slice 1 (backend)** adds a structured `RuleEmission[] Emissions` to `RuleEvaluationTrace` (+TS mirror, prune dead `matched`/`suppressed` outcomes, replay-corpus wipe+regen, tests). **Slice 2 (frontend)** = `RuleCard.tsx` + swap render + `.rule-card*` CSS, icons mandatory everywhere, outcome theming mirrors `advice-card`/`escalation-callout`. [plan](.plans/dashboard-rule-card.md)
- [ ] willie-requests-board-narrowing [2026-06-04] #willie #solver #requests #bug **Requests tab can collapse to 1 row even when multiple active Willie build requests exist.** Root cause: `ActiveWillieBuildingRequests` early-returns **only the triggering flag's** requests when `cycle.Flag` carries a Willie request ([MinisterOfWillie.cs:174](.plans/willie-requests-board-narrowing-fix.md)); the cabinet runs Willie **once per flag** (`RunWillieBuildingRequestFollowUpsAsync`) and `RecordInbound` **replaces** the board → last-write-wins → 1 row. Regresses the `willie-requests-tab` "record the board every cycle" intent. **Fix (Option A, surgical):** record the **union** of all active Willie requests to the board while leaving the narrowed list driving rules + per-flag solve — no extra solver load, no advice change, `MinisterOfWillie.cs` + tests only. Existing tests miss it (they use `ManualTrigger`, `directFlag==null`). [plan](.plans/willie-requests-board-narrowing-fix.md)
- [ ] willie-requests-tab [2026-06-03] #dashboard #willie #solver #requests **Willie → Requests dashboard tab.** New minister tab with a left sidebar listing every inbound building request aimed at Willie and a detail pane showing **all** request fields + the placement-solver outcome (validated option footprints, or no-fit/error message, or "awaiting solve"). Investigation: today the store keeps only the **latest single** solve per minister (`_latest`, overwritten each cycle — `WillieSolverStore.cs:8`) and Willie solves **one** selected request per cycle, so per-request options/error can't be shown without backend memory. Locked: **(1)** add per-request solver memory (store records the inbound request **board** every cycle via new `RecordInbound`, plus a `RequestKey`-keyed outcome map; `WillieSolverSnapshot` gains `Options` from `result.Options`, live-only — **not** persisted to the replay corpus); **(2)** building requests only. New read-only `GET /api/ministers/willie/solver/requests` joins board→outcome. Frontend: new `requests` view key (Willie-only, excluded from `allMinisterViews`), `MinisterRequestsView` master-detail, extract shared `BlueprintFootprintThumbnail`, and render option cards with Apply when the matching Willie advice carries `place_blueprint_group`. Build Queue tab retired 2026-06-05. Additive — no wire/persistence change, no wipe-and-regen. [plan](.plans/willie-requests-dashboard-tab.md)
- [ ] welfare-meta-plan [2026-06-01] #design #welfare #mood #index **Welfare meta-plan.** Master index for the Welfare (Mood & Needs) effort: north-star = new-colony shelter advice (Welfare emits a `building_request` to Willie for a basic temporary shelter with beds, reusing Willie's Placement Solver — no new engine). Holds the new-colony advice spec, rule set (trace ids), reused-machinery map, locked decisions, remaining work, open decisions (Welfare/Medical sequencing), research. **Updated 2026-06-05:** Slices A+B+table-port+C (LLM `5de6012`)+**D (`temperature_comfort` `29efd3b`, Willie Heater/Cooler template `adfda67`)** all landed — five deterministic rules + LLM escalation; §0 carries current architecture. **Next = promote design → `welfare.md`** (doc-only); remaining rule work signal-blocked (guest/animal/schedule) or human-gated (Welfare/Medical). [plan](.plans/welfare-meta-plan.md)
- [ ] willie-heater-cooler-placement-template [2026-06-05] #willie #solver #construction #temperature **Willie: add Heater and Cooler placement templates.** WD3 finding from Welfare Slice D: `BuildingClass.Heater`/`Cooler` exist but `RoomTemplateSet.Default` has no template for either, so a `RequestBuild(Heater|Cooler)` from Welfare's `temperature_comfort` rule produces a graceful no-fit. Add Heater + Cooler templates to Willie's solver so the placement options appear. Willie-side only; Welfare already emits the request correctly.
- [ ] chef-unforbid-in-rules [2026-05-31] #food #chef #rules #briefing #llm #flags Minister-refine (Chef). **S1 (primary):** the `nutrition_signal_gap` rule emits only `set_stockpile_zone` while forbidden survival meals sit on the map, because the forbidden-meal helpers (`ForbiddenMealCount` etc., `Rules.cs:622,1149`) read `briefing.UnclassifiedFoodItems` (upstream passthrough, empty 8/8 in corpus) instead of `briefing.UnforbidTargets` (derived live, populated; already used by `UnforbidApply`). Repoint helpers + prepend an `unforbid` action/item-request in the gap branch (revives it for the 36 `emergency_food_flag` cases too). Briefing already carries the fact; no derivation/wire change; matches `food.md:170`. **S2 (independent):** subagent flag silently dropped (flag_count 0→0→1) — `food.system.md:20` under-specifies the `AgentFlag` envelope + valid `BuildingClass`/`work_type` enums, and `NormalizeFlag` (`AdviceResponseNormalizer.cs:119`) drops malformed flags with no warning; tighten prompt + surface a dropped-flag count. Corpus: `nutrition_signal_gap` 5×, all with unforbid-meals, 0 unforbid emitted. [plan](.plans/chef-unforbid-in-rules-and-flag-contract.md)
- [ ] rimapi-map-reach-live-verify [2026-05-27] #rimapi #construction #pathfinding #live Verify loaded RIMAPI DLL exposes `/api/v1/map/reach`, `/api/v1/map/path-cost`, `/api/v1/map/path-cost/batch` after RimWorld starts; run same-room, sealed-cell, closed-door, single-cost, and batch smoke checks.
- [ ] rimapi-blueprint-placement-endpoint [2026-05-26] #high-prio #rimapi #construction Expand RIMAPI fork blueprint endpoints: validate, place, read pending, allow/disallow, cancel by id, backlog summary. Fork-only; no RimBob Host wiring. [plan](.plans/rimapi-blueprint-placement-endpoint.md)
- [ ] normalized-game-time [2026-05-26] #design #state #dashboard Normalize all game-date fields from `game_tick`: total days, completed days, colony day/year, day-of-year, parsed quadrum/hour, shared formatter; no compat code, wipe-and-regen persisted snapshots. [plan](.plans/normalized-game-time.md)
- [ ] base-image-scoring [2026-05-25] #idea #spike #construction Evaluate base-image analysis for scoring metric calibration.
- [ ] rimapi-blueprint-live-verify [2026-05-25] #rimapi #construction Verify the new blueprint lifecycle endpoints in live RimWorld: endpoint discovery, validate/place, allow/disallow, cancel blueprint/frame, and backlog grouping.
- [ ] willie-meta-plan [2026-05-22] #design #construction #index **Willie meta-plan.** Master index for the Willie effort: artifact map, locked decisions, ordered remaining work, open decisions. [plan](.plans/willie-meta-plan.md)
- [ ] wfc-variant-generator [2026-05-22] #idea #spike #construction Evaluate WFC as a bounded Willie variant generator. [plan](.plans/wfc-variant-generator.md)
- [ ] assisted-apply-mine-cut [2026-05-22] #assisted #apply #construction #food Add `mark_mine` + `cut_plants`/chop **designations** to the Assisted Apply allowlist. Same low-blast class as the current 4 (additive, ephemeral, game clears once done). Willie: clear build site / get steel; Food: wood + clearing. Design: extend `AdviceApplyKind` + validator; advice.md allowlist.
- [ ] assisted-apply-create-zone [2026-05-22] #assisted #apply #zones #construction #food Let a minister / Placement Solver suggest a **new** growing or material-stockpile zone as an Assisted Apply. Carve-out: CREATE-new-empty zone = additive/targeted (apply-eligible); EDITING an existing zone's filters/priority = policy knob (stays Suggest-only). Revisit advice.md "Policy Knobs vs Targeted Designations"; Solver emits the rectangle.
- [ ] assisted-apply-deconstruct-eval [2026-05-22] #assisted #apply #construction Evaluate `mark_deconstruct` as an apply — it DESTROYS a building (higher blast than additive designations). Needs a confirm/undo story before allowlisting; not in the "easy" tier.
- [ ] advice-action-vocab-expansion [2026-05-23] #design #advice #vocabulary Extend `AdviceActionKind` to cover common missing player designations: `mark_mine`, `cut_plants`/chop, `mark_deconstruct`, `smooth`, `claim`, `install`/`uninstall` (minified), `tame`, `slaughter`, `haul-to`. Prerequisite for the apply-expansion todos (`assisted-apply-mine-cut`, `assisted-apply-create-zone`). Equip/apparel/medical/prisoner/combat stay deferred per advice.md (pawn assignment → Auto).
- [ ] source-todo-mayor-briefing-context [2026-05-21] #mayor #briefing #state Cover remaining MayorBriefing source TODOs: TrendWindows, CoS digest, recent feedback, AgendaPosture, ScheduleSnapshot, and cold-snap forecast signals.
- [ ] source-todo-prisoner-surgery-signals [2026-05-21] #welfare #medical #rimapi Add prisoner count and pending surgery operation signals before filling the kept briefing derivation TODOs.
- [ ] source-todo-lord-hostility-signal [2026-05-21] #defense #rimapi #state Replace the Lord job-name raid heuristic with a live faction-hostility signal when RIMAPI exposes one.
- [ ] source-todo-pawn-edit-job-cache [2026-05-21] #labor #auto #rimapi Cache Pawn Edit and Pawn Job Controller field shapes before wiring policy/job advice beyond Suggest-only reads.
- [ ] source-todo-building-condition-read [2026-05-21] #construction #rimapi #state Add building hp/power/working-state reads before Willie relies on building condition evidence.
- [ ] source-todo-quest-awareness [2026-05-21] #cos #mayor #rimapi Wire quest fields into state and briefings once CoS/Mayor need quest pressure.
- [ ] base-layout-construction-tips [2026-05-21] #research #construction #layout Base Layout / Willie tips: fold community base-building heuristics into Willie spatial lint and dashboard evidence. [plan](.plans/base-layout-construction-tips.md)
- [ ] deterministic-cos-cabinet-issue-solver [2026-05-20] #design #cos #cabinet **Deterministic CoS Cabinet Issue Solver.** Implement the issue-report parent model and CoS routing plan. [plan](.plans/deterministic-cos-cabinet-issue-solver.md)
- [ ] fix-cross-agent-staged-commit [2026-05-19] #git #ops #multiagent Fix cross-agent staged-commit contamination on master. [plan](.plans/multi-agent-git-race.md)
- [ ] set-priority-advice-briefing-read [2026-05-19] #design #briefing #advice **`set_priority` advice — briefing read (near-term, Suggest-only).** Read current work priorities into Food's briefing so `set_priority` advice is accurate against live state. Action vocabulary already exists; gap is the input. Gated on a RIMAPI Pawn Edit Controller GET. [plan](.plans/set-priority-advice-briefing.md)
- [ ] auto-knob-write-shim-deferred [2026-05-19] #design #auto #labor **Auto knob-write shim (deferred, M7+).** At Auto graduation: validate → one declarative policy write → readback; Labor recommends the knob, RimWorld's job-giver allocates. Depends on the briefing-read plan landing first; design at re-engagement. [plan](.plans/auto-knob-write-shim.md)
- [ ] rimmind-structured-def-knowledge-index [2026-05-19] #rimmind #idea #defs **RimMind: Structured Def Knowledge Index.** Investigate a persistent RimWorld Def index for minister grounding; refs [index-defs.ts](<C:/dev/RimSage/src/scripts/index-defs.ts>), [db.ts](<C:/dev/RimSage/src/utils/db.ts>).
- [ ] rimmind-def-inheritance-resolver [2026-05-19] #rimmind #idea #defs **RimMind: Def Inheritance Resolver.** Investigate raw/merged XML Def handling for active/modded game truth; refs [def-resolver.ts](<C:/dev/RimSage/src/utils/def-resolver.ts>), [get-def-details.ts](<C:/dev/RimSage/src/tools/get-def-details.ts>).
- [ ] rimmind-label-def-resolution [2026-05-19] #rimmind #idea #lookup **RimMind: Label-To-Def Resolution.** Investigate label and `defName` lookup for odd live labels, icons, and advice grounding; ref [search-defs.ts](<C:/dev/RimSage/src/tools/search-defs.ts>).
- [ ] rimmind-source-symbol-lookup [2026-05-19] #rimmind #idea #refine **RimMind: Source Symbol Lookup.** Investigate dev-only C# symbol browsing for `minister-refine` and RimWorld/RIMAPI code archaeology; refs [index-csharp.ts](<C:/dev/RimSage/src/scripts/index-csharp.ts>), [read-csharp-symbol.ts](<C:/dev/RimSage/src/tools/read-csharp-symbol.ts>).
- [ ] rimmind-bounded-knowledge-tools [2026-05-19] #rimmind #idea #tools **RimMind: Bounded Knowledge Tools.** Investigate sandboxed, output-limited read/search tools for dashboard SYSTEM/INFO and dev-agent workflows; refs [server.ts](<C:/dev/RimSage/src/server.ts>), [search-source.ts](<C:/dev/RimSage/src/tools/search-source.ts>), [path-sandbox.ts](<C:/dev/RimSage/src/utils/path-sandbox.ts>).
- [ ] rimmind-persistent-knowledge-service [2026-05-19] #rimmind #idea #mcp **RimMind: Persistent Knowledge Service.** Investigate whether a RimSage-style MCP/HTTP service should remain dev-only or become bounded Host knowledge endpoints; refs [http.ts](<C:/dev/RimSage/src/http.ts>), [stdio.ts](<C:/dev/RimSage/src/stdio.ts>).
- [ ] investigate-exact-id-targeting-read [2026-05-19] #research #rimmind #assisted Investigate exact-ID targeting/read-back for Assisted Apply. [RimMind](C:/dev/RimMind) [DesignationTools.cs](C:/dev/RimMind/Source/RimMind/Tools/DesignationTools.cs)
- [ ] investigate-spatial-evidence-tools-location [2026-05-19] #research #rimmind #spatial Investigate spatial evidence tools for location-backed advice. [RimMind](C:/dev/RimMind) [MapTools.cs](C:/dev/RimMind/Source/RimMind/Tools/MapTools.cs)
- [ ] investigate-player-approved-construction-proposal [2026-05-19] #research #rimmind #construction Investigate player-approved construction proposal patterns. [RimMind](C:/dev/RimMind) [ProposalTracker.cs](C:/dev/RimMind/Source/RimMind/Core/ProposalTracker.cs)
- [ ] investigate-event-driven-wakeups-cooldowns [2026-05-19] #research #rimmind #events Investigate event-driven wakeups and cooldowns for ministers. [RimMind](C:/dev/RimMind) [Automation](C:/dev/RimMind/Source/RimMind/Automation/README.md)
- [ ] investigate-trend-history-minister-briefing [2026-05-19] #research #rimmind #history Investigate trend history as minister briefing fuel. [RimMind](C:/dev/RimMind) [MoodHistoryTracker.cs](C:/dev/RimMind/Source/RimMind/Core/MoodHistoryTracker.cs)
- [ ] investigate-defense-minister-algorithms-rimmind [2026-05-19] #research #rimmind #defense Investigate Defense minister algorithms from RimMind. [RimMind](C:/dev/RimMind) [CombatTools.cs](C:/dev/RimMind/Source/RimMind/Tools/CombatTools.cs)
- [ ] investigate-construction-minister-algorithms-rimmind [2026-05-19] #research #rimmind #construction Investigate Willie construction-domain algorithms from RimMind. [RimMind](C:/dev/RimMind) [SemanticTools.cs](C:/dev/RimMind/Source/RimMind/Tools/SemanticTools.cs)
- [ ] investigate-economy-minister-algorithms-rimmind [2026-05-19] #research #rimmind #economy Investigate Economy minister algorithms from RimMind. [RimMind](C:/dev/RimMind) [TradeTools.cs](C:/dev/RimMind/Source/RimMind/Tools/TradeTools.cs)
- [ ] rimmind-domain-snapshot-parts [2026-05-19] #rimmind #research #state RimMind: Domain Snapshot Parts - compare RimAI world-data parts against RimBob state-store and briefing boundaries. [IWorldDataService](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/IWorldDataService.cs) [WorldDataService](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/WorldDataService.cs)
- [ ] rimmind-food-wildlife-risk-scoring [2026-05-19] #rimmind #research #food #hunt RimMind: Food Wildlife Risk Scoring - inspect species-group value/risk scoring for Food hunt advice and Defense veto inputs. [WildlifeOpportunitiesPart](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/WildlifeOpportunitiesPart.cs)
- [ ] rimmind-storage-saturation [2026-05-19] #rimmind #research #storage #construction RimMind: Storage Saturation - investigate stockpile/storage utilization as a Food, Willie, and Mayor briefing signal. [StorageSaturationPart](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/StorageSaturationPart.cs)
- [ ] rimmind-construction-backlog [2026-05-19] #rimmind #research #construction #resources RimMind: Construction Backlog - inspect blueprint/frame/material-gap grouping for the first Willie briefing. [ConstructionBacklogPart](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/ConstructionBacklogPart.cs)
- [ ] rimmind-research-dependency-snapshot [2026-05-19] #rimmind #research #rimapi RimMind: Research Dependency Snapshot - inspect available/locked research, prerequisites, benches, techprints, and ETA fields for a Research minister slice. [ResearchPart](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/ResearchPart.cs)
- [ ] rimmind-medical-risk-digest [2026-05-19] #rimmind #research #medical #welfare RimMind: Medical Risk Digest - inspect injury, infection, bleeding, operation, capacity, and risk-score summaries for future Medical/Welfare work. [MedicalOverviewPart](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/MedicalOverviewPart.cs)
- [ ] rimmind-mood-risk-causes [2026-05-19] #rimmind #research #welfare #mood RimMind: Mood Risk Causes - inspect mood thresholds and top negative thought grouping for a compact Welfare briefing. [MoodRiskPart](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/MoodRiskPart.cs)
- [ ] rimmind-defense-posture-coverage [2026-05-19] #rimmind #research #defense #threats RimMind: Defense Posture Coverage - inspect perimeter, turret/trap coverage, and raid-readiness scoring for later Defense scope. [SecurityPosturePart](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/SecurityPosturePart.cs) [RaidReadinessPart](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/RaidReadinessPart.cs)
- [ ] rimmind-tool-registry-topk-index [2026-05-19] #rimmind #research #tooling #llm RimMind: Tool Registry And TopK Index - evaluate whether tool schemas and embedding-ranked tool selection belong in debug/refinement tooling only. [ToolRegistryService](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/Tooling/ToolRegistryService.cs) [IRimAITool](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/Tooling/IRimAITool.cs)
- [ ] rimmind-stage-cooldown-ticket-kernel [2026-05-19] #rimmind #research #cos #alerts RimMind: Stage Cooldown And Ticket Kernel - inspect cooldowns, leases, coalescing, and idempotency for CoS alert suppression. [StageKernel](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/Stage/Kernel/StageKernel.cs) [StageService](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/Stage/StageService.cs)
- [ ] rimmind-prompt-composer-blocks [2026-05-19] #rimmind #research #prompts #ministers RimMind: Prompt Composer Blocks - inspect scoped prompt block composition if RimBob minister prompt builders become repetitive. [PromptService](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/Prompting/PromptService.cs) [IPromptComposer](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/Prompting/IPromptComposer.cs)
- [ ] rimmind-history-recap-windows [2026-05-19] #rimmind #research #replay #feedback RimMind: History Recap Windows - inspect idempotent recap windows and stale-summary handling for replay and Pushback refinement. [HistoryService](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/History/HistoryService.cs) [RecapService](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/History/Recap/RecapService.cs)
- [ ] rimapi-stockpile-detail-read [2026-05-24] #rimapi #construction #storage Add stockpile detail read support: cells/bounds, priority, allowed filters, used/total cells, and item counts for storage pressure and material flow.
- [ ] rimapi-power-net-read [2026-05-24] #rimapi #construction #power Add power-net detail read support: net id, generators, batteries, consumers, stored/max energy, online/offline consumers, and disconnected critical assets.
- [ ] rimapi-buildability-layers-read [2026-05-24] #rimapi #construction #layout Add bounded buildability-layer read support for placement scoring: passability, roof type, edifice/blockers, terrain affordance, fog, and fertility over a caller-supplied rect.
- [ ] rimapi-blueprint-groups-overlay [2026-05-21] #rimapi #construction Blueprint GROUP placement (room shell + contents) + planning overlay in the fork; builds on `rimapi-blueprint-placement-endpoint`. [plan](.plans/rimapi-blueprint-groups-and-planning-overlay.md)
- [ ] willie-plan-new-base [2026-05-21] #construction #idea #future Willie plans a NEW base / major expansion: lay a planning overlay (vanilla Plan designator or forbidden blueprints), player commits region-by-region into real blueprint groups. Guides: more-planning-mod, rimworld-planner. See Capability B in [plan](.plans/rimapi-blueprint-groups-and-planning-overlay.md).
- [ ] willie-construction-design [2026-05-21] #construction #design Willie design anchors, advice output, and request/effect taxonomy; the linked pre-delete plan is historical. [plan](.plans/willie-advice-types.md)
- [ ] placement-solver [2026-05-21] #construction #design Placement Solver: deterministic engine turning building_requests/build_intent into validated layout options (no LLM); building_requests feed it directly. [plan](.plans/placement-solver.md)
- [ ] advice-chain-route-cards-grow [2026-05-19] #dashboard #food #ux Advice chain route cards (Grow/Hunt/Forage) above the Food snapshot. [plan](.plans/advice-chain-visualization.md)
- [ ] remove-legacy-repo-local-icon [2026-05-18] #ops #cleanup Remove legacy repo-local icon/log artifacts after AppData cache/log paths stay verified. [plan](.plans/icons-warm-fixes.md)
- [ ] legacy-worktree-migration [2026-05-17] #git #debt Migrate dirty legacy worktrees after their active slices land.
- [ ] skill-validator-dependency [2026-05-16] #skill #debt Fix local skill validator Python dependency.
- [ ] add-restricted-player-facing-markdown [2026-05-16] #dashboard #markdown Add restricted player-facing Markdown rendering when advice bodies or guide snippets need rich formatting; keep raw/debug views unrendered.
- [ ] minister-rag-knowledge [2026-05-15] #idea #llm #rag Provide ministers with more RAG knowledge.
- [ ] benchmark-optional-toon-prompt-encoding [2026-05-09] #spike #llm #test Benchmark optional TOON prompt encoding. [plan](.plans/toon-prompt-encoding-spike.md)

---

## Execution Board

> Completed items leave this active list when shipped, not when coded. Keep this list short.
> Big items belong in `Docs/ROADMAP.md`. This is day-to-day work.

### Now (live-state gaps)

- [ ] medical-hediff-severity-wiring **Medical hediff severity wiring.** If Mayor or a future Medical minister needs life-threatening condition awareness, preserve the relevant `Hediffs` signal into state/briefing instead of inferring severity from generic `Health`.

### Current implementation order

1. [ ] add-minimal-cos-handling **Add minimal CoS handling.** Implement the Mayor-side helper for dedupe, lead framing, and tactical-alert vs digest routing before multiple feeders exist.
2. [ ] construction-minister **Add Willie.** Food's first live dependencies are cooler / power / room / storage recommendations, not Defense coupling.
3. [ ] defense-minister **Add Defense.**
4. [ ] resolve-welfare-medical-sequencing **Resolve Welfare / Medical sequencing.** Decide whether they land together or whether Medical becomes its own follow-on slice (`M4.5` / second wave), then implement Welfare.
5. [ ] wire-pushback-feedback **Wire Pushback feedback.** Land M5 only after multiple ministers are emitting advice.

### Next (post-M3 follow-ups)

- [ ] guide-citation-footnotes Add polished guide-citation footnotes on feeder memo cards.
- [ ] replay-corpus-expansion Extend replay corpus persistence beyond Food and attach future Pushback/outcome fields once feedback is wired.
- [ ] improve-food-hunting-target-risk Improve Food hunting target risk/value scoring from live animal data.
- [ ] food-freezer-briefing Expand Food briefing with freezer room temperature and spoilage timers when RIMAPI exposes them.

### Later (M5 - Feedback loop)

- [ ] snapshot-feedback-route Minister snapshot feedback route writes a `FeedbackEvent` to the decision log.
- [ ] wire-dashboard-accept-dismiss-pushback Wire dashboard Accept / Dismiss / Pushback buttons (Pushback modal opens an editable text field, posts `pushback_text`).
- [ ] decision-log-tab-renders-last `Decision Log` tab renders the last N `FeedbackEvent`s with the originating Agenda bullet.
- [ ] minister-pushback-list Per-minister persistent pushback list (each minister owns + carries forward player corrections).

### Design TODOs (deferred)

#### Doc alignment

- [ ] action-ownership-catalogue Extend the action ownership sketch into a full RIMAPI action/endpoint catalogue, preserving owner/requester/executor labels.
- [ ] expand-roadmap-beyond-first-cabinet Expand the roadmap beyond the first cabinet wave: explicitly schedule Industry, Medical, Research, and Economy instead of leaving them only in post-MVP notes.

#### Cabinet rollout planning

- [ ] minister-scope-docs Add scope docs for Industry, Medical, Research, and Economy once their first slices are scheduled.
- [ ] minister-code-scope-docs Per-minister scope docs (`RimBob.Ministers/<name>/scope.md`) - write after first slice ships.
- [ ] construction-placement-layout-strategy-base Willie: placement / layout strategy (Base Layout Minister candidate). [plan](.plans/base-construction-layout-agent.md)
- [ ] define-minimum-viable-cos-solver Define the minimum viable CoS solver rule set for M4: consume Mayor posture/`cabinet_direction`, issue reports or current-runtime flags, and structured requests; dedupe same-issue pressure, choose lead framing, and decide when an issue becomes a tactical alert versus Mayor-digest input. [plan](.plans/deterministic-cos-cabinet-issue-solver.md)
- [ ] medical-welfare-rollout Decide whether Medical should stay coupled to Welfare in the rollout plan or become its own slice after Welfare.

#### Longer-tail design

- [ ] minister-promotion-criteria Candidate minister promotion criteria (CMO, Research, Trade, Treasury).
- [ ] memo-cadence-calibration Memo cadence calibration: flag-severity-gated tactical alerts vs. strict daily.
- [ ] dashboard-notification-ux Dashboard: notification UX (in-page only vs. browser notifications).

### Auto epic (M7 - defer until then)

- [ ] auto-write-shim Auto write shim (one validated declarative knob write + readback) per [`Docs/design/planning.md`](Docs/design/planning.md).
- [ ] labor-bulletin-board Bulletin board (`Coordination/BulletinBoard.cs`) — queues policy-change requests for Labor, not per-pawn tickets — per [`Docs/design/communication.md`](Docs/design/communication.md).
- [ ] labor-policy-recommender Labor policy recommender per [`Docs/design/ministers/labor.md`](Docs/design/ministers/labor.md).
- [ ] rimapi-policy-knob-write-endpoint RIMAPI policy-knob write-endpoint coverage map (Pawn Edit Controller etc.).
- [ ] minister-action-autonomy-dial Per-(minister, action kind) autonomy dial wired with real Auto execution.
- [ ] autonomy-dial-ui Autonomy dial UI with confirmation step.

### Claude skills to build (see ROADMAP)

- [ ] fixture-gen-skill-can-synthesize `fixture-gen` skill (can synthesize from Pushback events).
- [ ] briefing-check-skill `briefing-check` skill.
- [ ] rule-promote-skill `rule-promote` skill.

### Known open questions

See [`Docs/DESIGN.md`](Docs/DESIGN.md) decision log and Open Questions sections in sub-docs, especially [`Docs/design/advice.md`](Docs/design/advice.md) and [`Docs/design/dashboard.md`](Docs/design/dashboard.md).

---

## Loose Notes

Refine system prompts for agents - how systematically?

### Fill out guide corpus

Download and create guides.

### Get dashboard inspiration from existing mods

### Simple actions that may be straightforward enough for MVP Assisted Apply

Moved into the Assisted Apply roadmap slice. Keep the implementation narrow: `unforbid` known item stacks, `mark_harvest` validated safe plant clusters, `mark_hunt` deterministic low-risk animal batches, and one single-workbench simple-meal bill upsert; work priorities, broad bill editing, zones, pawn assignment, medical/prisoner actions, and combat controls stay out of the first slice.

### Scan GitHub repos for reference ideas

### Older notes

Is the agenda saved to a file that the dashboard reads from? Is there history?

Migrate from Codex Chrome plugin to Playwright for frontend.

### About Chef's Advice / Alerts

- The first alert "Resource requests attention food" - what does attention mean? Why not Trade?
- It is bad day-one advice; do not suggest sending caravans to traders.
- Too many things are generated. Focus on important and currently possible short-term actions; let the Mayor handle grand strategy.
- Alerts priorities use numbers 1-10.
- "Manage Food Stockpile" requested labor; why not tiles to be marked as storage?
- The most basic actions like hauling and cleaning should be automated by the game, and only request labor for urgent cases.
- LABOR requests should specify what kind of work type or skill is required.
- Forage advice should say exactly where the nearest edible plants are and suggest marking them for harvest.
