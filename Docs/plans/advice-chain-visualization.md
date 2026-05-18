# Advice Chain Visualization (Route Cards)

## Context

Food advice currently lands as text cards plus a bulleted "Current State" summary
above them (`FoodStateSummary` → snapshot envelope → dashboard table). Players
can't see *how the pieces connect* — that "low food" can be resolved via the
grow path, the hunt path, the forage path, etc., and which of those this cycle's
advice is actually driving.

This adds, per food snapshot (above the cards), a **planning/production route
view** as three small side-by-side cards: Grow, Hunt, and Forage. Each card has
its own route color and icon-led short rows for route facts: what the colony
already has, what this advice acts on, idle-but-available steps, and blocked
steps.

**Decisions already made with the user:**
- Generation is **deterministic code from `FoodBriefing` + emitted advice**, not
  per-cycle LLM. Rationale: the chain topology is stable domain knowledge; no
  prompt caching exists so a drawn diagram burns tokens every cycle; deterministic
  output is replay-traceable. Aligns with AGENTS.md invariants
  ("Rules handle common cases first… LLMs run only on escalation", "derived facts
  belong in the state store, not prompts").
- Scope is **one chain model per minister snapshot**, rendered above the cards on
  the existing snapshot envelope (the `advice.md` "whole minister read" surface) —
  *not* a new `AdviceItem` field (the LLM fills `AdviceItem`; the chain must stay
  server-authoritative and deterministic).
- Food only for v1 (only `Ready` feeder minister), but the carrier type is
  minister-agnostic so other ministers can populate it later.

Single source of truth: server derives one structured `AdviceChainModel`. The
dashboard route-card view renders directly from it. The structured model is
what's persisted to the replay corpus.

## Approach

### Backend — derive the chain model (deterministic, pure)

**New `Src/Common/Briefings/AdviceChainModel.cs`** (project `RimBob.Core.Briefings`,
no external deps, mirrors `FoodBriefing`/`FoodStateSummary` placement):

```
enum AdviceChainStepStatus { Trigger, Action, Have, Available, Blocked }
record AdviceChainStep(string Key, string Label, string Detail, AdviceChainStepStatus Status)
record AdviceChainPath(string Name, IReadOnlyList<AdviceChainStep> Steps)
record AdviceChainModel(IReadOnlyList<AdviceChainPath> Paths)
```

Status → color (used by route cards):
`Trigger` = amber/red (the "low food" deficit), `Action` = blue (a step this
advice's `AdviceType`/`AdviceAction.Kind` drives), `Have` = green (sufficient
capacity/stock), `Available` = gray (path exists but idle), `Blocked` = red
(prerequisite missing, e.g. no cooler / no cook bill / no targets).

**New `Src/Common/Briefings/FoodChainModelBuilder.cs`** — pure
`Build(FoodBriefing briefing, IReadOnlyList<AdviceItem> advice) -> AdviceChainModel`.
Reuse the labeling/threshold patterns already in
[FoodStateSummary.cs](Src/Common/Briefings/FoodStateSummary.cs) (days-of-food
posture bands, `LabelCrop`/`LabelAnimal`, cooking-bill lookup). Paths and the
fields that drive each step's status:
- **Trigger node**: `EstimatedDaysOfFood` vs colonist count (posture bands reused
  from `FoodStateSummary`).
- **Grow path**: `CropZoneSummaries`/`CropBreakdown` → `ReadyToHarvest` → Cook →
  Store (`Storage`, `Infrastructure.Coolers`) → Meals (`MealsCount`).
- **Hunt path**: `WildHuntTargets`/`WildAnimalCount` → Butcher
  (`Kitchen.ButcherTables`) → Cook → Store → Meals.
- **Forage path**: `WildHarvestCandidates`/`WildHarvestClusters` → Cook → ...
- **Cook step** status from `Kitchen.CookingBuildings` + the simple-meal bill
  lookup already done in `FoodStateSummary.FormatSimpleMealBill`.
- **Action (blue)**: map emitted `AdviceItem.AdviceType` (FoodAdviceType) and
  `AdviceAction.Kind` to the step(s) they drive (e.g. `HarvestNow`/`mark_harvest`
  → grow.harvest; `ManageCookBills` → cook; `HuntForFood`/`mark_hunt` → hunt;
  `WildHarvest` → forage; `ExpandGrowingCapacity` → grow.zones;
  `ManageFreezer` → store). Union across all active advice items in the snapshot.
- If `!DataCoverage.HasLiveState`, return an empty `AdviceChainModel` (no paths)
  so the dashboard renders nothing — consistent with `FoodStateSummary`'s
  unhydrated-state guard.

**Snapshot carrier** — [AdviceBus.cs](Src/Coordination/AdviceBus.cs):
- `AdviceSnapshot` record (line 173): add `AdviceChainModel? Chain = null` and
  `IReadOnlyDictionary<string, AdviceChainModel>? Chains = null`, exactly
  paralleling `StateSummary`/`StateSummaries`.
- Add `_ministerChains` dict; new optional `AdviceChainModel? chain = null` param
  on `ReplaceMinisterAdvice` (line 31); store/clear it like
  `_ministerStateSummaries` (lines 67-70); include in the
  `AdviceSnapshotPublished` invoke (line 77); preserve it in `ActiveSnapshot()`
  (line 145) and `RemoveAppliedAction` (line 126) the same way `StateSummaries`
  is rebuilt.

**Minister wiring** — [MinisterOfFood.cs](Src/Ministers/Food/MinisterOfFood.cs):
- In `PublishSnapshot` (line 138) accept the model and pass to
  `ReplaceMinisterAdvice`. Compute via `FoodChainModelBuilder.Build(briefing,
  advice)` at both call sites — rules path (line 50) and LLM path (line 93).
- Add the model to `MinisterReplayEntry` for replay/`minister-refine`
  traceability, mirroring how `StateSummary` is already recorded (verify exact
  `MinisterReplayEntry` field name before editing).

**SSE serialization** —
[AgendaStreamEndpoint.cs](Src/ApiHost/Endpoints/AgendaStreamEndpoint.cs)
`WriteAdviceSnapshotAsync` (line 138): add `chain = snapshot.Chain` and
`chains = snapshot.Chains` to the anonymous payload (snake_case via existing
serializer options → `chain` / `chains`).

### Frontend — render route cards

- **[types/advice.ts](Dashboard/src/types/advice.ts)**: add `AdviceChainStep`,
  `AdviceChainPath`, `AdviceChainModel` interfaces; extend `AdviceSnapshot`
  (line 97) with `chain?: AdviceChainModel | null` and
  `chains?: Record<string, AdviceChainModel> | null`.
- **[types/system.ts](Dashboard/src/types/system.ts)** `FeedState` (line 187):
  add `chains: Record<string, AdviceChainModel>`.
- **[adviceFeedReducer.ts](Dashboard/src/hooks/adviceFeedReducer.ts)**: add
  `mergeChains` paralleling `mergeStateSummaries` (lines 222-239); update the
  `adviceSnapshotReceived` case (line 127) and `initialAdviceFeedState`
  (line 38, add `chains: {}`).
- **[ministerViewRegistry.tsx](Dashboard/src/dashboard/ministerViewRegistry.tsx)**:
  add `chains: Record<string, AdviceChainModel>` to `MinisterViewContext`
  (line 13); in the `advice` renderer (line 41) pass
  `chain={chains[scope.label] ?? chains[scope.key] ?? null}` to
  `MinisterAdviceView`. Supply `chains` from `feed.chains` at the context
  construction site (same place `stateSummaries: feed.stateSummaries` is set —
  follow that wiring; `MinisterWorkspace` only spreads `...context`).
- **New `Dashboard/src/components/shared/ChainTable.tsx`**: one compact card per
  `AdviceChainPath`, rendered side by side as Grow, Hunt, and Forage. Shared
  start/end steps remain in the model but are excluded from the compact visual
  summary so `Food buffer` and `Meals` are not repeated three times. Each card
  renders icon-led short rows such as Zone, Crops, Harvest, Cook, Store, Target,
  Butcher, and Plants.
- **[MinisterAdviceView.tsx](Dashboard/src/components/minister/MinisterAdviceView.tsx)**:
  add prop `chain: AdviceChainModel | null`; render a new `<section
  className="advice-chain">` (eyebrow "Food Routes") between the
  `advice-state-summary` section (line 80) and `advice-stack` (line 84),
  containing `<ChainTable>`. Non-mayor scopes only; render nothing when `chain`
  is null/empty.
- **[styles.css](Dashboard/src/styles.css)**: add `.advice-chain` container
  styling near `.advice-state-summary` and `.chain-cell--action / --have /
  --available / --blocked` color tokens reusing existing palette vars so the
  route colors stay consistent across dashboard refreshes.

### Docs (same-turn per AGENTS.md routing)

- `Docs/design/ministers/food.md` — food chain model, path/step catalogue,
  status semantics, the route-card visualization.
- `Docs/design/advice.md` — snapshot envelope now also carries a deterministic
  chain model (whole-minister read, like the state summary); not an `AdviceItem`
  field.
- `Docs/design/dashboard.md` — new "Food Routes" panel above the
  advice cards; new `chain`/`chains` SSE snapshot fields.
- Copy this approved plan to `Docs/plans/advice-chain-visualization.md` as the
  first implementation step (AGENTS.md: agent plans live in `Docs/plans/`; plan
  mode only permitted me to write the harness plan file).

## Critical Files

- New: `Src/Common/Briefings/AdviceChainModel.cs`,
  `Src/Common/Briefings/FoodChainModelBuilder.cs`
- Edit: `Src/Coordination/AdviceBus.cs`, `Src/Ministers/Food/MinisterOfFood.cs`,
  `Src/ApiHost/Endpoints/AgendaStreamEndpoint.cs`
- New: `Dashboard/src/components/shared/ChainTable.tsx`
- Edit: `Dashboard/src/types/advice.ts`,
  `Dashboard/src/types/system.ts`,
  `Dashboard/src/hooks/adviceFeedReducer.ts`,
  `Dashboard/src/dashboard/ministerViewRegistry.tsx`,
  `Dashboard/src/components/minister/MinisterAdviceView.tsx`,
  `Dashboard/src/styles.css`
- Tests: add `FoodChainModelBuilder` unit tests under `Src/Tests/Food/` (status
  mapping per FoodAdviceType, unhydrated guard, hunt/grow/forage path shapes),
  reusing fixture JSON in `Src/Tests/Food/Fixtures/`; extend
  `Src/Tests/Coordination/AdviceBusTests.cs` for chain carry/preserve.

## Verification

- `dotnet test` — new `FoodChainModelBuilder` tests + AdviceBus snapshot tests
  green; full suite still green.
- `cd Dashboard && npm run build` — `tsc` + vite build clean.
- End-to-end: `.\run-rimbob.ps1`, open the dashboard Food → Advice view with
  live RimWorld/RIMAPI state. Confirm above the advice list: three small route
  cards labelled Grow, Hunt, and Forage, with distinct route colors, icon-led
  short rows, blue action-requested rows, green covered rows, gray future rows,
  and red blocked rows. Confirm the panel disappears when state is unhydrated and
  re-appears on refresh.
- Inspect a replay corpus record to confirm the structured chain model is
  persisted alongside the existing state summary.
