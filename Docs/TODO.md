# RimAI — TODO

> Moved to done when shipped, not when coded. Keep this list short.
> Big items belong in ROADMAP.md. This is day-to-day work.

---

## Now (next slice)

- [ ] **PawnMedicalInfo wiring.** `PawnMedicalInfoDto.IsDowned` and `Hediffs` flow through the DTO but `MayorBriefingDerivation.DeriveMedical` still uses a `Health < 0.30` heuristic. Plumb `is_downed` + life-threatening hediffs into `ColonistRecord` so Mayor can distinguish "anesthetised" from "dying".
- [ ] **`/resources/stored` integration.** Currently `Materials` dictionary is empty (only Medicine/Weapons rollups). When a stockpile zone has items and `/resources/stored` returns non-empty, populate per-def material counts so the Mayor can talk about steel, components, etc.
- [ ] **`total_nutrition == 0` upstream investigation.** RIMAPI returns 0 nutrition even when `food_total > 0` and meals exist on map. Check whether this is a bug we can patch around (e.g. compute from `meals_count * 0.9 + raw_food_count * 0.05`) or a deeper RIMAPI gap.

## Next (M3 — Food feeder)

- [ ] Choose the first Food briefing fields and fixture scenarios.
- [ ] Define Food's initial flag contract into the Mayor.
- [ ] Decide whether guide citations get a small dashboard footnote treatment in M3 or wait for the broader feedback UI.
- [ ] `Briefing` tab renders the latest `MayorBriefing` JSON pretty-printed (data already at `/api/colony/snapshot`; just needs UI).

## Later (M5 — Feedback loop)

- [ ] `POST /api/agenda/{version}/item/{id}/feedback` writes a `FeedbackEvent` to the decision log.
- [ ] Wire dashboard Accept / Dismiss / Pushback buttons (Pushback modal opens an editable text field, posts `pushback_text`).
- [ ] Implicit-feedback collector stub: snapshot relevant briefing fields at memo issuance, diff at memo expiry.
- [ ] `Decision Log` tab renders the last N `FeedbackEvent`s with the originating Agenda bullet.
- [ ] Per-minister persistent pushback list (each minister owns + carries forward player corrections).

## Design TODOs (deferred)

- [ ] Extend the action ownership sketch into a full RIMAPI action/endpoint catalogue, preserving owner/requester/executor labels.
- [ ] Add scope docs for Industry, Medical, Research, and Economy once their first slices are scheduled.
- [ ] Food minister M3 prep: replace remaining code-facing Agriculture names with Food when implementation starts (`FoodBriefing`, `MinisterOfFood`, fixtures under `Src/Tests/Food/Fixtures/`).
- [ ] Per-minister `advice_type` enums — define in each minister's session.
- [ ] Per-minister scope docs (`RimAI.Ministers/<name>/scope.md`) — write after first slice ships.
- [ ] Construction: placement / layout strategy (Base Layout Minister candidate).
- [ ] Specify `AgentFlag` field types precisely.
- [ ] Define CoS arbitration rules in detail (M3+).
- [ ] Implicit-feedback: per-`kind` field-mapping table (M3+; stub OK in M2).
- [ ] Candidate minister promotion criteria (CMO, Research, Trade, Treasury).
- [ ] Memo cadence calibration: severity-gated tactical alerts vs. strict daily.
- [ ] Dashboard: notification UX (in-page only vs. browser notifications).
- [ ] `ColonyContext` still carries the pre-Agenda WealthPolicy/ExpansionPolicy/etc. fields — refactor to expose `MayorPosture` directly when the first feeder minister lands (M3).

## Auto epic (M7 — defer until then)

- [ ] HTN engine (`Planner/`) per [`design/planning.md`](design/planning.md).
- [ ] Bulletin board (`Coordination/BulletinBoard.cs`) per [`design/communication.md`](design/communication.md).
- [ ] Labor / assignment solver per [`design/ministers/labor.md`](design/ministers/labor.md).
- [ ] RIMAPI write-endpoint coverage map.
- [ ] Per-(minister, advice_type) autonomy dial wired with real Auto execution.
- [ ] Autonomy dial UI with confirmation step.

## Claude skills to build (see ROADMAP)

- [ ] `minister-review` skill (now also reads `FeedbackEvent`s).
- [ ] `fixture-gen` skill (can synthesize from Modify events).
- [ ] `briefing-check` skill.
- [ ] `rule-promote` skill.

## Known open questions

See [`DESIGN.md`](DESIGN.md) decision log and Open Questions sections in sub-docs (especially [`design/advice.md`](design/advice.md) and [`design/dashboard.md`](design/dashboard.md)).

---

