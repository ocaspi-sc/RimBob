---
name: save-colony-state
description: Captures the live RimWorld colony state into a scenario snapshot JSON (and optionally the committed LFS test fixture). Use when the user wants to save, capture, snapshot, or recapture colony state, refresh a scenario's colony-state.json, or recapture the NC1 test fixture from the live game. Invoked by /save-colony-state.
---

# save-colony-state

Captures the current live-game colony state into a scenario snapshot file. The
RimBob Host continuously ingests game state from RIMAPI and persists the latest
snapshot to disk; this skill verifies a *fresh* ingest happened, sanity-checks
the snapshot, and copies it to its destination(s). It never commits.

## How capture works (mechanism)

- The Host ingests live state from RimWorld (RIMAPI, `http://localhost:8765`)
  and writes the latest `ColonyStateSnapshot` to
  `%LOCALAPPDATA%\RimBob\state\latest-colony-state.json` (data root resolved by
  `HostLogPaths.ResolveStableLocalRoot` — LOCALAPPDATA, falling back to
  `~/.local/share/RimBob`).
- **The data root is shared across ALL checkouts and worktrees.** A worktree
  Host and the master Host write the same file — last writer wins. Capture with
  only the intended Host running, and verify freshness via that Host's API, not
  by file mtime alone.
- `GET <host-url>/api/system/health` → `colony_snapshot` is the source of truth
  for freshness: `captured_at`, `age`, `game_tick`, `schema_version`, `source`,
  `path`. Prefer this JSON evidence over inspecting the file.
- The snapshot file is large (~18 MB). Never read it whole into context; use
  PowerShell `ConvertFrom-Json` with targeted property access, or scoped
  searches.

## Arguments

- **Scenario name** (optional, default `new-colony-1`): destination is
  `<repo-root>\var\scenarios\<scenario>\colony-state.json`.
- **Fixture update** (only when the user explicitly asks — e.g. "recapture the
  fixture", "update the test fixture"): additionally overwrite
  `Src\Tests\NewColony\Fixtures\<scenario>.colony-state.json` (Git LFS). The
  committed fixture is human-gated (see `.plans/milestone-nc1.md`); do not
  update it as a side effect of a plain capture.

## Steps

1. **Preconditions.** Confirm RimWorld is running (`Get-Process RimWorldWin64`;
   RIMAPI answers on port 8765 — any HTTP response, even 404, means it is
   listening; "connection refused" means it is not). Confirm the Host is up via
   the `run-rimbob` skill rules: `http://localhost:5000` for the real
   `C:\dev\RimBob` checkout, the worktree's own port otherwise. If the Host is
   down, start it per `run-rimbob` (default flags; `-BuildHost -Restore` only
   if the exe is missing or stale). Verify `/api/system/health` shows
   `runtime.runtime_root` = the intended checkout and the RIMAPI handshake is
   OK. If the handshake fails, stop and report — there is nothing to capture.

2. **Wait for a fresh ingest.** Poll `GET /api/system/health` →
   `colony_snapshot` every ~15s (up to 3 minutes) until `captured_at` is after
   the moment you started (or `age` is under ~1 minute) AND `game_tick` is
   plausible for the loaded save. If it never refreshes, read the newest Host
   log for ingestion errors and report the blocker. Cross-check that the
   `latest-colony-state.json` LastWriteTime moved as well.

3. **Sanity-check the snapshot** (targeted reads, not full-file):
   - `schema_version` matches what current code writes (compare against the
     existing destination file or fixture if present),
   - `game_tick`, colonist count, buildings count, work_tables count,
   - Area_Home under zones/areas: for a base-anchored scenario like NC1,
     `cell_count` must be > 0 with non-null bounds,
   - growing zones: count and list them; flag suspected stray/diagnostic zones
     in the report (do not block on them).
   If a check fails (e.g. empty Area_Home when the save clearly has a base, or
   tick 0), stop and report instead of overwriting anything.

4. **Copy to the scenario folder:**
   `Copy-Item "$env:LOCALAPPDATA\RimBob\state\latest-colony-state.json" "<repo-root>\var\scenarios\<scenario>\colony-state.json"`
   (create the folder if missing). This path is gitignored staging.

5. **Fixture update (only if explicitly requested).** Copy the same file over
   `Src\Tests\NewColony\Fixtures\<scenario>.colony-state.json`. Then:
   - verify LFS will take it: `git check-attr filter -- Src/Tests/NewColony/Fixtures/<scenario>.colony-state.json`
     must report `filter: lfs`; `git status --short` shows the fixture modified;
   - run `dotnet test Src/RimBob.sln --filter "kind!=reach"` and
     `--filter "kind=reach"`; report pass/fail counts and every failing test
     name with a one-line reason. Failures caused by changed fixture facts are
     **expected findings** — report them, never edit tests or source to make
     them pass, and never commit. Test fixes are a separate, planned slice.

6. **Report.** Snapshot stats (schema, game_tick, colonists, Area_Home
   cell_count/bounds, buildings, work_tables, growing zones incl. suspected
   strays), freshness evidence (`captured_at`/`age`), files written, LFS
   verification (if fixture), test results (if fixture), and any blockers.
   Leave the Host running.

## Guardrails

- Never commit; leave all changes in the working tree for review.
- Never edit C#/TS source or tests from this skill.
- Never overwrite a destination when the sanity checks fail.
- Multiple Hosts running concurrently make `latest-colony-state.json`
  ambiguous — say so in the report if you detect more than one
  `RimBob.Host.exe`, and identify which checkout each belongs to before
  trusting the capture.
