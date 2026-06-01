# Restore Last-Recorded Minister Outputs (Flags + Advice) from the Corpus at Boot

> **Absorbs the deferred `advice-restore-from-replay-corpus`** and adds the flag
> side the user asked for. Three runtime stores feed the cabinet/dashboard:
> `ColonyState` (restored at boot from its snapshot), `AdviceBus` (hydrated from
> latest-only `MinisterOutputStore`), and **`FlagChannel` (in-memory, restored
> from nothing)**. The cross-minister building_requests live in `FlagChannel`, so
> on restart — or on a Willie-only manual trigger where Chef didn't re-emit —
> Willie has no request to solve and Proposed goes empty. Every cycle already
> records those flags to `logs/replay/{minister}-*.jsonl`
> (`MinisterReplayRecord.Flags`). This slice replays the last-recorded flags (and,
> as a folded bonus, advice) back into the live stores at boot.

## Motivation (user framing)

"Willie should be able to use the request made last turn — it should be in the
logs." Correct: Chef's `building_request{ RequestedFrom: "Willie" }`
([Food/Rules.cs:285-298](../Src/Ministers/Food/Rules.cs)) is published to
`FlagChannel` and recorded in the corpus, but `FlagChannel` is in-memory only
([Program.cs:136](../Src/ApiHost/Program.cs)), so last turn's request is gone
after a restart and absent on a Willie-only trigger. Make the last-recorded
request durable so Willie can always solve against it.

## Context

- `FlagChannel` ([FlagChannel.cs](../Src/Coordination/FlagChannel.cs)): `Publish`
  upserts by `flag.Id`; `Active()` returns non-expired flags; `PruneExpired` drops
  `ExpiresAt <= now` (**wall-clock**). No load/save.
- Willie reads requests via `ActiveWillieBuildingRequests(flags.Active(), cycle.Flag)`
  ([MinisterOfWillie.cs:112-127](../Src/Ministers/Willie/MinisterOfWillie.cs)).
- Chef sets `ExpiresAt: now.AddHours(24)` on the request flag
  ([Food/Rules.cs:226](../Src/Ministers/Food/Rules.cs)) — wall-clock TTL.
- Corpus already records `Flags` and `Advice` per cycle
  ([MinisterReplayRecorder.cs:33-55](../Src/Coordination/MinisterReplayRecorder.cs)),
  read pattern in [ReplayCorpusRawOutputReader.cs](../Src/Coordination/ReplayCorpusRawOutputReader.cs).
- Restore-at-boot precedent: `ColonySnapshotRestoreHostedService`
  ([:12-36](../Src/ApiHost/ColonySnapshotRestoreHostedService.cs)) — load latest +
  `RestoreInto` + **skip if a live source already populated the store**.

## Scope

### S1 — `ReplayCorpusOutputReader` (shared reader)
New `Src/Coordination/ReplayCorpusOutputReader.cs`, on the existing `logs/replay`
dir. Mirror `ReplayCorpusRawOutputReader`:
- `MinisterReplayRecord? Latest(string minister, Func<MinisterReplayRecord,bool>? where = null)`
  — newest file→oldest, newest line→oldest, deserialize with the **same**
  `JsonSerializerOptions` the `ReplayCorpusWriter` uses; return the first match.
- Two callers below pass predicates: "has flags with building_requests" and "has
  advice with options".

### S2 — Restore flags into `FlagChannel` at boot  *(the user's ask)*
New `Src/ApiHost/FlagCorpusRestoreHostedService.cs`, mirroring the colony restore:
- For each cabinet minister, read `Latest(minister, r => r.Flags has any flag with
  non-empty BuildingRequests)`; for each such recorded flag, `flags.Publish(flag)`.
- **TTL on restore:** a flag recorded >24h ago would be pruned immediately by the
  wall-clock `ExpiresAt`. On restore, **re-stamp** `ExpiresAt` to `now + remaining`
  (or a fixed restore-grace window, e.g. `now.AddHours(24)`), so the last-known
  request survives the reboot. Keep the flag `Id` stable so a later live re-emit
  upserts (no duplicate).
- **Skip-if-live guard:** if `FlagChannel.Active()` already holds a building_request
  for that minister (live cycle ran first), don't restore. Log a warning like the
  colony restore.
- Net: after restart, `ActiveWillieBuildingRequests` sees last turn's freezer
  request → Willie solves (live RIMAPI) or preserves (offline guard slice).

### S3 — Restore advice/options into `AdviceBus` at boot  *(folds the deferred plan)*
New `Src/ApiHost/AdviceCorpusRestoreHostedService.cs` (or one combined restore
service with S2): for each minister, if `AdviceBus` has no options-bearing advice,
read `Latest(minister, r => r.Advice has any item with non-empty Options)` and
`ReplaceMinisterAdvice(record.Minister, record.Advice, record.StateSummary,
record.Chain, record.Flags)`. Skip-if-present guard. Recovers options already
clobbered before the `willie-options-preserve-offline` guard lands.

