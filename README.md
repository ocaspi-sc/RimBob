# RimAI

RimAI is an assisted-gameplay advisor for RimWorld. The player keeps control of
the colony; RimAI watches live game state from RIMAPI and surfaces strategic
suggestions through a local dashboard.

The MVP is suggest-only. Ministers emit advice and agendas, not game actions.
Autonomous play, pawn allocation, HTN planning, and RIMAPI write coverage are
deferred until the later Auto epic.

## Current Shape

- .NET 9 backend hosted by `RimAI.Host`
- React + TypeScript dashboard served by the host
- RIMAPI ingestion client for live RimWorld state
- Mayor-first cabinet model that produces a living Agenda
- Server-sent events for live dashboard updates
- Gemini Developer API through `Google.GenAI` for LLM escalation
- Rule-first ministers: deterministic rules handle common cases before LLM calls

## Repository Layout

```text
Docs/                  Design docs, roadmap, TODOs, and RimWorld guides
Src/ApiHost/           ASP.NET host, REST endpoints, SSE stream, dashboard serving
Src/Common/            Pure domain types and shared contracts
Src/Coordination/      Advice bus, agenda store, tick orchestration, flag channel
Src/Dashboard/         React + TypeScript dashboard
Src/GameStateSync/     RIMAPI HTTP/SSE ingestion client
Src/LlmGateway/        Gemini client and prompt builder
Src/Ministers/         Minister implementations and rules
Src/StateStore/        Colony state, briefings, derivations, parsing
Src/Tests/             Unit and integration-style tests
```

## Prerequisites

- .NET 9 SDK
- Node.js LTS
- RimWorld with RIMAPI available at `http://localhost:8765/`
- A Gemini API key for LLM-backed Mayor calls (env var or `appsettings.Local.json` — see Setup)

The host binds to localhost only. Do not expose it on `0.0.0.0`.

## Setup

Install dashboard dependencies:

```powershell
cd Src/Dashboard
npm install
```

Set the Gemini API key. Either export it as an env var (preferred for CI):

```powershell
$env:GEMINI_API_KEY = "your-key-here"
```

…or for local dev, drop it in `Src/ApiHost/appsettings.Local.json` (gitignored):

```json
{ "RimAi": { "GeminiApiKey": "your-key-here" } }
```

The env var wins when both are set. Don't put the key in `appsettings.json` or `appsettings.Development.json` — both are tracked.

Run RimAI and the dashboard with the helper script:

```powershell
.\run-rimai.ps1
```

The script installs dashboard dependencies with `npm.cmd ci` if
`node_modules` is missing, builds the dashboard into `Src/ApiHost/wwwroot`,
then starts `RimAI.Host`. Open the dashboard at:

```text
http://localhost:5000
```

For a faster repeat run after dependencies and dashboard assets are already
current:

```powershell
.\run-rimai.ps1 -SkipDashboardBuild -NoRestore
```

If local PowerShell script execution is blocked on the machine, run it through
an explicit process policy override:

```powershell
powershell -ExecutionPolicy Bypass -File .\run-rimai.ps1
```

Manual equivalent:

```powershell
cd Src/Dashboard
npm.cmd run build
```

Run the host:

```powershell
cd Src/ApiHost
dotnet run
```

## Verification

Run the dashboard build:

```powershell
cd Src/Dashboard
npm.cmd run build
```

Run the .NET test suite:

```powershell
dotnet test Src/RimAI.sln
```

## Design Docs

Start with:

- `Docs/DESIGN.md` for the overall product and architecture
- `Docs/ROADMAP.md` for milestone order
- `Docs/TODO.md` for active work
- `Docs/design/agenda.md` for the Mayor Agenda model
- `Docs/design/dashboard.md` for dashboard behavior and HTTP/SSE contract

When design decisions change, update the relevant design doc in the same turn.
