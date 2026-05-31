# Restore Last-Saved Advice/Options from the Replay Corpus at Boot

> **Not a new "mode" — read what we already save.** Every play cycle already
> writes the full advice (incl. `options[]` + apply payloads), briefing, flags,
> and solver output to the append-only replay corpus
> (`logs/replay/{minister}-*.jsonl`, `MinisterReplayRecord` schema v2). Colony
> world state already restores from its own snapshot at boot. The one thing that
> does **not** survive a RIMAPI-down boot is **advice/options** — so the Willie
> Build Queue → Proposed lane is empty even though the options are sitting on disk.
> This slice surfaces them.

## Why Proposed is empty offline (root cause)

- The dashboard reads `/api/ministers/willie/snapshot` → `AdviceBus` active advice.
- `AdviceBus` is seeded **only** from `MinisterOutputStore`
  ([`Program.cs:135`](../Src/ApiHost/Program.cs)), which is **latest-only**
  (`MinisterOutputStore` doc: *"Latest-only durable output snapshots"*).
- When a cycle runs with RIMAPI down, the solver's live validate call throws →
  `TrySolvePlacementAsync` returns failure → Willie emits the freezer advice
  **without** `options[]` → `ReplaceMinisterAdvice` → `QueueAdviceSnapshot`
  **overwrites** the good options snapshot with a no-options one. The latest-only
  store now holds the empty version; on reboot `AdviceBus` hydrates that.
- The **good** options cycle still exists in the append-only corpus jsonl — but
  nothing reads the corpus back into `AdviceBus`.

Meanwhile colony state is fine offline: `ColonySnapshotRestoreHostedService`
([`ColonySnapshotRestoreHostedService.cs:29`](../Src/ApiHost/ColonySnapshotRestoreHostedService.cs))
calls `ColonyStateSnapshotStore.RestoreInto(colony)` at boot (guarded to skip when
a live source already exists), and briefings derive from `ColonyState`, so the
briefing/world tabs already render from the saved snapshot. **Only advice/options
lack the equivalent restore.**

## Goal

