# Cabinet Toast: Willie Solver-Run Subitems

## Goal

When a manual cabinet run causes Willie to run the placement solver more than once, the Run Cabinet dialog should show **each** solver run as a subitem under one "Willie run" step, instead of collapsing them into a single row that only reflects the last solve. This extends the existing run-step dialog (`.plans/cabinet-run-step-dialog.md`, landed `cabinet-run-step-dialog`) — it is a transparency/debug fix, not a new control.

## The bug being fixed

The cabinet runs Willie **once per routed request flag**. In `RunWillieRequestFollowUpsAsync` ([CabinetCycle.cs:402-415](Src/Coordination/CabinetCycle.cs)) the loop calls `RunResolvedMinisterAsync(minister, willie, requestCycle, ...)` for every Willie-routed flag, and that helper always writes the run-log step under the same key `minister_willie` ([CabinetCycle.cs:320](Src/Coordination/CabinetCycle.cs)). The store dedups steps by key via `AppendOrReplace` ([CabinetRunLogStore.cs:371-380](Src/Coordination/CabinetRunLogStore.cs)), so **each Willie follow-up overwrites the previous one**. If Chef publishes a freezer request and a campfire request, Willie solves twice but the dialog shows a single "Willie run" row carrying only the second solve's trace. The first solve is invisible. (`food-rules-independent-concerns` made N-concern-flags → N Willie solves/cycle the normal case, so this is the common path, not an edge case.)

Willie can also run as a follow-up to **more than one source minister** in the same cabinet cycle — Chef routes build requests and Welfare routes a shelter `building_request` — and `AppendOrReplace` even moves the single Willie row to the end of the list each time it is rewritten, so today its position is non-deterministic relative to Mayor.

## Current state (relevant pieces)

- `CabinetRunStepSnapshot` is a flat record; there is no nesting field ([CabinetRunLogStore.cs:412-450](Src/Coordination/CabinetRunLogStore.cs)).
- The dialog renders a flat `<ol>` of steps, one `CabinetRunStepRow` each, with title + time + duration + detail + a compact trace summary ([CabinetRunDialog.tsx:55-94](Dashboard/src/components/layout/CabinetRunDialog.tsx)).
- The TS mirror `CabinetRunStepSnapshot` matches the record field-for-field ([system.ts:156-176](Dashboard/src/types/system.ts)).
- The snapshot is serialized whole to the `cabinet_run` SSE event and the `POST /api/cabinet/trigger` response by `System.Text.Json` ([AgendaStreamEndpoint.cs:196-199](Src/ApiHost/Endpoints/AgendaStreamEndpoint.cs)); the reducer merges by `run_id` ([adviceFeedReducer.ts:197,269](Dashboard/src/hooks/adviceFeedReducer.ts)). **A new additive field on the step record therefore flows end-to-end with no extra plumbing.**
- Each Willie follow-up run still produces its own `MinisterTrace` — `traces.Begin/Complete` wrap each `RunResolvedMinisterAsync` call, and `LatestTraceFor(willie, minister)` reads the trace for *that* solve right after it runs ([CabinetCycle.cs:332-353,452-453](Src/Coordination/CabinetCycle.cs)). So per-solve trace detail is already available at the exact point we need it.
- Per-request solver outcome (placed / no-fit / option count) is recorded separately in `WillieSolverStore` keyed by request ([WillieSolverStore.cs:19-34,108-127](Src/Ministers/Willie/WillieSolverStore.cs)), but that store is **not** injected into `CabinetCycle` today.
- The grain of "one solver run" = one Willie `RunPlayCycle` invocation = one routed flag follow-up. (A single run may internally solve a driving request plus missing-room requests; that finer split stays inside the trace's advice count, not separate subitems.)

## Design — Option A: nested subitems (recommended)

One parent "Willie run" step with one child step per solver run. This matches the literal ask ("subitems") and the dialog's mental model (Chef row, Welfare row, Willie row with its solves nested, Mayor row).

### 1. DTO — add `children` to the step record

Add a trailing field to `CabinetRunStepSnapshot`:

```csharp
[property: JsonPropertyName("children")]
IReadOnlyList<CabinetRunStepSnapshot> Children
```

Reuse the same record type recursively so the frontend can render children with the same row component. In practice depth is exactly 1 (only Willie populates children); note that invariant in a `// WHY` comment rather than enforcing it. Every existing `new CabinetRunStepSnapshot(...)` site in the store passes `[]` for `Children` (mechanical: `StartRun` seed, `CompleteRun`, `FailRun`, `UpdateStep`).

### 2. Store — preserve and append children

- `UpdateStep` must preserve a step's existing children when it rewrites the step: `Children: existing?.Children ?? []` (the parent's aggregate `StartStep`/`CompleteStep` calls must not wipe accumulated children).
- Add `AddChildStep(string runId, string parentKey, CabinetRunStepSnapshot child)`:
  - finds the parent step by `parentKey`, replaces/append the child by `child.Key` inside the parent's `Children` (same dedup-by-key semantics as `AppendOrReplace`),
  - rewrites the parent **in place** (preserve its list index — do **not** use the move-to-end `AppendOrReplace` for the parent here; fix the position non-determinism noted above),
  - publishes the snapshot so SSE streams each child as it lands.
