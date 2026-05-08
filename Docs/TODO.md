# RimAI — TODO

> Moved to done when shipped, not when coded. Keep this list short.
> Big items belong in ROADMAP.md. This is day-to-day work.

---

## Now (M1 wrap — verify against a live colony)

- [ ] End-to-end smoke: boot Host with `GEMINI_API_KEY` and a fresh RimWorld colony, fast-forward one in-game day, observe `agenda_update` event in browser DevTools, confirm dashboard renders posture + state-of-the-union + bullets.
- [ ] Stockpile inventory endpoint — extend `RimApiClient` so `ResourceSnapshot` and `EstimatedDaysOfFood` get real values (currently empty/null pending endpoint). Without it, the food_crisis lens can't fire.
- [ ] Research endpoint in `RimApiClient` so `ResearchSnapshot` populates.
- [ ] Decide what `EstimatedDaysOfFood == null` should look like in the prompt — explicit "food levels unknown" vs. omit field.
- [x] Vite `npm install && npm run build` confirmed locally with Node 24.15 LTS — 34 modules, 148 KB bundle, no TS errors.

## Next (M2 — feedback loop)

- [ ] `POST /api/agenda/{version}/item/{id}/feedback` writes a `FeedbackEvent` to the decision log.
- [ ] Wire dashboard Accept / Modify / Dismiss buttons (Modify modal opens an editable text field, posts `modified_text`).
- [ ] Implicit-feedback collector stub: snapshot relevant briefing fields at memo issuance, diff at memo expiry.
- [ ] `Decision Log` tab renders the last N `FeedbackEvent`s with the originating Agenda bullet.
- [ ] `Briefing` tab renders the latest `MayorBriefing` JSON pretty-printed.

## Design TODOs (deferred)

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
- [x] **M1 W1 — Core types.** `MayorAgenda`, `AgendaItem`, `AgendaItemStatus`, `MayorPosture`, `AutonomyMode`, `FeedbackAction`, `FeedbackEvent`, `MayorAgendaInput` under `RimAI.Core.Advice`. Snake-case wire format.
- [x] **M1 W2 — Mayor minister.** `RimAI.Ministers` project with `MayorRules` (4 lenses) and `Mayor` skeleton implementing `IMinister`.
- [x] **M1 W3 — Coordination.** `RimAI.Coordination` project: `AdviceBus`, `AgendaStore` (30-day ring), `DayTickOrchestrator` (`BackgroundService` polling `Tick / 60000`), `FlagChannel` stub.
- [x] **M1 W4 — LLM call.** `mayor.system.md` rewritten; `PromptBuilder`; `LlmClient.CallMayorAsync` with `responseMimeType=application/json`; Mayor wired end-to-end (briefing → rules → LLM → store → bus) with retry-on-cap-violation.
- [x] **M1 W5 — Host wiring.** DI registrations; SSE handler at `/api/advice/stream` (replay-on-connect, drop-oldest channel, 15s ping); `/api/agenda/{latest,history}` REST; `/api/autonomy` placeholder.
- [x] **M1 W6 — Dashboard.** Agenda tab renders the live SSE feed: posture badges, state-of-the-union, what-changed, ranked short-term cards with delta badges (`NEW`/`UPDATED`/`DONE`/`DEFERRED`), long-term list. Feedback buttons disabled.
- [x] **M1 W7 — Tests.** 22 tests across MayorRules, Mayor play cycle, AgendaStore, AdviceBus, DayTickOrchestrator. 62/62 total passing.
- [x] **M1 W8 — Doc reconciliation.** ROADMAP M1 + this file rewritten to reflect Agenda pivot; agenda.md / dashboard.md SSE envelope simplified to `data: {full MayorAgenda}`.
