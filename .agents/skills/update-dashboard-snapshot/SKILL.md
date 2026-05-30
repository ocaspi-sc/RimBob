---
name: update-dashboard-snapshot
description: Refreshes the RimBob dashboard static HTML website snapshot under `web/Snapshot`. Captures every first-class dashboard scope/view page, makes `index.html` open the cached Mayor Advice page, rewrites scope/view navigation to relative cached-page links, adds a large `SNAPSHOT` header tag, disables remaining action/disclosure buttons, removes scripts, and embeds images. Use when the user asks to update, recapture, refresh, or create the dashboard snapshot as a full static HTML export, especially `web/Snapshot`, "snapshot the dashboard", "update the snapshot", "all pages", "static html", or "cached page" navigation. Only trigger on an explicit request for a static HTML / `web/Snapshot` export of dashboard pages. Do NOT use this to verify or prove backend/live state (confirming advice options attached, an anchor surfaced, a value changed, a chain works) — that proof comes from JSON API responses and `logs/replay/*.jsonl`, never a browser export. Do not use this for image-only screenshots.
---

# Update Dashboard Snapshot

Capture every first-class RimBob dashboard scope/view route into `web/Snapshot` as a static HTML website, with proof that the browser is reading the intended checkout.

## Snapshot Decisions

- Capture HTML, not PNG screenshots.
- Capture every first-class dashboard scope/view page.
- Use `web/Snapshot/index.html` as the offline entrypoint, but render the cached Mayor Advice page there instead of a sitemap.
- Keep cached pages at `web/Snapshot/pages/<scope>/<view>.html`.
- Rewrite only dashboard scope-rail buttons and view-tab buttons into relative links to cached pages.
- Disable every remaining `<button>` so the artifact cannot imply live backend actions.
- Add a prominent `SNAPSHOT` tag beside the dashboard title on every captured page.
- Remove scripts and script preload links.
- Inline same-origin stylesheets.
- Embed fetchable images as data URLs; replace unfetchable images with inline placeholders that preserve source/error attributes.
- Treat the output as point-in-time inspection HTML, not an interactive React app.

## Workflow

1. Resolve the repo root with `git rev-parse --show-toplevel`.
2. Pick the dashboard URL:
   - Use `http://localhost:5000` only when the repo root is exactly `C:\dev\RimBob`.
   - For any worktree, never use `5000`; pick a free non-5000 port, usually `5012` or the next open port.
3. Check existing Hosts with `/api/system/health`. Reuse a Host only when `runtime.runtime_root` equals the current repo root and `runtime.host_process_path` points inside that repo.
4. If no matching Host is live:
   - If `Dashboard/node_modules` is missing, run `npm.cmd ci` from `Dashboard/`.
   - Run `npm.cmd run build` from `Dashboard/` so the Host serves current dashboard assets.
   - Run `dotnet build .\Src\ApiHost\RimBob.Host.csproj --configuration Debug`.
   - Start the Host through `run-rimbob.ps1` with `-ListenUrl http://localhost:<port>` for worktrees. If execution policy blocks the script, use `powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\run-rimbob.ps1 ...`.
   - If a sandboxed launch returns but the port never answers or reports a sandbox runtime root, request a narrow elevated `Start-Process` for the built `RimBob.Host.exe` with:
     `RimBob:ListenUrl=http://localhost:<port> RimBob:PingLlmOnStartup=false RimBob:Rag:Enabled=false`.
5. Wait for `/api/system/health`; do not capture until it reports the current repo root and matching executable path.
6. Use the Browser skill and the in-app browser to open the dashboard URL.
7. Import and run `scripts/static-dashboard-snapshot.mjs` from this skill directory in the same browser runtime. Prefer `writeStaticDashboardSiteSnapshot(...)` for normal use. It writes:
   - `web/Snapshot/index.html` as the static Mayor Advice entrypoint.
   - `web/Snapshot/pages/<scope>/<view>.html` for every first-class dashboard scope/view route.
   - `web/Snapshot/metadata.json` with URL, entry page, health proof, page count, static-navigation flags, per-page paths, and embedded-image counts.
   Use `writeStaticDashboardSnapshot(...)` only when the user explicitly asks for one page.
8. Confirm the saved files exist and report:
   - clickable path to `web/Snapshot/index.html`
   - entry page, page count, embedded-image count, and failed-image count
   - dashboard URL
   - `runtime_root` and `host_process_path` from `/api/system/health`
   - proof that all HTML files have the `SNAPSHOT` tag, no scripts, no live `src` dependencies, and no enabled `<button>` elements

## Static Export Shape

The static snapshot is not a PNG. It is the rendered dashboard DOM after data has loaded. `web/Snapshot/index.html` opens to the cached Mayor Advice page, not a sitemap. Scope-rail and view-tab controls are links because they navigate between cached pages. All other buttons are disabled because the snapshot has no live backend. The dashboard header carries a large `SNAPSHOT` tag. Each page has scripts removed, same-origin CSS inlined, images embedded as data URLs when possible, and unfetchable images replaced by inline placeholders with `data-snapshot-*` source/error attributes.

## Verification Checks

After regenerating, inspect files on disk and verify:

- `web/Snapshot/index.html` contains Mayor content and does not contain the old "All Dashboard Pages" sitemap.
- `web/Snapshot/metadata.json` has `entry_page.scope = "mayor"` and `entry_page.view = "advice"`.
- There are 87 page files under `web/Snapshot/pages`.
- Scope navigation links use `data-snapshot-nav="scope"` and relative `href` values.
- View navigation links use `data-snapshot-nav="view"` and relative `href` values.
- Every HTML file has `.snapshot-header-tag`.
- Every `<button>` has `disabled`.
- No HTML file has `<script>` tags.
- No HTML file has live `src` attributes such as `/api/...` or `http://localhost:...`.
- `metadata.json` health proof points at the current repo root and Host process.

## Rules

- Do not stop or replace a Host on `5000` from a worktree.
- Do not use another worktree's live Host as the snapshot source.
- Do not save PNG screenshots for this skill unless the user separately asks for an image.
- Do not stage or commit snapshot files unless the user explicitly asks.
- Leave a newly-started Host running for inspection unless the user asks for cleanup; if cleaning up, stop only the process started for this snapshot.
