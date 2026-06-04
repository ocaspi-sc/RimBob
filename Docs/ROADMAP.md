# RimBob — Roadmap

> **Living document.** Update when milestones are hit or scope changes.

---

## Milestone overview

| Milestone | Description | Status |
|---|---|---|
| M0 | Repo lit — scaffold compiles, RIMAPI handshake, dashboard skeleton serves a hello-world page | Done |
| M1 | Mayor's Agenda spine — colony-wide briefing → Mayor LLM → versioned `MayorAgenda` rendered in the dashboard | Done |
| M1.5 | Live operability — startup briefing, periodic ingestion, sidebar telemetry, manual re-evaluation, run-state + prompt-introspection endpoints | Done |
| M2 | Grounded reasoning (RAG) — Mayor cites guide passages; measurable agenda-quality improvement before adding feeders | Done |
| M3 | First feeder advisor (Chef food chain) — sub-briefing into the Mayor; first cross-minister flag | Implemented |
| M4 | First cabinet wave — Willie, Defense, Welfare feeding the Mayor; flag-priority-gated tactical alerts surface independently of the daily digest | Not started |
| M4.5 | Assisted Apply — player-confirmed execution for the safest allowlisted advice actions | Implemented |
| M5 | Feedback loop — Accept / Dismiss / Pushback wired; each minister owns and persists its own pushback list | Not started |
| M6 | Oracle refinement loop closes — pushbacks drive the first promoted rule per minister | Not started |
| M7 (post-MVP) | First Auto graduation - one minister/action-kind pair gains an `Auto` mode behind the dial. Scope designed at re-engagement; see DESIGN.md decision "Auto execution delegates to the game's native automation." | Not started |

> **Out of MVP scope:** the original "Year 1 survived, no human input" milestone is deprecated under the assisted-gameplay pivot. Equivalent autonomous play is now a long-tail goal reached by graduating multiple ministers to `Auto` over many cycles, not a single milestone.

---

## M0 — Repo lit

**Done when:** `dotnet run --project Host` completes a typed GET to RIMAPI, prints one pawn's name to the console, and serves the dashboard's hello-world page at `http://localhost:<port>`.

**Scope:**
- Solution + project skeletons matching [`design/architecture.md`](design/architecture.md) structure (Core, Ingestion, State, LLM, Ministers, Knowledge, Host, **Dashboard**).
- `RimApiClient` with one working endpoint.
- RIMAPI license posture documented.
- Google GenAI SDK wired (Gemini Developer API), ordered `GEMINI_API_KEYS` / `RimBob.GeminiApiKeys` config, and startup `LlmClient.PingAsync()` smoke check.
- React+TS dashboard scaffold inside `Dashboard/`, served as static assets by Host (or via Vite dev proxy in development).
- Host exposes `/api/health` and an empty `/api/advice/stream` SSE endpoint.
- CI skeleton (build + test + dashboard build).

---

## M1 — Mayor's Agenda spine

**Done when:** at the close of an in-game day, the Mayor receives a colony-wide briefing, calls the LLM, and the resulting `MayorAgenda` (versioned living plan) is broadcast as an `agenda_update` SSE event and rendered on the dashboard's Agenda tab — posture badges, state-of-the-union paragraph, update_notes line, ranked short-term cards, long-term list. Feedback buttons render disabled (M2 wires them).

**Demo:** start a fresh colony, fast-forward one in-game day; the Agenda tab updates with `posture`, a state-of-the-union paragraph, an `update_notes` line, and 2–5 short-term bullets each tagged `NEW`. Second day → second `MayorAgenda` version with carried-forward IDs and delta badges. No update mid-day.

