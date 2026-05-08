# RimAI — Code Architecture

> **Living document.** See `CLAUDE.md` for update rules.
> This is the closest thing to a spec. Update when interfaces are proven by a shipped slice, not before.

---

## Stack

```
                              Browser
                                 │
                          Dashboard (React + TS, served by Host)
                                 │  HTTP + SSE  (/api/advice/stream)
┌──────────────────────────────────────────────┐
│  Host  (.NET 9 service)                      │
│  Wires everything; runs the main loop        │
│  Serves Dashboard static assets + SSE feed   │
│  Orchestrator/Budgeter, AdviceBus            │
└──────┬──────────┬──────────┬─────────────────┘
       │          │          │
  Ministers   Coordination  Knowledge
       │
      LLM
                  │          │
                 State
                  │
            Ingestion     Core
                  │
            RIMAPI  (HTTP/SSE  localhost:8765, reads only in MVP)
                  │
            RimWorld Engine
```

> **Deferred (Auto epic):** the `Planner/` project (HTN) and the bulletin-board half of `Coordination/` are designed but not built in MVP. Re-engaged at M7. Their design docs ([`planning.md`](planning.md), [`ministers/labor.md`](ministers/labor.md)) are preserved verbatim with deferred banners.

**External .NET 9 service.** Not a RimWorld mod. Mods are pinned to .NET Fx 4.7.2; the external service uses modern libraries.

**Project naming:** no prefix on folder names. Namespaces inside each project are `RimAI.<Project>` via `<RootNamespace>` in each `.csproj`. `Core/` → `RimAI.Core`, `LLM/` → `RimAI.LLM`, `Ministers/` → `RimAI.Ministers`, etc.

---

## Projects

### Core
Shared types, contracts, domain model. **No external dependencies.** Everything else may depend on Core; Core depends on nothing.

```
Core/
├── Aggregates/
│   ├── ColonistRegistry.cs
│   ├── StockpileLedger.cs
│   ├── BuildingRegistry.cs
│   ├── PowerNetwork.cs
│   ├── ThreatBoard.cs
│   ├── ResearchState.cs
│   ├── FactionRegistry.cs
│   └── EconomyLedger.cs
├── Briefings/
│   ├── IBriefing.cs
│   ├── AgricultureBriefing.cs
│   ├── DefenseBriefing.cs
│   ├── ConstructionBriefing.cs
│   ├── WelfareBriefing.cs
│   └── LaborBriefing.cs
├── Flags/
│   ├── AgentFlag.cs
│   ├── FlagSeverity.cs
│   └── ResourceRequest.cs
├── Advice/
│   ├── AdviceItem.cs       // see design/advice.md for schema
│   ├── AdviceSeverity.cs
│   ├── SuggestedAction.cs
│   ├── FeedbackEvent.cs    // Accept | Dismiss | Modify
│   └── AutonomyMode.cs     // Off | Suggest | Auto (per-minister)
├── Ministers/
│   ├── IMinister.cs        // RunPlayCycle(WakeupTrigger, ct) + RunRefinement(ct)
│   ├── IMinisterRules.cs   // Evaluate(briefing, context) → Decision | Escalate
│   ├── ColonyContext.cs    // shared posture propagated to all ministers
│   ├── MinisterGoal.cs
│   ├── WakeupTrigger.cs    // BriefingChanged | FlagFired | Heartbeat | ScheduledWakeupFired(payload)
│   └── ScheduledWakeup.cs  // FireAt + Payload; at most one pending per minister
├── Labor/                  // Deferred — Auto epic
│   ├── LaborRequest.cs
│   ├── WorkType.cs
│   ├── Priority.cs
│   └── GoalId.cs           // per-minister enums live here
└── Versioning/
    └── Versioned.cs        // Versioned<T> base with monotonic version
```

### Ingestion
Talks to RIMAPI. No domain logic — only translation from RIMAPI DTOs to aggregate updates.

```
Ingestion/
├── RimApiClient.cs         // typed HTTP wrapper; one method per RIMAPI endpoint used
├── SsePushListener.cs      // SSE stream → domain events
├── Pollers/
│   ├── SlowPoller.cs       // every 5-10 in-game min
│   ├── MediumPoller.cs     // every in-game min
│   └── FastPoller.cs       // every 10 in-game sec
└── IngestionDispatcher.cs  // routes polled data to aggregate updates
```

