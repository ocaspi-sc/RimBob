# Preserve Willie Options When the Solver Is Offline (no-clobber guard)

> **Pivot from `advice-restore-from-replay-corpus`.** Reading the unlanded branch
> `codex/willie-offline-snapshot-advice` revealed a smaller fix than a corpus
> reader: stop **clobbering** the good options-bearing advice when the placement
> solver can't reach RIMAPI. With the clobber gone, the already-wired boot hydrate
> (`AdviceBus` ← `MinisterOutputStore`) keeps showing the last solved options
> offline. Salvage that branch's reusable down-detector + no-map fix instead of
> re-deriving them.

## Motivation

User goal: see Willie Build Queue → Proposed option cards with **no live
RimWorld/RIMAPI**. Options can only be *generated* live (the solver validates each
footprint via RIMAPI `blueprint-group/validate`), so offline we can only show the
**last solved + saved** options. Today they vanish: a cycle that runs while RIMAPI
is unreachable re-emits Willie's freezer advice **without** options and
`ReplaceMinisterAdvice` overwrites the good snapshot with the empty one. Fix =
don't overwrite when the solver is offline.

## Context

- Clobber path: solver `ValidateAsync` (live RIMAPI,
  [`PlacementSolver.cs:130`](../Src/Ministers/Willie/PlacementSolver.cs)) throws →
  `MinisterOfWillie.TrySolvePlacementAsync` ([`:132-154`](../Src/Ministers/Willie/MinisterOfWillie.cs))
  swallows all exceptions → `FromFailure` → no options → `RunPlayCycle` calls
  `PublishSnapshot` → `AdviceBus.ReplaceMinisterAdvice` clobbers + persists empty.
- Boot already hydrates `AdviceBus` from latest-only `MinisterOutputStore`
  ([`Program.cs:135`](../Src/ApiHost/Program.cs)); colony state + briefings already
  restore from their own snapshot
  ([`ColonySnapshotRestoreHostedService.cs:29`](../Src/ApiHost/ColonySnapshotRestoreHostedService.cs)).
  So preserving the last good Willie snapshot is enough — no new read path needed.
- **Salvage source — unlanded branch `codex/willie-offline-snapshot-advice`**
  (worktree `C:\Users\orca\.codex\worktrees\bd67\RimBob`; commits `7cd5eaa`,
  `fb94e87`). Port these self-contained pieces; **do not** merge the branch (its
  `CabinetCycle` change re-runs ministers offline, which still can't validate →
  no options + re-clobbers — explicitly rejected):
  - `Src/StateStore/RimApiConnectionFailure.cs` — `IsConnectionFailure(ex)` socket/
    message down-detector (new file, copy verbatim).
  - `Src/StateStore/RimApiLiveStateUnavailableException.cs` + `IngestionDispatcher`
    throwing `NoLoadedMap` instead of bare `InvalidOperationException` + the
    `ManualTriggerErrorResults` 503 `rimworld_map_not_loaded` mapping (also dedups
    the inline detector there to use the new helper).
  - `MinisterTraceStore.Complete(string minister, string? note = null)` — lets a
    cycle annotate why advice is stale.

## Scope

### S1 — Port the down-detector + no-map fix (salvage, low risk)
- Add `Src/StateStore/RimApiConnectionFailure.cs` verbatim from the branch.
- Add `Src/StateStore/RimApiLiveStateUnavailableException.cs` (enum `NoLoadedMap`
  + exception) from the branch.
- `IngestionDispatcher.RefreshAllAsync`: when no home map, throw
  `RimApiLiveStateUnavailableException(NoLoadedMap, …)` instead of
  `InvalidOperationException("RIMAPI returned no maps.")` (branch diff).
- `ManualTriggerErrorResults.TryMap`: call `RimApiConnectionFailure.IsConnectionFailure`
  (delete the inline copy) and add the `RimApiLiveStateUnavailableException` → 503
  `rimworld_map_not_loaded` branch (branch diff).
- `MinisterTraceStore.Complete`: add optional `note` param (branch diff).
- **Do not** port `IColonyStateRefresher`, the `CabinetCycle` offline re-run, or the
  `UsedRestoredSnapshot`/`CabinetTriggerResult` plumbing — those belong to the
  rejected restore-and-re-run approach.

### S2 — No-clobber guard in `MinisterOfWillie` (the fix)
- `TrySolvePlacementAsync` ([`:132`](../Src/Ministers/Willie/MinisterOfWillie.cs)):
  in the `catch`, branch on `RimApiConnectionFailure.IsConnectionFailure(ex) || ex
  is RimApiLiveStateUnavailableException`. On that → return an attempt flagged
  `SolverOffline = true` (add a bool to `PlacementSolveAttempt`). Other exceptions
  keep today's `FromFailure` (prose degrade) behavior unchanged.
- `RunPlayCycle` Decision branch ([`:32-65`](../Src/Ministers/Willie/MinisterOfWillie.cs)):
  when a placement request drove the cycle **and** the attempt is `SolverOffline`,
  **skip `PublishSnapshot`** for Willie this cycle — return early so the prior
  options-bearing `AdviceBus`/`MinisterOutputStore` snapshot is preserved. Still
  record a replay entry (so the corpus notes the offline cycle) and annotate the
  trace via `traces`/`MinisterTraceStore.Complete(note: "solver offline; preserved
  prior advice")`.
- If there is **no** prior snapshot (never solved online), skipping leaves Willie
  empty — same as today; acceptable (nothing to preserve).
- Leave the `Escalate` path unchanged; the offline freezer case still produces a
  `Decision` (the request is present), so the guard lives on the Decision path.

