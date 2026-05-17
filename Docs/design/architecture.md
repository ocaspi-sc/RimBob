# RimBob - Code Architecture

> **Living document.** See `AGENTS.md` for update rules.
> This doc records durable architecture boundaries and runtime contracts. Exact
> file trees, class signatures, DTO fields, endpoint payloads, and implementation
> helper names live in source and tests; do not mirror them here unless the
> design depends on the shape.

---

## Stack

```text
Browser dashboard
  -> RimBob Host (.NET service, localhost HTTP + SSE)
  -> Coordination + Ministers + State + LLM + Knowledge
  -> Ingestion client
  -> RIMAPI HTTP on localhost
  -> RimWorld
```

RimBob is an external .NET 9 service, not a RimWorld mod. RimWorld mods are
pinned to older .NET runtime constraints; the external process keeps modern
libraries available and avoids linking against RIMAPI.

MVP remains `Suggest` mode by default. The service reads RIMAPI data, derives
briefings, and publishes advice. A narrow Assisted Apply path may issue an
allowlisted non-pawn RIMAPI write only after a player clicks a concrete advice
action. Autonomous execution waits for a future per-minister `Auto` graduation.

---

## Architecture Rules

- Host binds loopback only and serves both the dashboard and bounded `/api/*`
  surfaces.
- Ministers never call RIMAPI directly. They read briefings from the state
  store and emit structured advice.
- Assisted Apply writes, when present, are owned by Host/infrastructure code that
  validates the advice action, current state, allowlist, RIMAPI result, and
  read-back evidence.
- Shared domain contracts stay dependency-light. Code that is meant to be pure
  domain model must not take external infrastructure dependencies.
- Rules are pure and deterministic. I/O belongs in ingestion, state refresh,
  LLM gateways, persistence, or Host endpoints.
- Design docs define ownership, contracts, and behavior. Source files and tests
  define exact names, signatures, and field lists.

---

## Modules

### Shared Contracts

`Src/Common/` owns shared advice, agenda, briefing, minister, versioning, and
aggregate contracts. Everything else may depend on these shared types.

The design-level contract is stable: ministers receive a play-cycle context,
evaluate a briefing through rules first, optionally escalate to an LLM, and emit
advice plus flags. The exact C# signatures live under `Src/Common/Ministers/`
and `Src/Common/Advice/`.

### Ingestion

`Src/GameStateSync/` talks to RIMAPI and translates raw HTTP responses into data
the state store can consume. It should not contain minister logic.

RIMAPI endpoint coverage is captured in [`RimAPI.md`](RimAPI.md). The live
client and tests are the source of truth for exact DTO shapes.

### State Store

`Src/StateStore/` owns the current colony snapshot, briefing cache, and derived
facts. It answers "what does the colony state mean for a minister?" without
calling the LLM.

Briefing derivations should compress raw game data into actionable summaries.
Avoid passing raw map-wide lists into prompts unless a rule proves that detail
is needed.

### Coordination

`Src/Coordination/` owns cabinet scheduling, advice publication, agenda storage,
flag routing, minister traces, status surfaces, and replay-corpus persistence.

It also owns the runtime minister registry: the ordered list of live and planned
minister scopes, readiness, dashboard views, and trigger/introspection
capabilities. Host endpoints and cabinet scheduling consume that registry
instead of maintaining separate hard-coded minister lists.

The AdviceBus is the inspectable player-facing stream. Active advice is
published as minister snapshots so stale prior-cycle cards can be replaced
without losing audit history.

The bulletin-board/Labor execution side remains deferred until the Auto epic.
Assisted Apply is intentionally smaller: one player-confirmed, allowlisted
operation, not planning or pawn allocation.

### Ministers

`Src/Ministers/` owns the cabinet. Each minister gets its own directory, with a
rules layer first and an LLM escalation path only when rules cannot decide.

Minister-specific docs define domain ownership, advice quality constraints,
escalation boundaries, and open questions. They should not copy every rule,
enum, fixture, or helper from the implementation.

