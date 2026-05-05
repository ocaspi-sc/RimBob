# RimAI — TODO

> Moved to done when shipped, not when coded. Keep this list short.
> Big items belong in ROADMAP.md. This is day-to-day work.

---

## Now (M0 blockers — finish repo lit under the new advisor architecture)

- [x] Create `.sln` and all `.csproj` files matching [`design/architecture.md`](design/architecture.md)
- [x] Wire `Google.GenAI` (official SDK) + API key env var (`GEMINI_API_KEY`) + startup ping smoke check
- [x] Write `RimApiClient` with typed GET for `/pawns` + handshake method
- [x] `.github/workflows/ci.yml` — build + test on push
- [x] Confirm RIMAPI runs locally against RimWorld (`dotnet run --project ApiHost`)
- [x] Add `Dashboard/` project (Vite + React + TS scaffold) per [`design/dashboard.md`](design/dashboard.md)
- [x] Add ASP.NET Core to Host; expose `GET /api/health` and stub `GET /api/advice/stream` SSE
- [x] Bind Host to `127.0.0.1` only (regression-test against `0.0.0.0`)
- [x] CI: extend pipeline to `npm ci && npm run build` for Dashboard
- [x] Serilog reads from `appsettings.json` (console + rolling file)
- [x] Typed config via `RimAiOptions` (`ListenUrl`, `RimApiBaseUrl`, `PingLlmOnStartup`); `GEMINI_API_KEY` stays env-only
- [x] CI smoke test: boot Host, curl `/api/health`, assert 200
- [x] Vite `outDir` points at `ApiHost/wwwroot`; dev proxy verified against `/api/*`

## Next (M1 — Mayor digest spine)

- [ ] `AdviceItem`, `SuggestedAction`, `FeedbackEvent`, `AutonomyMode` types in `Core/Advice/`
- [ ] `AdviceBus` in `Coordination/`; bridge to Host SSE
- [ ] Daily-tick poller wakes Mayor
- [ ] `MayorBriefing` aggregating across food / mood / threat / wealth (minimum viable for one useful memo)
- [ ] Mayor system prompt rewritten for memo output (was posture); see `RimAI.LLM/prompts/mayor.system.md`
- [ ] Dashboard `MemoFeed` + `MemoCard` components rendering live SSE feed
- [ ] First Mayor fixture (1 scenario) verifying memo shape

## Design TODOs (deferred)

- [ ] Per-minister `advice_type` enums — define in each minister's session
- [ ] Per-minister scope docs (`RimAI.Ministers/<name>/scope.md`) — write after first slice ships
- [ ] Construction: placement / layout strategy (Base Layout Minister candidate)
- [ ] Specify `AgentFlag` field types precisely
- [ ] Define CoS arbitration rules in detail (M3+)
- [ ] Implicit-feedback: per-`kind` field-mapping table (M3+; stub OK in M2)
- [ ] Candidate minister promotion criteria (CMO, Research, Trade, Treasury)
- [ ] Memo cadence calibration: severity-gated tactical alerts vs. strict daily
- [ ] Dashboard: notification UX (in-page only vs. browser notifications)

## Auto epic (M7 — defer until then)

- [ ] HTN engine (`Planner/`) per [`design/planning.md`](design/planning.md)
- [ ] Bulletin board (`Coordination/BulletinBoard.cs`) per [`design/communication.md`](design/communication.md)
- [ ] Labor / assignment solver per [`design/ministers/labor.md`](design/ministers/labor.md)
- [ ] RIMAPI write-endpoint coverage map
- [ ] Per-(minister, advice_type) autonomy dial wired with real Auto execution
- [ ] Autonomy dial UI with confirmation step

## Claude skills to build (see ROADMAP)

- [ ] `minister-review` skill (now also reads `FeedbackEvent`s)
- [ ] `fixture-gen` skill (can synthesize from Modify events)
- [ ] `briefing-check` skill
- [ ] `rule-promote` skill

## Known open questions

See [`DESIGN.md`](DESIGN.md) decision log and Open Questions sections in sub-docs (especially [`design/advice.md`](design/advice.md) and [`design/dashboard.md`](design/dashboard.md)).

---

## Done

- [x] Initial DESIGN.md written
- [x] Strategic plan Y1-Y2 guide written (`Docs/guides/strategic-plan-y1-y2.md`)
- [x] Mayor system prompt drafted (`RimAI.LLM/prompts/mayor.system.md`)
- [x] Split `RimAI.Agents` into `RimAI.LLM` + `RimAI.Ministers`
- [x] HTN design discussed and documented (now deferred — Auto epic)
- [x] Minister shape (rules + escalation + optimizer) decided
- [x] No-modules decision made
- [x] No-direct-minister-comms rule established
- [x] **Pivot to assisted-gameplay advisor** — DESIGN, ROADMAP, architecture, ministers, communication, mayor docs rewritten; planning + labor marked deferred; new `design/dashboard.md` and `design/advice.md` created; CLAUDE.md routing table updated.