**Scope:**
- `ColonistRegistry` + `StockpileLedger` + related aggregates feeding `MayorBriefing` (food, mood, threat, wealth, weather, research).
- `DayTickOrchestrator` wakes the Mayor on in-game day rollover.
- `MayorBriefing` aggregating across domains.
- `MayorAgenda` / `MayorAgendaInput` / `AgendaPriority` / `MayorPosture` / `AutonomyMode` / `FeedbackEvent` types in `Core/Advice/`.
- `AdviceBus` in-process emitter; `AgendaStore` with 30-day history ring.
- Mayor system prompt rewritten for Agenda output (state_of_the_union, update_notes, short_term ≤5, long_term, posture).
- `LlmClient.CallMayorAsync` with `responseMimeType=application/json`.
- Host SSE pushes `agenda_update` events; `/api/agenda/{latest,history}` and `/api/autonomy` REST endpoints.
- Dashboard renders the Agenda tab (default tab; placeholder Alerts/Briefing/Log/Autonomy).

---

## M1.5 — Live operability

**Done when:** Host start-up triggers a Mayor briefing within seconds of RIMAPI handshake (no day-rollover wait); `ColonyState` stays current via periodic ingestion; the dashboard renders the agenda alongside a sidebar of dry colony numbers; the player can manually run a fresh cabinet evaluation from the dashboard; Mayor run state and the next prompt are introspectable over HTTP.

**Demo:** boot Host with RimWorld already running; the dashboard shows posture, sidebar telemetry, and a v1 agenda within ~15 seconds. Click **Run Cabinet Now** in the topbar — status pill flips to a pending run state, a new cabinet evaluation lands seconds later with `UPDATED`/`NEW` agenda deltas or feeder advice. `curl /api/status` shows `mayor_running` and the last completion timestamp.

**Scope:**
- `DayTickOrchestrator` calls `IngestionDispatcher.RefreshAllAsync` on every poll (so `ColonyState` actually updates), and fires Mayor on the *first* successful poll instead of skipping it.
- `IngestionDispatcher` promoted to Singleton so the BackgroundService can take it directly.
- `state_of_the_union` schema change: free-text paragraph → `Record<string, string>` keyed by category (`food`, `defense`, `welfare`, `construction`, `treasury`, `research`).
- `MayorAgenda.GeneratedAt` (UTC) — server-stamped by `AgendaStore.UpdateAsync` so the dashboard can show "Updated Xs ago" without depending on the in-game clock.
- `MayorStatus` singleton tracks `IsRunning` / `StartedAt` / `CompletedAt` / `LastError`; `Mayor.RunPlayCycle` brackets each call with `Begin/End`.
- `POST /api/cabinet/trigger` — demand-trigger ingestion + cabinet cycle.
- `GET /api/colony/snapshot` — full `MayorBriefing` for the dashboard sidebar.
- `GET /api/status` — server / RIMAPI / LLM health + Mayor run state.
- `GET /api/mayor/prompt` — system + user message that would be sent to Gemini next turn (introspection).
- Dashboard restyled to a dark command-center console: topbar (status pill + manual cabinet run + poll cadence), tab rail (with `MODULE LOCKED · Coming in M{n}` placeholders), main-console panel (Agenda tab), sidebar panel (Colony telemetry).

**Lifecycle update:** Mayor Agenda and feeder active-advice snapshots now reload
from the unified latest-only minister output store. `/api/agenda/*` has been
retired in favor of `/api/ministers/{minister}/snapshot`, and Host rebuilds no
longer fire a cabinet cycle just to repopulate the dashboard.

---

## M2 — Grounded reasoning (RAG)

**Done when:** in-process RAG store loads ≥2 guides; retrievals appear in prompt traces; the Mayor cites a specific guide passage in at least one agenda field (state-of-the-union or item rationale); side-by-side comparison shows a measurably better-justified agenda with RAG vs without.

**Demo:** boot Host, fast-forward one in-game day with RAG enabled vs disabled; the RAG agenda cites a guide passage (e.g. "fall checklist: ensure 60-day food buffer") in `state_of_the_union.food` or in a short-term card's rationale.

**Why this is M2 instead of feedback:** before we wire feedback UI on top of the Mayor's output, the Mayor needs to be worth reacting to. RAG is the cheapest quality lever and is independent of the feedback infra. It also gives the eventual feedback corpus more signal per memo.

