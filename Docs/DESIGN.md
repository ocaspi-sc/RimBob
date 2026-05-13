# RimAI — Design Document

> **Living document.** When a design decision is made, update this file.
> When Claude sees "remember", "always", "from now on", or "going forward" in a design discussion, update the relevant doc immediately.

---

## What is RimAI?

An AI advisor system for RimWorld. The human plays the colony; RimAI watches the live game state and sends suggestions — like a cabinet of advisors writing memos. A self-improving LLM cabinet, led by the Mayor, surfaces strategic memos to a dashboard the player reads alongside the game. Over time, advice gets sharper, more advisor types come online, and individual advisors can graduate from *suggesting* to *acting* — but only when explicitly trusted by the player.

This is not an RL agent and (for the MVP) not an autonomous player. Strategy and judgment come from LLMs.
For the MVP, Output is only advice to player, not RIMAPI writes.

---

## Design Goals

1. **Advise well.** Suggestions should match what a thoughtful human player would recognise as good. The player keeps control; RimAI earns trust one accepted memo at a time.
2. **Grounded in community knowledge.** Advice is informed by actual RimWorld guides, not just LLM training data.
3. **Self-improving from real feedback.** Accept / Dismiss / Pushback on each memo is the primary training signal; implicit state-watching is a fallback. The system gets better the more the player uses it.
4. **Transparent.** Every memo carries its rationale, suggested actions, and the briefing it was based on. A single dashboard view should explain what the cabinet is recommending and why.
5. **Cheap by default.** LLMs are called only when judgment is genuinely needed. Rules handle routine; the LLM handles exceptions.
6. **Debuggable over clever.** Prefer deterministic, inspectable behaviour over emergent complexity.
7. **Suggest by default; autonomy is opt-in.** Per-minister `Off / Suggest / Auto` dial. MVP ships with `Suggest` only; `Auto` graduations come later, one minister at a time, behind explicit player consent.

---

## Architecture Overview

```
┌──────────────────────────────────────────────┐
│  Advisor Layer                               │
│  Dashboard (React + TS, served by Host)      │
│  AdviceBus (SSE: AdviceItems + agenda_update)│
├──────────────────────────────────────────────┤
│  Strategic Layer                             │
│  Mayor (LLM, updates Agenda once/day)        │
│  Agenda (living plan: short+long term goals) │
│  ↓ read-only broadcast                       │
├──────────────────────────────────────────────┤
│  Cabinet Layer                               │
│  Chief of Staff + feeder ministers           │
│  Each minister: Rules → LLM escalations      │
│  Ministers read Agenda for direction         │
│  Flag Channel (cross-minister signaling)     │
├──────────────────────────────────────────────┤
│  Ingestion Layer                             │
│  RIMAPI HTTP / SSE client (reads only in MVP)│
└──────────────────────────────────────────────┘
```

> **Deferred (Auto epic):** HTN planner, bulletin board, Labor solver, RIMAPI write coverage.
> Designed but not built in MVP; re-engaged when the first minister graduates from `Suggest` to `Auto`.

Two operational modes run in parallel:

- **Play mode** — live game loop; ministers wake on briefing changes, evaluate rules, escalate to LLM when needed, emit `AdviceItem`s onto the AdviceBus.
- **Refinement** — async; performed by the smart coding agent creating the system, not by the minister llm. review the decision log (future: enriched with human Pushbacks). Improve the ministers workflow: rules, system prompt, briefing.

→ See [`design/architecture.md`](design/architecture.md) for code structure and stack.

---

## The Cabinet

Every minister has the same shape: a rules layer that handles routine cases, escalation conditions that trigger an LLM call, and an improve loop that tightens the rules over time. See [`design/ministers.md`](design/ministers.md).

### MVP advisor (M1)

| Minister | Domain |
|---|---|
| **Mayor** | Daily strategic memo synthesised from colony-wide briefing. The whole MVP. |

### Feeder advisors (added M3+ as the cabinet grows)

