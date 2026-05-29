# RimBob - Tasks

> Single task inbox and execution board for RimBob.
> Big milestone sequencing belongs in `Docs/ROADMAP.md`.
> `/todo` entries go under "Captured by /todo"; promote serious items into the execution board when they become active work.
> Every checkbox line starts with a unique one-word identifier immediately after the checkbox.
> Lifecycle: completed `/todo` captures move to "Done"; promoted items leave the inbox for the board; stale or duplicate captures are deleted.

---

## Captured by /todo

<!-- entries go here -->
- [x] placement-solver-1 [2026-05-28] #construction #willie #solver Implementation plan for Placement Solver Solver1 skeleton: PlacementSpec + group-validate client (Solver1a), anchor resolution + TemplateAnchoredGenerator (Solver1b), gates/path-cost score/validate/assemble (Solver1c), determinism tests (Solver1d). Freezer/near-kitchen/one-option; FORK3 walkable scoring with centroid fallback target cells. Landed `5a8e6c9`. [plan](.plans/placement-solver-1.md)
- [x] placement-solver-2 [2026-05-29] #construction #willie #solver Implementation plan for Placement Solver Solver2: extract IPlacementScorer seam + index-aligned PathCostLookup (fixes reverse-match bug) (2a), per-generator budgets + template variants + dedupe + diverse top-K + 1-3 options (2b), LargestEmptyRectangleGenerator + free-space evidence + expansion_room component (2c), multi-option tradeoff notes + determinism/diversity tests (2d). Bounded competition on the landed Solver1 skeleton (5a8e6c9). Landed `9f6fe90`..`0e86f5f`. [plan](.plans/placement-solver-2.md)
- [x] willie-placement-wiring [2026-05-29] #construction #willie #solver #integration Wire the landed Placement Solver into MinisterOfWillie: inject PlacementSolver + ColonyState, call SolveAsync on freezer_request_active and attach result.Options to the AdviceItem (W1); no-fit NoFitReason note + readiness/solver-trace surfacing (W2). Rules stays sync/pure; async solver call lives in the orchestrator. Freezer path only. Landed `fbad240`. [plan](.plans/willie-placement-wiring.md)
- [x] placement-solver-3 [2026-05-29] #construction #willie #solver Room-class breadth for the Placement Solver: extracted IRoomTemplate seam + shared shell geometry behind FreezerTemplate (S3a), added hospital/bedroom/workshop/storage templates + per-measure capacity sizing + RoomClass alias map (S3b), build_order_safety score component (S3c), and RIMAPI-evidence-gated ReuseExistingFootprintGenerator (S3d) backed by fork room footprint cells. Solver-internal breadth; missing-room orchestrator wiring is a separate pass. [plan](.plans/placement-solver-3.md)
- [x] willie-build-queue-tab [2026-05-29] #dashboard #willie #ui Landed Willie "Build Queue" dashboard tab with stacked collapsible Requested / Proposed / In-backlog sections, SVG footprint thumbnails from `AdviceOption.blueprint_group`, readiness/material chips, backlog table reuse, launcher/deep-link wiring, and explicit unsupported Apply state when no backend option action payload exists. [plan](.plans/willie-build-queue-tab.md)
- [ ] willie-option-apply-payload [2026-05-29] #assisted #apply #willie Emit per-option `place_blueprint_group` action apply payloads, then replace the Build Queue unsupported Apply state with a real player-click round trip once the RIMAPI group-place executor is intentionally in scope.
- [ ] willie-briefing-hud [2026-05-29] #dashboard #willie #ui Replace the raw-JSON Willie briefing render with a HUD: promote the existing WillieBriefingReadout to primary, add an 8-concern tile strip (severity tone) + data-coverage signal bars, demote JsonTree groups to a collapsed "Raw briefing" disclosure (willie branch only). MVP signal bars from the current bool; 3-tier have/need-fork/defer needs a wire change (follow-up). Minimap + gauges + solver-trace filmstrip are follow-ups; minimap shares a base-map render component with Build Queue D. [plan](.plans/willie-briefing-hud.md)
- [x] willie-reuse-footprint [2026-05-29] #construction #willie #solver Completed Solver3 S3d: ReuseExistingFootprintGenerator uses bounded room footprint evidence from `/api/v1/map/rooms?include_cells=true&include_entry_cells=true` to propose same-class interior fixture/floor reuse; wall replacement/freezer cooler retrofits remain out of scope. [plan](.plans/placement-solver-3.md)
- [x] willie-code-review [2026-05-29] #review #willie #construction #subagents Read-only deep audit of the landed Willie surface (Rules + orchestrator, Placement Solver 1/2/3a-c, solver->Rules wiring, ports/DI, tests) via 5 parallel caveman:cavecrew-reviewer subagents on pinned file lists; Claude pins the manifest + known-approximation allowlist, synthesizes/ranks P0-P2, writes willie-code-review-findings.md + follow-up gimp slices. No source edits in the review pass. [plan](.plans/willie-code-review.md) -> [findings](.plans/willie-code-review-findings.md): 0 P0, 5 P1, 4 P2; 5 follow-up slices.
- [x] willie-rules-slice-a [2026-05-28] #construction #willie #rules First deterministic Willie rule cut: WillieConcern enum (WR1), Rules.cs against populated WillieConcernSummaries fields (WR2), MinisterOfWillie + registry + dashboard tab (WR3), tests + fixtures (WR4). Scoped to landed-minimal briefing fields; richer rules wait on derivation extensions. [plan](.plans/willie-rules-slice-a.md)
- [x] willie-briefing-derivation [2026-05-28] #construction #briefing #willie #state Landed Willie briefing record, `WillieBacklog` state aggregate, construction-backlog ingestion, anchor inventory derivation, and RimApiClient FORK3 wrappers. Unblocks Willie Rules Slice A + Placement Solver PS1. [plan](.plans/willie-briefing-derivation.md)
- [x] rimapi-map-region-at [2026-05-27] #rimapi #construction #pathfinding #willie Added `GET /api/v1/map/region-at?map_id=...&x=...&z=...` returning the `Region.id` for the cell via `map.regionGrid.GetValidRegionAt_NoRebuild`; RimBob has a typed client wrapper and endpoint-coverage row. [plan](.plans/willie-briefing-schema.md)
- [x] rimapi-room-entry-cells [2026-05-27] #rimapi #construction #willie #welfare Folded into `/api/v1/map/rooms?include_entry_cells=true`; RimBob ingests per-room boundary door/opening cells into `RoomRecord.EntryCells` and Willie `AnchorInventory.EntryCells[]`. [plan](.plans/willie-briefing-schema.md)
- [x] rimworld-pathfinding-api-doc [2026-05-27] #spike #doc #construction #pathfinding Verify RimWorld native pathfinding/region/reachability API claims; add reference doc. [doc](Docs/reference/rimworld-pathfinding-api.md)
- [ ] rimapi-map-reach-live-verify [2026-05-27] #rimapi #construction #pathfinding #live Verify loaded RIMAPI DLL exposes `/api/v1/map/reach`, `/api/v1/map/path-cost`, `/api/v1/map/path-cost/batch` after RimWorld starts; run same-room, sealed-cell, closed-door, single-cost, and batch smoke checks.
- [x] rimapi-map-reach-and-path-cost [2026-05-27] #rimapi #construction #pathfinding #willie Added RIMAPI in-map reachability + path-cost endpoints (`/api/v1/map/reach`, `/api/v1/map/path-cost`, `/api/v1/map/path-cost/batch`). Wraps `Map.reachability.CanReach`, region-BFS for coarse rank, A* for tiebreak. Unblocks Willie Placement Solver `near:<class>` scoring beyond euclidean-from-centroid. [plan](.plans/rimapi-map-reach-and-path-cost.md)
- [x] rimapi-power-info-dto [2026-05-27] #rimapi #state #bug `PowerInfoDto` mismatch fixed. RIMAPI returns 9 PascalCase fields (6 ints + 3 `List<int>`); RimBob DTO + `MapAggregateMapper.FromPower` + fixture updated. Aggregate power fields surface end-to-end. [plan](.plans/rimapi-power-info-dto.md)
- [x] willie-briefing-schema [2026-05-27] #design #construction #briefing #m4 Willie Briefing Schema: per-field signal/source/availability/consumers/notes for 8 concerns + anchor inventory contract. Renamed from willie-gate-design; S1 fields offloaded to companion doc. [plan](.plans/willie-briefing-schema.md) [fields](.plans/willie-briefing-fields.md)
- [ ] rimapi-blueprint-placement-endpoint [2026-05-26] #rimapi #construction Expand RIMAPI fork blueprint endpoints: validate, place, read pending, allow/disallow, cancel by id, backlog summary. Fork-only; no RimBob Host wiring. [plan](.plans/rimapi-blueprint-placement-endpoint.md)
- [ ] normalized-game-time [2026-05-26] #design #state #dashboard Normalize all game-date fields from `game_tick`: total days, completed days, colony day/year, day-of-year, parsed quadrum/hour, shared formatter; no compat code, wipe-and-regen persisted snapshots. [plan](.plans/normalized-game-time.md)
- [ ] base-image-scoring [2026-05-25] #idea #spike #construction Evaluate base-image analysis for scoring metric calibration.
- [ ] rimapi-blueprint-live-verify [2026-05-25] #rimapi #construction Verify the new blueprint lifecycle endpoints in live RimWorld: endpoint discovery, validate/place, allow/disallow, cancel blueprint/frame, and backlog grouping.
- [x] food-to-chef-rename [2026-05-24] #food #debt #doc Rename the Food minister to Chef.
- [ ] concern-code-rename [2026-05-23] #refactor #advice #rename **Code rename `advice_type` → `concern`.** C# property + enum types (`FoodAdviceType` → `FoodConcern`), JSON wire field, prompts, fixtures, dashboard TS mirrors, tolerant inbound + replay reader. Gimping via Codex; doc rename runs in parallel. [plan](.plans/code-rename-to-concern.md)
- [ ] willie-meta-plan [2026-05-22] #design #construction #index **Willie meta-plan.** Master index for the Willie effort: artifact map, locked decisions, ordered remaining work, open decisions. [plan](.plans/willie-meta-plan.md)
- [ ] wfc-variant-generator [2026-05-22] #idea #spike #construction Evaluate WFC as a bounded Willie variant generator. [plan](.plans/wfc-variant-generator.md)
- [ ] assisted-apply-mine-cut [2026-05-22] #assisted #apply #construction #food Add `mark_mine` + `cut_plants`/chop **designations** to the Assisted Apply allowlist. Same low-blast class as the current 4 (additive, ephemeral, game clears once done). Willie: clear build site / get steel; Food: wood + clearing. Design: extend `AdviceApplyKind` + validator; advice.md allowlist.
- [ ] assisted-apply-create-zone [2026-05-22] #assisted #apply #zones #construction #food Let a minister / Placement Solver suggest a **new** growing or material-stockpile zone as an Assisted Apply. Carve-out: CREATE-new-empty zone = additive/targeted (apply-eligible); EDITING an existing zone's filters/priority = policy knob (stays Suggest-only). Revisit advice.md "Policy Knobs vs Targeted Designations"; Solver emits the rectangle.
- [ ] assisted-apply-deconstruct-eval [2026-05-22] #assisted #apply #construction Evaluate `mark_deconstruct` as an apply — it DESTROYS a building (higher blast than additive designations). Needs a confirm/undo story before allowlisting; not in the "easy" tier.
- [ ] advice-action-vocab-expansion [2026-05-23] #design #advice #vocabulary Extend `AdviceActionKind` to cover common missing player designations: `mark_mine`, `cut_plants`/chop, `mark_deconstruct`, `smooth`, `claim`, `install`/`uninstall` (minified), `tame`, `slaughter`, `haul-to`. Prerequisite for the apply-expansion todos (`assisted-apply-mine-cut`, `assisted-apply-create-zone`). Equip/apparel/medical/prisoner/combat stay deferred per advice.md (pawn assignment → Auto).
- [x] refactor-baseline-scope-lock [2026-05-15] #refactor #ops Captured the baseline build/test state, searched old advice schema names, and scoped the first refactor slice to schema repair.
- [x] refactor-advice-priority-schema [2026-05-15] #refactor #advice Replaced severity/priority-score behavior with `AdvicePriority`, priority sorting, and tolerant legacy input parsing.
- [x] refactor-llm-advice-normalizer [2026-05-15] #refactor #food #llm Split Food LLM advice normalization into orchestration, compatibility parsing, resource requests, suggested actions, and work-type inference.
- [x] refactor-minister-registry [2026-05-15] #refactor #cabinet Added the minister registry/descriptors for scope metadata, cabinet order, dashboard readiness, manual triggers, prompts, raw output, and manual LLM ingestion.
- [x] refactor-endpoint-coverage-catalog [2026-05-15] #refactor #system Centralized endpoint coverage metadata beside endpoint mapping instead of hand-maintaining `/api/system/health` rows in `SystemEndpoints`.
- [x] refactor-rag-retrieval [2026-05-15] #refactor #rag Generalized Mayor/Food RAG retrieval with retrieval profiles, shared retrieval flow, and per-minister query builders.
- [x] refactor-ingestion-mappers [2026-05-15] #refactor #state Split ingestion aggregate mapping into pure pawn, map, resource, and threat mappers while leaving `IngestionDispatcher` as orchestration.
- [x] refactor-common-derivations [2026-05-15] #refactor #state Extracted shared season, pawn, threat, building, and map-distance derivation helpers used by Mayor and Food.
- [x] refactor-briefing-cache [2026-05-15] #refactor #briefing Generalized briefing cache entries with `CachedBriefing<TBriefing>` while preserving Mayor/Food public cache methods.
- [ ] source-todo-mayor-briefing-context [2026-05-21] #mayor #briefing #state Cover remaining MayorBriefing source TODOs: TrendWindows, CoS digest, recent feedback, AgendaPosture, ScheduleSnapshot, and cold-snap forecast signals.
- [ ] source-todo-prisoner-surgery-signals [2026-05-21] #welfare #medical #rimapi Add prisoner count and pending surgery operation signals before filling the kept briefing derivation TODOs.
- [ ] source-todo-lord-hostility-signal [2026-05-21] #defense #rimapi #state Replace the Lord job-name raid heuristic with a live faction-hostility signal when RIMAPI exposes one.
- [ ] source-todo-pawn-edit-job-cache [2026-05-21] #labor #auto #rimapi Cache Pawn Edit and Pawn Job Controller field shapes before wiring policy/job advice beyond Suggest-only reads.
- [ ] source-todo-building-condition-read [2026-05-21] #construction #rimapi #state Add building hp/power/working-state reads before Willie relies on building condition evidence.
- [x] source-todo-room-quality-read [2026-05-21] #welfare #rimapi #state Landed room quality read support for Welfare source evidence: RIMAPI room stats, RoomRegistry ingestion, snapshot persistence, and Welfare source briefing.
- [ ] source-todo-quest-awareness [2026-05-21] #cos #mayor #rimapi Wire quest fields into state and briefings once CoS/Mayor need quest pressure.
- [x] testlogger-concurrent-queue [2026-05-21] #ops #test Replace coarse lock in TestLogFile with ConcurrentQueue + background writer. [plan](.plans/testlogger-concurrent-queue.md)
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
- [ ] add-rimapi-fork-endpoints-blueprint [2026-05-19] #high-prio #rimapi #construction Add RIMAPI fork endpoints for complete pending-blueprint lifecycle: validate, place, read, allow/disallow, cancel, and backlog summary. [plan](.plans/rimapi-blueprint-placement-endpoint.md)
- [ ] rimapi-building-detail-read [2026-05-24] #rimapi #construction #state Add detailed building read support for Willie: hp/max hp, stuff, room id, working state, power/fuel/flickable state, and flammability.
- [ ] rimapi-stockpile-detail-read [2026-05-24] #rimapi #construction #storage Add stockpile detail read support: cells/bounds, priority, allowed filters, used/total cells, and item counts for storage pressure and material flow.
- [x] rimapi-room-detail-read [2026-05-24] #rimapi #construction #welfare Added room detail read support on `/api/v1/map/rooms`: role, bounded `cells[]`, bounds, entry cells, contained building ids, temperature, roof/open roof, region id, and room quality stats; RimBob ingests the fields for Welfare/Willie evidence.
- [ ] rimapi-power-net-read [2026-05-24] #rimapi #construction #power Add power-net detail read support: net id, generators, batteries, consumers, stored/max energy, online/offline consumers, and disconnected critical assets.
- [ ] rimapi-buildability-layers-read [2026-05-24] #rimapi #construction #layout Add bounded buildability-layer read support for placement scoring: passability, roof type, edifice/blockers, terrain affordance, fog, and fertility over a caller-supplied rect.
- [ ] rimapi-blueprint-groups-overlay [2026-05-21] #rimapi #construction Blueprint GROUP placement (room shell + contents) + planning overlay in the fork; builds on add-rimapi-fork-endpoints-blueprint. [plan](.plans/rimapi-blueprint-groups-and-planning-overlay.md)
- [ ] willie-plan-new-base [2026-05-21] #construction #idea #future Willie plans a NEW base / major expansion: lay a planning overlay (vanilla Plan designator or forbidden blueprints), player commits region-by-region into real blueprint groups. Guides: more-planning-mod, rimworld-planner. See Capability B in [plan](.plans/rimapi-blueprint-groups-and-planning-overlay.md).
- [ ] willie-construction-design [2026-05-21] #construction #design Willie design anchors: canonical concerns (done), advice-output schema + request taxonomy (in flight). [concerns](.plans/willie-advice-types.md)
- [ ] placement-solver [2026-05-21] #construction #design Placement Solver: deterministic engine turning building_requests/build_intent into validated layout options (no LLM); building_requests feed it directly. [plan](.plans/placement-solver.md)
- [ ] schema-landing [2026-05-22] #refactor #advice #schema Land advice/flag schema: S1 additive types + AdviceAction icon/reason drop; S2 flag typed request arrays; S3 apply per-kind split. Gimp-able, slice-by-slice. [plan](.plans/schema-landing.md)
- [ ] advice-chain-route-cards-grow [2026-05-19] #dashboard #food #ux Advice chain route cards (Grow/Hunt/Forage) above the Food snapshot. [plan](.plans/advice-chain-visualization.md)
- [ ] remove-legacy-repo-local-icon [2026-05-18] #ops #cleanup Remove legacy repo-local icon/log artifacts after AppData cache/log paths stay verified. [plan](.plans/icons-warm-fixes.md)
- [ ] legacy-worktree-migration [2026-05-17] #git #debt Migrate dirty legacy worktrees after their active slices land.
- [ ] skill-validator-dependency [2026-05-16] #skill #debt Fix local skill validator Python dependency.
- [ ] add-restricted-player-facing-markdown [2026-05-16] #dashboard #markdown Add restricted player-facing Markdown rendering when advice bodies or guide snippets need rich formatting; keep raw/debug views unrendered.
- [ ] minister-rag-knowledge [2026-05-15] #idea #llm #rag Provide ministers with more RAG knowledge.
- [ ] benchmark-optional-toon-prompt-encoding [2026-05-09] #spike #llm #test Benchmark optional TOON prompt encoding. [plan](.plans/toon-prompt-encoding-spike.md)

