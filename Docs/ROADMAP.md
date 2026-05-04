# RimAI — Roadmap

> **Living document.** Update when milestones are hit or scope changes.

---

## Milestone overview

| Milestone | Description | Status |
|---|---|---|
| M0 | Repo lit — scaffold compiles, RIMAPI handshake, dashboard skeleton serves a hello-world page | Done |
| M1 | Mayor digest spine — colony-wide briefing → Mayor LLM → one daily memo rendered in the dashboard | Not started |
| M2 | Feedback loop — Accept / Dismiss / Modify wired; decisions logged; implicit state-diff stub | Not started |
| M3 | First feeder advisor (Agriculture) — sub-briefing into the Mayor; first cross-minister flag | Not started |
| M4 | Grounded reasoning — RAG retrieval cited in memos; measurable advice improvement | Not started |
| M5 | Cabinet of advisors — Defense, Construction, Welfare feeding the Mayor; severity-gated tactical alerts surface independently of the daily digest | Not started |
| M6 | Refinement loop closes — accept/dismiss data drives the first promoted rule per minister | Not started |
| M7 (post-MVP) | First Auto graduation — one minister's narrowest advice type (e.g. stockpile-zone suggestions) gains an `Auto` mode behind the dial. Re-engages deferred HTN / Labor pieces | Not started |

> **Out of MVP scope:** the original "Year 1 survived, no human input" milestone is deprecated under the assisted-gameplay pivot. Equivalent autonomous play is now a long-tail goal reached by graduating multiple ministers to `Auto` over many cycles, not a single milestone.

---

## M0 — Repo lit

**Done when:** `dotnet run --project Host` completes a typed GET to RIMAPI, prints one pawn's name to the console, and serves the dashboard's hello-world page at `http://localhost:<port>`.

**Scope:**
- Solution + project skeletons matching [`design/architecture.md`](design/architecture.md) structure (Core, Ingestion, State, LLM, Ministers, Knowledge, Host, **Dashboard**).
- `RimApiClient` with one working endpoint.
- RIMAPI license posture documented.
- Google GenAI SDK wired (Gemini Developer API), `GEMINI_API_KEY` env var, and startup `LlmClient.PingAsync()` smoke check.
- React+TS dashboard scaffold inside `Dashboard/`, served as static assets by Host (or via Vite dev proxy in development).
- Host exposes `/api/health` and an empty `/api/advice/stream` SSE endpoint.
- CI skeleton (build + test + dashboard build).

---

## M1 — Mayor digest spine

**Done when:** at the close of an in-game day, the Mayor receives a colony-wide briefing, calls the LLM, and the resulting `AdviceItem` (one daily memo) appears in the dashboard's memo feed with body, rationale, and `suggested_actions` rendered.

**Demo:** start a fresh colony, fast-forward one in-game day; a memo appears titled "End of Day 1 — Brief" with strategic observations and 2–4 suggested next steps. No second memo until the next in-game day.

**Scope:**
- `ColonistRegistry` + `StockpileLedger` aggregates (minimum viable for a useful memo).
- Daily-tick poller wakes the Mayor.
- `MayorBriefing` aggregating across domains (food, mood, threat, wealth, schedule).
- `AdviceItem` type + `AdviceBus` in-process emitter.
- Mayor system prompt updated for memo output (not posture).
- Host SSE pushes new `AdviceItem`s to subscribers.
- Dashboard renders the memo feed (single column, newest first).

---

## M2 — Feedback loop

**Done when:** each memo in the dashboard has working Accept / Dismiss / Modify controls; the action writes a `FeedbackEvent` to the decision log; an implicit-feedback stub records pre/post game-state snapshots tied to the memo's `suggested_actions`.

**Demo:** dismiss yesterday's memo with a note "we already did this"; the decision log shows the dismissal; query the log by minister and see the Mayor's accept/dismiss/modify ratio.

**Scope:**
- `FeedbackEvent` schema (memo id, action, optional player note, timestamp, in-game tick).
- Dashboard buttons + modify modal (free-text edits to `suggested_actions`).
- Decision log writer (Serilog → structured file).
- Implicit-feedback collector: snapshot relevant briefing fields at memo issuance, diff at memo expiry, score overlap with `suggested_actions`. Stub-quality is fine here — see [`design/advice.md`](design/advice.md) open questions.
- Dashboard "decision log" tab shows the last N feedback events with the originating memo.

