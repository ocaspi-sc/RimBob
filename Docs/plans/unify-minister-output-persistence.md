# Unify Minister Output Persistence

## Goal

One persistence system for every minister's player-facing output (Mayor + feeders), reloaded on Host startup, so a Host rebuild/restart shows the **last known** advice and agenda instead of a blank dashboard or an auto-regenerated cycle.

Three agreed goals:

- **A.** One unified persisted snapshot store for all minister output.
- **B.** No auto-regeneration on Host rebuild — reload the last persisted output instead. Same idea as serving last-known colony/briefing state rather than recomputing it on boot.
- **C.** Collapse the Agenda's privileged-subsystem special-casing.

This is a refactor the user explicitly requested. It is medium+; this doc is the plan, not the implementation.

## Design Decisions Captured

These were settled in design discussion before this plan:

1. **The Mayor is a producer in the unified pipeline, not a privileged "Agenda" subsystem.** The conceptual privilege is removed; the persistence machinery is shared.
2. **Mayor output stays a *structured* payload.** Keep the `MayorAgenda` shape (posture, per-category `state_of_the_union`, short/long-term bullets with stable ids + status, `cabinet_direction`, `update_notes`, `guide_citations`). It is **not** flattened to free text and **not** forced into `AdviceItem`. Rationale: the structure is already load-bearing for the dashboard, and the latest-colony-state direction persists *structure*, not untyped text.
3. **Continuity is a prompt-context property, not a storage-architecture property.** The Mayor's "what changed since last time" narrative (`update_notes`, carried-forward bullet ids, NEW/UPDATED/completed/deferred badges) is produced by feeding the Mayor its **previous persisted output** as prompt context. This needs only *latest-1*, not a version ring. `Mayor.cs` already receives a `previous` agenda; this plan formalizes that as "read last persisted snapshot" and removes the bespoke store that was justifying it.
4. **`cabinet_direction` must survive as a field feeders read from the Mayor's latest persisted snapshot.** This is the sanctioned Mayor→feeder coordination channel (no direct minister-to-minister messaging). It is the one thread that must not be dropped by the refactor.
5. **Historical output already has a durable home: the replay corpus** (`logs/replay/*.jsonl`, append-only, `schema_version: 2`). The unified store is the *active / last-known* surface, **not** the system of historical record. We are not duplicating history into the active store.
6. **Mechanism precedent is `AgendaStore`.** It already does atomic temp+rename writes, a `schema_version` guard, `LoadAsync` before endpoints come up, a `SemaphoreSlim` mutation gate, and persist-before-swap. Generalize this; do not reinvent it.
7. **Concurrency constraint.** `AdviceBus.ReplaceMinisterAdvice` is **synchronous** and called from minister play cycles (`Src/Ministers/Food/MinisterOfFood.cs:151`, `Src/ApiHost/Endpoints/MinisterEndpoints.cs:228`). Persistence must not `await` inside the bus lock and must not force an async signature ripple into minister code.

## Decision-Log Impact (update at implementation time)

This refactor refines several `Docs/DESIGN.md` decisions. The implementer must append (never delete) decision-log entries reflecting:

- *"Mayor's output is the Agenda, not a daily_digest AdviceItem"* — **not reversed.** `MayorAgenda` stays a structured payload; it is just no longer a privileged subsystem.
- *"Mayor Agenda current/history are durable Host runtime state"* — **generalized** to all minister output via one shared store.
- *"Mayor wakes on Host startup, not only on day rollover"* — **refined.** The original rationale was "don't leave the dashboard blank for ~24 in-game minutes." Reloading last-known persisted output satisfies that rationale *better* (instant, real content) without a boot-time cabinet cycle. Frame this as a superior solution to the same problem, not a regression.
- *"Missing Mayor Agenda initializes from a labeled server bootstrap"* — **narrowed** to true cold start only (no persisted snapshot has ever existed).
- *"Active feeder advice is replaced by minister snapshots; decision-log persistence remains the place for historical advice"* — **reinforced**, not changed.

## Open Decision (pin before Phase 3)

**History depth in the unified store.** Two options:

- **Latest-only (recommended).** The active store keeps just the most recent snapshot per minister. Deltas need only latest-1; the replay corpus is the durable history; a dashboard "agenda history" view, if kept, reads the replay corpus. Smallest, most consistent, bounded disk.
- **Uniform bounded ring.** Every minister (incl. Mayor) gets an N-deep version ring like today's 30-deep agenda ring. Consistent across ministers but duplicates what the replay corpus already stores.

Recommendation: latest-only for the active store. Confirm before Phase 3 because it determines the snapshot schema and the fate of `/api/agenda/history`.

## Phase 1 — Shared persistence primitive

Status: Not started.

1. Extract `AgendaStore`'s proven mechanism into a reusable snapshot store:
   - Atomic temp-file write + `File.Move(overwrite: true)`.
   - `schema_version` field with a hard load-time guard.
   - `LoadAsync(path)` returning an empty store when the file is absent.
   - `SemaphoreSlim` mutation gate; persist-before-swap ordering.
2. Keep it payload-agnostic (generic over the persisted record type) so Mayor and feeder payloads reuse it.
3. Tests: round-trip, absent-file, schema-mismatch rejection, atomic-write leaves no partial file on failure, concurrent mutation serialization.

## Phase 2 — Persist feeder advice (AdviceBus)

Status: Not started.