---

## Execution Board

> Moved to done when shipped, not when coded. Keep this list short.
> Big items belong in `Docs/ROADMAP.md`. This is day-to-day work.

### Now (live-state gaps)

- [ ] medical-hediff-severity-wiring **Medical hediff severity wiring.** If Mayor or a future Medical minister needs life-threatening condition awareness, preserve the relevant `Hediffs` signal into state/briefing instead of inferring severity from generic `Health`.
- [ ] total-nutrition-0-upstream-investigation **`total_nutrition == 0` upstream investigation.** RIMAPI returns 0 nutrition even when `food_total > 0` and meals exist on map. Check whether this is a bug we can patch around (e.g. compute from `meals_count * 0.9 + raw_food_count * 0.05`) or a deeper RIMAPI gap. [plan](.plans/total-nutrition-zero-fallback.md)

### Current implementation order

1. [ ] investigate-fix-patch-total-nutrition **Investigate and fix/patch `total_nutrition == 0`.** Decide whether to patch RIMAPI upstream or compute a documented RimBob fallback from stored meal/raw-food counts.
2. [ ] add-minimal-cos-handling **Add minimal CoS handling.** Implement the Mayor-side helper for dedupe, lead framing, and tactical-alert vs digest routing before multiple feeders exist.
3. [ ] construction-minister **Add Willie.** Food's first live dependencies are cooler / power / room / storage recommendations, not Defense coupling.
4. [ ] defense-minister **Add Defense.**
5. [ ] resolve-welfare-medical-sequencing **Resolve Welfare / Medical sequencing.** Decide whether they land together or whether Medical becomes its own follow-on slice (`M4.5` / second wave), then implement Welfare.
6. [ ] wire-pushback-feedback **Wire Pushback feedback.** Land M5 only after multiple ministers are emitting advice.

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
- [x] reconcile-medical-welfare-boundary-across Reconciled the Medical/Welfare boundary across docs: Welfare owns Mood & Needs; Medical owns treatment.
- [ ] expand-roadmap-beyond-first-cabinet Expand the roadmap beyond the first cabinet wave: explicitly schedule Industry, Medical, Research, and Economy instead of leaving them only in post-MVP notes.