- Add a small parent-finalize that sets the parent's status + aggregate detail from its children (e.g. `completed` when all children completed, `failed` if any child failed; detail like `"3 solver runs - 2 placed, 1 no-fit"` or, if we keep it trace-only, `"3 solver runs"`). This can be a dedicated `CompleteParentStep(runId, key, detail, status)` or folded into `AddChildStep` recomputing the aggregate each append. Prefer recomputing on each append so the streamed parent is always consistent and no separate finalize ordering is needed.

### 3. CabinetCycle — write parent + child per solve

Rework `RunWillieRequestFollowUpsAsync` so that, when `cabinetRunId is not null`:

- Before the per-flag loop, ensure the parent `minister_willie` step exists and is `running` (idempotent — the second source minister's follow-up in the same cycle must append to the *same* parent, not reset it).
- For each routed `requestFlag`: run Willie with the run-log suppressed for the inner call (`RunResolvedMinisterAsync(..., cabinetRunId: null)` so it does **not** write a top-level `minister_willie` step), capture start/stop time around it, read `LatestTraceFor(willie, minister)`, then `AddChildStep` a child whose:
  - `key` is unique per solve, e.g. `minister_willie__{requestFlag.Id}`,
  - `label` names the solve target + source, e.g. `"Freezer (from Chef)"` derived from the flag's Willie-routed `BuildingRequests`/`ZoneRequests` (`TargetClass`/`TargetDef`/`RoomClass`, or `ZoneClass`/`PlantDef`) + `requestFlag.SourceMinister`,
  - carries the per-solve `trace` (so the existing `compactTraceSummary` shows path / rule / advice / flag counts for that solve),
  - `status`/`duration` reflect that solve, failing the child (and so the parent) if the inner run threw.
- After the loop, the parent aggregate is already current (recomputed per append). Keep returning `[willie.Key]` so Willie's own scheduled slot is still skipped.

Willie's **own scheduled slot** (when nothing routed a request to it) is untouched: it runs through `RunResolvedMinisterAsync` and produces a normal childless `minister_willie` step. The existing "already ran as follow-up" skip ([CabinetCycle.cs:110-116](Src/Coordination/CabinetCycle.cs)) still writes no separate row — fine, because the parent step now already represents Willie.

`TriggerMinisterAsync` (single manual minister trigger) calls the follow-up path with no `cabinetRunId`, so it is unaffected — no dialog there.

Keep the inner-call seam minimal: the cleanest split is to let `RunResolvedMinisterAsync` keep its single responsibility (run minister + record trace + optionally write its own top-level step) and have the follow-up loop own all parent/child run-log writes by passing `cabinetRunId: null` into the inner call. Do not thread a `parentKey` overload through `RunResolvedMinisterAsync` unless the timing capture turns out cleaner that way.

### 4. Frontend — render children recursively

- `CabinetRunStepRow` renders `step.children` (when non-empty) as a nested `<ol className="cabinet-run-substeps">`, mapping each child back through `CabinetRunStepRow` (recursive reuse — no second component). The parent row keeps its title/detail/trace summary as the aggregate line.
- Add `children: CabinetRunStepSnapshot[]` to the TS `CabinetRunStepSnapshot` interface ([system.ts:156](Dashboard/src/types/system.ts)).
- CSS: add `.cabinet-run-substeps` (indented, denser gap) and a `.cabinet-run-step.is-child` (smaller min-height, lighter dot) alongside the existing `.cabinet-run-step*` rules ([styles.css:345-446](Dashboard/src/styles.css)); reuse existing status tones. Respect width so indented children do not reintroduce the overflow that the sibling `cabinet-toast-overflow` todo targets — but do not fix that overflow here.

## Alternative — Option B: flat distinct sibling rows (cheaper, not recommended)

Give each follow-up solve a unique step key (`minister_willie__{flagId}`) and a request-naming label so they become **sibling** top-level rows. No DTO change, no store change, no frontend change. Smaller and lower-risk, but it does not deliver visual "subitems", drops the single-Willie grouping, and interleaves Willie rows with Chef/Welfare/Mayor. Documented as the fallback if nesting is judged not worth the DTO/CSS cost.

## Optional enrichment (flag, do not require)

The child detail uses the per-solve **trace** (advice count ≈ options surfaced, rule fired, path) which is already in hand. Richer "placed vs no-fit + validated option count" lives in `WillieSolverStore`. Injecting `WillieSolverStore` into `CabinetCycle` and reading `RequestBoard("Willie")` after each solve would let the child say `"placed - 3 options"` / `"no-fit: <reason>"`. This couples `CabinetCycle` to a Willie store, so leave it out of the core slice and note it as a follow-up; the trace summary is an adequate first cut.

## Compatibility and state

The cabinet run log is **ephemeral in-memory** (bounded ring buffer, `MaxRuns = 12`); it is not persisted game/minister state. `children` is additive and defaults to `[]`. No compat code, no wipe-and-regen, no persistence migration. A browser still holding an old `cabinet_run` snapshot simply lacks `children` on its steps; the next run replaces it. Do not add any tolerant parser or legacy branch.

## Docs

Same-turn doc update: `Docs/design/dashboard.md` — extend the cabinet run-step dialog / `cabinet_run` SSE notes ([dashboard.md:573](Docs/design/dashboard.md), and the CABINET run-dialog description) to state that a Willie step nests one subitem per solver run when the cabinet routes multiple build/zone requests to Willie in a single run, so all solves are visible instead of only the last. No new HTTP/SSE event type — the same `cabinet_run` snapshot now carries `children` on steps.

## Tests and verification

- Extend `Src/Tests/Coordination/CabinetCycleTests.cs`: a cabinet run where the source minister publishes **two** Willie-routed request flags asserts the run log has **one** `minister_willie` parent step with **two** child steps (distinct keys, each carrying its solve's trace), not a single overwritten step. Add a case where two different source ministers (e.g. Chef + Welfare) both route to Willie and assert both children land under the same parent and the parent keeps a stable position.
- A failure case: an inner Willie solve throwing marks that child `failed` and propagates `failed` to the parent (and the run), without losing already-recorded sibling children.
- `dotnet test`, then `npm.cmd run build` in `Dashboard/`.
- Live (launch RimBob from the worktree on a non-5000 port, give the worktree dashboard URL): drive a colony state that routes ≥2 Willie build/zone requests in one cabinet cycle (e.g. Chef freezer + campfire), click `Run Cabinet Now`, confirm the Willie step shows N indented subitems, each naming its request + source and its own trace summary, and that the SYSTEM trace table still shows the latest Willie outcome consistently. Verify the JSON of `POST /api/cabinet/trigger` (or the `cabinet_run` SSE frame) carries `steps[].children` — confirm via API/JSON, not only the visual dialog.

## Out of scope

- `cabinet-toast-overflow` (sibling todo) — fixing the Run Cabinet toast text overflowing adjacent cards. Layout-only; tracked separately.
- `willie-emojis` (sibling todo) — distinct emojis for Willie Solver/Requests surfaces.
- Any new RimWorld/RIMAPI write behavior; manual cabinet runs stay suggest-only.
- New SSE event type, new endpoint, or any persisted run history across Host restarts.
- The per-placement (driving vs missing-room) finer breakdown inside a single Willie run, and the `WillieSolverStore` outcome enrichment (flagged above as optional).
- Changing cabinet run order or the follow-up routing logic itself.

---

## Summary (landed `4211944`)

Shipped Option A (nested subitems). The Run Cabinet dialog now shows one "Willie run" parent step with one child per routed solver run, so earlier solves stay visible instead of being overwritten by the last one.

What landed, faithful to the plan:

- **DTO** — `CabinetRunStepSnapshot` gained an additive `children` field (recursive same-type); TS mirror updated. Flows through the existing `cabinet_run` SSE + `POST /api/cabinet/trigger` response with no extra plumbing.
- **Store** (`CabinetRunLogStore.cs`) — new `AddChildStep(runId, parentKey, child)` appends/replaces a child by key and rewrites the parent **in place** (`AppendOrReplace(..., preserveExistingIndex: true)` — fixes the move-to-end position non-determinism). `RecomputeParentFromChildren` derives parent status + an aggregate detail line; `UpdateStep` preserves accumulated children.
- **CabinetCycle.cs** — `RunWillieRequestFollowUpsAsync` ensures one `WillieRunStepKey = "minister_willie"` parent, runs each routed flag with the inner top-level step suppressed, and adds a child per solve keyed `minister_willie__{source}__{flagId}` with `Kind: "solver"`, carrying that solve's trace/status/duration. Chef-routed + Welfare-routed solves nest under the same parent. Willie's own scheduled slot stays a normal childless step.
- **Frontend** — `CabinetRunDialog` renders `step.children` recursively; `.cabinet-run-substeps` / child CSS added.
- **Tests** — `CabinetCycleTests.cs` (+174 lines): N follow-up solves → one parent + N children, multi-source nesting, and failure propagation child→parent.

Landed directly to master by the user (not via the gimp flow). Siblings from the same capture batch also landed: `cabinet-toast-overflow` (`ce2a9b4`, contain toast text) and `willie-emojis` (`671f144`, dashboard view emojis).

The flagged optional enrichment (inject `WillieSolverStore` for placed/no-fit/option-count instead of trace advice count) was **not** taken — child detail uses the per-solve trace summary, as scoped.
