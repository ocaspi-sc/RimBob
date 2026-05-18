# RimBob

RimBob is an assisted-gameplay advisor for RimWorld. The player keeps control of
the colony; RimBob watches live game state from RIMAPI and surfaces strategic
suggestions through a local dashboard.

The MVP is `Suggest` mode by default. Ministers emit advice and agendas; narrow
Assisted Apply actions may execute only after a player clicks an allowlisted
step. Autonomous play, pawn allocation, HTN planning, and broad RIMAPI write
coverage are deferred until the later Auto epic.

## Current Shape

- .NET 9 backend hosted by `RimBob.Host`
- React + TypeScript dashboard served by the host
- RIMAPI ingestion client for live RimWorld state
- Mayor-first cabinet model that produces a living Agenda
- Server-sent events for live dashboard updates
- Gemini Developer API through `Google.GenAI` for LLM escalation
- Rule-first ministers: deterministic rules handle common cases before LLM calls

## Repository Layout

```text
Docs/                  Design docs, roadmap, and RimWorld guides
Dashboard/             React + TypeScript dashboard
HumanTodo.md           Human task inbox and execution board
Src/ApiHost/           ASP.NET host, REST endpoints, SSE stream, dashboard serving
Src/Common/            Pure domain types and shared contracts
Src/Coordination/      Advice bus, agenda store, tick orchestration, flag channel
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
cd Dashboard
npm install
```

Set Gemini API keys with either env var:

```powershell
$env:GEMINI_API_KEY = "single-key-here"
$env:GEMINI_API_KEYS = "primary-key-here;secondary-key-here"
```

…or for local dev, drop it in `Src/ApiHost/appsettings.Local.json` (gitignored):

```json
{
  "RimBob": {
    "GeminiApiKeys": [
      "primary-key-here",
      "secondary-key-here"
    ]
  }
}
```

`GEMINI_API_KEY` accepts one key. `GEMINI_API_KEYS` accepts a semicolon, comma, or newline separated ordered list. The host tries keys in order and only advances to the next key for quota/rate-limit/key failures.

Don't put keys in `appsettings.json` or `appsettings.Development.json` - both are tracked.

Run RimBob and the dashboard with the helper script:

```powershell
.\run-rimbob.ps1
```

The script installs dashboard dependencies with `npm.cmd ci` if
`node_modules` is missing, builds the dashboard into `Src/ApiHost/wwwroot`,
builds `RimBob.Host`, then starts the host hidden with a RimBob icon in the
Windows notification area. Right-click the icon to open the dashboard in Chrome
or stop the host. The icon menu also has direct Chrome commands for the
dashboard's console scopes and minister inspection views. If Chrome cannot be
found, the launcher falls back to the default browser. Windows may place new
notification-area icons behind the overflow chevron until you pin them. Open the
dashboard at:

```text
http://localhost:5000
```

For a faster repeat run after dependencies and dashboard assets are already
current:

```powershell
.\run-rimbob.ps1 -SkipDashboardBuild -NoRestore
```

For debugging, or when an agent needs terminal output captured in the current
shell, keep the server attached instead:

```powershell
.\run-rimbob.ps1 -Foreground
```

If local PowerShell script execution is blocked on the machine, run it through
an explicit process policy override:

```powershell
powershell -ExecutionPolicy Bypass -File .\run-rimbob.ps1
```

Manual equivalent:

```powershell
cd Dashboard
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
cd Dashboard
npm.cmd run build
```

Run the .NET test suite:

```powershell
dotnet test Src/RimBob.sln
```

## Design Docs

Start with:

- `Docs/DESIGN.md` for the overall product and architecture
- `Docs/ROADMAP.md` for milestone order
- `HumanTodo.md` for active work and loose task capture
- `Docs/design/agenda.md` for the Mayor Agenda model
- `Docs/design/dashboard.md` for dashboard behavior and HTTP/SSE contract

When design decisions change, update the relevant design doc in the same turn.