| Minister | Domain |
|---|---|
| **Minister of Food** | Full food chain: harvesting, farming, hunting, cooking, storage, freezer |
| **Defense Minister** | Raids, combat, fortifications |
| **Minister of Construction** | Buildings, power, layout (placement deferred) |
| **Minister of Welfare** | Mood, recreation, schedules, relationships |
| **Minister of Industry** | Non-food production: stonecutting, tailoring, smithing, machining, fabrication, drugs |
| **Medical Minister** | Wounds, disease, surgery, medicine stock, hospital readiness |
| **Research Minister** | Research queue, tech path, unlock dependencies |
| **Economy Minister** | Trade, caravans, wealth pressure, surplus liquidation |
| **Chief of Staff** | Flag triage, conflict arbitration into the Mayor's digest |

### Deferred until Auto graduation

| Minister | Domain | Why deferred |
|---|---|---|
| **Minister of Labor** | Assignment solver; bulletin board clearinghouse | No consumer in suggest-only mode — nothing allocates pawns. Re-engaged when the first feeder minister graduates to `Auto`. |

### Candidate ministers (post-MVP)

Spun out from a host minister when its rules and prompts can't keep up — e.g. medical reasoning leaving Welfare, base layout leaving Construction, trade strategy leaving Welfare. Promoted case-by-case; no pre-allocated roster.

---

### Cabinet boundary rule

Each minister owns either a production chain or a well-defined subsystem. Food owns the full nutrition chain from acquisition to cooked meals and freezer/storage integrity. Defense owns the threat-response subsystem. Construction owns built infrastructure, power, rooms, and material flow. Welfare owns pawn wellbeing, medical sub-blocks, schedules, and social/recreation systems.

Every in-game action should eventually map to one primary owning minister. The first pass maps only clear-cut actions; contested cases are documented as open questions until their ownership is justified by actual advice/rule complexity.

---

## Core Principles

**Ministers output is actionable Advice.** Ministers understand what needs to be done and . 

**Only the Mayor strategically**, but even the Agenda should prioritize concrete next moves. -> [`design/advice.md`](design/advice.md), [`design/ministers.md`](design/ministers.md)

**Suggest by default; autonomy is per-minister and opt-in.** MVP ships with every advisor in `Suggest` mode. Graduating an advisor to `Auto` is a deliberate, per-minister event gated by track record + explicit player consent. → [`design/advice.md`](design/advice.md)

**Ministers do not talk to each other directly.** All coordination is via flags. CoS arbitrates conflicts; the Mayor synthesises a daily digest. → [`design/communication.md`](design/communication.md)

**Briefings are the quality lever.** Tight, focused, ~500 tokens per minister. The state store computes derived facts so the LLM doesn't have to. → [`design/state-store.md`](design/state-store.md)

**Rules first, LLM second.** The rules layer handles the majority of decisions cheaply. The LLM earns its cost on genuine judgment calls. → [`design/ministers.md`](design/ministers.md)

> **Deferred principle (Auto epic):** *Only Labor touches pawn allocation.* Re-engaged when the first minister graduates to `Auto` and needs to issue RIMAPI pawn writes. Until then, no minister touches pawn allocation. → [`design/ministers/labor.md`](design/ministers/labor.md), [`design/planning.md`](design/planning.md)

---

## External Dependencies

**RIMAPI** — https://github.com/IlyaChichkov/RIMAPI. RimWorld mod embedding a REST + SSE server. 167 endpoints. GPL-3.0; we call over HTTP only (no linking).

**Google GenAI SDK (`Google.GenAI`)** — LLM calls via Gemini Developer API (Google AI Studio) in MVP. Same client can later target Vertex AI. Streaming for UI; non-streaming for background minister thinking.

**RIMAPI write coverage** — Not MVP-critical (suggest-only). Reads must cover every briefing input. Write coverage gets re-evaluated when the first minister graduates to `Auto`.

---

## Decision Log

Decisions made and the reasoning behind them. Append; do not delete.