1. Persist, per minister: active advice list, `state_summary`, and `chain` (`AdviceChainModel`). One file per minister or one keyed file under `var/` (decide; per-minister files match the per-minister snapshot model and are independently debuggable).
2. Load persisted snapshots into `AdviceBus` during `LoadAsync` at startup, **before** endpoints/SSE are mapped (mirror the `AgendaStore.LoadAsync` call site in `Src/ApiHost/Program.cs:25`).
3. Concurrency: keep `ReplaceMinisterAdvice` synchronous. Snapshot the new state under the lock, then hand it to a **serialized background flush** (single-writer queue) outside the lock. No `await` in the lock; no minister-cycle signature change. Document the durability/lag tradeoff (a crash within the flush window loses the last snapshot — acceptable; replay corpus still has it).
4. Tests: snapshot persists and reloads; per-minister isolation; expired-advice pruning still correct after reload; empty snapshot persists (a successful empty state is meaningful).

## Phase 3 — Fold Mayor/Agenda into the unified pipeline

Status: Not started. Depends on the Open Decision.

1. Persist `MayorAgenda` through the shared primitive instead of the standalone `AgendaStore`. Either delete `AgendaStore` or reduce it to a thin typed adapter over the shared store (prefer deletion — repo policy is "No legacy"; keep an adapter only if endpoint churn would otherwise be large).
2. Formalize continuity-via-context: the Mayor's prompt builder reads the **last persisted Mayor snapshot** as the `previous` input. This replaces the implicit dependency on the bespoke store; behavior the Mayor already has via `previous` is preserved.
3. Preserve `cabinet_direction`: feeders continue to read it from the Mayor's latest persisted snapshot (now via the unified store).
4. Tests: second Mayor cycle still produces `update_notes` and carried-forward ids from the persisted previous snapshot; failed Mayor run still falls back to the last persisted snapshot.

## Phase 4 — Boot behavior (Goal B)

Status: Not started.

1. `DayTickOrchestrator.ExecuteAsync` currently fires `_runCycle(PlayCycleContext.StartupBootstrap, …)` on the first poll (`Src/Coordination/DayTickOrchestrator.cs:65-72`). Stop firing a cabinet/Mayor cycle **solely to populate the dashboard**. The dashboard is populated by reload-from-disk instead.
2. Regenerate only on real triggers: day rollover, flag fired, or manual `POST /api/cabinet/trigger`.
3. `AgendaBootstrapHostedService`: keep only as the true cold-start path (no persisted snapshot has ever existed). It must not run when a persisted snapshot is reloaded.
4. Preserve the `PlayCycleContext.StartupBootstrap` enum and its semantics for the genuine first-ever run; just don't trigger it when last-known state exists.
5. Tests: boot with persisted state ⇒ no cycle fires, dashboard serves last-known immediately; boot with no persisted state ⇒ cold-start bootstrap path runs once.

## Phase 5 — Endpoints & SSE

Status: Not started.

1. `/api/agenda/latest` and `/api/agenda/manual` keep working (back the latest endpoint with the unified store). `/api/agenda/history` fate follows the Open Decision (drop, or back it with the replay corpus).
2. SSE `/api/advice/stream` replay-on-connect already replays `AgendaStore.Current` + `AdviceBus.ActiveSnapshot()`; once both are persisted+reloaded this automatically serves last-known after a rebuild with no code change to the stream handler.
3. Dashboard: confirm it tolerates the `/api/agenda/history` decision; adjust the client only if that endpoint changes.

## Phase 6 — Documentation

Status: Not started. Per `AGENTS.md` doc routing, update in the same turn as the code:

- `Docs/design/agenda.md` — Mayor is a producer in the unified store; continuity is prompt-context, not a subsystem.
- `Docs/design/advice.md` — the active advice surface is persisted/last-known and reloaded on boot.
- `Docs/design/state-store.md` — add a "minister output persistence" section describing the shared primitive.
- `Docs/design/dashboard.md` — boot shows last-known output; no boot-time regeneration.
- `Docs/DESIGN.md` — append decision-log entries per "Decision-Log Impact" above.
- `Docs/ROADMAP.md` — note if any milestone wording assumed the boot-fire behavior.

## Phase 7 — Verification

Status: Not started.

1. Stop any running `RimBob.Host` if DLLs are locked; backend build; backend tests; dashboard build.
2. Run via `.\run-rimbob.ps1`; confirm `/api/health` OK.
3. The decisive check: with a populated `var/`, stop and **rebuild** the Host, restart it, and confirm the dashboard shows the prior agenda **and** feeder advice immediately, with **no** cabinet/Mayor cycle in the logs until a real trigger.
4. Confirm a true cold start (empty `var/`) still produces a labeled bootstrap and one initial cycle.

## Data Migration

Existing `var/agenda/agenda-store.json` (`schema_version: 1`) must not be lost on upgrade. On first boot under the new code, load the old agenda snapshot and convert it into the unified store, then proceed. This is one-time data migration, not a code legacy shim — acceptable under "No legacy". The replay corpus is untouched.

## Out Of Scope

- Feedback / Pushback (M5; `FeedbackEvent` is schema-only today).
- Auto epic, HTN, Labor.
- Multi-map.
- Any change to what the Mayor or feeders *reason about* — this is a persistence/lifecycle refactor only.

## Open Implementation Questions

- History depth (the Open Decision) — latest-only vs uniform bounded ring.
- Delete `AgendaStore` outright vs keep a thin adapter to minimize endpoint churn.
- `/api/agenda/history` — drop it, or re-implement over the replay corpus.
- One file per minister under `var/advice/` vs a single keyed snapshot file.
- Final concurrency mechanism for the feeder flush: serialized background writer (recommended) vs making the persist the caller's responsibility before publish.