### S4 — provenance
Mark restored flags/advice as corpus-sourced (parallels `ColonyStateOrigin.Snapshot`):
expose a `restoredFromCorpus` + `capturedAt` on the willie/minister snapshot or the
system/health block ([SystemEndpoints.cs:136](../Src/ApiHost/Endpoints/SystemEndpoints.cs))
so the dashboard can badge "last-known, not live." Frontend badge optional follow-up.

### S5 — wire + tests
- Register `ReplayCorpusOutputReader` + the restore hosted service(s) in
  `Program.cs`, ordered **after** the existing `AdviceBus`/colony hydrate.
- Tests: corpus with an older request-bearing record → after boot,
  `FlagChannel.Active()` yields the request (TTL re-stamped, not pruned); guard
  test: live request present → restore skipped. Same shape for advice/options (S3).
  Reader unit: newest-matching-record selection + empty/missing dir → null.

## Verification
- `dotnet build` clean; `dotnet test` green incl. new restore tests.
- Live: with RIMAPI up, run a cabinet cycle (Chef emits freezer request, Willie
  solves). Stop Host. Start Host with RIMAPI still **down**. `GET
  /api/ministers/willie/snapshot` / a Willie-only trigger → Willie has last turn's
  request (from restored flag) and shows the last options (from S3 / preserved).

## Where to see it (dashboard)
Willie → Build Queue → **Requested** lane (restored building_request) and
**Proposed** lane (restored options). Both survive a restart instead of emptying.

## Out of scope / notes
- **Wall-clock vs game-tick TTL** on request flags is a latent fragility (a paused
  game ages out the request in real time). This slice re-stamps on restore but does
  **not** convert TTL to game-tick — that's a separate cleanup; flag it.
- **Offline re-solve** stays impossible (needs live RIMAPI validate); restored
  options are the last solved ones. Complements `willie-options-preserve-offline`
  (don't clobber going forward) — this recovers/seeds at boot.
- Apply stays live-only.

## Open questions
- One combined restore hosted service vs two (flags + advice)? Lean one service,
  two passes, shared reader.
- Restore grace window value for the TTL re-stamp (fixed 24h vs preserve original
  remaining)? Default: re-stamp to `now + 24h` for request-bearing flags.

---

## Summary (landed 2026-05-31)

**Motivation.** "Willie should be able to use the request made last turn — it should be
in the logs." `FlagChannel` was in-memory only, so Chef's `building_request` died on
restart and was absent on a Willie-only manual trigger. Now the last-recorded flags
(and advice/options) are restored from the replay corpus at boot.

**Context.** Third in the offline trio: `ColonyState` already restored at boot,
`willie-options-preserve-offline` stops clobbering advice going forward, and this
restores flags + advice from the corpus at boot. Mirrors
`ColonySnapshotRestoreHostedService` (load latest + skip-if-live guard). Absorbed the
deferred `advice-restore-from-replay-corpus`.

**Scope (shipped).**
- `ReplayCorpusOutputReader` (async): newest corpus record per minister matching a
  predicate; deserializes with the writer's `JsonSerializerOptions`.
- `MinisterCorpusRestoreHostedService`: flag restore into `FlagChannel` (TTL re-stamp
  to `now + 24h` so the wall-clock `ExpiresAt` doesn't immediately prune; skip-if-live
  guard) + advice/options restore into `AdviceBus` (skip-if-present guard). Runs at boot
  before `DayTickOrchestrator`.
- `CorpusRestoreStatusStore` + `/api/system/health` `corpus_restore` provenance
  (`restoredFromCorpus` / `capturedAt` / `restoredCount` per minister per lane).
- Async file I/O (`File.ReadAllLinesAsync`) per AGENTS.md; sibling
  `ReplayCorpusRawOutputReader` left as a follow-up.

**How to verify (human).**
- Dashboard: Willie → Build Queue → **Requested** (restored `building_request`) +
  **Proposed** (restored options) survive a restart. `GET /api/system/health` →
  `corpus_restore` block shows provenance.
- Commands: `dotnet test Src\Tests\RimBob.Tests.csproj` (493/493).
- Files: `Src/ApiHost/MinisterCorpusRestoreHostedService.cs`,
  `Src/Coordination/ReplayCorpusOutputReader.cs`.

**Follow-ups.** Wall-clock→game-tick TTL on request flags (flagged latent); async-ify
`ReplayCorpusRawOutputReader` (sibling, left untouched).

**Codex run:** `20260531-231406-restore-minister-outputs-from-corpus` · branch
`codex/prompt-20260531-231406-restore-minister-outputs-from-corpus` · commits `61468aa`,
`d300dc0`, `df86410` · verifier Adherent: yes · full suite 493/493 · landed commit
`1af8790` (squash `[codex] Land prompt run 20260531-231406-restore-minister-outputs-from-corpus`).
