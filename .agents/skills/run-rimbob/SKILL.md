---
name: run-rimbob
description: Starts the RimBob backend server (RimBob.Host). Use this skill whenever the user wants to run, start, launch, or boot up RimBob. Also trigger when the user asks to check if RimBob is running, restart the server, or test the dashboard. Invoked by /run-rimbob.
---

# run-rimbob

Builds and runs the RimBob.Host ASP.NET Core project. This single process serves both the API and the React dashboard — there is no separate dashboard process to start.

## Project layout

- **Solution**: `<current repo root>\Src\RimBob.sln`
- **Startup project**: `<current repo root>\Src\ApiHost` (`RimBob.Host`)
- **Default URL**: `http://localhost:5000` for the real `C:\dev\RimBob` checkout only. Worktrees must use a different localhost port with `-ListenUrl`.
- **Stack**: .NET 9, Serilog, Kestrel (localhost-only bind)

## Steps

1. **Resolve checkout and URL** — run `git rev-parse --show-toplevel` and compare the repo root to `C:\dev\RimBob`.

   - If this is the real `C:\dev\RimBob` checkout, use `http://localhost:5000`.
   - If this is any worktree, do **not** stop or reuse the server on port `5000`. Treat that port as owned by the real checkout. Pick a free non-5000 localhost port instead, usually `5002` or the next open port, and pass it as `-ListenUrl http://localhost:<port>`.

2. **Check if already running** — ping the selected URL's `/api/health`. If it returns `{"status":"ok"}`, verify the `RimBob.Host.exe` process path before telling the user it is already running. For worktree runs, a healthy `http://localhost:5000` response is not enough; still run this worktree on its selected non-5000 port.

   **Exception: if you (or anyone in this session) just rebuilt code that the Host loads,** the running Host for the selected URL is stale. Restart only that selected-url Host so the new build takes effect. From a worktree, do not kill a `5000` Host unless the user explicitly asked to restart the real checkout.

3. **Build** — run from the current repo root:
   ```
   dotnet build .\Src\ApiHost\RimBob.Host.csproj --configuration Debug
   ```
   Surface any build errors to the user immediately. Do not proceed if the build fails.

4. **Run** — start the server with the repo launcher. Use the selected `-ListenUrl` argument when running from a worktree:
   ```
   .\run-rimbob.ps1
   .\run-rimbob.ps1 -ListenUrl http://localhost:<port>
   ```
   Add `-Foreground` only when terminal output must stay attached for debugging. After launch, wait up to 15 seconds for the selected URL's `/api/health` to return `{"status":"ok"}`.

5. **Read the startup output** — if foreground stdout is available, the process writes these lines after startup checks complete. Parse them and relay each one to the user:

   | Line | Meaning |
   |---|---|
   | `✓ RIMAPI handshake OK — first pawn: {name}` | RimWorld is running with a colony open |
   | `✗ {reason}` / `Start RimWorld with the RIMAPI mod…` | RimWorld not reachable — advisory only, server still runs |
   | `✓ Gemini API key(s) present: {count}` | LLM calls will work |
   | `✗ Gemini API key not set` | LLM calls will fail; rules-based advice still works |
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

- `GEMINI_API_KEY` provides one Gemini key. `GEMINI_API_KEYS` provides an ordered fallback list. Missing key → warn, but don't abort.
- RIMAPI runs at `http://localhost:8765/` by default. If RimWorld isn't running the handshake fails but the server continues normally.
- The server binds to `127.0.0.1` only (never `0.0.0.0`) — this is by design.

## Quick health check (no build)

If the user just wants to verify the server is up, resolve the checkout and hit the selected URL's `/api/health`. Use `http://localhost:5000/api/health` only for the real `C:\dev\RimBob` checkout; from a worktree, use the chosen non-5000 port.
