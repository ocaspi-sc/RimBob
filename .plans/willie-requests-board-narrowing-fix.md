# Willie → Requests board collapses to one row (build-queue / requests count mismatch)

**Status:** PLANNED — not started. Design-only; awaiting "land it" before Codex executes.
**Owner:** Willie (Construction minister).
**Scope (recommended Option A):** `Src/Ministers/Willie/MinisterOfWillie.cs`, `Src/Tests/Willie/MinisterOfWillieTests.cs`. No wire/persistence/dashboard change.

---

## Symptom

- **Willie → Build Queue → Requested** lane shows **6** items.
- **Willie → Requests** tab shows **1** item ("starter freezer").

Both lanes claim to list "inbound building requests aimed at Willie", so they should agree.

---

## Why the two surfaces disagree (different data sources)

| Surface | Reads | Path |
| --- | --- | --- |
| Build Queue → **Requested** | **All active flags**, client-side | `buildRequestCards(flags)` → every flag's `building_requests` filtered to `requested_from === 'Willie'` — [MinisterBuildQueueView.tsx:339](../Dashboard/src/components/minister/MinisterBuildQueueView.tsx#L339) |
| **Requests** tab | Backend **solver request board** | `fetchSolverRequests` → `GET /api/ministers/willie/solver/requests` → `WillieSolverStore.RequestBoard` — [MinisterRequestsView.tsx:29](../Dashboard/src/components/minister/MinisterRequestsView.tsx#L29), [MinisterEndpoints.cs:229](../Src/ApiHost/Endpoints/MinisterEndpoints.cs#L229) |

The Build Queue reads the live flag set directly (6 active Willie-aimed requests). The Requests tab reads the server-recorded board, which has collapsed to 1.

---

## Root cause — `ActiveWillieBuildingRequests` early-return narrows the board

The board is recorded each Willie cycle from `ActiveWillieBuildingRequests(activeFlags, cycle.Flag)`:

```csharp
// MinisterOfWillie.cs:174
private static IReadOnlyList<BuildingRequest> ActiveWillieBuildingRequests(
    IReadOnlyList<AgentFlag> activeFlags,
    AgentFlag? directFlag)
{
    IReadOnlyList<BuildingRequest>? directRequests = directFlag?.BuildingRequests?
        .Where(IsRequestedFromWillie)
        .ToList();
    if (directRequests is { Count: > 0 })
        return directRequests;          // ← EARLY RETURN: only the triggering flag's requests

    List<AgentFlag> flagsToRead = [.. activeFlags];
    if (directFlag is not null && flagsToRead.All(f => !string.Equals(f.Id, directFlag.Id, ...)))
        flagsToRead.Add(directFlag);
    return flagsToRead
        .SelectMany(flag => flag.BuildingRequests ?? [])
        .Where(IsRequestedFromWillie)
        .ToList();                       // union of all active flags (correct breadth)
}
```

When `directFlag` (the triggering flag) carries ≥1 Willie request, the method **returns only that flag's requests** and ignores every other active flag's Willie requests.

That narrowed list becomes `inboundBoard`, which is handed to `solverStore.RecordInbound(Name, inboundBoard)` — and `RecordInbound` **replaces** the whole board (`_board[minister] = snapshot` — [WillieSolverStore.cs:40](../Src/Ministers/Willie/WillieSolverStore.cs#L40)).

### Why it ends at exactly 1 (the per-flag invocation + last-write-wins)

The cabinet runs Willie **once per Willie-request flag**, not once per cycle:

```csharp
// CabinetCycle.cs:345 — RunWillieBuildingRequestFollowUpsAsync
foreach (AgentFlag requestFlag in requestFlags)
{
    PlayCycleContext requestCycle = new(
        PlayCycleTrigger.FlagFired,
        Flag: requestFlag,                 // ← directFlag = this one flag
        ...);
    await RunResolvedMinisterAsync(minister, willie, requestCycle, ...);
}
```

So each per-flag run hits the early-return, records a board of just **that** flag's request, and `RecordInbound` overwrites the previous board. After all per-flag runs, the board holds **only the last flag's request** → the Requests tab shows 1 ("starter freezer" happened to run last). The Build Queue, reading flags directly, still shows all 6.

### Conflict with intent

This early-return defeats the landed `willie-requests-tab` / `willie-rules-allhits-board-solve` design, whose stated goal was to *"record the inbound request **board** every cycle"* and *"solve the board"* so the Requests tab lists **every** inbound request. The narrowing is a regression against that intent.

> Note: existing `MinisterOfWillieTests` never caught this because they trigger with `PlayCycleContext.ManualTrigger` (`directFlag == null`), which skips the early-return and exercises only the union branch. The bug fires only on `FlagFired` cycles with a request-bearing `directFlag`.

---

## Fix

### Option A — decouple the board from the solve set (RECOMMENDED, surgical)

Keep the narrowed list driving rules + per-flag solve (no behavior change there), but record the **full union** to the board so the Requests tab lists all active inbound requests.

In `MinisterOfWillie.RunPlayCycle` ([MinisterOfWillie.cs:31](../Src/Ministers/Willie/MinisterOfWillie.cs#L31)):

1. Add a pure-union helper `AllActiveWillieBuildingRequests(activeFlags, directFlag)` = the existing **fallback branch** of `ActiveWillieBuildingRequests` with **no early-return** (union of all active flags + `directFlag` if absent, filtered to `IsRequestedFromWillie`).
2. Build the board (`inboundBoard` passed to `RecordInbound`) from that union, attributing `SourceMinisterForRequest` per request (already union-aware).
3. Leave `inboundRequests` (the narrowed `ActiveWillieBuildingRequests`) feeding `rules.Evaluate` and the existing solve paths (driving request, missing-room, and the `foreach (inbound …)` board-solve loop) **unchanged**.

Result:
- **Board (Requests tab) = union of all active Willie requests** → 6 rows. Because the union does not depend on which flag triggered the run, every per-flag run records the **same** board, so `RecordInbound`'s replace is idempotent and the count no longer collapses.
- **Per-request outcomes accumulate** in `_byRequest` across the per-flag runs (each per-flag run still solves its own flag's request via the narrowed path; `Record` adds that outcome; `RecordInbound`'s prune keeps only board-present keys). By the end of a cabinet cycle each of the 6 rows shows its own solver outcome (or "awaiting" until its flag's run lands).
- **No extra solver load.** Each per-flag run still solves only its own request (the `foreach` board-solve loop continues to iterate the **narrowed** `inboundBoard`, not the union).
- **No rules/advice behavior change** — `SelectPlacementRequest` still sees the narrowed list, so the driving `building_request_active` card is unchanged.

Touch: `MinisterOfWillie.cs` only (one helper + wire the board off the union). Decoupling means the `foreach` solve-loop must iterate the narrowed set, not the union board — keep a separate narrowed `WillieInboundRequest` list for the solve loop, or iterate `inboundRequests` directly there.

### Option B — remove the early-return entirely (simplest diff, heavier runtime)

Delete the early-return so `ActiveWillieBuildingRequests` always returns the union; the union then flows to rules, board, and the solve loop alike.

- ✅ Board = 6; one-line-ish change.
- ⚠️ **Solver load multiplies:** the `foreach` board-solve loop now solves all N inbound requests on **every** per-flag run → up to N×N placement solves per cabinet cycle (each hits live RIMAPI map validation). With 6 requests that is ~36 solves/cycle vs ~6 today.
- ⚠️ **Advice selection shifts:** `SelectPlacementRequest` now picks the global highest-priority request regardless of which flag triggered the run (arguably more correct, but it is a behavior change and will churn trace/advice assertions).

Recommend Option A unless we deliberately want the global-priority advice change.

### Deferred (out of scope) — run Willie's board-solve once per cabinet cycle

The deeper redundancy is that Willie is invoked **per flag** and re-derives/re-solves on each invocation. Collapsing the per-flag follow-ups into a single per-cycle Willie board-solve would remove both the last-write-wins fragility and the repeated work, but it is a coordination-layer change already tracked as the "Willie dirty-solve" deferral in `rules-decisions-advice-great-simplification-refactor`. Not needed to fix this bug.

---

## Tests

Add to `Src/Tests/Willie/MinisterOfWillieTests.cs` (the harness publishes flags via `harness.Flags.Publish` and triggers cycles):

1. **`FlagFiredCycle_RecordsFullInboundBoard_NotJustTriggeringFlag`** — publish two distinct active Willie-request flags (e.g. Chef freezer + Industry workshop), run a `FlagFired` cycle with `Flag:` set to the freezer flag, assert `RequestBoard("Willie")` has **2** rows (regression test for the early-return narrowing). This test fails on `master` today.
2. **Idempotent board across per-flag runs** — run the cycle once per flag (mirroring `RunWillieBuildingRequestFollowUpsAsync`) and assert the board still has both rows after the last run (proves `RecordInbound` replace no longer collapses).
3. Confirm existing `AllHitsDecision_RecordsAndSolvesInboundRequestBoard` and the `ManualTrigger` tests still pass unchanged (Option A leaves the narrowed/rules path intact).

`WillieSolverStoreTests` need no change (the store contract is unchanged).

---

## Verification (live)

1. Boot RimBob from the Codex worktree on its own port (per the launch-from-worktree workflow) so the dashboard can be inspected before land.
2. Drive a cabinet cycle with ≥2 active Willie-aimed building requests from different source ministers.
3. Confirm **Build Queue → Requested** count == **Requests** sidebar count, and each Requests row shows its own solver outcome (options / no-fit / awaiting). Confirm via the `/api/ministers/willie/solver/requests` JSON, not just the visual.

---

## Risks / non-goals

- **Non-goal:** any dashboard, wire, or persistence change. The Requests payload shape is already correct; only the recorded board breadth is wrong.
- **Risk (Option A):** within a single cabinet cycle, a request whose per-flag Willie run has not yet fired shows "awaiting" transiently; it settles by cycle end. Acceptable and honest.
- **Active-flag definition:** the board uses `flags.Active()` (non-expired), the same effective set the dashboard reads. An expired-but-still-shown flag could differ by one; verify the live counts match during verification.