### LLM Gateway

`Src/LlmGateway/` owns provider calls, prompt assembly, raw-output capture, and
strict-first response parsing/normalization. Normalization may repair useful
near-miss payloads, but it must not invent game facts or execute actions.

Gemini Developer API is the MVP provider. Secrets stay outside tracked config.
The code owns exact option names and retry behavior.

### Knowledge

`Src/KnowledgeBase/` owns guide ingestion, embedding cache, retrieval profiles,
and in-process vector search. The guide corpus lives under `Docs/guides/`.

The design guarantee is that guide retrieval is advisory context for LLM
escalation, not a replacement for live state or deterministic rules.

### Host

`Src/ApiHost/` wires services, exposes HTTP/SSE endpoints, serves the dashboard,
and publishes bounded operational metadata. Endpoint files and Dashboard API
clients are the source of truth for exact routes and payload fields.

### Dashboard

`Dashboard/` is the React + TypeScript operations UI. Production builds emit
static assets into the Host web root; dev mode may run through Vite.

The dashboard is an inspection surface, not a translation layer. Raw/debug views
should preserve backend contract names; player-facing advice views may format
for readability.

---

## Local Run

`run-rimbob.ps1` is the preferred local launcher. It builds the dashboard and
starts the Host hidden with a RimBob icon in the Windows notification area.
Right-click the icon to open the dashboard, jump directly to dashboard
scope/view combinations, or stop the Host; the icon exits when the Host exits.

Use `run-rimbob.ps1 -Foreground` when terminal output must stay attached to the
current shell for debugging or verification.

---

## Configuration

Tracked config contains only non-secret defaults. Gemini API keys and other
secrets must stay in environment variables or gitignored local config.

The Host should tolerate missing LLM credentials well enough to boot health and
dashboard surfaces; actual LLM calls can report degraded provider health.

---

## Observability

Structured logging is part of the design. It is the audit surface for minister
decisions and the input to refinement.

Required observability surfaces:

- Human-readable Host logs for local debugging.
- Structured decision logs for rules/LLM path audit.
- Replay corpus records for before/after refinement benchmarking.
- In-memory latest traces and raw LLM output for dashboard inspection.
- Bounded `/api/system/health` metadata for logs, replay corpus, SSE, provider,
  RAG, and endpoint/data coverage.

Exact log filenames, event fields, and endpoint payloads are owned by code and
tests. The durable design requirement is that enough context is recorded to
reconstruct why advice was emitted and to replay historical minister inputs
before promoting rule, prompt, briefing, or RAG changes.

---

## Library Choices

| Concern | Choice | Design reason |
|---|---|---|
| LLM calls | Google GenAI SDK | First-party Gemini path for MVP, with a Vertex migration path later |
| HTTP to RIMAPI | Typed `HttpClient` wrapper | Simple, debuggable integration boundary |
| Serialization | `System.Text.Json` | Built into .NET and matches current contracts |
| Testing | xUnit + FluentAssertions | Standard .NET test stack already in use |
| Logging | Microsoft logging + Serilog | Structured local audit logs |
| Web host | ASP.NET Core minimal APIs | Small localhost API and dashboard host |
| Dashboard | Vite + React + TypeScript | Fast local UI iteration with typed API clients |
| Vector store | In-process cosine store | Current guide corpus is small; avoid external service complexity |

---

## Deferred Auto Epic

The HTN planner, bulletin board, Labor solver, broad RIMAPI write coverage, and
per-advice-type `Auto` execution are deferred until M7+. Assisted Apply may map a
small allowlist earlier, but it does not implement the Auto stack. These docs
remain design sketches, not implementation promises.

---

## Open Questions

- [ ] RIMAPI read-endpoint inventory by minister.
- [ ] Test project organization if the suite grows beyond the current shape.
- [ ] Per-minister advice-type catalogues for future autonomy graduation.
- [ ] When the guide corpus grows, whether the in-process vector store still
      meets latency and maintenance needs.
