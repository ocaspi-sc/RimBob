# RimBob - Human Todo

> Single task inbox and execution board for RimBob.
> Big milestone sequencing belongs in `Docs/ROADMAP.md`.
> `/todo` entries go under "Captured by /todo"; promote serious items into the execution board when they become active work.

---

## Captured by /todo

<!-- entries go here -->
- [ ] [2026-05-18] #ops #cleanup Remove legacy repo-local icon/log artifacts after AppData cache/log paths stay verified.
- [x] [2026-05-17] #rimapi #fork Clone, build, and load the RimBob-compatible RIMAPI fork from `C:\dev\RIMAPI-for-RimBob`. [plan](Docs/plans/rimapi-fork-migration.md)
- [x] [2026-05-17] #rimapi #assisted After the RIMAPI fork lands, verify Food uses safe unforbid apply end to end.
- [x] [2026-05-17] #rimapi #harvest Add `is_harvestable` / `growth_progress` to `/map/plants` after migrating to a RIMAPI fork.
- [x] [2026-05-16] #rimapi #assisted Validate companion safe `/api/v1/order/unforbid` endpoint and remove RimBob fallback caveat.
- [ ] [2026-05-16] #skill #debt Fix local skill validator Python dependency.
- [ ] [2026-05-16] #replay #mayor Decide whether legacy `/api/agenda/manual` should emit replay records or be retired.
- [ ] [2026-05-16] #dashboard #markdown Add restricted player-facing Markdown rendering when advice bodies or guide snippets need rich formatting; keep raw/debug views unrendered.
- [x] [2026-05-15] #food #advice #schema Collapse Food advice into one priority-tagged action list. [plan](.plans/collapse-food-advice-steps.md)
- [ ] [2026-05-15] #idea #llm #rag Provide ministers with more RAG knowledge.
- [x] [2026-05-14] #doc #debt Avoid over-specifying code-documented details in design docs.
- [x] [2026-05-14] #spike #backend #debt Review RimBob C# codebase and propose refactorings. [plan](.plans/recommended-refactorings.md)
- [x] [2026-05-14] #debt #doc Complete the RimBob product rename.
- [ ] [2026-05-17] #git #debt Migrate dirty legacy worktrees after their active slices land.
- [ ] [2026-05-17] #git #ops Update origin URL after upstream repository rename.
- [ ] [2026-05-09] #spike #llm #test Benchmark optional TOON prompt encoding. [plan](Docs/plans/toon-prompt-encoding-spike.md)

---

## Execution Board

> Moved to done when shipped, not when coded. Keep this list short.
> Big items belong in `Docs/ROADMAP.md`. This is day-to-day work.

### Now (live-state gaps)

- [x] **PawnMedicalInfo downed/dead wiring.** `is_downed` and `is_dead` now flow into `ColonistRecord`; Mayor pawn lines expose `IsDowned`, dead pawns are filtered, and `DeriveMedical` counts authoritative downed state instead of a low-health heuristic.
- [ ] **Medical hediff severity wiring.** If Mayor or a future Medical minister needs life-threatening condition awareness, preserve the relevant `Hediffs` signal into state/briefing instead of inferring severity from generic `Health`.
- [x] **`/resources/stored` integration.** Populate per-def stored item counts from `/resources/stored` so Food can classify meals/raw food and Mayor can surface material counts.
- [ ] **`total_nutrition == 0` upstream investigation.** RIMAPI returns 0 nutrition even when `food_total > 0` and meals exist on map. Check whether this is a bug we can patch around (e.g. compute from `meals_count * 0.9 + raw_food_count * 0.05`) or a deeper RIMAPI gap.

### Current implementation order