### State
The state store. Computes and caches derived facts. Ministers read from here; they never call RIMAPI directly.

```
State/
├── ColonyState.cs          // root container for all aggregates
├── BriefingCache.cs        // versioned view cache; recomputes on aggregate version bump
└── Derivations/
    ├── FoodDerivations.cs
    ├── DefenseDerivations.cs
    ├── MoodDerivations.cs
    └── WealthDerivations.cs
```

### Planner — Deferred (Auto epic)
HTN planner only. Goal decomposition into primitive tasks. Ministers register their own domains; the engine is neutral. **Not built in MVP** — re-engaged at M7. See [`planning.md`](planning.md).

```
Planner/                     // Deferred — Auto epic
└── HTN/
    ├── Planner.cs            // recursive expansion engine
    ├── CompoundTask.cs
    ├── PrimitiveTask.cs
    ├── Method.cs
    ├── WatchedPrimitive.cs   // observability contract wrapper
    └── IDomain.cs            // interface ministers implement to register a domain
```

### Coordination
The shared coordination layer between ministers. Owns the flag channel (cross-minister signaling, consumed by the Mayor and CoS). The bulletin board half is **deferred — Auto epic**.

```
Coordination/
├── AdviceBus.cs              // in-process emitter; Host bridges it to the SSE feed
├── AgendaStore.cs            // versioned MayorAgenda store + 30-day history ring
├── DayTickOrchestrator.cs    // BackgroundService; refreshes ColonyState each poll, fires Mayor on startup + day rollover
├── MayorStatus.cs            // tracks IsRunning / StartedAt / CompletedAt / LastError; surfaced via /api/status
├── FlagChannel.cs            // flag emission, routing, severity tiers, expiry
└── BulletinBoard.cs          // Deferred — Auto epic. LaborRequest lifecycle.
```

The advice bus is the single inspectable surface for colony advice. A dashboard view should show every memo traceable to `minister → briefing → rule_or_llm_path → AdviceItem`.

### Ministers
The cabinet ministers and their rules layers.

```
Ministers/
├── Agriculture/
│   ├── MinisterOfAgriculture.cs
│   ├── Rules.cs              // IMinisterRules<AgricultureBriefing>
│   ├── AgricultureDomain.cs  // HTN domain registration
│   └── scope.md              // written after first slice ships
├── Defense/
│   ├── DefenseMinister.cs
│   ├── Rules.cs
│   ├── DefenseDomain.cs
│   └── scope.md
├── Construction/
│   ├── ConstructionMinister.cs
│   ├── Rules.cs
│   ├── ConstructionDomain.cs
│   └── scope.md
├── Welfare/
│   ├── WelfareMinister.cs
│   ├── Rules.cs
│   ├── WelfareDomain.cs
│   └── scope.md
├── Labor/                    // Deferred — Auto epic
│   ├── MinisterOfLabor.cs
│   ├── Rules.cs
│   ├── AssignmentSolver.cs   // reads BulletinBoard, assigns pawns via RIMAPI
│   └── scope.md
├── Mayor/
│   ├── Mayor.cs
│   ├── Rules.cs
│   └── scope.md
└── ChiefOfStaff/
    ├── ChiefOfStaff.cs
    ├── Rules.cs
    └── scope.md
```

### LLM
Provider wrapper and prompts used by ministers on escalation.

```
LLM/
├── LlmClient.cs              // Google GenAI SDK wrapper
├── PromptBuilder.cs          // assembles briefing + RAG + system prompt
├── AdviceSchema.cs           // JSON schema for AdviceItem LLM output
└── prompts/
    ├── mayor.system.md
    ├── agriculture.system.md
    ├── defense.system.md
    ├── construction.system.md
    ├── welfare.system.md
    └── labor.system.md       // minimal; Labor rarely escalates
```

### Knowledge
RAG layer.