#### Cabinet rollout planning

- [ ] minister-scope-docs Add scope docs for Industry, Medical, Research, and Economy once their first slices are scheduled.
- [ ] concern-enums Per-minister `concern` enums - define in each minister's session.
- [ ] minister-code-scope-docs Per-minister scope docs (`RimBob.Ministers/<name>/scope.md`) - write after first slice ships.
- [ ] construction-placement-layout-strategy-base Willie: placement / layout strategy (Base Layout Minister candidate). [plan](.plans/base-construction-layout-agent.md)
- [ ] define-minimum-viable-cos-solver Define the minimum viable CoS solver rule set for M4: consume Mayor posture/`cabinet_direction`, issue reports or current-runtime flags, and structured requests; dedupe same-issue pressure, choose lead framing, and decide when an issue becomes a tactical alert versus Mayor-digest input. [plan](.plans/deterministic-cos-cabinet-issue-solver.md)
- [ ] medical-welfare-rollout Decide whether Medical should stay coupled to Welfare in the rollout plan or become its own slice after Welfare.

#### Longer-tail design

- [ ] minister-promotion-criteria Candidate minister promotion criteria (CMO, Research, Trade, Treasury).
- [ ] memo-cadence-calibration Memo cadence calibration: flag-severity-gated tactical alerts vs. strict daily.
- [ ] dashboard-notification-ux Dashboard: notification UX (in-page only vs. browser notifications).
- [ ] colonycontext-retirement Retire or narrow legacy `ColonyContext` on the rules path now that minister wake reasons moved to `PlayCycleContext` and feeder ministers use agenda-derived briefing context.