| Decision | Rationale |
|---|---|
| External .NET 9 service, not in-game mod | Unity pins mods to .NET Fx 4.7.2; external service has full modern library access |
| HTN + LLM, not RL | Episodes too long, action space huge, reward function fuzzy |
| No MCP | HTN needs deterministic execution; MCP makes tool calls non-deterministic |
| Per-colonist agents rejected | Colonists are resources; minister layer is sufficient |
| No direct minister-to-minister comms | Observable flag + board model is debuggable; direct messaging becomes spaghetti |
| No modules (all roles are real ministers) | Modules dilute briefing focus; briefing quality is the primary lever |
| Rules-first, LLM-on-escalation | LLMs are expensive and slow; rules handle the common case cheaply |
| v1 human-gated rule promotion | Self-modification without audit accumulates drift; automate once fixture suite is strong |
| Microsoft.SemanticKernel.Memory rejected | Mid-migration API churn; in-process cosine store is sufficient and debuggable |
| Unified IMinisterRules interface | Standard shape enables shared test harness, code-gen templates, consistent rule-refinement behaviour |
| Pivot to assisted-gameplay advisor (suggest-only MVP) | Original "autonomous player" framing required full RIMAPI write coverage, HTN execution spine, and a year-long success metric before any user value. Advisor framing ships value at M1 (a single useful memo) and turns player feedback into the primary training signal — strictly better than "did the colony survive year 1." |
| Mayor-first MVP; per-domain ministers feed the digest | Strategic memos are the natural advisory voice. Building Mayor first lets a single minister demonstrate the whole loop (briefing → LLM → memo → feedback) before fanning out to a cabinet. |
| HTN / bulletin board / Labor solver deferred until first Auto graduation | Their entire purpose is to allocate pawns. Under suggest-only there is no consumer. Design docs preserved verbatim, marked `Deferred — Auto epic`, so the future autonomy work doesn't redesign from scratch. |
| Per-minister `Off / Suggest / Auto` autonomy dial named as a future construct | First-class concept in the design language even though only `Suggest` is implemented. Lets future docs reference the dial without re-introducing it; sets player expectations early. |
| External advisor dashboard (React + TS) served by `RimAI.Host` over HTTP+SSE | Web UI iterates faster than desktop, runs cross-platform alongside the game, and reuses the SSE pattern already in play with RIMAPI. Adds a JS toolchain to the repo — accepted cost. Localhost-only auth posture. |
| Explicit feedback only; ministers own their own pushback lists | Accept / Dismiss / **Pushback** is the entire training signal in MVP. Pushback replaces the earlier "Modify" action — instead of editing suggested-action text, the player tells the minister *why he's wrong* in natural language. Each minister persists its own scoped pushback list under `Src/Cabinet/<Minister>/Pushbacks/`; pushbacks are injected into that minister's next prompt and clustered at refinement time. **Implicit state-diff inference is dropped from MVP** — the explicit channel is load-bearing and the kind-to-field mapping was speculative. → [`design/advice.md`](design/advice.md) |
| LLM provider switched to Gemini Developer API via `Google.GenAI` | First-party .NET SDK with a clean migration path to Vertex AI, plus first-party embedding models for the M4 RAG slice. |
| `RimAI.Agents` split into `RimAI.LLM` + `RimAI.Ministers` | Makes dependency direction explicit (`Ministers` → `LLM`), keeps the LLM wrapper independently testable, and aligns project names with cabinet terminology. |
| Mayor's output is the Agenda, not a daily_digest AdviceItem | A living planning document (short-term priorities + long-term goals, updated once/day) is a more natural advisory voice than a one-shot memo — it has persistent state, delta signals, and a clean Mayor→ministers direction channel. The Agenda *is* the MVP advice; AdviceItems survive as the feeder-minister format (M3+). → [`design/agenda.md`](design/agenda.md) |
| Refinement / dev agents may shell out to Claude Code via prompt files | Two-tier model: in-process Gemini for fast schema-bound play decisions; Claude Code (subprocess, prompt file in `.plans/`) for code-shaped work — drafting `Rules.cs` diffs, generating fixtures, bootstrapping a new minister's initial automations. Stronger model + full repo tool access where it matters; no bespoke agent-SDK integration. → [`design/evaluation.md`](design/evaluation.md) |
| Mayor wakes on Host startup, not only on day rollover | Original M1 deferred firing the Mayor until the first in-game day rolled over so we'd never publish a "fake" agenda from cold state. In practice that left the dashboard blank for 24 in-game minutes per session — the player can't tell whether the system is alive. `DayTickOrchestrator` now fires Mayor on the first successful poll (after a full `IngestionDispatcher.RefreshAllAsync`), so the dashboard is useful within ~15s of Host start. The same orchestrator drives subsequent rollovers. |
| `state_of_the_union` is a per-category dict, not a paragraph | A 100–200 word paragraph mixed strategy with raw numbers and was hard to skim during play. Splitting into one terse sentence per category (`food`, `defense`, `welfare`, `construction`, `treasury`, `research`) lets the dashboard render a checklist that reads in one glance, and pairs cleanly with the new sidebar (which carries the raw numbers). The Mayor's job is the interpretation; the sidebar is the readout. → [`design/agenda.md`](design/agenda.md) |
| Dashboard sidebar reads `MayorBriefing` directly via `/api/colony/snapshot` | Rather than threading the briefing through the agenda payload (which would couple the polled telemetry to the once-per-day agenda update), the sidebar polls the same `BriefingCache` the Mayor reads. One source of truth, two consumers, independent cadences. The agenda body can stay interpretive instead of repeating numbers. → [`design/dashboard.md`](design/dashboard.md) |
| Demand-trigger: `POST /api/agenda/refresh` | Once the Mayor wakes on every day rollover and on Host start, the only remaining gap is the player wanting a fresh agenda *now* — after they've made a major change. A topbar "Refresh" button on the dashboard hits this endpoint, which runs the same ingestion + Mayor cycle. No body, no auth surface beyond localhost. |
| Mayor briefing carries explicit `is_downed` / `is_dead` per colonist | The briefing was carrying a `Health < 0.30f` heuristic-derived `Medical.Downed` count and discarding the authoritative `is_downed` / `is_dead` flags RIMAPI v2 returns. `ColonistRecord` now stores both flags; `PawnLine` exposes `IsDowned`; `DeriveMedical` uses the flags directly; dead pawns are filtered out of the briefing entirely. → [`design/ministers/mayor.md`](design/ministers/mayor.md) |
| Mayor system prompt collapses the strategic-frame doctrine | The prompt was 116 lines, dominated by ~33 lines of RimWorld doctrine (two-phase, wealth-velocity, chain-shotgun, posture-shift). Doctrine now lives in `design/ministers/mayor.md`; the prompt carries a six-line summary plus a doc cross-reference. Net effect: shorter prompt, lower hallucination surface, single source of truth for the strategy. The prompt also now mandates LLM-emitted category emojis on `state_of_the_union` values (the dashboard renders them verbatim). → [`design/ministers/mayor.md`](design/ministers/mayor.md), [`design/agenda.md`](design/agenda.md) |
| Agriculture renamed Food; cabinet domains are production chains or crisp subsystems | "Agriculture" was too narrow for the actual owner of food security. The Food minister owns harvesting, farming, hunting, cooking, food stockpiles, freezer integrity, and food-chain resource requests. More broadly, each minister should own a production chain or clearly bounded subsystem; every game action should eventually have one primary owner. Clear-cut action ownership is mapped first, with hard cases left explicit. See [`design/ministers.md`](design/ministers.md) and [`design/ministers/food.md`](design/ministers/food.md). |
| Hybrid cabinet accepted: production chains plus crisp subsystems | The cabinet shape is Food, Construction, Industry, Economy as production/economic chains; Defense, Welfare, Medical, Research as bounded subsystems; Mayor for strategy; CoS for arbitration; Labor deferred for Auto pawn assignment. Action mapping uses owner/requester/executor language so one minister is accountable while dependencies stay visible. See [`design/ministers.md`](design/ministers.md). |
| Resource requests are first-class on advice | Ministers can request resources explicitly, not only imply them through suggested actions. `AdviceItem` therefore carries `resource_requests[]` for "need Y" alongside `suggested_actions[]` for "do X". Flags still carry `AgentFlag.Requests` for cross-minister urgency; both remain advisory in MVP. See [`design/advice.md`](design/advice.md) and [`design/ministers.md`](design/ministers.md). |
| CoS stays split in the design, but not as an immediate runtime split | Chief of Staff has a distinct job from the Mayor: dedupe overlapping flags, choose lead framing for the same issue, and decide whether something becomes a tactical alert or normal digest input. That separation is worth keeping in the design language and logs, but it does not need a fully separate loop/process while feeder volume is still low. Early M4 can implement CoS as a Mayor-side deterministic helper first and split it operationally later if the traffic justifies it. |
| Construction precedes Defense in the first cabinet rollout after Food | Food's first live dependencies are cooler/power/room/storage recommendations, which Construction owns directly. Hunt-risk and emergency coupling with Defense matter later, but they are not prerequisites for the first Food feeder slice. The roadmap should therefore sequence Construction before Defense even though both remain part of the same early cabinet wave. |
| M3 Food feeder ships as rules-first plus LLM escalation | Food is the first full feeder minister. It derives a focused `FoodBriefing`, emits `AdviceItem`s and `AgentFlag`s, escalates ambiguous crop/hunt/freezer tradeoffs to Gemini, and feeds active Medium+ flags into the Mayor through `CabinetCycle` / `FlagChannel`. Food remains suggest-only: resource requests are rendered, not executed. See [`design/ministers/food.md`](design/ministers/food.md), [`design/communication.md`](design/communication.md), and [`design/dashboard.md`](design/dashboard.md). |
| Minister wake reason is explicit `PlayCycleContext`, not hidden runtime state | A feeder minister's first live cycle is a real execution trigger, not game state. Rather than smuggling bootstrap behavior through a singleton tracker, the runtime should pass a typed `PlayCycleContext` (`StartupBootstrap`, `CabinetRefresh`, later `FlagFired`, `Heartbeat`, `ScheduledWakeupFired`) into `RunPlayCycle`. That keeps lifecycle semantics visible in code, tests, and docs. See [`design/ministers.md`](design/ministers.md). |
| Host file logs resolve to stable `./logs/` paths | Relative Serilog file sinks were landing under `Src/ApiHost/logs/` when the Host started from its project directory, which made log discovery inconsistent. Host now resolves local repo runs to root `./logs/` and falls back to `<content-root>/logs/` outside the repo layout. See [`design/architecture.md`](design/architecture.md). |
| Local launcher gives the Host a taskbar-visible server window | Normal local runs should leave an obvious Windows taskbar affordance so the player can find RimAI while playing. `run-rimai.ps1` therefore starts `RimAI.Host` in a minimized PowerShell window named `RimAI Server`, and that window closes when RimAI exits. `-Foreground` keeps the old attached-console behavior for debugging and verification. See [`design/architecture.md`](design/architecture.md). |
| Advice carries both severity and priority score | `severity` remains semantic routing input for the Mayor, CoS, and tactical alert behavior. A first-class `priority_score` from 1-10 ranks importance within a severity tier for dashboard sorting, debugging, and future arbitration. Rules may compute both dynamically from live state. See [`design/advice.md`](design/advice.md). |
| Suggested action kind names should describe the UI/game operation | `suggested_actions[].kind` values should be explicit enough to stand alone in the dashboard and future Auto mapping. Prefer names like `mark_harvest`, `place_blueprint`, `production_bill`, and `set_stockpile_zone` over terse or generic labels such as `build` or `note` when a structured operation exists. See [`design/advice.md`](design/advice.md). |
| Ministers use canonical RimWorld work types for labor requests | Labor/resource requests must name the relevant RimWorld work-tab type when they request pawn time. Text such as "labor capacity" is too vague for advice, logs, or future Auto wiring. Work types are distinct from skills: e.g. `Cook` is a work type, `Cooking` is the skill signal. See [`design/ministers.md`](design/ministers.md). |
| Briefings prefer compact opportunity summaries over raw dumps | Spatial and operational data should be summarized into actionable aggregates: nearest harvest clusters, proximity buckets, tile counts, and bottleneck signals. Do not bloat minister briefings with raw plant/tile/building lists unless a future rule proves it needs them. See [`design/state-store.md`](design/state-store.md) and [`design/ministers/food.md`](design/ministers/food.md). |
| Active feeder advice dedupes by stable issue id | Until the decision log grows a full supersession/history view, feeder rules should use stable ids for the same unresolved issue so dashboard refreshes replace the active card instead of stacking duplicates. See [`design/advice.md`](design/advice.md). |
| Active feeder advice is replaced by minister snapshots | The active dashboard should show each minister's latest successful view, not a TTL-based accumulation of older cards. A minister publishes a full current `AdviceItem` snapshot; `AdviceBus` replaces prior active cards from that minister atomically. Decision-log persistence remains the place for historical advice. See [`design/advice.md`](design/advice.md), [`design/dashboard.md`](design/dashboard.md), and [`design/ministers/food.md`](design/ministers/food.md). |
| Food emergency advice uses concrete food-chain actions | Food shortage output should not hide behind vague "audit" wording or generic `note` actions when the briefing supports a concrete next step. If no meal/raw/harvest path is visible, Food should still request/setup the minimum chain when possible: stockpile visibility, emergency growing tiles, Grow/PlantCut labor, cooking building, and simple-meal bill/cook labor. See [`design/advice.md`](design/advice.md) and [`design/ministers/food.md`](design/ministers/food.md). |
| Dashboard v2 is a from-scratch cabinet inspection surface | Keep the dashboard package, Vite build, and Host serving model, but replace the React source architecture with a fixed scope rail, SYSTEM overview, minister tabs, structured evidence panels, SSE/system diagnostics, endpoint coverage markers, and compact colony sidebar. The old dashboard is reference only. V2 remains read-only: no RIMAPI write controls, autonomy toggles, or feedback/Pushback controls. See [`design/dashboard.md`](design/dashboard.md) and [`plans/dashboard_v2.md`](plans/dashboard_v2.md). |