1. [x] **Verify Food safe `unforbid` Assisted Apply end to end.** Confirm the RIMAPI fork endpoint, RimBob backend executor, dashboard Apply UI, validation/read-back, and live game behavior work together.
2. [x] **Add harvestability/growth plant signals.** Add `is_harvestable` / `growth_progress` to RIMAPI `/map/plants`, then wire Food harvest logic and `mark_harvest` validation to those fields.
3. [ ] **Investigate and fix/patch `total_nutrition == 0`.** Decide whether to patch RIMAPI upstream or compute a documented RimBob fallback from stored meal/raw-food counts.
4. [x] **Ship Food M3 end to end.** Briefing fields, initial rules, first flag contract, Mayor digest ingestion, dashboard rendering for advice actions, then fixtures.
5. [ ] **Add minimal CoS handling.** Implement the Mayor-side helper for dedupe, lead framing, and tactical-alert vs digest routing before multiple feeders exist.
6. [ ] **Add Construction.** Food's first live dependencies are cooler / power / room / storage recommendations, not Defense coupling.
7. [ ] **Add Defense.**
8. [ ] **Resolve Welfare / Medical sequencing.** Decide whether they land together or whether Medical becomes its own follow-on slice (`M4.5` / second wave), then implement Welfare.
9. [ ] **Wire Pushback feedback.** Land M5 only after multiple ministers are emitting advice.

### Next (post-M3 follow-ups)

- [x] Add deterministic Food crop-yield math (`FoodCropMath`/candidate table) so rules can choose rice/potato/corn from grow time, nutrition per tile, fertility sensitivity, days to winter, current food buffer, and terrain fertility; exact grow-zone placement remains deferred. [plan](.plans/deterministic-crop-yield-math.md)
- [x] Expose computed crop candidates directly to Food LLM escalation when crop choice remains ambiguous.
- [x] Use def-backed harvest nutrition for Food crop candidate projections when `/def/all` exposes raw crop nutrition.
- [ ] Add polished guide-citation footnotes on feeder memo cards.
- [x] Add MVP Assisted Apply for allowlisted player-confirmed actions (`unforbid`, `mark_harvest`, `mark_hunt`, simple-meal bill): backend executor, validation/read-back, dashboard Apply/result state.
- [x] Add player-clicked simple-meal cook-bill upsert with single-workbench guard and RIMAPI read-back.
- [x] Add durable Food decision/replay corpus persistence so M6 refinement can compare before/after outputs on historic inputs, not just fixtures.
- [ ] Extend replay corpus persistence beyond Food and attach future Pushback/outcome fields once feedback is wired.
- [ ] Improve Food hunting target risk/value scoring from live animal data.
- [x] Expand Food briefing with current cooking bill state so satisfied simple-meal bills suppress repeated bill advice.
- [ ] Expand Food briefing with freezer room temperature and spoilage timers when RIMAPI exposes them.
- [x] `Briefing` tab renders latest Mayor/Food briefing JSON.
- [x] One-time bootstrap escalation now runs through explicit `PlayCycleContext.StartupBootstrap`; Food uses it in M3.
- [x] Food active advice now publishes as a minister-scoped snapshot, so bootstrap LLM cards are replaced by the next successful Food cycle instead of lingering by unique id.

### Later (M5 - Feedback loop)

- [ ] `POST /api/agenda/{version}/item/{id}/feedback` writes a `FeedbackEvent` to the decision log.
- [ ] Wire dashboard Accept / Dismiss / Pushback buttons (Pushback modal opens an editable text field, posts `pushback_text`).
- [ ] `Decision Log` tab renders the last N `FeedbackEvent`s with the originating Agenda bullet.
- [ ] Per-minister persistent pushback list (each minister owns + carries forward player corrections).

### Design TODOs (deferred)

#### Doc alignment

- [ ] Extend the action ownership sketch into a full RIMAPI action/endpoint catalogue, preserving owner/requester/executor labels.
- [ ] Reconcile the Medical/Welfare boundary across docs (`Docs/DESIGN.md` still describes Welfare as owning medical sub-blocks, while `Docs/design/ministers.md` splits Medical into its own subsystem).
- [x] Update `Docs/design/dashboard.md` to remove stale Modify / `modified_actions` / implicit-feedback language and align it with Pushback-only feedback.
- [ ] Expand the roadmap beyond the first cabinet wave: explicitly schedule Industry, Medical, Research, and Economy instead of leaving them only in post-MVP notes.

#### Cabinet rollout planning