### S3 — Tests
- `MinisterOfWillieTests`: placement solve throws a simulated connection failure
  (fake validator throwing `HttpRequestException`/`SocketException`) **with** a
  prior options-bearing snapshot in the bus → after the cycle, the bus still
  serves the options (no clobber); assert `PublishSnapshot`/`ReplaceMinisterAdvice`
  was not called with empty. Non-connection solver failure → unchanged prose
  degrade. No-map → `RimApiLiveStateUnavailableException`.
- `IngestionDispatcher`/endpoint test: no maps → 503 `rimworld_map_not_loaded`
  (mirror any branch test worth keeping; do not import the `CabinetCycle` suite).
- Port only the branch tests that cover S1 pieces; skip `CabinetCycleTests` offline
  re-run cases (rejected behavior).

## Approach (Codex order)
1. Port S1 files from the branch worktree `C:\Users\orca\.codex\worktrees\bd67\RimBob`
   (read them there; copy the self-contained ones, apply the small edits). Build green.
2. Add `SolverOffline` to `PlacementSolveAttempt` + the `catch` branch (S2).
3. Add the skip-publish guard in `RunPlayCycle` + trace note.
4. Tests (S3). Run Verification.

## Verification
- `dotnet build C:\dev\RimBob` clean.
- `dotnet test` (Willie + ingestion suites) green, incl. the new no-clobber test.
- Targeted: the new `MinisterOfWillie` connection-failure test proves options
  survive a RIMAPI-down cycle.
- Manual/live (note, not CI): with RIMAPI **up**, run Willie once → options in
  Proposed. Stop RIMAPI, trigger Willie again → Proposed still shows the options
  (not the empty `LaneEmpty`); trace/snapshot notes "solver offline; preserved".

## Where to see it (dashboard)
Willie → Build Queue → **Proposed** lane (`/api/ministers/willie/snapshot`). After
one online solve, the option cards persist across offline cycles instead of
reverting to the empty placeholder. The minister trace note explains the stale
state.

## Out of scope / follow-ups
- **Corpus reader recovery** (`advice-restore-from-replay-corpus`, superseded by
  this for the go-forward case): only needed to recover options **already**
  clobbered without a fresh online cycle. Keep as an optional follow-up; not built
  here. After this lands, one online Willie cycle repopulates the good snapshot.
- **Offline briefings/state via snapshot restore on manual trigger** (the branch's
  `CabinetCycle` behavior): legitimate but separate concern (briefing tab offline),
  its own slice; not this.
- **Offline re-solve**: impossible without live RIMAPI validate; explicitly a non-goal.
- The branch `codex/willie-offline-snapshot-advice` is **superseded** — its useful
  files are ported here; its `CabinetCycle` offline re-run is dropped. Delete the
  branch/worktree after this lands (separate cleanup).

## Open questions
- None blocking. If `PublishSnapshot` is also reached via a non-placement Willie
  rule while offline, confirm the skip is scoped to the placement-driven path only
  (don't suppress unrelated Willie advice).

---

## Summary (landed 2026-05-31)

**Motivation.** Willie's Build Queue → Proposed lane emptied whenever a play cycle
ran while RIMAPI was unreachable: the placement solver's live validate threw, Willie
re-emitted freezer advice *without* options, and `ReplaceMinisterAdvice` clobbered
the good options-bearing snapshot. Options can only be *generated* live, so the goal
was to stop destroying the last solved ones.

**Context.** Pivoted from the corpus-reader idea (`advice-restore-from-replay-corpus`)
after reading the unlanded branch `codex/willie-offline-snapshot-advice`: a no-clobber
guard is smaller and the already-wired boot hydrate (`AdviceBus` ← `MinisterOutputStore`)
then keeps showing the last options offline. The branch's reusable down-detector
(`RimApiConnectionFailure`), no-map typed exception + 503 mapping, and
`MinisterTraceStore.Complete(note)` had **already landed on master** independently, so
this slice consumed them rather than re-porting. Recovering *already*-clobbered options
and restoring last-turn building_requests remain follow-ups
(`restore-minister-outputs-from-corpus`).

**Scope (shipped).**
- `MinisterOfWillie.TrySolvePlacementAsync`: connection-failure / `RimApiLiveStateUnavailableException`
  → `PlacementSolveAttempt.FromSolverOffline` (`SolverOffline = true`); other failures keep
  the prose-degrade path.
- `RunPlayCycle`: when the placement-driven attempt is `SolverOffline`, skip
  `PublishSnapshot` (early return) so the prior options-bearing snapshot is preserved;
  still records a replay entry (`Status: "offline"`) and annotates the trace
  ("solver offline; preserved prior advice").
- Guard is scoped to the placement path only (verifier-confirmed); Escalate / non-placement
  advice untouched.
- Tests: connection-failure + no-map preservation, non-connection degrade, no-map reason,
  503 `rimworld_map_not_loaded`; `InternalsVisibleTo` for the ApiHost test.

**How to verify (human).**
- Dashboard: Willie → Build Queue → **Proposed**. With RIMAPI up, run Willie once (options
  appear). Stop RIMAPI, trigger Willie again → options persist (not the empty `LaneEmpty`);
  trace shows "solver offline; preserved prior advice".
- Commands: `dotnet test Src\Tests\RimBob.Tests.csproj --filter FullyQualifiedName~RimBob.Tests.Willie` (114/114).
- Files: `Src/Ministers/Willie/MinisterOfWillie.cs` (guard), `Src/Tests/Willie/MinisterOfWillieTests.cs`.

**Codex run:** `20260531-223458-willie-options-preserve-offline` · branch
`codex/prompt-20260531-223458-willie-options-preserve-offline` · verifier Adherent: yes ·
landed commit `6d6d2d4` (squash `[codex] Land prompt run 20260531-223458-willie-options-preserve-offline`).