At boot, seed `AdviceBus` from the saved replay corpus so the **last-saved**
Willie options (and every other minister's last advice) render with no live
RimWorld/RIMAPI. Generic across ministers; mirrors the existing colony-state
restore one-for-one.

This shows **last-saved** options. It does **not** re-run the solver offline (that
needs live RIMAPI validate) and does **not** make Apply work offline (validate →
place need RIMAPI). View-only restore of work already generated and saved.

## Existing seams reused (little new code)

- **Read pattern:** `ReplayCorpusRawOutputReader`
  ([`ReplayCorpusRawOutputReader.cs:9`](../Src/Coordination/ReplayCorpusRawOutputReader.cs))
  already enumerates `logs/replay/{minister}-*.jsonl` newest-first and parses
  records — copy its shape for an advice reader.
- **Restore pattern:** `ColonySnapshotRestoreHostedService` — copy its boot
  hosted-service shape + the "skip if a live source already exists" guard.
- **Bus write:** `AdviceBus.ReplaceMinisterAdvice(minister, advice, stateSummary,
  chain, flags)` ([`AdviceBus.cs:51`](../Src/Coordination/AdviceBus.cs)) already
  exists and is exactly what a recorded `MinisterReplayRecord` carries
  (`Advice`/`StateSummary`/`Chain`/`Flags`).
- **Record shape:** `MinisterReplayRecord` (schema v2) fields `Advice`, `Flags`,
  `StateSummary`, `Chain`, `OutputKind`, `Output`
  ([`MinisterReplayRecorder.cs:33-55`](../Src/Coordination/MinisterReplayRecorder.cs)).

## Scope

### S1 — `ReplayCorpusAdviceReader`
New `Src/Coordination/ReplayCorpusAdviceReader.cs`, constructed with the same
`logs/replay` directory already registered at
[`Program.cs:148`](../Src/ApiHost/Program.cs). Mirror
`ReplayCorpusRawOutputReader`:
- `MinisterReplayRecord? LatestWithAdvice(string minister)` — enumerate
  `{minister}-*.jsonl` newest file→oldest, scan lines newest→oldest, deserialize
  `MinisterReplayRecord` with the **same** `JsonSerializerOptions` the
  `ReplayCorpusWriter` serializes with (snake_case; match the writer exactly).
- **Selection rule:** return the newest record whose `Advice` contains ≥1 item
  with non-empty `Options` (so the Willie Proposed lane is populated and the
  newer no-options RIMAPI-down record is skipped). If no record carries options,
  fall back to the newest record with non-empty `Advice`. If none, return null.
- Bound the scan like the raw reader (newest ~20 files; stop at first hit).

### S2 — `AdviceCorpusRestoreHostedService`
New `Src/ApiHost/AdviceCorpusRestoreHostedService.cs`, mirroring
`ColonySnapshotRestoreHostedService`. On `StartAsync`, for each registered
minister (reuse the minister registry / the same minister set the cabinet uses):
- If `AdviceBus` already has advice **with options** for that minister, skip
  (don't override a good live/seeded snapshot).
- Else read `ReplayCorpusAdviceReader.LatestWithAdvice(minister)`; if found, call
  `AdviceBus.ReplaceMinisterAdvice(record.Minister, record.Advice, record.StateSummary,
  record.Chain, record.Flags)`.
- Log a `LogWarning` like the colony restore: *"Restored {minister} advice from
  replay corpus captured_at=… options={n}; stale until first live cycle."*
- **Ordering:** must run **after** the `AdviceBus`/`MinisterOutputStore` hydrate
  (so it only fills gaps) and alongside `ColonySnapshotRestoreHostedService`.
  Register as an `IHostedService` after the colony restore.

### S3 — wire it in `Program.cs`
- Register `ReplayCorpusAdviceReader` on the existing `logs/replay` path (next to
  `ReplayCorpusRawOutputReader`, [`Program.cs:148`](../Src/ApiHost/Program.cs)).
- Register `AdviceCorpusRestoreHostedService` as an `IHostedService`.

### S4 — provenance (small, so the UI can say "saved, not live")
Surface that the advice is corpus-restored, not live, so the dashboard can badge
it (parallels `ColonyStateOrigin.Snapshot`). Minimal: include a
`restoredFromCorpus` + `capturedAt` field on the willie/minister snapshot
response (or the existing system/health colony-snapshot status block at
[`SystemEndpoints.cs:136`](../Src/ApiHost/Endpoints/SystemEndpoints.cs)). Frontend
badge is optional follow-up; just expose the flag here.

### S5 — tests
- `ReplayCorpusAdviceReader` unit: a temp `logs/replay/willie-*.jsonl` with two
  records (older = options, newer = no-options) → reader returns the **older**
  options record (selection rule). Empty/missing dir → null.
- `AdviceCorpusRestoreHostedService` unit: empty `AdviceBus` + corpus with options
  → after `StartAsync`, `AdviceBus` serves the Willie advice **with** options;
  guard test: pre-seeded bus-with-options is left untouched.

## Out of scope
- **Re-running the solver offline.** No replay-backed `IPlacementValidator`; this
  shows saved options, it does not regenerate them. (If live re-solve offline is
  ever wanted, that's a separate slice feeding recorded validations into a mock
  validator.)
- **Apply offline.** `validate`→`place` need live RIMAPI; corpus-restored options
  stay view-only and Apply returns `rimapi_unavailable` as today.
- **Runtime reload endpoint / mode toggle / scrubber timeline.** Boot-time restore
  only. A `POST /api/ministers/{m}/advice/restore-from-corpus` reload and a
  cycle-scrubber are possible later, not here.
- **MinisterOutputStore clobber hardening.** Optionally stop persisting a
  no-options snapshot over a good one — risky (can't tell legit-empty from
  transient), so left as a noted follow-up; the corpus restore sidesteps it.
- **Colony/world tabs + briefings.** Already restored via the colony snapshot — no
  work here; just confirm they render in the offline verify.
- **`willie-solver-tab`** (separate) surfaces the `PlacementSolverReplayOutput`
  trace; complementary, not this.

## Assumptions
- `ReplayCorpusWriter` is always registered ([`Program.cs:144`](../Src/ApiHost/Program.cs)),
  so the corpus exists whenever cycles have run. If the corpus is empty (never ran
  online), there is nothing to restore — expected; Proposed stays empty.
- The newest options-bearing record is the desired "last good" view. (A later
  pinned-cycle/scrubber selection is a separate enhancement.)
- Restored advice is replaced normally by the next live cycle — `ReplaceMinisterAdvice`
  already overwrites per minister, so going back online needs no extra teardown.

## Verification
- **Repro setup:** with RIMAPI **down**, restart the Host. Before this slice:
  `GET /api/ministers/willie/snapshot` → no `options[]`, Build Queue → Proposed
  shows the empty `LaneEmpty`. After: the snapshot carries the last-saved
  `willie_*` advice **with** `options[]`, Proposed renders the saved option cards.
- **Unit:** S5 tests green; `dotnet test` suite green.
- **Guard:** start the Host **online** (RIMAPI up) → a live cycle runs → restore
  service does not override the live advice (skip-if-present guard); options shown
  are the live ones.
- **Provenance:** offline snapshot reports `restoredFromCorpus=true` + `capturedAt`.

## Tasks.md
Linked from "Captured by /todo" as `advice-restore-from-corpus`.