**Scope:**
- `KnowledgeBase` in-process cosine store.
- Guide ingestion from `Docs/guides/**/*.md` with SHA-256 embedding cache under the stable machine-local RimBob data root.
- Gemini embedding via `gemini-embedding-001`.
- RAG retrieval for long-tail lookups, surfaced in agenda `guide_citations[]` and per-item `cite_ids[]`.
- Side-by-side fixture: same briefing, with/without RAG, agenda diff captured.

**Follow-up:** Tier 1 evergreen prompt distillation remains a prompt-quality task after M2; the shipped slice grounds the Mayor through Tier 2 retrieval.

---

## M3 — First feeder advisor (Chef)

**Done when:** the Mayor's daily agenda visibly incorporates Chef's food-chain sub-briefing; Chef can emit a flag (e.g. "food crisis imminent") that the Mayor reflects in body or priority.

**Implementation:** M3 ships `FoodBriefing`, rules-first `Chef`, Chef Gemini escalation, Chef RAG retrieval, active `FlagChannel`, `CabinetCycle` (Chef before Mayor), SSE `advice` replay, Alerts rendering for Chef `AdviceItem`s, and Mayor/Chef briefing inspection.

**Demo:** induce a food shortage; next daily agenda leads with food security and cites Chef's flag in its rationale.

**Scope:**
- `IMinisterRules<FoodBriefing>` interface + Food rules layer.
- Food briefing derivations (`DaysOfFoodRemaining`, etc).
- Flag channel (in-process, priority-tiered) — Mayor consumes; no other consumer.
- Mayor prompt includes flag digest section.
- 5 Chef food-chain scenario fixtures (memo-shape expectations, not goals).

---

## M4 — First cabinet wave

**Done when:** Willie, Defense, Welfare each feed sub-briefings + flags into the Mayor; flag-priority-gated **tactical alerts** can surface as their own dashboard items (separate from the daily digest) when a Critical or High flag fires.

**Demo:** raid scenario — Defense emits Critical flag → tactical alert appears in dashboard immediately; next daily Mayor agenda summarises the incident and proposes follow-up.

**Scope:**
- Willie, Defense, Welfare ministers + their rules layers, in that order. Willie lands before Defense because Chef's first live dependencies are build/storage/power issues, not hunt-risk arbitration.
- Their briefings.
- Mayor-side CoS helper for cross-minister flag arbitration into the Mayor's digest; split into a separate runtime role later only if flag volume justifies it.
- Tactical-alert advice (`priority >= high`) bypasses the daily-tick cadence.

---

## M4.5 — Assisted Apply

**Done when:** an advice step that the backend has proven safe and current can
show **Apply** in the dashboard; clicking it executes exactly one allowlisted
non-pawn RIMAPI operation, reads back/logs the result, and leaves the advice in
`Suggest` mode.

**Implemented allowlist:** `unforbid` known item stacks, `mark_harvest`
validated safe plant clusters, `mark_hunt` deterministic low-risk animal
batches, and one idempotent simple-meal cook-bill upsert behind a
single-workbench guard. Work priorities, broad bill editing, zones, pawn
assignment, equipment, medical/prisoner actions, and combat controls stay out
of the first slice.

**Scope:** executable step handle, Host-owned allowlist and validator, RIMAPI
write wrapper for the first operation, dashboard Apply/result state, and SYSTEM
trace/read-back visibility. Ministers and LLMs never emit raw endpoints or
payloads.

---

## M5 — Feedback loop

**Done when:** each memo / agenda item in the dashboard has working **Accept / Dismiss / Pushback** controls; each minister maintains its own persisted **pushback list** containing the player's natural-language explanations of why that minister was wrong; pushbacks for a given minister flow into that minister's next prompt as "recent player corrections."

