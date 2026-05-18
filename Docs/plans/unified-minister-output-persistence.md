# Unified Minister Output Persistence

## Goal

Persist every minister's player-facing output — the Mayor's `MayorAgenda` and
each feeder's advice snapshot — through one mechanism, reload it on Host
startup, and **stop regenerating output just because the Host was rebuilt**.
The dashboard should show the last good output immediately after a restart, the
same way the recently-landed latest-colony-state snapshot does, instead of going
blank until the next cabinet cycle.

This folds the Mayor in as an ordinary snapshot producer and retires the
Agenda's privileged subsystem (dedicated store, boot-fire, bootstrap, prior-
output continuity), while keeping its **structured** payload intact.

## Why (decision trail)

- Today the Agenda is durable (`AgendaStore` → `var/agenda/agenda-store.json`,
  current + 30-version ring, atomic write, reloaded in `Program.cs` before
  endpoints). `AdviceBus` is pure in-memory, so feeder cards / state summaries /
  chains blank out on every Host restart until the next cabinet cycle.
- The asymmetry was justified by the Mayor's output being self-referential
  (`update_notes`, carried-forward bullet ids, NEW/UPDATED deltas need the prior
  agenda as prompt input).
- That justification does not survive scrutiny: the dashboard is an
  inspect/debug surface the player dips into for the *current* read, not a plan
  tracked day-over-day; cadence is irregular (day-rollover + manual), not a
  clean daily rhythm; and the anti-churn benefit is not Mayor-specific —
  feeders damp churn with rules-first + stable issue ids, not by re-reading
  their prior output. So Mayor continuity is dropped, not relocated.

## Design Decisions Captured

- **One persistence mechanism, keyed by minister.** Generalize the proven
  `AgendaStore` pattern (schema-versioned snapshot, atomic temp-file +
  rename, mutation gate, load-before-endpoints) into a store that holds the
  latest snapshot per minister. The Mayor is just another producer in it.
- **Mayor output stays structured.** `MayorAgenda` (posture, per-category
  `state_of_the_union`, short/long-term bullets, `cabinet_direction`,
  `guide_citations`) is persisted as a typed payload — **not** flattened to
  free text, **not** coerced into `AdviceItem`. Same principle as
  latest-colony-state: persist the structure, don't untype it.
- **Mayor becomes stateless, like feeders.** It no longer receives its prior
  agenda as prompt input. No prior-output-in-prompt wiring.
- **Deltas become cosmetic, not architectural.** `update_notes` /
  NEW/UPDATED/completed/deferred badges are either dropped or computed
  client-side by diffing two snapshots by stable id (exactly how feeder cards
  already behave). They are no longer a server-side input.
- **`cabinet_direction` is the one load-bearing carry-over.** It must survive
  as a field feeders read from the Mayor's latest persisted snapshot. This is
  the sanctioned Mayor→feeder channel (no direct minister-to-minister comms);
  it must not be dropped on the floor.
- **No boot-time regeneration.** Reload persisted snapshots before endpoints/SSE
  are exposed. Do **not** fire the cabinet/Mayor cycle on Host start just to
  populate the dashboard. Regenerate only on real triggers (day rollover,
  flags, manual `/api/cabinet/trigger`). This reverses the "Mayor wakes on Host
  startup" decision.
- **The replay corpus stays the deep history record.** `logs/replay/*.jsonl`
  already append-records every minister output. The unified store only needs
  *latest* per minister; it is not the system of record for history.
- **Crash-loss of the newest snapshot is acceptable.** Inspect-first dashboard:
  if a hard crash loses the in-flight snapshot, the prior persisted one shows
  until the next cycle. This relaxes the agenda's persist-before-publish
  ordering and lets the writer be a serialized background flush (see Phase 3).

## Scope

