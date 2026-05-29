# Willie Deep Code Review — Findings

> Read-only audit output for [`.plans/willie-code-review.md`](willie-code-review.md). No source files were modified.
> Method: 5 parallel `caveman:cavecrew-reviewer` subagents on pinned file lists (Slices A–E), then Claude verified every cross-cutting P0 lens by re-reading the exact flagged lines. Slice D (contracts/ports/DI/layering) was re-run by Claude directly because its subagent went off-script.

## Summary

| Severity | Count |
|---|---|
| P0 | **0** |
| P1 | 5 |
| P2 | 4 |
| Known/accepted | 6 |

**Headline:** the landed Willie surface (Rules + orchestrator, Placement Solver 1/2/3a–c, solver→Rules wiring, ports/DI) has **no P0** correctness, determinism, or layering bug. Both subagent-reported P0s were over-ranked and were downgraded after reading the exact lines (see "Reviewed & downgraded"). The four mandated cross-cutting lenses were personally verified clean:

- **Determinism** — `DraftDedupe` uses `HashSet` for membership only and emits in input order (no enumeration leak); `PathCostLookup` keys by int index; `RankScored` ([PlacementSolver.cs:254](../Src/Ministers/Willie/PlacementSolver.cs)) and the final selection ([PlacementSolver.cs:148](../Src/Ministers/Willie/PlacementSolver.cs)) carry full deterministic tie-break chains. *(DiverseSelector determinism is reviewer-confirmed, not personally re-read — it operates on the already-ranked list.)*
- **Layering** — `RimBob.Core` has zero `ProjectReference`s (pure domain, AGENTS §Repo Conventions); `RimBob.Ministers.csproj` does **not** reference `RimBob.Ingestion`, so ministers reach the wire only through `IPlacementValidator` / `IPathCostProbe` ([PlacementPorts.cs](../Src/Common/Placement/PlacementPorts.cs), defined in Core).
- **Index-aligned path cost** — `PathCostLookup` matches `results[pairIndex]` positionally and looks up `EntriesForDraft(int)`; the `DraftKey` string is trace-only. The Solver2a coordinate reverse-match stays dead.
- **Rules purity** — no I/O, no solver reference, no async. One ambient-clock smell (P1.1 below), not a structural violation.

**Headline risks (all P1, none blocking):** advice timestamps are wall-clock non-deterministic; several real solver branches and the end-to-end orchestrator-wiring path have no test.

---

## P0

None.

---

## P1

### P1.1 — Ambient clock in a "deterministic" rules engine
- [`Src/Ministers/Willie/Rules.cs:186`](../Src/Ministers/Willie/Rules.cs) — `DateTimeOffset now = DateTimeOffset.UtcNow;` feeds `IssuedAt` / `ExpiresAt` (lines 199, 217). Same briefing input → different `AdviceItem` each call, so advice is not byte-reproducible and golden/replay tests can't assert full equality.
- [`Src/Tests/Willie/MinisterOfWillieTests.cs:155`](../Src/Tests/Willie/MinisterOfWillieTests.cs) — test fixture sets `DateTimeOffset.UtcNow`, the same non-determinism on the test side.
- **Fix:** inject `TimeProvider` (or an `IClock`) into Willie `Rules` and the orchestrator; tests pass a fixed instant. **Caveat:** this clock pattern is likely shared across all ministers, so the real decision is accept-project-wide vs. fix-now — flag for the human, don't assume Willie-only.

### P1.2 — PlacementSolver NoFit branches are untested
[`Src/Tests/Willie/PlacementSolverTests.cs`](../Src/Tests/Willie/PlacementSolverTests.cs) has no coverage for these real return branches in [`PlacementSolver.SolveAsync`](../Src/Ministers/Willie/PlacementSolver.cs):
- `NoFitReason.NoAnchors` (PlacementSolver.cs:52)
- `NoFitReason.NoDrafts` (PlacementSolver.cs:68)
- `NoFitReason.NoReachablePath` (PlacementSolver.cs:102)
- `NoFitReason.ValidationRejected` (PlacementSolver.cs:139)
- **Fix:** one test per branch with a fixture that forces it; assert `NoFit` value + readiness flags.