**Demo:** dismiss yesterday's `food_security` advice with a Pushback note ("we already built the freezer"); next day, the Mayor's prompt to Chef includes that note in the corrections section, and the next agenda doesn't repeat the same suggestion.

**Why this is M5 (not M2):** feedback is only valuable once there are multiple ministers producing enough advice to find patterns in (M3, M4 first), and it's only consumed by M6. Landing it just before M6 keeps it fresh and avoids building UI on top of an output we hadn't yet lived with.

**Scope:**
- **Pushback** is the renamed Modify action. Semantics: the player explains in natural language why the minister is wrong, rather than editing advice step text.
- `FeedbackEvent` schema (memo id, action, player note, timestamp, in-game tick).
- Each minister owns and persists its own pushback list. Exact storage paths and payload shapes live in source and tests. Pushbacks are scoped — the Mayor doesn't see Chef's pushbacks and vice versa.
- Dashboard buttons + Pushback modal (free-text textarea, prompt: *"Tell the minister why he's wrong."*).
- Per-minister pushback view in the dashboard (replaces the old "decision log" tab idea — there's no global log, only per-minister lists).
- Pushbacks injected into the issuing minister's next prompt as a "recent player corrections" section, capped to the last N entries by age and advice priority.
- **Implicit-feedback / state-diff inference is dropped from the MVP.** The explicit Pushback channel is load-bearing; implicit signals were always a fallback and the cost wasn't justified.

---

## M6 — Oracle refinement loop closes

**Done when:** for one minister, the Oracle refinement pass reads that minister's pushback list, identifies a cluster of consistent corrections (e.g. 6 pushbacks all saying "no hunting in winter"), generates a candidate `Rules.cs` change, compares before/after outputs on a historic corpus of prior minister inputs, runs it against fixtures, and surfaces the diff for human approval. One rule is promoted end-to-end.

**Demo:** run the `minister-refine` skill against Chef's pushback list; see a proposed rule + fixture pass-rate diff; approve; observe rule appear in `Rules.cs`.

**Scope:**
- Oracle refinement tooling for at least one minister.
- Historic replay corpus for before/after output comparison on real prior turns.
- Pushback-clustering pass (LLM-assisted theme extraction over that minister's pushback list).
- Fixture suite at ≥10 scenarios for that minister.
- Approval-gated promotion path.

---

## M7 — First Auto graduation (post-MVP)

**Done when:** the player can flip one narrow minister/action-kind pair from `Suggest` to `Auto`. When in `Auto`, the relevant validated action is automatically applied without a per-step player click. Player can revert to `Suggest` at any time. Assisted Apply does not satisfy this milestone because it is manual, single-step, and allowlisted.

**Execution model:** RimBob writes one validated declarative automation knob
and RimWorld's job-giver allocates pawns. See [`DESIGN.md`](DESIGN.md) decision
"Auto execution delegates to the game's native automation,"
[`design/planning.md`](design/planning.md), and
[`design/ministers/labor.md`](design/ministers/labor.md). Shim/Labor/allowlist
scope is designed at re-engagement, not now.

**Demo:** zone change suggested by Chef is created in-game without the player clicking Accept.

---

## Post-MVP / later cabinet expansion

- Additional Auto graduations per minister/action-kind pair.
- Industry minister.
- Medical minister.
- Research minister.
- Economy minister (trade, caravans, wealth pressure).
- Base Layout Minister (spatial placement).
- Animal management minister if Chef/Economy/Defense sharing becomes noisy.
- Multi-map support.
- Cross-session memory / colony history.
- Auto-approve gate for rule promotion (once fixture suites are strong).

---

## Claude skills to build

These skills would accelerate minister-specific development sessions:

- `minister-refine` — proposal-first review of a minister's decision logs and pushbacks; surface rule, prompt, briefing, or fixture candidates
- `fixture-gen` — generate scenario fixture JSON from a described situation
- `briefing-check` — validate a briefing against its schema and flag derivation errors
- `rule-promote` — take an LLM escalation pattern and generate a candidate Rules.cs change