| Store | In scope? | Treatment |
|---|---|---|
| `AgendaStore` / `MayorAgenda` | **Yes** | Folded into unified store as the `mayor` producer; privileged subsystem retired |
| `AdviceBus` active advice + per-minister state summaries + chains | **Yes** | Persisted + reloaded via unified store |
| `MinisterTraceStore` | Deferred | Debug-only; history already in replay corpus. Fast-follow using same store if wanted |
| `RawLlmOutputStore` | Deferred | History already in replay corpus; latest is debug-only |
| `FlagChannel` | Deferred | Inter-minister signal regenerated each cabinet cycle; narrow restart window; not player-facing output |
| `MayorStatus`, `SseDiagnostics` | Out | Intentionally ephemeral run/session state — must NOT persist |
| Feedback / pushback | Out | M5, schema-only today |

## Phases

### Phase 1 — Unified snapshot store

1. Introduce a `MinisterOutputStore` (working name) generalizing
   `AgendaStore`: per-minister **latest snapshot only**, schema-versioned
   envelope, atomic temp+rename write, `LoadAsync`, mutation gate. No version
   ring — the replay corpus is the history record (Decision 1).
2. Payload is a tagged union / per-minister typed record: `mayor` →
   `MayorAgenda`; feeder → the existing `AdviceSnapshot` shape (advice,
   `state_summary`, `chain`).
