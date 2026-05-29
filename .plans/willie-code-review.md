# Willie Deep Code Review — Subagent-Driven Audit

> **Executed 2026-05-29 → [findings report](willie-code-review-findings.md): 0 P0, 5 P1, 4 P2, 6 known/accepted, 5 follow-up gimp slices.**

> **Review plan, not an implementation plan.** Claude executes this **read-only**:
> no source edits in the review pass. Output is a ranked findings report
> (`.plans/willie-code-review-findings.md`) + a list of follow-up gimp slices.
> Each real fix lands later through `bring-out-the-gimp`, not here.
>
> Audits the **landed Willie surface so far**: Rules + orchestrator, the Placement
> Solver (Solver1 `5a8e6c9`, Solver2 `9f6fe90..0e86f5f`, Solver3 S3a–c `7e3be6f`),
> the solver→Rules wiring (`fbad240`), ports/DI, and tests. Uses **parallel review
> subagents** (one per bounded slice) so the principal context eats findings, not
> raw file reads.

---

## 0. Why subagents + how the cost stays low

The Willie surface is ~25 source files + ~15 test files across four projects. Reading
all of it into the principal context to review is wasteful. Instead:

- **Claude pins the file manifest first** (Glob/Grep), then spawns **`caveman:cavecrew-reviewer`**
  subagents with **explicit file lists** — that reviewer is read-only
  (Read/Grep/Bash), severity-tagged, no-scope-creep, and cheap (Sonnet). Pinning the
  files means it never needs Glob. Keep the cheap tier cheap (per memory: gimp/cheap
  tiers exist for token savings).
- **One subagent per slice, all spawned in a single message** (parallel). Each returns
  `path:line: <sev>: <problem>. <fix>.` lines only.
- **Claude does the cross-cutting synthesis itself** — determinism and layering span
  files, so Claude re-reads the specific spots subagents flag and makes the call. Do
  **not** delegate the ranking/synthesis.

If a slice needs discovery or deeper cross-file reasoning the reviewer can't do with a
pinned list, escalate **that slice only** to `general-purpose` (has Glob + full
reasoning). Default is the cheap reviewer.

---

## 1. Step 1 — Claude builds the manifest + allowlist (no subagents yet)