```
Knowledge/
├── KnowledgeBase.cs       // in-process cosine store
├── Embeddings.cs          // calls Gemini embeddings API (or other)
├── EmbeddingCache.cs      // on-disk JSON cache keyed by chunk hash
├── Ingest.cs              // one-time guide ingestion
└── (no guides here — guide corpus lives in Docs/guides/)
```

### Host
.NET 9 service that wires everything and serves the dashboard.

```
ApiHost/                              // project: RimAI.Host
├── Program.cs                        // DI wiring + startup checks
├── RimAiOptions.cs                   // typed config bound from "RimAi" section
├── appsettings.json                  // Serilog config + RimAi (ListenUrl, RimApiBaseUrl, PingLlmOnStartup)
├── appsettings.Development.json
├── Endpoints/
│   ├── AgendaStreamEndpoint.cs       // GET  /api/advice/stream      (SSE)
│   ├── AgendaEndpoints.cs            // GET  /api/agenda/{latest|history}
│   │                                 // POST /api/agenda/refresh     (demand-trigger)
│   ├── ColonyEndpoints.cs            // GET  /api/colony/snapshot    (latest MayorBriefing)
│   ├── StatusEndpoints.cs            // GET  /api/status             (server + Mayor run state)
│   │                                 // GET  /api/mayor/prompt       (next system + user message)
│   └── AutonomyEndpoints.cs          // GET/PUT /api/autonomy        (per-minister Off|Suggest|Auto)
├── wwwroot/                          // Dashboard build output (Vite outDir)
└── Orchestrator/                     // (planned) main-loop budgeter; not built yet — DayTickOrchestrator
                                      // in Coordination/ drives Mayor wake-ups in M1.5
```

Host binds to `localhost` only — see [`dashboard.md`](dashboard.md) auth posture.

**Configuration.** Non-secret config lives in `appsettings.json` under the `RimAi` section (typed via `RimAiOptions`). The Gemini key may be set either via the `GEMINI_API_KEY` env var (preferred for CI / production) or via `RimAi.GeminiApiKey` in `appsettings.Local.json` (gitignored — local dev convenience only). The env var wins when both are present. `appsettings.json` and `appsettings.Development.json` are tracked, so never put secrets in those. `Program.cs` resolves the key once during DI registration and passes it as a string into `LlmClient` — the LLM project does not depend on `IConfiguration`. `LlmClient` tolerates a missing key (constructor does not throw, `IsConfigured` is false, `PingAsync` returns false) so the Host can boot for `/api/health` smoke checks without credentials.

### Dashboard
React + TypeScript advisor UI. Built with Vite. Output served as static assets by Host (or via Vite dev proxy in development).

```
Dashboard/
├── package.json
├── vite.config.ts
├── index.html
├── src/
│   ├── main.tsx
│   ├── App.tsx
│   ├── api/
│   │   ├── adviceStream.ts      // EventSource subscriber
│   │   └── feedback.ts
│   ├── components/
│   │   ├── AgendaTab.tsx
│   │   ├── AgendaItemCard.tsx   // Accept | Dismiss | Modify controls (M2 wires)
│   │   ├── BriefingInspector.tsx
│   │   ├── DecisionLog.tsx
│   │   └── AutonomyPanel.tsx    // per-minister Off | Suggest | Auto dial
│   └── types/
│       └── advice.ts            // mirrors Core/Advice shapes
└── tests/
```

See [`dashboard.md`](dashboard.md) for the contract.

---

## Key interfaces (draft — update after first slice)

```csharp
// All ministers implement this for their rules layer
public interface IMinisterRules<TBriefing>
{
    RulesResult Evaluate(TBriefing briefing, ColonyContext context);
}

// All ministers implement this for their main loop
public interface IMinister
{
    string Name { get; }
    Task RunPlayCycle(WakeupTrigger trigger, CancellationToken ct);  // trigger: why we were woken
    Task RunRefinement(CancellationToken ct);                        // called manually or on schedule
}

// Why a minister was woken — passed into RunPlayCycle by the Orchestrator
public abstract record WakeupTrigger;
public record BriefingChanged                      : WakeupTrigger;
public record FlagFired(AgentFlag Flag)            : WakeupTrigger;
public record Heartbeat                            : WakeupTrigger;
public record ScheduledWakeupFired(string Payload) : WakeupTrigger;

// Domain registration for the HTN planner — Deferred (Auto epic)
public interface IDomain
{
    string OwnerMinister { get; }
    IEnumerable<CompoundTask> GetCompounds();
}
```