### P1.3 — No end-to-end orchestrator-wiring test
No test exercises the `MinisterOfWillie` guard→solve→enrich path. Specifically: `trace == "freezer_request_active"` **and** a matching freezer `BuildingRequest` present → solver runs → on no-fit the `NoFitNote` is appended to the freezer advice rationale ([MinisterOfWillie.cs:174-199](../Src/Ministers/Willie/MinisterOfWillie.cs)) and the replay entry carries `NoFit` / `MaterialsReady` / `Trace` ([MinisterOfWillie.cs:301-311](../Src/Ministers/Willie/MinisterOfWillie.cs)).
- **Fix:** add a `MinisterOfWillie` placement test covering guard-fires and guard-skips (non-freezer trace, no matching request) plus the no-fit enrichment + replay carry. Belongs in [`MinisterOfWillieTests.cs`](../Src/Tests/Willie/MinisterOfWillieTests.cs).

### P1.4 — AnchorResolver parse/fallback coverage gap
[`Src/Tests/Willie/AnchorResolverTests.cs`](../Src/Tests/Willie/AnchorResolverTests.cs) covers the alias map but not: unaliased `ParseRoomClass` names resolving directly, and the `ResolveAnchor` `EntryCells → Centroid` fallback when `EntryCells` is empty ([AnchorResolver.cs](../Src/Ministers/Willie/Placement/AnchorResolver.cs)).
- **Fix:** add direct-name parse cases + an empty-`EntryCells` fixture asserting centroid fallback.

### P1.5 — No scorer normalization edge test
[`WalkablePathCostScorer`](../Src/Ministers/Willie/Placement/WalkablePathCostScorer.cs) normalizes path-cost metrics; there is no regression test for the all-equal / all-zero path-cost case (min == max range). The code path is believed safe, but the edge is unguarded by a test.
- **Fix:** add a `WalkablePathCostScorerTests` case where every candidate has identical (and zero) cost; assert finite, deterministic normalized scores (no NaN/divide-by-zero).

---

## P2

### P2.1 — Guard reads the pre-dedup variable
[`Src/Ministers/Willie/PlacementSolver.cs:90`](../Src/Ministers/Willie/PlacementSolver.cs) — after `uniqueDrafts = DraftDedupe.ByCellsShapeAnchor(gatedDrafts)` (line 89), the guard checks `gatedDrafts.Count == 0` but everything downstream uses `uniqueDrafts`. Functionally identical **today** because dedup of a non-empty list can never return empty (confirmed: [DraftDedupe.cs:7-26](../Src/Ministers/Willie/Placement/DraftDedupe.cs) is pure membership dedup). It silently breaks the moment `DraftDedupe` ever gains a filter.
- **Fix:** `if (uniqueDrafts.Count == 0)`.
- *(Two subagents ranked this P0; downgraded — see below.)*

### P2.2 — TemplateAnchoredGenerator lacks the access-cell occupancy pre-filter
[`Src/Ministers/Willie/Generators/TemplateAnchoredGenerator.cs:~69`](../Src/Ministers/Willie/Generators/TemplateAnchoredGenerator.cs) checks asset-cell occupancy but not `accessCells.Any(evidence.IsOccupied)`, unlike [`LargestEmptyRectangleGenerator`](../Src/Ministers/Willie/Generators/LargestEmptyRectangleGenerator.cs). **Not a correctness gap** — the shared `PassesHardGate` ([PlacementSolver.cs:234-238](../Src/Ministers/Willie/PlacementSolver.cs)) rejects access-occupied drafts centrally. But emitting doomed drafts wastes the generator's `MaxDrafts` budget and could starve a valid candidate.
- **Fix:** add the same `accessCells.Any(evidence.IsOccupied)` early-reject for budget yield + generator consistency.