## Done

- [x] Initial DESIGN.md written.
- [x] Strategic plan Y1-Y2 guide written (`Docs/guides/strategic-plan-y1-y2.md`).
- [x] Mayor system prompt drafted; rewritten for Agenda schema in M1 W4.
- [x] Split `RimAI.Agents` into `RimAI.LLM` + `RimAI.Ministers`.
- [x] HTN design discussed and documented (now deferred — Auto epic).
- [x] Minister shape (rules + escalation + optimizer) decided.
- [x] No-modules decision made.
- [x] No-direct-minister-comms rule established.
- [x] **Pivot to assisted-gameplay advisor** — DESIGN, ROADMAP, architecture, ministers, communication, mayor docs rewritten; planning + labor marked deferred; new `design/dashboard.md` and `design/advice.md` created; CLAUDE.md routing table updated.
- [x] **M0 — Repo lit.** Solution + projects, RIMAPI handshake, Google.GenAI ping, Dashboard scaffold, Host with `/api/health` + SSE skeleton, localhost-only bind, CI building both .NET and Dashboard.
- [x] **M1 W1 — Core types.** `MayorAgenda`, `AgendaPriority`, `AgendaPriorityStatus`, `MayorPosture`, `AutonomyMode`, `FeedbackAction`, `FeedbackEvent`, `MayorAgendaInput` under `RimAI.Core.Advice`. Snake-case wire format.
- [x] **M1 W2 — Mayor minister.** `RimAI.Ministers` project with `MayorAgendaRules` (4 agenda directives) and `Mayor` skeleton implementing `IMinister`.
- [x] **M1 W3 — Coordination.** `RimAI.Coordination` project: `AdviceBus`, `AgendaStore` (30-day ring), `DayTickOrchestrator` (`BackgroundService` polling `Tick / 60000`), `FlagChannel` stub.
- [x] **M1 W4 — LLM call.** `mayor.system.md` rewritten; `PromptBuilder`; `LlmClient.CallMayorAsync` with `responseMimeType=application/json`; Mayor wired end-to-end (briefing → rules → LLM → store → bus) with retry-on-cap-violation.
- [x] **M1 W5 — Host wiring.** DI registrations; SSE handler at `/api/advice/stream` (replay-on-connect, drop-oldest channel, 15s ping); `/api/agenda/{latest,history}` REST; `/api/autonomy` placeholder.
- [x] **M1 W6 — Dashboard.** Agenda tab renders the live SSE feed: posture badges, state-of-the-union, what-changed, ranked short-term cards with delta badges (`NEW`/`UPDATED`/`DONE`/`DEFERRED`), long-term list. Feedback buttons disabled.
- [x] **M1 W7 — Tests.** 22 tests across MayorAgendaRules, Mayor play cycle, AgendaStore, AdviceBus, DayTickOrchestrator. 62/62 total passing.
- [x] **M1 W8 — Doc reconciliation.** ROADMAP M1 + this file rewritten to reflect Agenda pivot; agenda.md / dashboard.md SSE envelope simplified to `data: {full MayorAgenda}`.
- [x] **M1.5 — Live operability.** Wake Mayor on Host startup, periodic `IngestionDispatcher` calls in `DayTickOrchestrator`, `state_of_the_union` per-category dict, `MayorAgenda.GeneratedAt`, `MayorStatus`, `/api/colony/snapshot`, `/api/status`, `/api/mayor/prompt`, `POST /api/agenda/refresh`, dark command-center dashboard with sidebar telemetry + Refresh button.
- [x] **RIMAPI gap closure.** `ColonistDetailedDto` rewritten for actual nested v2 shape (`pawn` + `detailes.work_info` + `detailes.medical_info`) — names, ages, mood, skills, traits, current_job now populate. New `/resources/summary` and `/research/progress` endpoints in `RimApiClient` feed `ResourceSummary` + `ResearchInfo` aggregates. Mayor system prompt teaches the model what `food.estimated_days_of_food == null` means (request stockpile audit, do not assume starvation). `Docs/design/RimAPI.md` annotated with verified shapes for the three controllers.
- [x] **M2 — Grounded reasoning / RAG.** `RimAI.Knowledge` now has an in-process cosine store, markdown guide ingestion from `Docs/guides`, Gemini embedding + disk cache under `var/embeddings`, Mayor retrieval via `guide_context[]`, server-stamped `MayorAgenda.guide_citations[]`, `AgendaPriority.cite_ids`, and RAG-vs-no-RAG fixture snapshots under `Src/Tests/Mayor/Fixtures/rag-vs-norag/`. Tier 1 evergreen prompt distillation and polished dashboard footnote rendering are follow-ups.
