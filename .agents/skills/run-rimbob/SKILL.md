---
name: run-rimbob
description: Starts the RimBob backend server (RimBob.Host). Use this skill whenever the user wants to run, start, launch, or boot up RimBob. Also trigger when the user asks to check if RimBob is running, restart the server, or test the dashboard. Invoked by /run-rimbob.
---

# run-rimbob

Builds and runs the RimBob.Host ASP.NET Core project. This single process serves both the API and the React dashboard — there is no separate dashboard process to start.

## Project layout

- **Solution**: `C:\dev\RimBob\Src\RimBob.sln`
- **Startup project**: `C:\dev\RimBob\Src\ApiHost` (`RimBob.Host`)
- **Default URL**: `http://localhost:5000`
- **Stack**: .NET 9, Serilog, Kestrel (localhost-only bind)

## Steps

1. **Check if already running** — ping `http://localhost:5000/api/health`. If it returns `{"status":"ok"}`, tell the user RimBob is already running and provide the dashboard URL. Skip to step 4.

   **Exception: if you (or anyone in this session) just rebuilt code that the Host loads,** the running Host is stale. Kill it (`taskkill //F //PID <pid>`) and continue from step 2 so the new build takes effect. The user's standing rule: **always restart the host after a rebuild.** Don't ask before killing — see the kill-host-for-rebuild memory.

2. **Build** — run:
   ```
   dotnet build C:\dev\RimBob\Src\ApiHost\RimBob.Host.csproj --configuration Debug
   ```
   Surface any build errors to the user immediately. Do not proceed if the build fails.

3. **Run** — start the server in the background and capture stdout:
   ```
   dotnet run --project C:\dev\RimBob\Src\ApiHost\RimBob.Host.csproj --no-build
   ```
   Wait up to 15 seconds, reading stdout until you see either `Dashboard:` (success) or a fatal error line.

4. **Read the startup output** — the process writes these lines to stdout after startup checks complete. Parse them and relay each one to the user:

   | Line | Meaning |
   |---|---|
   | `✓ RIMAPI handshake OK — first pawn: {name}` | RimWorld is running with a colony open |
   | `✗ {reason}` / `Start RimWorld with the RIMAPI mod…` | RimWorld not reachable — advisory only, server still runs |
   | `✓ GEMINI_API_KEY present` | LLM calls will work |
   | `✗ GEMINI_API_KEY env var not set` | LLM calls will fail; rules-based advice still works |
   | `✓ Gemini ping OK` | Gemini API reachable |
   | `✗ Gemini ping failed (see logs)` | Gemini unreachable |
   | `Dashboard: {url}` | Server is up; show this URL to the user |

   If you don't see `Dashboard:` within 15 seconds, report whatever output was captured and suggest checking logs.

## Node / Dashboard build

The React dashboard (`Src/Dashboard/`, Vite + TypeScript) is pre-built. Its compiled output lives in `Src/ApiHost/wwwroot/` and is served by `UseStaticFiles` — **Node is not needed to run RimBob**.

Node is only needed when editing the dashboard UI:
```
cd Src/Dashboard
npm run build        # tsc + vite build → outputs to ../ApiHost/wwwroot/
```
After building, restart the .NET host to pick up the new bundle. If the user asks to run the dashboard in dev/hot-reload mode, they can run `npm run dev` from `Src/Dashboard/` — this starts a Vite dev server on a separate port (typically 5173) with HMR, proxying API calls to the .NET host at port 5000.

## Environment notes

- `GEMINI_API_KEY` must be set in the environment for LLM calls to work. Missing key → warn, but don't abort.
- RIMAPI runs at `http://localhost:8765/` by default. If RimWorld isn't running the handshake fails but the server continues normally.
- The server binds to `127.0.0.1` only (never `0.0.0.0`) — this is by design.

## Quick health check (no build)

If the user just wants to verify the server is up, hit `http://localhost:5000/api/health` and report the JSON response.