- [ ] Add scope docs for Industry, Medical, Research, and Economy once their first slices are scheduled.
- [x] Food minister M3 prep: replace remaining code-facing Agriculture names with Food where the M3 runtime touched it (`FoodBriefing`, `MinisterOfFood`, fixtures under `Src/Tests/Food/Fixtures/`).
- [ ] Per-minister `advice_type` enums - define in each minister's session.
- [ ] Per-minister scope docs (`RimBob.Ministers/<name>/scope.md`) - write after first slice ships.
- [ ] Construction: placement / layout strategy (Base Layout Minister candidate).
- [ ] Define the minimum viable CoS arbitration rule set for M4: dedupe same-issue flags, choose lead framing when multiple ministers point at the same problem, and decide when a flag becomes a tactical alert versus Mayor-digest input.
- [ ] Decide whether Medical should stay coupled to Welfare in the rollout plan or become its own slice after Welfare.

#### Longer-tail design

- [ ] Candidate minister promotion criteria (CMO, Research, Trade, Treasury).
- [ ] Memo cadence calibration: flag-severity-gated tactical alerts vs. strict daily.
- [ ] Dashboard: notification UX (in-page only vs. browser notifications).
- [ ] Retire or narrow legacy `ColonyContext` on the rules path now that minister wake reasons moved to `PlayCycleContext` and feeder ministers use agenda-derived briefing context.

### Auto epic (M7 - defer until then)

- [ ] HTN engine (`Planner/`) per [`Docs/design/planning.md`](Docs/design/planning.md).
- [ ] Bulletin board (`Coordination/BulletinBoard.cs`) per [`Docs/design/communication.md`](Docs/design/communication.md).
- [ ] Labor / assignment solver per [`Docs/design/ministers/labor.md`](Docs/design/ministers/labor.md).
- [ ] RIMAPI write-endpoint coverage map.
- [ ] Per-(minister, advice_type) autonomy dial wired with real Auto execution.
- [ ] Autonomy dial UI with confirmation step.

### Claude skills to build (see ROADMAP)

- [x] `minister-refine` skill (proposal-first review of decision logs and future Pushbacks).
- [ ] `fixture-gen` skill (can synthesize from Pushback events).
- [ ] `briefing-check` skill.
- [ ] `rule-promote` skill.

### Known open questions

See [`Docs/DESIGN.md`](Docs/DESIGN.md) decision log and Open Questions sections in sub-docs, especially [`Docs/design/advice.md`](Docs/design/advice.md) and [`Docs/design/dashboard.md`](Docs/design/dashboard.md).

### Done