### Auto epic (M7 - defer until then)

- [ ] auto-write-shim Auto write shim (one validated declarative knob write + readback) per [`Docs/design/planning.md`](Docs/design/planning.md).
- [ ] labor-bulletin-board Bulletin board (`Coordination/BulletinBoard.cs`) — queues policy-change requests for Labor, not per-pawn tickets — per [`Docs/design/communication.md`](Docs/design/communication.md).
- [ ] labor-policy-recommender Labor policy recommender per [`Docs/design/ministers/labor.md`](Docs/design/ministers/labor.md).
- [ ] rimapi-policy-knob-write-endpoint RIMAPI policy-knob write-endpoint coverage map (Pawn Edit Controller etc.).
- [ ] minister-concern-autonomy-dial Per-(minister, concern) autonomy dial wired with real Auto execution.
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

### Dashboard v2

Full redesign of the dashboard. First brainstorm; do not make changes yet. [plan](.plans/dashboard_v2.md)

- Left side: tab for each minister, with emojis.
- Add a SYSTEM tab for info about LLM usage, logs, etc.
- Top of main area: tabs for System Prompt, Briefing, RAG, Rules, Advice.
- Each view is structured into sections for readability and navigation.
- Remove the pushback buttons for now.
- Right sidebar: no need to break up into sections when there are just a few items in each. Add a small panel for each colonist.
- Header is fine.
- Read the design docs to see if something important is missing, e.g. whether we show triggers.

Then research either/or:

- Frontend dashboard skill for AI agents.
- React library for dashboards with good support for collapsible panels and data.
