# RimBob - Tasks

> Single task inbox and execution board for RimBob.
> Big milestone sequencing belongs in `Docs/ROADMAP.md`.
> `/todo` entries go under "Captured by /todo"; promote serious items into the execution board when they become active work.
> Every checkbox line starts with a unique one-word identifier immediately after the checkbox.
> Lifecycle: completed `/todo` captures move to "Done"; promoted items leave the inbox for the board; stale or duplicate captures are deleted.

---

## Captured by /todo

<!-- entries go here -->
- [ ] landing-helper-stop-host [2026-06-04] #ops #git #host Stop the matching Host before landing-helper build/test when `-RestartHost` is set, so master landings do not fail on locked ApiHost DLLs.
- [x] minister-rules-table-refactor [2026-06-03] #design #rules #refactor #ministers #all-hits **Table-driven minister rules (all-hits) — ALL 4 SLICES LANDED** (`483fa38` dedup, `2d02576` base+Chef, `b8189f3` Welfare, `81ff590` Willie). Replace the triple-written rule shape (executable `Evaluate` guards + parallel `RuleMatches` + static `AllRuleEvaluations`) with one per-minister ordered rule-as-data table; live eval, match trace, and all-rules catalogue all derive from it. All ministers use **all-hits** (no first-match, no policy field): every matched rule emits its advice + flags. Shared base in `Src/Common/Ministers` owns the evaluator + `Aggregate` (priority sort/cap, same-`trace` dedup, explicit dominance predicates). Rule record = id/matches/reason/build — no static condition/output strings (diagnostics derive from a live `reason` + emission trace), no concern field. Sequencing: Chef dedup/cap slice → hoist base → flip Welfare → flip Willie (+kitchen-defer dominance predicate). **Unblocked — `delete-concern` landed (`5fdff44`).** Decisions in `Docs/DESIGN.md` (table-driven; all-hits only). [plan](.plans/minister-rules-table-refactor.md)
  - [x] chef-flag-request-dedup [2026-06-03] #food #chef #rules #flags #dedup **Slice 1 — Chef flag-request dedup — LANDED `483fa38`.** All-hits Chef emits the same `BuildingRequest` on multiple concern flags (campfire from emergency+hunt; freezer from emergency+freezer_missing) → `CabinetCycle` runs Willie once per flag → N solves per build + last-write-wins Willie snapshot. Dedup cross-minister requests across the priority-ordered concern flags (highest-priority concern carries each distinct build/labor/item; identity = BuildingRequest(TargetClass,RoomClass,TargetDef)/LaborRequest(WorkType,Skill)/ItemRequest(ItemDef)). Advice cards unchanged (transparency); flags stay 1/concern (signal); no cap (deferred), no shared-base, no coordination change. Chef-only: `Rules.cs` `DecisionForEmissions` + Food tests. [plan](.plans/chef-flag-request-dedup.md)
  - [x] minister-rules-shared-base [2026-06-03] #design #rules #refactor #ministers #base **Slice 2 — shared rule-table base + port Chef — LANDED `2d02576`.** Extract generic `MinisterRule<TB>` (id/matches/reason/build) + table evaluator + `Aggregate` (priority sort + move slice-1 dedup here) + `BuildTrace` (derive `RuleTraceDetails`; AllRules condition-text ← live `reason`, not hand strings) into `Src/Common/Ministers`. Port Chef: `RuleEmissions` → declarative `MinisterRule<FoodBriefing>[]`; **delete** `RuleMatches` + `AllRuleEvaluations` (triple-copy killed). Behavior-neutral: advice/flags/selected-trace identical, replay corpus unchanged; only AllRules condition-text assertions move. Escalation stays Chef-side fallback. [plan](.plans/minister-rules-shared-base.md)
  - [x] welfare-rules-table-port [2026-06-03] #welfare #rules #refactor #ministers #base **Slice 3 — port Welfare onto shared base — LANDED `b8189f3`.** Welfare already all-hits (4 concerns); kill its triple-copy (`RuleMatches`+`AllRuleEvaluations`+private `RuleEmission`+bespoke `DecisionForEmissions`), adopt base `EvaluateAllHits`/`Aggregate`/`BuildTrace`, model `needs_stable` as a fallback descriptor. Mirror Chef's `2d02576` port. Drop redundant `ColonistCount>0` from `break_risk` (verify invariant). Behavior-neutral except AllRules condition-text + equal-priority tiebreak (insertion→rule-name). [plan](.plans/welfare-rules-table-port.md)
  - [x] willie-rules-allhits-board-solve [2026-06-03] #willie #rules #refactor #solver #ministers **Slice 4 (final) — Willie all-hits + solve the board — LANDED `81ff590`.** Flip Willie's 9-rule first-match cascade to all-hits (table/base, dominance predicate on `building_request_active` replacing `ShouldDeferToMissingKitchen`, kill triple-copy, `maintain_build_program` fallback descriptor). Rewire `MinisterOfWillie`: composite `decision.Trace` breaks the single-request solve, so solve the inbound board + emitted missing-room requests, record each in `_byRequest` (fixes willie-requests "one solve/cycle"), enrich each driving advice by its own trace. Real behavior change (1→N cards). Keep public placement helpers + coordination logic. [plan](.plans/willie-rules-allhits-board-solve.md)