- [x] Initial DESIGN.md written.
- [x] Strategic plan Y1-Y2 guide written (`Docs/guides/strategic-plan-y1-y2.md`).
- [x] Mayor system prompt drafted; rewritten for Agenda schema in M1 W4.
- [x] Split `RimBob.Agents` into `RimBob.LLM` + `RimBob.Ministers`.
- [x] HTN design discussed and documented (now deferred - Auto epic).
- [x] Minister shape (rules + escalation + optimizer) decided.
- [x] No-modules decision made.
- [x] No-direct-minister-comms rule established.
- [x] **Pivot to assisted-gameplay advisor** - DESIGN, ROADMAP, architecture, ministers, communication, mayor docs rewritten; planning + labor marked deferred; new `design/dashboard.md` and `design/advice.md` created; CLAUDE.md routing table updated.
- [x] **M0 - Repo lit.** Solution + projects, RIMAPI handshake, Google.GenAI ping, Dashboard scaffold, Host with `/api/health` + SSE skeleton, localhost-only bind, CI building both .NET and Dashboard.
- [x] **M1 W1 - Core types.** `MayorAgenda`, `AgendaPriority`, `AgendaPriorityStatus`, `MayorPosture`, `AutonomyMode`, `FeedbackAction`, `FeedbackEvent`, `MayorAgendaInput` under `RimBob.Core.Advice`. Snake-case wire format.
- [x] **M1 W2 - Mayor minister.** `RimBob.Ministers` project with `MayorAgendaRules` (4 agenda directives) and `Mayor` skeleton implementing `IMinister`.
- [x] **M1 W3 - Coordination.** `RimBob.Coordination` project: `AdviceBus`, `AgendaStore` (30-day ring), `DayTickOrchestrator` (`BackgroundService` polling `Tick / 60000`), `FlagChannel` stub.
- [x] **M1 W4 - LLM call.** `mayor.system.md` rewritten; `PromptBuilder`; `LlmClient.CallMayorAsync` with `responseMimeType=application/json`; Mayor wired end-to-end (briefing -> rules -> LLM -> store -> bus) with retry-on-cap-violation.
- [x] **M1 W5 - Host wiring.** DI registrations; SSE handler at `/api/advice/stream` (replay-on-connect, drop-oldest channel, 15s ping); `/api/agenda/{latest,history}` REST; `/api/autonomy` placeholder.
- [x] **M1 W6 - Dashboard.** Agenda tab renders the live SSE feed: posture badges, state-of-the-union, what-changed, ranked short-term cards with delta badges (`NEW`/`UPDATED`/`DONE`/`DEFERRED`), long-term list. Feedback buttons disabled.
- [x] **M1 W7 - Tests.** 22 tests across MayorAgendaRules, Mayor play cycle, AgendaStore, AdviceBus, DayTickOrchestrator. 62/62 total passing.
- [x] **M1 W8 - Doc reconciliation.** ROADMAP M1 + this file rewritten to reflect Agenda pivot; agenda.md / dashboard.md SSE envelope simplified to `data: {full MayorAgenda}`.
- [x] **M1.5 - Live operability.** Wake Mayor on Host startup, periodic `IngestionDispatcher` calls in `DayTickOrchestrator`, `state_of_the_union` per-category dict, `MayorAgenda.GeneratedAt`, `MayorStatus`, `/api/colony/snapshot`, `/api/status`, `/api/mayor/prompt`, manual cabinet run control, dark command-center dashboard with sidebar telemetry.
- [x] **RIMAPI gap closure.** `ColonistDetailedDto` rewritten for actual nested v2 shape (`pawn` + `detailes.work_info` + `detailes.medical_info`) - names, ages, mood, skills, traits, current_job now populate. New `/resources/summary` and `/research/progress` endpoints in `RimApiClient` feed `ResourceSummary` + `ResearchInfo` aggregates. Mayor system prompt teaches the model what `food.estimated_days_of_food == null` means (request stockpile audit, do not assume starvation). `Docs/design/RimAPI.md` annotated with verified shapes for the three controllers.
- [x] **M2 - Grounded reasoning / RAG.** `RimBob.Knowledge` now has an in-process cosine store, markdown guide ingestion from `Docs/guides`, Gemini embedding + disk cache under the stable machine-local RimBob data root, Mayor retrieval via `guide_context[]`, server-stamped `MayorAgenda.guide_citations[]`, `AgendaPriority.cite_ids`, and RAG-vs-no-RAG fixture snapshots under `Src/Tests/Mayor/Fixtures/rag-vs-norag/`. Tier 1 evergreen prompt distillation and polished dashboard footnote rendering are follow-ups.
- [x] **Advice actions and flag resource requests.** `AdviceItem.actions[]` is the player-facing action list, while `AgentFlag.Requests` keeps the shared `ResourceRequest` schema for cross-minister needs without executing allocation in MVP.

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

### About the Food minister's Advice / Alerts

- The first alert "Resource requests attention food" - what does attention mean? Why not Trade?
- It is bad day-one advice; do not suggest sending caravans to traders.
- Too many things are generated. Focus on important and currently possible short-term actions; let the Mayor handle grand strategy.
- Alerts priorities use numbers 1-10.
- "Manage Food Stockpile" requested labor; why not tiles to be marked as storage?
- The most basic actions like hauling and cleaning should be automated by the game, and only request labor for urgent cases.
- LABOR requests should specify what kind of work type or skill is required.
- Forage advice should say exactly where the nearest edible plants are and suggest marking them for harvest.

### Dashboard v2

Full redesign of the dashboard. First brainstorm; do not make changes yet.

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