---

## Sub-documents

| File | Contents |
|---|---|
| [`design/architecture.md`](design/architecture.md) | Stack, project structure, library choices, interfaces |
| [`design/dashboard.md`](design/dashboard.md) | React+TS advisor dashboard: layout, HTTP+SSE contract, auth posture |
| [`design/advice.md`](design/advice.md) | `AdviceItem` schema, Accept/Dismiss/Pushback lifecycle, minister-owned pushback lists |
| [`design/ministers.md`](design/ministers.md) | Minister shape, rules system, LLM escalation, rule refinement |
| [`design/state-store.md`](design/state-store.md) | Aggregates, briefings, cadences, versioning |
| [`design/communication.md`](design/communication.md) | Flag schema, severity, inter-minister comms rules |
| [`design/rag.md`](design/rag.md) | Knowledge base, ingestion, retrieval strategy |
| [`design/evaluation.md`](design/evaluation.md) | Decision logging, improvement framework, fixture testing |
| [`design/agenda.md`](design/agenda.md) | Mayor's Agenda: living plan schema, cabinet_direction interface, dashboard layout, API contract |
| [`design/ministers/mayor.md`](design/ministers/mayor.md) | Mayor scope, Agenda update schema (MVP centerpiece) |
| [`design/ministers/chief-of-staff.md`](design/ministers/chief-of-staff.md) | CoS scope, arbitration logic |
| [`design/ministers/food.md`](design/ministers/food.md) | Food scope, briefing, rules |
| [`design/ministers/defense.md`](design/ministers/defense.md) | Defense scope, briefing, rules |
| [`design/ministers/construction.md`](design/ministers/construction.md) | Construction scope, briefing, rules |
| [`design/ministers/welfare.md`](design/ministers/welfare.md) | Welfare scope, briefing, rules |
| [`design/planning.md`](design/planning.md) | **Deferred — Auto epic.** HTN, primitive contract, failure model |
| [`design/ministers/labor.md`](design/ministers/labor.md) | **Deferred — Auto epic.** Labor scope, assignment solver, bulletin board |