---

## M3 — First feeder advisor (Agriculture)

**Done when:** the Mayor's daily digest visibly incorporates Agriculture's sub-briefing; Agriculture can emit a flag (e.g. "food crisis imminent") that the Mayor's memo reflects in body or severity.

**Demo:** induce a food shortage; next daily memo leads with food security and cites Agriculture's flag in its rationale.

**Scope:**
- `IMinisterRules<AgricultureBriefing>` interface + Agriculture rules layer.
- Agriculture briefing derivations (`DaysOfFoodRemaining`, etc).
- Flag channel (in-process, severity-tiered) — Mayor consumes; no other consumer.
- Mayor prompt includes flag digest section.
- 5 Agriculture scenario fixtures (memo-shape expectations, not goals).

---

## M4 — Grounded reasoning

**Done when:** in-process RAG store loads ≥2 guides; retrievals appear in prompt traces; one fixture shows a measurably better-justified memo with RAG vs without (cited guide passage in the rationale).

**Demo:** side-by-side memo rationales with and without retrieval; RAG version cites a specific guide passage.

**Scope:**
- `KnowledgeBase` in-process cosine store.
- Guide ingestion (`strategic-plan-y1-y2.md` + one wiki guide).
- Evergreen content distilled into Mayor / Agriculture system prompts (cached).
- RAG retrieval for long-tail lookups, surfaced in memo `citations[]`.

---

## M5 — Cabinet of advisors

**Done when:** Defense, Construction, Welfare each feed sub-briefings + flags into the Mayor; severity-gated **tactical alerts** can surface as their own dashboard items (separate from the daily digest) when a Critical or High flag fires.

**Demo:** raid scenario — Defense emits Critical flag → tactical alert appears in dashboard immediately; next daily Mayor memo summarises the incident and proposes follow-up.

**Scope:**
- Defense, Construction, Welfare ministers + their rules layers.
- Their briefings.
- CoS stub for cross-minister flag arbitration into the Mayor's digest.
- Tactical-alert advice type (severity ≥ High) bypasses the daily-tick cadence.

---

## M6 — Refinement loop closes

**Done when:** for one minister, the refinement loop reads the decision log (now with feedback events), identifies a cluster of consistently-Accepted-or-Modified escalations, generates a candidate `Rules.cs` change, runs it against fixtures, and surfaces the diff for human approval. One rule is promoted end-to-end.

**Demo:** run the `minister-review` skill against Agriculture's log; see a proposed rule + fixture pass-rate diff; approve; observe rule appear in `Rules.cs`.

**Scope:**
- Refinement-mode tooling for at least one minister.
- Fixture suite at ≥10 scenarios for that minister.
- Approval-gated promotion path.

---

## M7 — First Auto graduation (post-MVP)

**Done when:** the player can flip one narrow advice type (e.g. Agriculture's stockpile-zone suggestions) from `Suggest` to `Auto`. When in `Auto`, the relevant `AdviceItem` is automatically applied via RIMAPI writes instead of being shown for approval. Player can revert to `Suggest` at any time.

**This re-engages the deferred design** — see [`design/planning.md`](design/planning.md) (HTN) and [`design/ministers/labor.md`](design/ministers/labor.md). Scope decided then, not now.

**Demo:** zone change suggested by Agriculture is created in-game without the player clicking Accept.

---

## Post-MVP candidates

- Additional Auto graduations per advice type.
- Chief Medical Officer minister.
- Research Director minister.
- Minister of Trade.
- Minister of Treasury (in-game wealth management).
- Base Layout Minister (spatial placement).
- Multi-map support.
- Cross-session memory / colony history.
- Auto-approve gate for rule promotion (once fixture suites are strong).

---

## Claude skills to build

These skills would accelerate minister-specific development sessions:

- `minister-review` — review a minister's recent decision log (with feedback events) and surface rule-promotion candidates
- `fixture-gen` — generate scenario fixture JSON from a described situation
- `briefing-check` — validate a briefing against its schema and flag derivation errors
- `rule-promote` — take an LLM escalation pattern and generate a candidate Rules.cs change