Minister output in MVP:

```csharp
// Replaces Goal in the suggest-only MVP. See design/advice.md for full schema.
public record AdviceItem(
    string             Id,
    string             Minister,
    AdviceSeverity     Severity,
    string             Title,
    string             Body,
    string             Rationale,
    SuggestedAction[]  SuggestedActions,
    string[]           Citations,
    DateTime           IssuedAt,
    DateTime           ExpiresAt
);
```

---

## Library choices

| Concern | Library | Notes |
|---|---|---|
| LLM calls | `Google.GenAI` (official SDK, NuGet `Google.GenAI` by Google) | Streaming for UI; non-streaming for background |
| HTTP to RIMAPI | `HttpClient` (typed wrapper) | No extra framework |
| SSE from RIMAPI | `HttpClient` streaming response | Built-in |
| RAG vector store | In-process cosine store (~150 lines) | No SK.Memory; upgrade to Qdrant if corpus grows |
| Serialisation | `System.Text.Json` | Built-in |
| Testing | `xUnit` + `FluentAssertions` | Standard .NET |
| DI | `Microsoft.Extensions.DependencyInjection` | Built-in |
| Logging | `Microsoft.Extensions.Logging` + `Serilog` | Structured JSON logs for decision audit |
| Web/SSE host | `ASP.NET Core` minimal API on Host | Serves dashboard static assets + SSE feed; localhost-only bind |
| Dashboard build | `Vite` + `React 18` + `TypeScript` | Outputs to Host's `Static/` |

---

## Logging and observability

Structured JSON logging is not optional — it is the audit surface for minister decisions and the input to refinement.

Two event types are logged to `logs/decisions-YYYYMMDD.jsonl` (JSONL, one object per line):

**Briefing recompute** — emitted by `BriefingCache` on every cache miss. Contains the full serialized briefing so the exact inputs to any LLM call are reconstructable.

```json
{
  "ts":       "2026-05-06T14:23:01.412Z",
  "level":    "Debug",
  "category": "BriefingCache",
  "message":  "MayorBriefing recompute version=1 aggregateVersions=[2,1,3,1,1,1,1,1,1] briefing={...full MayorBriefing JSON...}"
}
```

**Minister decision** — emitted when a minister's rules layer or LLM call produces output.

```json
{
  "minister":        "Agriculture",
  "tick":            "Y1Q3D7H14",
  "trigger":         "briefing_change",
  "briefing_version": 142,
  "path":            "rules",
  "rule_fired":      "harvest_when_mature",
  "decision":        { "advice": [], "flags": [] },
  "outcome_window_ticks": 2880
}
```

Valid `trigger` values: `briefing_change` | `flag_fired` | `heartbeat` | `scheduled_wakeup`.
For `scheduled_wakeup`, the event also includes `"wakeup_payload": "<string>"`.

**Sinks:**
- `logs/rimai-YYYYMMDD.log` — human-readable rolling log (Info+).
- `logs/decisions-YYYYMMDD.jsonl` — structured JSON, all events from `RimAI.State` and `RimAI.Ministers` namespaces (Debug+). This is the file refinement reads.
- Tests write to `logs/test-YYYYMMDD-HHmmss.jsonl` (one file per `dotnet test` invocation, shared across all test classes).

---

## Open questions / deferred spec

- [ ] RIMAPI endpoint inventory — map each minister's owned **read** endpoints (writes deferred to Auto epic).
- [ ] Embedding model — Gemini embeddings (`text-embedding-004` / `gemini-embedding-001`) or a local model? Decide when RAG slice ships.
- [ ] Test project structure — `Tests/` monolith or per-project test projects?
- [ ] Dashboard packaging — bundle Vite output into Host at publish time, or run dev mode separately and ship a built output for release?
- [ ] Per-minister advice-type catalogue — used by the autonomy dial when M7 lands.