1. `Glob Src/Ministers/Willie/**/*.cs` + `Glob Src/Tests/Willie/*.cs` — pin the **actual**
   landed file list (Solver3 S3a/b added `IRoomTemplate.cs`, `RoomShellBuilder.cs`,
   `RoomTemplateSet.cs`, `Templates/*`, plus new tests — discover them fresh, do not
   trust this plan's list).
2. Grep the cross-project contracts: `Src/Common/Placement/PlacementPorts.cs`,
   `Src/Common/Advice/AdviceItem.cs` + `AdviceOption`/`BlueprintGroup`/`FlagRequests.cs`,
   `Src/GameStateSync/RimApiPlacementProbe.cs`, `Src/ApiHost/Program.cs` (placement +
   Willie registration block).
3. Build the **known-approximation allowlist** so subagents don't drown the report in
   already-documented TODOs. These are **accepted scope cuts, not bugs** — flag only if
   they cause a correctness break *beyond* their documented boundary:
   - `PlacementEvidence.OccupancyIsPointApprox` — `BuildingRecord.Position` is a single
     cell, not a footprint (`PlacementEvidence.cs:41`).
   - `WillieRoomAnchor.EntryCells` is live evidence consumed by anchor resolution; raw `RoomRecord.RegionId` remains available, but derived anchor `RegionId` was retired by `willie-anchor-detection-closeout.md`.
   - `PlacementSpec.Constraints` always `[]` (`PlacementSpec.cs:36`).
   - Terrain affordance absent from free-space (`PlacementEvidence.cs:36`).
   - Non-freezer rooms not orchestrator-wired (missing-room stays prose).
   - Solver3 S3d (`ReuseExistingFootprintGenerator`) deferred.

---

## 2. Step 2 — spawn the slice reviewers (parallel, one message)

Each prompt is **self-contained**: pinned file list + the dimensions that apply + the
allowlist + the output format (`path:line: P0|P1|P2: problem. fix.`). Severity rubric:
**P0** correctness/determinism bug or layering violation; **P1** missing test for a
real branch, bounds/perf risk, contract drift; **P2** clarity/dead-code/convention.

### Slice A — Orchestration & Rules boundary
Files: `MinisterOfWillie.cs`, `Rules.cs`, `WillieFlagRequests.cs`, `WillieStateSummary.cs`.
Look for:
- **Rules stays sync/pure/zero-dep** — no I/O, no solver ref, deterministic (AGENTS
  minister convention). Any creep is **P0**.
- Wiring guard: `SolveAsync` runs **only** when `trace == freezer_request_active` **and**
  a matching freezer `BuildingRequest` is present (bounds the validate/path-cost call
  count). Missing guard = **P0**.
- Option-attach correctness: `item with { Options = result.Options }` replaces the right
  `AdviceItem`; non-freezer / zero-option / no-fit paths leave advice unchanged.
- W2: no-fit `NoFitReason` note + `MaterialsReady` + solver `Trace` carried into
  `MinisterReplayEntry`.
- **Exception/cancellation safety** around `await solver.SolveAsync` — a solver throw or
  cancellation must not crash `RunPlayCycle` or drop the prose advice. `ct` threaded.

### Slice B — Solver core & ranking
Files: `PlacementSolver.cs`, `Placement/PathCostLookup.cs`, `Placement/DiverseSelector.cs`,
`Placement/DraftDedupe.cs`, `Placement/IPlacementScorer.cs`,
`Placement/WalkablePathCostScorer.cs`, `Placement/ScoreWeights.cs`.
Look for:
- **Determinism is the headline lens.** No ambient RNG; stable sorts with full
  tie-break chains; **no `HashSet`/`Dictionary` iteration order leaking into output
  ordering**. Any nondeterminism = **P0**.
- **Regression guard:** path-cost results matched by **index** (`PathCostLookup`), never
  by cell coordinate — the Solver2a reverse-match fix must stay dead. Re-introduced
  coordinate match = **P0**.
- Diversity/selection bounds: 1–3 options, validate count bounded, `ProbeUnavailable`
  handled, `NoFitReason` assigned to the correct branch.
- `build_order_safety` (S3c) metric math + normalization; weights wired into
  `ScoreWeights.Default`; contributions sum/normalize sanely.

### Slice C — Generators, templates, evidence geometry
Files: `Generators/*.cs`, `Placement/FreezerTemplate.cs`, the S3 seam
(`IRoomTemplate.cs`, `RoomShellBuilder.cs`, `RoomTemplateSet.cs`, `Templates/*`),
`PlacementEvidence.cs`, `Placement/PlacementGenerationTypes.cs`.
Look for:
- **Geometry off-by-one:** `RingOrigins` ring coverage (no dup/missed cells),
  `EdgeCenter`/`AccessCell`, the free-rect histogram scan
  (`BuildClearRunRight`/`LargestRectFromOrigin`/`ReduceFreeRects`), `FreeRect.Contains`/
  `CanFit`. In-bounds **and** occupancy checks must cover **access cells**, not just
  footprint.
- **Behavior-identical freezer after the S3a seam** — `RoomTemplateSet.ForSpec` resolves
  freezer by `RoomClass` **and** by `TargetClass=Freezer` fallback (null `RoomClass`);
  output must match pre-seam `FreezerTemplate`. Drift = **P0**.
- Capacity sizing per `CapacityMeasure` (Beds/WorkSlots/Occupants/StorageStacks/FoodUnits)
  — null/zero/huge `amount`, wrong-measure → null. Deterministic.
- Budget honored (`MaxDrafts`, `MaxSearchRadius`); `ForSpec` null-safe for unregistered
  classes.

### Slice D — Contracts, ports, DI, layering
Files: `Src/Common/Placement/PlacementPorts.cs`, `Src/Common/Advice/AdviceItem.cs`
(+ `AdviceOption`/`BlueprintGroup`), `Src/Common/Advice/FlagRequests.cs`,
`PlacementSpec.cs`, `PlacementResult.cs`, `Src/GameStateSync/RimApiPlacementProbe.cs`,
`Src/ApiHost/Program.cs`, the Willie/`.csproj` references.
Look for:
- **Layering:** Ministers reach the wire **only** through `IPlacementValidator` /
  `IPathCostProbe`; `RimBob.Core` has **no** Ministers/State refs; Willie csproj refs
  match AGENTS layering. Violation = **P0**.
- **DI lifetimes:** `PlacementSolver` + scorer + generators + templates registered as
  singletons must be **stateless / per-request-safe** (no mutable field carrying request
  state across cycles). All `IRoomTemplate` + `IPlacementGenerator` registered.
- `PlacementSpec.FromBuildingRequest` mapping fidelity; STJ wire shapes; **no-compat /
  wipe-and-regen** honored on any persisted option/trace shape change; MapCell↔dto
  translation correct.

### Slice E — Tests & briefing/derivation
Files: `Src/Tests/Willie/*.cs`, `Placement/AnchorResolver.cs`, and the briefing inputs
(`WillieBriefing`, `WillieBacklog` aggregate, `WillieAnchorInventoryDerivation`, FORK3
`RimApiClient` wrappers, placement DTOs — Claude pins exact paths in Step 1).
Look for:
- **Coverage gaps → proposed new tests (P1):** no-fit branches, W2 no-fit note +
  replay-trace carry, the `RoomClass` alias map, `build_order_safety` edges,
  hospital/workshop/storage sizing, the **orchestrator wiring** path
  (`MinisterOfWilliePlacementTests`), determinism/diversity (confirm still present).
- `AnchorResolver` alias map + `ParseRoomClass` correctness; `ResolveAnchor`
  EntryCells→Centroid fallback.
- Derivation null/empty handling; fixtures realistic; tests seeded/deterministic.

---

## 3. Step 3 — Claude synthesizes (no subagents)

1. Merge all slice findings; **dedupe** cross-slice repeats.
2. **Verify the cross-cutting P0 lenses yourself** by re-reading the exact flagged
   spots: (a) determinism — any ordering that depends on `HashSet`/`Dictionary`
   enumeration; (b) layering — Ministers↔wire isolation; (c) the index-aligned
   path-cost regression; (d) Rules sync/pure. Don't trust a subagent's P0 without
   reading the line.
3. Drop allowlisted known-approximations unless they break beyond their boundary.
4. Rank **P0 / P1 / P2**; each P0/P1 gets a one-line repro + fix + a **proposed
   follow-up gimp slice** (so fixes land through the normal path).

## 4. Step 4 — write `.plans/willie-code-review-findings.md`

Sections: **Summary** (counts by severity + headline risks), **P0**, **P1**, **P2**,
**Known/accepted** (the allowlist, for the record), **Proposed follow-up slices**
(ordered, each gimp-able). Link it from this plan + Tasks.md.

---

## 5. Out of scope

- **Fixing the code** — every fix is a separate gimp slice off the findings report.
- **RIMAPI-side review** (separate repo).
- **Runtime/perf profiling** — static reasoning about bounds only; no live load test.
- **Re-litigating locked design decisions** (meta-plan §3) — review the *implementation*
  against them, not the decisions.

## 6. Verification

- Findings doc exists with every section populated; counts match the merged slice
  output.
- Every P0/P1 has a repro pointer (`path:line`), a concrete fix, and a named follow-up
  slice.
- No source file modified during the review (git status clean except the findings doc).

---

## 7. HumanTodo capture (append in same commit as this plan)

```
- [ ] willie-code-review [2026-05-29] #review #willie #construction #subagents Read-only deep audit of the landed Willie surface (Rules + orchestrator, Placement Solver 1/2/3a–c, solver→Rules wiring, ports/DI, tests) via 5 parallel caveman:cavecrew-reviewer subagents on pinned file lists; Claude pins the manifest + known-approximation allowlist, synthesizes/ranks P0–P2, writes willie-code-review-findings.md + follow-up gimp slices. No source edits in the review pass. [plan](.plans/willie-code-review.md)
```
