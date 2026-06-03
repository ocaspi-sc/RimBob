# Chef flag-request dedup (slice 1 of the rules-table refactor)

**Status:** PLANNED. Slice 1 of [`minister-rules-table-refactor`](minister-rules-table-refactor.md).
**Owner:** Chef (Food minister).
**Scope:** `Src/Ministers/Food/Rules.cs` + `Src/Tests/Food/*`. No shared-base extraction (slice 2). No coordination/Willie/FlagChannel code change.

---

## Motivation

`food-rules-independent-concerns` (landed `9357831`) made Chef all-hits: every matched
concern emits its own advice card **and its own `AgentFlag`** with its own requests.
It explicitly deferred **dedup**: when two concerns request the same physical build
(campfire from `emergency_food_flag` + `hunt_low_risk_animals`; freezer/cooler from
`emergency_food_flag` + `freezer_missing`), Chef emits the same `BuildingRequest` on
two flags. Downstream:

- `CabinetCycle.RunWillieBuildingRequestFollowUpsAsync` runs Willie **once per distinct
  Food flag id carrying a Willie building request** (`CabinetCycle.cs:328`).
- ⇒ N flags carrying the same build → **N placement solves for one build**, and each
  Willie `ReplaceMinisterAdvice` overwrites the prior → **last flag's solve wins** the
  Willie snapshot (snapshot churn).

This slice removes the duplicate cross-minister requests so each distinct build/labor/item
request is carried **once**, by the highest-priority concern that wants it. Willie solves
each build once; no last-write-wins churn.

This is the "biggest lever" deferred item in `food-rules-independent-concerns.md` →
Deferred → "Dedup / consolidated flag."

## Context

- `Src/Ministers/Food/Rules.cs` — `Evaluate` → `RuleEmissions` (collects all matched
  concerns) → `DecisionForEmissions` (priority-orders, flattens advice + flags). Flags
  are built per-concern in `EmitAdvice` from a `FoodFlagRequests` bundle
  (`BuildingRequests` / `LaborRequests` / `ItemRequests` / `Attention`).
- `AgentFlag` is a record; request lists are nullable (null when empty, via the
  `*OrNull` accessors).
- Coordination consumer: `CabinetCycle.cs:328`, `MinisterOfWillie.cs:42`. **Unchanged** by
  this slice — it just sees deduped requests.
- Design: `Docs/design/ministers.md` Rules Layer (all-hits; same-`trace` dedup is one of
  the three things all-hits needs), `Docs/DESIGN.md` "All-hits is the only rules
  evaluation policy."

## Scope

**In:**
- Add a deterministic dedup pass in `DecisionForEmissions` (or a helper it calls): across
  the priority-ordered emissions' flags, drop a cross-minister request from a flag if an
  identical request already appeared on an earlier (higher-priority) flag.
- Rebuild affected `AgentFlag`s with the filtered request lists (empty list → null via the
  existing `*OrNull` pattern).
- A flag whose requests all dedup away **stays** (it still signals the concern's
  severity/summary to CoS/Mayor) — only its now-redundant requests are removed.

**Out (non-goals):**
- **No cap / priority gate** — emit all matched concerns (deferred; would fight all-hits
  transparency).
- **No advice-card / action change** — player-facing `AdviceItem.Actions` stay full per
  concern (transparency); dedup touches **only** cross-minister flag requests.
- **No flag-shape change** — still one `food:<trace>` flag per concern; no merged "cabinet"
  flag.
- **No shared-base extraction** — that is slice 2.
- **No coordination/Willie/FlagChannel/CabinetCycle code change.**

## Approach

1. Define request **identity** keys (case-insensitive where string):
   - `BuildingRequest` → (`TargetClass`, `RoomClass`, `TargetDef`).
   - `LaborRequest` → (`WorkType`, `Skill`).
   - `ItemRequest` → (`ItemDef`).
   - `AttentionRequest` → (`Request`, `RequestedFrom`).
   Identity ignores `Reason`/`Priority`/`CapacityNeed` etc. — same physical ask.
