# Icon Warm Hardening

**Status:** planned (follow-ups after the bounded-retry fix landed on `master`
as `Retry transient RIMAPI failures during icon warm`).
**Implementation worktree:** create a fresh worktree off current `master`
(e.g. `C:\dev\RimBob-worktrees\icons-warm-fixes`, branch
`claude/icons-warm-fixes`). Do not implement in `C:\dev\RimBob`.

## Goal

Make static icon-cache warming robust and observable so a single warm reliably
populates the full renderable def set, never leaves large swaths of valid defs
marked "missing", and survives client disconnects. The bounded retry already
shipped is a mitigation, not a complete fix — the warm still hammers RIMAPI
faster than it can render icons, is coupled to the HTTP request lifetime, and
has no incremental recovery path.

## Background / Current State

- `Src/ApiHost/IconCacheService.cs`
  - `WarmStaticAsync(ct)` pulls `/api/v1/def/all` + `/api/v1/factions`, builds
    item/terrain/faction candidates, fans out through
    `WarmCandidateAsync` under `SemaphoreSlim gate` with `WarmConcurrency = 2`,
    then writes the configured stable icon cache manifest via
    `WriteManifestAsync`.
  - `WarmCandidateAsync` now retries each candidate `warmAttempts` (default 3)
    times with linear backoff `warmRetryDelay` (default 400ms, injectable via
    ctor; tests pass `TimeSpan.Zero`). Deterministic `ArgumentException`
    (bad/unknown key) fails fast.
  - `GetCachedPngAsync` short-circuits on `File.Exists`, so re-running a warm
    only refetches uncached/failed defs (this is why a follow-up warm completed
    in ~1.5s once the cache was full).
  - Manifest is overwrite-only: `IconWarmSummary` is replaced wholesale each
    run; a cancelled/partial warm never writes it, losing all success
    accounting even though atomic PNG writes persisted.
- `Src/ApiHost/Endpoints/IconEndpoints.cs`
  - `POST /api/icons/cache/warm` awaits `WarmStaticAsync(ct)` inline; `ct` is
    the request `RequestAborted` token, so a client disconnect (~44s observed)
    cancels the whole warm mid-run.
  - `GET /api/icons/cache/status` reads the on-disk inventory + manifest.
- Observed failure mode: a fresh warm against healthy RIMAPI still produced
  ~400+ `result: "missing"` responses because ~37 req/s outruns RIMAPI icon
  rendering, while the same defs resolve fine when fetched individually.
- Faction candidates 500 / return no image when no world is loaded
  (`/api/v1/factions` + `faction/icon` depend on an active game).
- Dashboard surface: `Dashboard/src/components/system/SystemOverview.tsx`
  ("Icon cache" `DisclosureSection`), types in
  `Dashboard/src/types/icons.ts`. Design contract in
  `Docs/design/dashboard.md` (icon cache metadata + warm retry note).

## Decisions / Constraints

- Read-only posture preserved: warming only fetches RIMAPI image endpoints; no
  writes, no new RIMAPI surface.
- Localhost-only; no new external dependencies in pure domain projects.
- Every behavior change updates `Docs/design/dashboard.md` in the same turn and
  is reflected in the dashboard Icon cache panel (counts, job state, failures).
- Prefer general correctness over one-off workarounds (per AGENTS.md).
- Keep briefings/prompts unaffected — this is infra only.

## Phase 1 — Pace the warm so it stops outrunning RIMAPI

Problem: concurrency + raw fan-out causes RIMAPI to return `missing` under load.

- Add a configurable request-rate limit to the warm path (e.g. a token-bucket
  / minimum inter-request delay) in addition to `WarmConcurrency`, applied
  inside the `gate` section of `WarmCandidateAsync` or a shared pacer.
- Make pacing + concurrency + retry tunable via `RimBobOptions`
  (`Src/ApiHost/RimBobOptions.cs`) and ctor params, defaulting conservatively;
  keep `TimeSpan.Zero`/no-pace path for tests.
- Distinguish RIMAPI response shapes: `result == "missing"` (likely
  under-load/no-asset) vs transport error (500/timeout). Both still retry, but
  log/aggregate them separately so the cause is visible.
- Acceptance: a single warm from a cold cache against a healthy loaded game
  reaches ~0 failures for renderable defs without manual re-runs.

## Phase 2 — Decouple warm from the HTTP request lifetime

Problem: client disconnect cancels `WarmStaticAsync` via `RequestAborted`;
long warms can never complete over a normal client, and the manifest is lost.

- Convert warm to a background job: `POST /api/icons/cache/warm` enqueues/starts
  a single-flight warm and returns `202` with a job id immediately; reject
  concurrent starts (return the in-flight job).
- Run the job under `IHostApplicationLifetime.ApplicationStopping` (not the
  request token) so a disconnect does not abort it.
- Extend `GET /api/icons/cache/status` (or add `/api/icons/cache/warm/status`)
  with job state: `idle | running | completed | failed`, started/updated
  timestamps, and live progress (done/total, succeeded/failed so far).