3. **Per-minister files** under `var/ministers/<name>.json` (Decision 3),
   gitignored. Corruption is isolated to one minister. `AgendaStore`'s
   `var/agenda/agenda-store.json` is replaced (no back-compat shim — "No
   legacy"); a one-time read of the old path to seed `mayor` on first load is
   acceptable but not a retained code path.
4. Schema version starts at 1 with an explicit load-time mismatch error
   (mirror `AgendaStore`).

### Phase 2 — Fold the Mayor in

1. `Mayor.cs`: stop reading the previous agenda; remove
   `previous?.Version` prompt/diagnostic inputs; stop authoring
   server-side `update_notes` as a diff (keep field optional or drop).
2. Keep `MayorAgenda` producing the structured payload; route it through the
   unified store instead of `AgendaStore.UpdateAsync`.
3. Preserve `cabinet_direction`: confirm the feeder read path pulls it from
   the Mayor's latest persisted snapshot via the unified store.
4. `MayorAgenda.Version` is retained as an **opaque monotonic generation
   counter** (Decision 4) — used only for SSE event id and status reporting,
   with no "diff index" meaning. `update_notes` is dropped server-side.

### Phase 3 — AdviceBus persistence + concurrency

1. `AdviceBus.ReplaceMinisterAdvice` / `Publish` / `RemoveAppliedAction`
   currently mutate under a lock and are called **synchronously** from
   `MinisterOfFood.cs` and `MinisterEndpoints.cs` (no `await`). Persistence
   must not change those call sites' signatures.
2. Use a single-writer serialized flush: snapshot under lock → enqueue →
   background writer persists outside the lock (debounce/coalesce rapid
   updates). Crash-loss of the newest snapshot is accepted (see decisions).
3. SSE replay-on-connect (`AgendaStreamEndpoint`) is unchanged in wire format;
   it now replays from reloaded-from-disk state.

### Phase 4 — Boot path

1. `Program.cs`: load the unified store before endpoints/SSE are mapped
   (replace the `AgendaStore.LoadAsync` call; hydrate `AdviceBus` from it).
2. Remove the boot-time Mayor/cabinet fire in `DayTickOrchestrator` (keep day
   rollover + manual trigger).
3. `AgendaBootstrapHostedService`: keep a clearly-labeled bootstrap **only**
   when there is zero persisted output at all (genuine first run on a fresh
   colony). A rebuild has a persisted snapshot, so it reloads — no
   regeneration, no bootstrap (Decision 2).

### Phase 5 — Endpoints & dashboard (breaking migration)

Decision 5: **uniform-only, no back-compat aliases** ("No legacy"). All
`/api/agenda/*` consumers move in the same change.

1. Replace the endpoint surface:
   - `GET /api/agenda/latest` → `GET /api/ministers/{minister}/snapshot`
     (returns the typed payload: `MayorAgenda` for `mayor`, `AdviceSnapshot`
     for feeders).
   - `POST /api/agenda/manual` → `POST /api/ministers/mayor/snapshot/manual`,
     aligned with the existing `/api/ministers/{minister}/llm-output/manual`
     convention.
   - `GET /api/agenda/history` is dropped; if a history view is still wanted,
     expose `GET /api/ministers/{minister}/history` backed by a replay-corpus
     read (not the store — Decision 1).
   - Delete `/api/agenda/*` entirely; no aliases.
2. Reconcile `agenda_version` fields in `/api/status` and `/api/system` with
   the rename (e.g. `mayor_snapshot_version`); update those payloads in the
   same change.
3. SSE `/api/advice/stream` is unaffected in wire format (separate from the
   REST surface); it now replays reloaded-from-disk state.
4. Dashboard API clients + components that call `/api/agenda/*` are rewritten
   in the same change. Deltas/badges become client-side cosmetic (diff two
   snapshots by stable id) or are removed; no structural change to card
   rendering.
5. Add/adjust SYSTEM-panel visibility so the unified store's per-minister
   `persisted_at` / generation is inspectable (dashboard reflects exact state).

### Phase 6 — Docs (same-turn at implementation time)

Per AGENTS.md Documentation Discipline, the implementing change must update:

- `Docs/design/agenda.md` — Agenda is no longer a privileged durable subsystem
  or self-referential; it is a structured snapshot from one producer.
- `Docs/design/advice.md` — active advice surface is now durable across restart.
- `Docs/design/state-store.md` — document the unified output store alongside
  aggregates.
- `Docs/design/dashboard.md` — reload-on-boot, no boot regeneration, deltas
  cosmetic, new endpoint surface.
- `Docs/design/RimAPI.md` — endpoint catalogue: remove `/api/agenda/*`, add
  `/api/ministers/{minister}/snapshot` (+ `/snapshot/manual`, optional
  `/history`); reconcile `/api/status` + `/api/system` field rename.
- `Docs/ROADMAP.md` — note the persistence/lifecycle change if it touches a
  milestone.
- `Docs/DESIGN.md` decision log — append entries reversing/superseding:
  "Mayor's output is the Agenda…", "Mayor Agenda current/history are durable
  Host runtime state", "Missing Mayor Agenda initializes from a labeled server
  bootstrap", "Mayor wakes on Host startup, not only on day rollover", and the
  retired demand-trigger/agenda-route line; record the breaking
  `/api/agenda/*` → `/api/ministers/{minister}/*` migration. Append; do not
  delete prior entries.

### Phase 7 — Tests & verification

1. Round-trip: persist → reload reproduces Mayor + feeder snapshots.
2. Schema-version mismatch fails load loudly (parity with `AgendaStore`).
3. Restart simulation: dashboard/SSE serve last snapshots with **no** cabinet
   cycle fired on boot.
4. `cabinet_direction` survives reload and is visible to the feeder read path.
5. Concurrency: rapid `ReplaceMinisterAdvice` calls coalesce; no await added to
   minister cycle; no torn files.
6. Existing `AgendaStore` / `AdviceBus` tests migrated, not deleted.
7. Build + dotnet test + dashboard build; run via `run-rimbob.ps1`; confirm
   `/api/health`, reload behavior, and that a rebuild does not regenerate.

## Resolved Decisions

1. **History depth → latest-only.** The unified store keeps only the newest
   snapshot per minister. The replay corpus remains the sole history record;
   any history view reads the corpus.
2. **Empty genuine-first-run UX → labeled bootstrap.** Keep a clearly-labeled
   conservative bootstrap snapshot only when zero output is persisted. Rebuilds
   reload and never regenerate.
3. **Storage layout → per-minister files** (`var/ministers/<name>.json`).
   Corruption isolated per minister.
4. **`MayorAgenda.Version` → kept as an opaque monotonic counter.** SSE
   event id + status reporting only; no diff-index meaning. `update_notes`
   dropped server-side.
5. **Endpoint surface → uniform-only, breaking.** Delete `/api/agenda/*`; no
   back-compat aliases. All consumers (dashboard clients, status/system
   fields, endpoint docs) migrate in the same change.

## Non-Goals

- No autonomy/feedback/pushback work (M5).
- No flattening Mayor output to free text.
- No persistence of ephemeral run/session state (`MayorStatus`,
  `SseDiagnostics`).
- Not making the unified store the history system of record (replay corpus
  keeps that role).
