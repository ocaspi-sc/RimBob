---
name: run-rimbob
description: Starts the RimBob backend server (RimBob.Host). Use this skill whenever the user wants to run, start, launch, or boot up RimBob. Also trigger when the user asks to check if RimBob is running, restart the server, or test the dashboard. Invoked by /run-rimbob.
---

# run-rimbob

Starts the RimBob.Host ASP.NET Core project. This single process serves both the API and the React dashboard - there is no separate dashboard process to start. Build/setup work is opt-in: the launcher starts the already-built Host by default and only installs, rebuilds, or restores when passed positive flags.

## Project layout

- **Solution**: `<current repo root>\Src\RimBob.sln`
- **Startup project**: `<current repo root>\Src\ApiHost` (`RimBob.Host`)
- **Dashboard project**: `<current repo root>\Dashboard` (Vite + TypeScript; emits into `Src\ApiHost\wwwroot`)
- **Default URL**: `http://localhost:5000` for the real `C:\dev\RimBob` checkout only. Worktrees must use a different localhost port with `-ListenUrl`.
- **Stack**: .NET 9, Serilog, Kestrel (localhost-only bind)

## Steps

1. **Resolve checkout and URL** — run `git rev-parse --show-toplevel` and compare the repo root to `C:\dev\RimBob`.

   - If this is the real `C:\dev\RimBob` checkout, use `http://localhost:5000`.
   - If this is any worktree, do **not** stop or reuse the server on port `5000`. Treat that port as owned by the real checkout. Probe `5002`, then `5003`, and keep going until a free port is found. If the selected port is already served by this same worktree, report and reuse it; if it is served by another checkout, choose the next port. Pass the selected worktree URL as `-ListenUrl http://localhost:<port>`.

2. **Check if already running** — ping the selected URL's `/api/health` and `/api/system/health`. If they return healthy responses, verify `runtime.runtime_root`, `runtime.host_process_path`, and `Get-Process RimBob.Host` before telling the user it is already running. For worktree runs, a healthy `http://localhost:5000` response is not enough; still run this worktree on its selected non-5000 port.

   **Exception: if you (or anyone in this session) just rebuilt code that the Host loads,** the running Host for the selected URL is stale. Restart only that selected-url Host so the new build takes effect. From a worktree, do not kill a `5000` Host unless the user explicitly asked to restart the real checkout.

3. **Check launcher-generated dirt** - before running the launcher with `-InstallDashboard` or `-BuildDashboard`, capture path-scoped status for `Dashboard/`, `Src/ApiHost/wwwroot/`, and `.agents/skills/run-rimbob/SKILL.md`. Avoid broad `git status` if the repo hits LFS or permission noise. After launch, re-check the same paths and report any tracked changes caused by dashboard install/build output. Do not stage or commit from this skill.

4. **Choose build/setup flags** - default launch uses the existing `RimBob.Host.exe` and existing dashboard assets. Add `-BuildHost` when Host code changed or the executable is missing. Add `-Restore` only with `-BuildHost` when package assets may be missing or stale. Add `-BuildDashboard` when dashboard code changed. Add `-InstallDashboard` only when `Dashboard\node_modules` is missing or dependencies need a clean install. If a Host build fails with `MSB3021`, `MSB3027`, or copy-denied errors under `bin\Debug\net9.0`, stop only the `RimBob.Host.exe` process whose path matches the selected checkout and retry once with the same positive flags.

5. **Run** - start the server with the repo launcher. Use PowerShell execution-policy bypass when the shell blocks script execution. Use the selected `-ListenUrl` argument when running from a worktree:
   ```
   powershell.exe -ExecutionPolicy Bypass -File .\run-rimbob.ps1
   powershell.exe -ExecutionPolicy Bypass -File .\run-rimbob.ps1 -ListenUrl http://localhost:<port>
   powershell.exe -ExecutionPolicy Bypass -File .\run-rimbob.ps1 -BuildHost -Restore
   powershell.exe -ExecutionPolicy Bypass -File .\run-rimbob.ps1 -InstallDashboard -BuildDashboard -BuildHost -Restore
   ```
   Add `-Foreground` only when terminal output must stay attached for debugging. After launch, wait up to 20 seconds for the selected URL's `/api/health` to return `{"status":"ok"}`.

6. **Handle sandbox and launcher failures** - do not treat the launcher banner as proof the server stayed up. If an opted-in dashboard build fails with `Access is denied`, Vite cannot resolve `Dashboard\vite.config.ts`, the background process exits, or health never responds, rerun the same launcher command with sandbox escalation. If the escalated launcher still fails, rerun with `-Foreground` for attached logs. Use direct `RimBob.Host.exe` startup only as a last resort and still verify the selected URL, runtime root, and process path.

7. **Prove the server is live** — after the launcher returns, verify all of these before saying RimBob is running:

   - `GET <selected-url>/api/health` returns `{"status":"ok"}`
   - `GET <selected-url>/api/system/health` returns `runtime.server = ok`
   - `runtime.runtime_root` equals the intended checkout root
   - `runtime.host_process_path` and `Get-Process RimBob.Host` point at the intended checkout's `RimBob.Host.exe`
   - `/` returns HTTP 200 so the served dashboard is reachable

8. **Read the startup output** — if foreground stdout is available, the process writes these lines after startup checks complete. Parse them and relay each one to the user:

   | Line | Meaning |
   |---|---|
   | `✓ RIMAPI handshake OK — first pawn: {name}` | RimWorld is running with a colony open |
   | `✗ {reason}` / `Start RimWorld with the RIMAPI mod…` | RimWorld not reachable — advisory only, server still runs |
   | `✓ Gemini API key(s) present: {count}` | LLM calls will work |
   | `✗ Gemini API key not set` | LLM calls will fail; rules-based advice still works |
   | `✓ Gemini ping OK` | Gemini API reachable |
   | `✗ Gemini ping failed (see logs)` | Gemini unreachable |
   | `Dashboard: {url}` | Server is up; show this URL to the user |

   If you don't see `Dashboard:` within 20 seconds, report whatever output was captured and suggest checking logs.

## Node / Dashboard build

The React dashboard (`Dashboard/`, Vite + TypeScript) is pre-built. Its compiled output lives in `Src/ApiHost/wwwroot/` and is served by `UseStaticFiles` - **Node is not needed to run an already-built RimBob Host, and the launcher only installs/builds dashboard assets when `-InstallDashboard` or `-BuildDashboard` is passed.**

Node is only needed when editing the dashboard UI:
```
cd Dashboard
npm.cmd run build    # tsc + vite build -> outputs to ../Src/ApiHost/wwwroot/
```
After building, restart the .NET host to pick up the new bundle. If the user asks to run the dashboard in dev/hot-reload mode, they can run `npm.cmd run dev` from `Dashboard/` — this starts a Vite dev server on a separate port (typically 5173) with HMR, proxying API calls to the .NET host at port 5000.

## Environment notes

- `GEMINI_API_KEY` provides one Gemini key. `GEMINI_API_KEYS` provides an ordered fallback list. Missing key → warn, but don't abort.
- RIMAPI runs at `http://localhost:8765/` by default. If RimWorld isn't running the handshake fails but the server continues normally.
- The server binds to `127.0.0.1` only (never `0.0.0.0`) — this is by design.

## Quick health check (no build)

If the user just wants to verify the server is up, resolve the checkout and hit the selected URL's `/api/health` plus `/api/system/health`; then confirm `runtime.runtime_root` and `runtime.host_process_path`. Use `http://localhost:5000` only for the real `C:\dev\RimBob` checkout; from a worktree, use the chosen non-5000 port.