- Persist progress periodically (see Phase 3) so status survives a host
  restart mid-warm.
- Acceptance: starting a warm then closing the client still completes the warm
  and writes the manifest; status reflects progress throughout.

## Phase 3 — Incremental, mergeable manifest (no lost accounting)

Problem: manifest is overwrite-only; partial/cancelled warms lose all summary
data even though PNGs persisted.

- Write manifest incrementally (checkpoint every N candidates and on
  completion) instead of only at the end.
- Merge with prior state: a def that succeeded in an earlier warm and is still
  present on disk stays "succeeded" even if the current run did not revisit it;
  only re-warmed defs update their entry.
- Track per-def state (`kind`, `id`, `status`, `lastAttemptAt`, `attempts`,
  `lastError`) rather than only a bounded failure sample, so failures and
  successes are individually addressable (enables Phase 4).
- Keep the existing `IconWarmSummary` rollup for the dashboard; derive it from
  per-def state. Maintain `MaxStatusFailures` bounding for the status payload.
- Acceptance: a cancelled warm leaves an accurate manifest consistent with the
  PNGs actually on disk; a subsequent warm does not regress success counts.

## Phase 4 — Targeted re-warm of the failed set (lazy + on demand)

Problem: a def marked missing stays missing until a full manual re-warm.

- Add `POST /api/icons/cache/warm?scope=failed` (or a dedicated endpoint) that
  re-attempts only defs whose per-def state is `failed` and that are still
  absent on disk — cheap and fast.
- Lazy heal: when a single icon GET (`/api/icons/{kind}/{id}`) succeeds for a
  def previously marked failed, update its per-def state to `succeeded` so the
  dashboard stops showing it as a missing-icon error (the design doc already
  requires not surfacing stale failures for now-present ids — make the data
  back that up).
- Optional: a low-frequency background sweep that retries the failed set while
  a world is loaded; gated/off by default, configurable.
- Acceptance: clearing failures does not require a full 958-candidate warm;
  dashboard failure list shrinks as defs resolve.

## Phase 5 — Faction warming gated on a loaded world

Problem: faction candidates 500 / return no image with no active game and are
recorded as hard failures, polluting the failure list.

- Before adding faction candidates, confirm RIMAPI handshake / a loaded world
  (reuse the startup handshake signal or a cheap probe). If absent, mark
  faction warming `deferred` (a distinct state, not `failed`) and surface that
  in status.
- When the handshake later succeeds, faction warming becomes eligible (covered
  by Phase 4's targeted re-warm / lazy heal).
- Acceptance: warming with no world loaded reports faction icons as deferred,
  not as 11 errors; warming with a world loaded caches them.

## Tests

- `Src/Tests/ApiHost/IconCacheServiceTests.cs`:
  - Pacing: warm respects the configured rate (observable via a counting/timed
    fake handler) and still succeeds; zero-pace path keeps tests fast.
  - Background job: warm completes when the triggering token is cancelled but
    the application lifetime token is not.
  - Incremental manifest: a warm cancelled partway leaves a manifest whose
    success set matches PNGs on disk; a follow-up warm merges, not regresses.
  - Failed-scope re-warm only touches still-missing defs (assert call counts
    via the delegating handler).
  - Lazy heal: a single successful icon GET flips a previously-failed def's
    state and removes it from the failure sample.
  - Faction gating: no-world path yields `deferred`, not `failed`.
- Keep existing tests green, including
  `WarmStaticAsync_StoresAllFailuresButBoundsStatusSample` and
  `WarmStaticAsync_RetriesTransientImageFailureThenSucceeds`.

## Dashboard Reflection (required)

- `Dashboard/src/types/icons.ts` + `SystemOverview.tsx`: show warm job state
  (idle/running/completed/failed), live progress while running, `deferred`
  vs `failed` breakdown, and a "re-warm failed only" affordance is acceptable
  as read-only status text if no control is added (MVP is suggest/observe).
- Update `Docs/design/dashboard.md` icon-cache section for: job lifecycle,
  deferred state semantics, incremental manifest, and that failure samples are
  per-def state, not a frozen end-of-run list.

## Acceptance Criteria (overall)

- One warm from an empty cache against a healthy loaded game reaches ~0
  failures for renderable defs with no manual re-runs.
- A client disconnect never aborts an in-flight warm or corrupts the manifest.
- Manifest/status always agree with PNGs on disk; partial runs don't lose
  success accounting.
- Failed defs can be recovered without a full re-warm; resolved defs stop
  showing as missing in the dashboard.
- No-world warms report faction icons as deferred, not errored.
- No new RIMAPI write surface; localhost-only; no pure-domain external deps.

## Sources To Verify During Implementation

- `Src/ApiHost/IconCacheService.cs`, `Src/ApiHost/Endpoints/IconEndpoints.cs`,
  `Src/ApiHost/RimBobOptions.cs`, `Src/GameStateSync/RimApiClient.cs`.
- Local fork `C:\dev\RIMAPI-for-RimBob` for image/faction endpoint behavior
  under load and no-world conditions.
- `Docs/design/dashboard.md` icon-cache contract.