2. In `DecisionForEmissions`, after the existing `OrderByDescending(Priority)` (add a
   stable `.ThenBy(trace)` tiebreak for determinism), walk flags in order keeping four
   `HashSet`s of seen identities. For each flag, filter each request list to entries whose
   identity is unseen, mark them seen, and rebuild the flag (`flag with { BuildingRequests =
   …OrNull, … }`).
3. Keep advice (`emission.Advice`) untouched. Compose the decision from the (unchanged)
   advice list + the deduped flags + existing composite trace + diagnostics.
4. Keep escalation fallback path (`Evaluate` lines 22-43) unchanged.

## Verification

Codex runs and must pass:
- `dotnet build` (solution).
- `dotnet test` targeting Food: `dotnet test --filter "FullyQualifiedName~Food"`.
- Add/adjust `FoodRulesTests`: a multi-concern briefing where two concerns request the
  same build (e.g. emergency + hunt both want a campfire when no cooking building) asserts
  the campfire `BuildingRequest` appears on **exactly one** flag; the other concern's flag
  still exists (signal) but no longer carries the duplicate; advice cards unchanged (still
  one per concern, full actions).
- Regenerate `Src/Tests/Food/Fixtures/replay-corpus/food-rules-history.jsonl` (2 cases) via
  the existing recorder if flag requests shift; `FoodReplayCorpusTests` green.
- Determinism: dedup result independent of original concern insertion order (priority +
  trace tiebreak).

## Where to see it (dashboard / JSON)

JSON-first: run a cabinet cycle on a struggling/new colony. In the manual `cabinet_run`
log / `CabinetRunLog`, confirm **one** Willie placement solve per distinct build (campfire,
freezer) instead of one per requesting concern. Chef advice cards still show every concern;
Willie Build Queue no longer churns last-write-wins between duplicate solves.

## Open questions (escalate, don't guess)

- If `AttentionRequest` dedup proves to drop a meaningfully different attention ask (same
  text, different intent), leave attention un-deduped and dedup only building/labor/item —
  flag it rather than guessing.

---

## Summary (landed 2026-06-03)

**Motivation.** Chef went all-hits (`9357831`) but emitted the same `BuildingRequest` on
multiple concern flags, so `CabinetCycle` ran Willie once per flag → duplicate placement
solves for one build + last-write-wins Willie snapshot churn. This slice dedups
cross-minister requests so each distinct build/labor/item is carried once, by the
highest-priority concern that wants it.

**Context.** Slice 1 of [`minister-rules-table-refactor`](minister-rules-table-refactor.md);
consumes the "Dedup / consolidated flag" deferred item from
[`food-rules-independent-concerns`](food-rules-independent-concerns.md). Design:
`DESIGN.md` all-hits-only; `ministers.md` Rules Layer (same-`trace` dedup is one of the
three things all-hits needs).

**Scope (shipped).**
- `DecisionForEmissions`: priority order + `.ThenBy(Rule)` deterministic tiebreak; new
  `DeduplicateFlagRequests` pass — four seen-key sets, highest-priority flag keeps each
  request, flags rebuilt with `NullIfEmpty`.
- Identity keys (case-insensitive): Building(TargetClass,RoomClass,TargetDef),
  Labor(WorkType,Skill), Item(ItemDef), Attention(Request,RequestedFrom).
- Advice cards unchanged (full per concern); flags stay 1/concern (signal); requests
  deduped. No cap, no shared-base, no coordination change.
- Tests: 2 existing tests now assert the shared request lives on the higher-priority flag;
  replay corpus regen'd for the tiebreak reorder.

**How to verify (human).**
- Commands: `dotnet test Src/RimBob.sln --filter "FullyQualifiedName~Food"` → 127 passed.
- JSON: cabinet run on a struggling colony → `CabinetRunLog` shows one Willie solve per
  distinct build (campfire/freezer), not one per requesting concern.
- Files: `Src/Ministers/Food/Rules.cs` `DecisionForEmissions` / `DeduplicateFlagRequests`.

**Codex run:** 20260603-175607-chef-flag-request-dedup · branch
`codex/prompt-20260603-175607-chef-flag-request-dedup` · landed commit `483fa38`
