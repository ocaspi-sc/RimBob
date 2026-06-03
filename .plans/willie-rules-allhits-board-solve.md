# Willie — all-hits rules + solve the request board (slice 4)

**Status:** PLANNED. Slice 4 (final) of [`minister-rules-table-refactor`](minister-rules-table-refactor.md).
**Owner:** Willie (Construction minister).
**Depends on:** slice 2 (`minister-rules-shared-base`, `2d02576`) for the base; consumes the
`willie-requests-dashboard-tab` (`ba1c5bc`) per-request infra (`WillieSolverStore._byRequest`,
`RecordInbound`, `RequestKey`).

---

## Motivation

Willie is the last minister still **first-match** (9-rule cascade, each `return`s) with the
triple copy (`RuleMatches` + `AllRuleEvaluations` + `DiagnosticsFor`). Two coupled changes:

1. **Flip rules to all-hits** (port onto the shared base) so Willie surfaces every matched
   concern (power, battery, material, blocked, missing-rooms, cooler, inbound build requests)
   as independent cards — consistent with Chef/Welfare and the all-hits-only decision.
2. **Solve the request board.** Today `MinisterOfWillie` solves exactly **one** request per
   cycle, picked from the single `decision.Trace` (`MinisterOfWillie.cs:47`). All-hits makes
   `decision.Trace` a **composite** (`rules:building_request_active+kitchen_missing+...`), which
   `TryGetPlacementRequest` cannot match → the solver would stop. The fix (chosen direction:
   *Full*): drive the solver from the **emitted placement rules + the inbound board**, not the
   composite trace. This also fixes the `willie-requests` deferred limitation ("Willie solves
   one request per cycle → only one fresh outcome") — now every inbound request gets a fresh
   per-cycle outcome in `_byRequest`.

## Context

- Rules: `Src/Ministers/Willie/Rules.cs` — 9-rule first-match cascade + `maintain_build_program`
  fallback; `ShouldDeferToMissingKitchen` (line 84) is the dominance case; public
  `TryGetPlacementRequest` / `TryMissingRoomClass` / `SelectPlacementRequest` /
  `MissingRoomRequest` map a trace → the request to solve (consumed by `MinisterOfWillie`).
- Wiring: `Src/Ministers/Willie/MinisterOfWillie.cs` — `RunPlayCycle` records the inbound board
  (`RecordInbound`, line 39), then `rules.Evaluate`, then for the single `decision.Trace`:
  `TryGetPlacementRequest` → `TrySolvePlacementAsync` → `solverStore.Record(...)` (one
  `_byRequest` upsert) + `EnrichSolverAdvice` (enriches the driving advice by id
  `willie_<trace>`).
- Store: `Src/Ministers/Willie/WillieSolverStore.cs` — `_byRequest` keyed by `RequestKey`,
  pruned to the current board by `RecordInbound`.
- Base + templates: `Src/Common/Ministers/MinisterRule*.cs`; Chef's ported `Rules.cs` (`2d02576`)
  is the rules-port template (table + fallback descriptors + `BuildTrace`).

## Scope

**In — Willie rules → all-hits (mirror Chef's port):**
- 9 rules → `MinisterRule<WillieBriefing>[]` table. Lambdas are built per-`Evaluate` so they
  close over `now` **and** `inboundBuildingRequests` (Chef closes over `now`; same pattern).
- **Dominance predicate:** `building_request_active.Matches` =
  `SelectPlacementRequest(briefing, inbound) is { } r && !ShouldDeferToMissingKitchen(briefing, r)`.
  This replaces the inline line-84 guard — kitchen-dependent inbound requests are deferred (the
  rule does not match) while the kitchen is missing, exactly as today, but now as an explicit
  predicate.
- `maintain_build_program` → a `MinisterRuleTraceDescriptor` fallback (like Chef's
  `maintain_security_threshold`); `AllRules` keeps all 10 rows.
- **Delete** `RuleMatches`, `AllRuleEvaluations`, `RuleEvaluation`, `DiagnosticsFor`, and the
  bespoke `DecisionFor`/trace plumbing the base now provides. **Keep** the public
  `TryGetPlacementRequest`, `TryMissingRoomClass`, `SelectPlacementRequest`,
  `MissingKitchenDependencyRank`, `MissingRoomRequest`, `IsFreezingBuildRequest` helpers
  (still consumed by the wiring).

**In — MinisterOfWillie solves the board (`MinisterOfWillie.cs`):**
- Replace the single `TryGetPlacementRequest(decision.Trace, ...)` solve with a loop:
  - **Inbound board:** solve every request in `inboundBoard` (the full board already computed
    at line 33). Record each via `solverStore.Record(...)` so `_byRequest` carries a fresh
    outcome for every inbound request each cycle (this is the board fix). Enrich the
    `building_request_active` advice item with the **driving** request's solve (the request
    `SelectPlacementRequest` features — the same one the rule's advice describes), preserving
    today's advice content for that card.
  - **Missing-room rules:** for each emitted `*_missing` rule (`kitchen_missing`,
    `hospital_missing`, `storage_room_missing`), synthesize its `MissingRoomRequest` (reuse
    `TryGetPlacementRequest` per that rule's trace) and solve it; enrich that rule's advice
    item (`willie_<trace>`) with the outcome, as today.
  - Preserve solver-offline handling (`SolverOffline` short-circuits to preserved advice) and
    the replay-output persistence for the driving solve.
- Drive enrichment per emitted rule by the rule's own trace (`willie_<rule>`), not the composite
  `decision.Trace`. Dedup solves by `RequestKey` so the same physical request is solved once per
  cycle.

**Out (non-goals):**
- No base changes (the base already supports everything; if it genuinely does not, STOP and
  surface — do not fork the base).
- No dashboard frontend change (the Requests tab already renders `_byRequest`; it simply now
  shows fresh outcomes for all requests).
- No new advice concerns; no cap. No FlagChannel/CabinetCycle logic change (if multi-Willie-flag
  handling needs a coordination-logic change, STOP and surface).

## Behavior change (NOT neutral — unlike slices 2/3)

This is a real flip. Expect:
- Willie advice goes from **one** card to **N** (all matched concerns), priority-sorted.
- Every inbound building request gets a fresh `_byRequest` outcome each cycle (was: one).
- Heavy `WillieRulesTests` rewrite (single-decision/first-match assertions → multi-concern
  all-hits set), plus `MinisterOfWillieTests` (board-solve loop, per-request records) and
  `WillieSolverStoreTests`. Regenerate the Willie replay corpus if present.

**Invariants that MUST still hold (assert in tests):**
- **Dominance:** a kitchen-dependent inbound request with the kitchen missing is NOT solved/
  featured by `building_request_active` (deferred); `kitchen_missing` fires and its kitchen
  request is solved instead. (Same end-state as the old first-match defer.)
- `AllRules` keeps all 10 Willie rules (9 + `maintain_build_program`) — do not shrink (the
  slice-2 regression); no hand-written condition/output strings.
- Solver-offline still preserves prior advice.

## Approach

1. Read Chef's ported `Rules.cs` (template) + the current `MinisterOfWillie`/`WillieSolverStore`.
2. Port Willie rules to the table (closures over `now`+`inbound`); dominance predicate on
   `building_request_active`; `maintain_build_program` descriptor; delete the triple copy; keep
   the public placement helpers.
3. Rewire `MinisterOfWillie` to solve the board + emitted missing-room requests, record each in
   `_byRequest`, enrich each driving advice by its own trace; dedup by `RequestKey`.
4. Rewrite tests; regen Willie replay corpus if it shifts.
5. Self-review; run full verification.

## Verification

- `dotnet build Src/RimBob.sln` — 0 errors.
- `dotnet test Src/RimBob.sln --filter "FullyQualifiedName~Willie"` — green.
- `dotnet test Src/RimBob.sln` — full suite green (coordination + endpoint + store tests
  included; the solver path changed, so watch these).
- Grep: `Src/Ministers/Willie/Rules.cs` no longer defines `RuleMatches` or `AllRuleEvaluations`.
- Behavior assertions: multi-concern all-hits advice; dominance defer preserved; every inbound
  request has a `_byRequest` outcome after a cycle; `AllRules` count = 10.
- JSON-first (optional, needs Host): cabinet cycle with ≥2 inbound Willie requests →
  `GET /api/ministers/willie/solver/requests` shows a fresh outcome for **each** request (not
  just one).

## Where to see it

Willie dashboard: multiple concern cards (all-hits); the **Requests** tab now shows a fresh
solver outcome for every inbound request each cycle (previously only one).

## Open questions (escalate, don't guess)

- If solving the full board per cycle has a coordination cost that needs a FlagChannel/
  CabinetCycle **logic** change (not just test updates), STOP and surface — coordination logic
  is out of scope.
- If the dominance defer cannot be expressed as a `building_request_active` predicate without
  changing other rules' behavior, STOP and surface.
- If `AllRules` cannot stay at 10 without hand strings, STOP (this was the slice-2 regression).
- If the flip changes a non-target rule's advice/flags, STOP and report.

---

## Summary (landed 2026-06-03)

**Motivation.** Willie was the last first-match minister. Flipped it to all-hits **and** fixed
the long-standing "one solve/cycle" limitation — `MinisterOfWillie` now solves the whole inbound
request board each cycle, so every request gets a fresh outcome immediately (the original intent
that `willie-requests-dashboard-tab` shipped display-only).

**Scope (shipped).**
- **Rules → all-hits** (`eeacb9d`): 9 rules → `MinisterRule<WillieBriefing>[]` table (lambdas
  close over `now` + `inboundBuildingRequests`); the `ShouldDeferToMissingKitchen` dominance case
  is now `building_request_active`'s `Matches` predicate; `maintain_build_program` → fallback
  descriptor (`AllRules` = 10). Deleted `RuleMatches`/`AllRuleEvaluations`/`RuleEvaluation`/
  `DiagnosticsFor`/bespoke `DecisionFor`. Public placement helpers kept.
- **Solve the board** (`f8c3eae`): `MinisterOfWillie` solves the driving inbound request +
  emitted missing-room requests + the full inbound board, deduped by `RequestKey`, recording each
  in `_byRequest`; enriches each driving advice by its own trace. No `decision.Trace` dependency.
  Solver-offline handling preserved.

**Behavior change (intended).** Willie advice goes 1 → N cards (all-hits). Every inbound building
request is solved and recorded each cycle (was: one). Dominance preserved: a kitchen-dependent
inbound request with the kitchen missing defers (tested at rules + minister level).

**How to verify (human).**
- Commands: `dotnet test Src/RimBob.sln --filter "FullyQualifiedName~Willie"` → 147; full suite
  → 551.
- JSON (needs Host): cabinet cycle with ≥2 inbound Willie requests →
  `GET /api/ministers/willie/solver/requests` shows a fresh outcome for **every** request.
- Files: `Src/Ministers/Willie/Rules.cs`, `Src/Ministers/Willie/MinisterOfWillie.cs`.

**Codex run:** 20260603-221115-willie-rules-allhits-board-solve · landed commit `81ff590`
(squash of `eeacb9d` + `f8c3eae`).