### P2.3 — Dual generator ctor: DI is template-backed, default is template-less
[`PlacementSolver.cs:37-39`](../Src/Ministers/Willie/PlacementSolver.cs) — when `generators` is null the solver builds `new TemplateAnchoredGenerator()` / `new LargestEmptyRectangleGenerator()` (no `RoomTemplateSet`), but DI injects the template-backed overloads ([Program.cs:81-85](../Src/ApiHost/Program.cs)). Tests that construct `new PlacementSolver(probe, validator)` therefore exercise different generator behavior than production.
- **Fix:** have the default ctor build template-backed generators from a shared default `RoomTemplateSet`, or drop the parameterless generator ctors so the dependency is explicit.

### P2.4 — Cancellation re-throw (reviewed, correct-by-design)
[`MinisterOfWillie.cs:149-151`](../Src/Ministers/Willie/MinisterOfWillie.cs) re-throws `OperationCanceledException` out of `TrySolvePlacementAsync`. Slice A flagged this P0 ("drops prose advice"). It is **not a defect**: arbitrary solver exceptions are already caught and degraded to prose ([MinisterOfWillie.cs:153-157](../Src/Ministers/Willie/MinisterOfWillie.cs) → `FromFailure`), and propagating OCE on a cancelled token is correct cooperative cancellation — a cancelled cycle *should* publish nothing. No action; recorded for transparency.

---

## Known / accepted (allowlist — confirmed within boundary)

These are documented scope cuts from the meta-plan, re-confirmed during this pass as *not* breaking beyond their stated boundary:

1. `PlacementEvidence.OccupancyIsPointApprox` — `BuildingRecord.Position` is one cell, not a footprint ([PlacementEvidence.cs:41](../Src/Ministers/Willie/PlacementEvidence.cs)).
2. `WillieRoomAnchor.EntryCells` is live evidence consumed by anchor resolution and reuse-footprint generation; raw `RoomRecord.RegionId` remains available, but derived anchor `RegionId` was retired by `willie-anchor-detection-closeout.md`.
3. `PlacementSpec.Constraints` is always `[]` ([PlacementSpec.cs:36-37](../Src/Ministers/Willie/PlacementSpec.cs), carries its TODO).
4. Terrain affordance absent from free-space ([PlacementEvidence.cs:36](../Src/Ministers/Willie/PlacementEvidence.cs)).
5. Non-freezer rooms not orchestrator-wired — missing-room advice stays prose ([Rules.cs:151-172](../Src/Ministers/Willie/Rules.cs), `MissingRoomDecision` + its TODO).
6. Solver3 S3d (`ReuseExistingFootprintGenerator`) deferred.

---

## Reviewed & downgraded (subagent P0 → not P0)

| Reported | By | Verdict |
|---|---|---|
| `MinisterOfWillie.cs:37` P0 — cancellation drops prose | Slice A | **Not a defect** → P2.4. General exceptions degrade to prose; OCE re-throw is correct cancellation. |
| `PlacementSolver.cs:90` P0 — proceeds with empty draft list | Slice B (+ misrouted D) | **Functionally equivalent today** → P2.1. Dedup of non-empty can't be empty. |

---

## Proposed follow-up gimp slices (ordered)

Each is an independent `bring-out-the-gimp` slice off this report.

1. **willie-rules-clock** — inject `TimeProvider` into Willie `Rules` + orchestrator; fixed clock in tests. *(Confirm cross-minister scope first; may widen.)* — addresses **P1.1**.
2. **willie-solver-nofit-tests** — add the 4 NoFit-branch tests + the scorer all-equal/zero-cost edge test. — addresses **P1.2, P1.5**.
3. **willie-orchestrator-wiring-test** — `MinisterOfWillie` end-to-end: guard fires / skips, no-fit note + replay carry. — addresses **P1.3**.
4. **willie-anchorresolver-tests** — unaliased `ParseRoomClass` + `EntryCells→Centroid` fallback. — addresses **P1.4**.
5. **willie-solver-tidy** — `uniqueDrafts` guard fix, `TemplateAnchoredGenerator` access pre-filter, unify generator default ctor to template-backed. — addresses **P2.1, P2.2, P2.3**.