- [x] rules-decisions-advice-great-simplification-refactor [2026-06-03] #design #rules #refactor #ministers #flags #advice **LANDED `a711649`. Flatten rules/decisions/advice into a typed effect union.** Rules emit `Decision` effects (`Advise`, `RequestBuild`/`RequestLabor`/`RequestItem`/`RequestAttention`, `Escalate`); the boundary projects `Advise` to stamped `AdviceItem` and `Request*` to `AgentFlag`. Unified advice/flag enum is `Priority`, and the wire field is `priority` everywhere; no compat, old persisted minister/replay state moved to schema backup and regenerated. Dashboard/docs/tests/replay fixtures updated. [plan](.plans/rules-decisions-advice-great-simplification-refactor.md)
- [x] delete-concern [2026-06-03] #design #advice #refactor #llm #dashboard **LANDED `e62591c`. Delete the `concern` concept — rules emit actions + flags only.** Discussion-locked: `concern` doesn't earn its keep. Autonomy dial moves to **action-kind + validation** (already how Assisted Apply gates; the "Auto a concern bundle" story is illusory since builds/knobs inside a concern stay gated, so Auto only ever touches the designation action). In code `concern` is already just a label — only behavioral consumer is `FoodChainModelBuilder` step grouping; no dial/supersession/pushback reads it. Delete: `AdviceItem.Concern`, the 3 per-minister enums (`FoodConcern`/`WelfareConcern`/`WillieConcern` + dead values), `allowed_concerns` LLM contract (prompt/parser/normalizer/id/title-fallback), diagnostics `Concern`, dashboard mirrors. Hard part = chain-model disambiguation (forage-vs-crop `mark_harvest`, cook-vs-butcher `production_bill`) → derive from `Apply.Label`+instruction sniff (no new field; the builder already sniffs blueprints). No compat; wipe-and-regen; replay corpus regen (2 cases). Supersedes the premise of `concern-code-rename`. Lands atomically (~33 files, mostly mechanical test edits). [plan](.plans/delete-concern.md)
- [ ] willie-requests-tab [2026-06-03] #dashboard #willie #solver #requests **Willie → Requests dashboard tab.** New minister tab with a left sidebar listing every inbound building request aimed at Willie and a detail pane showing **all** request fields + the placement-solver outcome (validated option footprints, or no-fit/error message, or "awaiting solve"). Investigation: today the store keeps only the **latest single** solve per minister (`_latest`, overwritten each cycle — `WillieSolverStore.cs:8`) and Willie solves **one** selected request per cycle, so per-request options/error can't be shown without backend memory. Locked: **(1)** add per-request solver memory (store records the inbound request **board** every cycle via new `RecordInbound`, plus a `RequestKey`-keyed outcome map; `WillieSolverSnapshot` gains `Options` from `result.Options`, live-only — **not** persisted to the replay corpus); **(2)** building requests only. New read-only `GET /api/ministers/willie/solver/requests` joins board→outcome. Frontend: new `requests` view key (Willie-only, excluded from `allMinisterViews`), `MinisterRequestsView` master-detail, extract shared `BlueprintFootprintThumbnail`, read-only option cards (Apply stays on Build Queue). Additive — no wire/persistence change, no wipe-and-regen. [plan](.plans/willie-requests-dashboard-tab.md)
- [x] food-independent-concerns [2026-06-02] #food #chef #rules #flags #design **Make Food rules independent concerns.** Replace `Rules.Evaluate()`'s first-match cascade (every `if`+`return` = one rule/tick) with independent concern evaluation: every matched rule emits its own advice item **and its own flag**, ordered by the bus's existing priority sort. Locked: **no dedup, no cap, no folding** (for now) — ship simplest full independence. Investigation: advice axis already free (`Decision.Advice`/`AdviceBus.SortAdvice`/`FoodChainModelBuilder` all list-correct, dashboard already priority-sorts); accepted cost is the flag path — `CabinetCycle` runs Willie once per distinct `food:<trace>` flag with a build (`CabinetCycle.cs:328`), each = one placement solve, so N concern-flags → **N solver runs/cycle** (+ duplicate solves & last-write-wins Willie snapshot, no dedup). Coordination **code** untouched; behavior changes. Escalations stay terminal fallbacks (Chef pipeline treats `Escalate` as mutually exclusive with `Decision`). Fixes `expand_growing_capacity` starvation; deletes the ~135-line `EmergencyRequests`/`EmergencyActions` superset + per-branch freezer wrapping. Heavy test rewrite (`FoodRulesTests` ~39 trace asserts); replay corpus only 2 cases. Deferred: dedup/consolidated flag, priority gate/cap. [plan](.plans/food-rules-independent-concerns.md)
- [x] cabinet-run-step-dialog [2026-06-01] #dashboard #cabinet #sse #debug Added a backend-authored manual cabinet run-step log, `run_id` POST correlation, `cabinet_run` SSE snapshots, and a compact dashboard dialog that opens immediately on `Run Cabinet Now` and reconciles real progress/failure rows. Source plan: `C:\dev\RimBob\.plans\cabinet-run-step-dialog.md`.
- [x] willie-home-area-anchor [2026-06-01] #construction #willie #anchor #ingestion #solver Landed Home-area buildable-region fallback anchor: ingest `Area_Home` rows (verified live `type="Area_Home"`) from the already-fetched `/map/zones` into a light `MapAreaRegistry`, derive a low-priority `RoomClass.BuildableRegion` anchor, and have the solver fall back to it **only** when `ResolveNear` finds zero room anchors (resolution-order priority, no score-weight change), clamped to area bounds. Room anchors keep strict priority. No RIMAPI change; dashboard label/icon + snapshot persistence + tests landed. Cell-mask for non-rectangular areas deferred to buildability-layers. Landed `add66e7`. [plan](.plans/willie-home-area-buildable-region-anchor.md)
- [ ] welfare-meta-plan [2026-06-01] #design #welfare #mood #index **Welfare meta-plan.** Master index for the Welfare (Mood & Needs) effort: north-star = new-colony shelter advice (Welfare emits a `building_request` to Willie for a basic temporary shelter with beds, reusing Willie's `functional_rooms` + Placement Solver — no new engine). Holds the new-colony advice spec (day-1 expected/empty), candidate concern set, reused-machinery map, locked decisions, ordered remaining work, open decisions (Welfare/Medical sequencing), and research steps R1–R6. Source briefing slice already exists; no advice/rules/LLM/Apply yet. [plan](.plans/welfare-meta-plan.md)
- [x] welfare-rules-slice-a [2026-06-02] #welfare #rules #mood #construction #slice **Welfare Rules Slice A — LANDED `eee91c1`.** First deterministic rule cut + minister skeleton. Ships the meta-plan north-star: new colony with no beds → Welfare emits `shelter_floor` advice + an `AgentFlag` carrying `BuildingRequest{RoomClass:Barracks, CapacityNeed:(Beds, ColonistCount), RequestedFrom:"Willie"}`, which Willie's existing `functional_rooms`→PlacementSolver path turns into barracks `options[]` (zero new placement code, modulo a `Barracks`-template alias check). WS1 `WelfareConcern` enum; WS2 `Rules.cs : IMinisterRules<WelfareSourceBriefing>` (wired: `shelter_floor` on `Rooms.BedroomCount==0`, `break_risk` on `Mood.BreakRiskCount>0`; `needs_stable` fallthrough); WS3 `MinisterOfWelfare`+`WelfareStateSummary`+registry (flip `welfare` Ready, `CabinetOrder:12` before Willie)+DI; WS4 new-colony/has-beds/break-risk fixtures. Rules read the **existing** `WelfareSourceBriefing` directly (no schema change, no compat). Mirrors `willie-rules-slice-a`. Depends on [meta-plan](.plans/welfare-meta-plan.md). [plan](.plans/welfare-rules-slice-a.md)
- [x] welfare-rules-slice-b [2026-06-02] #welfare #rules #mood #briefing #dashboard #slice Landed Welfare Rules Slice B from `C:\dev\RimBob\.plans\welfare-rules-slice-b.md`: briefing summaries for sleep/recreation/thought categories (schema change, no compat; wipe-and-regen persisted snapshots), shared `WelfareThoughtTaxonomy`, independent concern emission for `break_risk`, `shelter_floor`, `recreation_gap`, and `comfort_beauty`, refined Willie/Chef owner routing, recreation/dining Willie templates, fixture coverage including multi-concern emission, and a Welfare Mood & Needs briefing HUD with raw briefing collapsed.
- [x] welfare-llm-slice-c [2026-06-02] #welfare #llm #rag #escalation #slice **Welfare LLM Slice C — LANDED.** Give Welfare an LLM escalation path (Chef parity, full feeder). Mirrors `Chef.cs` **minus the first-cycle bootstrap** (removed from contract). WC1 Rules escalation as terminal fallthrough — escalate `unexplained_mood_pressure` only when no deterministic Welfare rule matched but material out-of-scope mood pressure remains (else `needs_stable`); WC2 `welfare.system.md` prompt; WC3 `MinisterOfWelfare` LLM arms (`ForceLlm`/`RulesOnly`, no bootstrap) + `LlmClient`/RAG deps; WC4 `WelfareLlmResponseParser`/normalizer with no concern field, note-only Welfare actions, `RequestedFrom:"Willie"` flags, dropped malformed-flag count, and model-authored Apply/`place_blueprint` stripped; WC5 `WelfareRagRetriever`+query builder over mood/room guides; WC6 registry flip (`CanRunLlm`/`HasPrompt`/`HasRag`/`HasRawLlmOutput`/`HasManualLlmOutput`, full views)+DI; WC7 escalation/normalization/replay tests. Promoting recurring social/ideology/temperature categories to deterministic rules is evidence-based Slice D. Gimp C1(escalate seam)→C2(prompt+normalize)→C3(RAG+activation). [plan](.plans/welfare-llm-slice-c.md)
- [ ] verify-first-cycle-bootstrap-removed [2026-06-01] #verify #ministers #cleanup Verify no feeder implements a forced first-cycle (bootstrap) LLM escalation branch and delete it if present. `Docs/design/ministers.md` dropped the "First Live Cycle Bootstrap" rule (rules+escalation now run identically on the first cycle; `StartupBootstrap` stays a wake *reason* only). Doc said runtime uses cabinet-refresh + manual triggers and `StartupBootstrap` was "not a Host-rebuild cabinet wake," so this is likely doc-only — but Food/Willie are built; grep `MinisterOf*`/`Rules.cs`/cabinet wiring + the `StartupBootstrap`/`PlayCycleContext` trigger path for any "first cycle → force escalate" logic. Keep Mayor's no-persisted-output bootstrap Agenda (separate mechanism).
- [ ] chef-unforbid-in-rules [2026-05-31] #food #chef #rules #briefing #llm #flags Minister-refine (Chef). **S1 (primary):** the `nutrition_signal_gap` rule emits only `set_stockpile_zone` while forbidden survival meals sit on the map, because the forbidden-meal helpers (`ForbiddenMealCount` etc., `Rules.cs:622,1149`) read `briefing.UnclassifiedFoodItems` (upstream passthrough, empty 8/8 in corpus) instead of `briefing.UnforbidTargets` (derived live, populated; already used by `UnforbidApply`). Repoint helpers + prepend an `unforbid` action/item-request in the gap branch (revives it for the 36 `emergency_food_flag` cases too). Briefing already carries the fact; no derivation/wire change; matches `food.md:170`. **S2 (independent):** subagent flag silently dropped (flag_count 0→0→1) — `food.system.md:20` under-specifies the `AgentFlag` envelope + valid `BuildingClass`/`work_type` enums, and `NormalizeFlag` (`AdviceResponseNormalizer.cs:119`) drops malformed flags with no warning; tighten prompt + surface a dropped-flag count. Corpus: `nutrition_signal_gap` 5×, all with unforbid-meals, 0 unforbid emitted. [plan](.plans/chef-unforbid-in-rules-and-flag-contract.md)
- [ ] willie-proposed-empty-copy [2026-05-31] #dashboard #willie #ui #copy Willie Build Queue **Proposed** empty-state reads jargon *"Solver placement options appear here when Willie emits `options[]`."* — confusing, hides the real cause (no `building_request` has reached Willie because Chef hasn't run, so the solver has nothing to place). Make it context-aware off `requests.length` (the Requested lane it already computes): Case A (no request) names Chef + Requested + missing-room self-trigger; Case B (request but no options) points at the driving advice no-fit reason. Single file (`MinisterBuildQueueView.tsx:73`), copy + one branch; no backend/solver/`options[]` change (that's `rimapi-room-reads-live-verify`). [plan](.plans/willie-proposed-empty-state-copy.md)
- [ ] rimapi-map-reach-live-verify [2026-05-27] #rimapi #construction #pathfinding #live Verify loaded RIMAPI DLL exposes `/api/v1/map/reach`, `/api/v1/map/path-cost`, `/api/v1/map/path-cost/batch` after RimWorld starts; run same-room, sealed-cell, closed-door, single-cost, and batch smoke checks.
- [ ] rimapi-blueprint-placement-endpoint [2026-05-26] #rimapi #construction Expand RIMAPI fork blueprint endpoints: validate, place, read pending, allow/disallow, cancel by id, backlog summary. Fork-only; no RimBob Host wiring. [plan](.plans/rimapi-blueprint-placement-endpoint.md)
- [ ] normalized-game-time [2026-05-26] #design #state #dashboard Normalize all game-date fields from `game_tick`: total days, completed days, colony day/year, day-of-year, parsed quadrum/hour, shared formatter; no compat code, wipe-and-regen persisted snapshots. [plan](.plans/normalized-game-time.md)
- [ ] base-image-scoring [2026-05-25] #idea #spike #construction Evaluate base-image analysis for scoring metric calibration.
- [ ] rimapi-blueprint-live-verify [2026-05-25] #rimapi #construction Verify the new blueprint lifecycle endpoints in live RimWorld: endpoint discovery, validate/place, allow/disallow, cancel blueprint/frame, and backlog grouping.
- [ ] concern-code-rename [2026-05-23] #refactor #advice #rename **Code rename `advice_type` → `concern`.** C# property + enum types (`FoodAdviceType` → `FoodConcern`), JSON wire field, prompts, fixtures, dashboard TS mirrors, tolerant inbound + replay reader. Gimping via Codex; doc rename runs in parallel. [plan](.plans/code-rename-to-concern.md)
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
- [ ] add-rimapi-fork-endpoints-blueprint [2026-05-19] #high-prio #rimapi #construction Add RIMAPI fork endpoints for complete pending-blueprint lifecycle: validate, place, read, allow/disallow, cancel, and backlog summary. [plan](.plans/rimapi-blueprint-placement-endpoint.md)
- [ ] rimapi-stockpile-detail-read [2026-05-24] #rimapi #construction #storage Add stockpile detail read support: cells/bounds, priority, allowed filters, used/total cells, and item counts for storage pressure and material flow.
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
