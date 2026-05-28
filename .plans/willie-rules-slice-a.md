# Willie Rules Slice A — Implementation Plan

> Implementation plan. Lands **code** in RimBob. No fork work.
>
> First deterministic rule cut for the Willie minister, mirroring Food's
> `Rules.cs`. Reads `WillieBriefing` (landed `3235902`) and emits
> `AdviceItem`s. Rules-first; **no LLM** in this slice.
>
> Design sources: [`willie-advice-types.md`](willie-advice-types.md) (concern
> set + Slice-A split), [`willie-briefing-schema.md`](willie-briefing-schema.md)
> §S4 (rule-vs-aspiration cut), [`willie-advice-schema.md`](willie-advice-schema.md)
> (advice/output shape).
>
> Sliced by risk — **gimp one slice at a time**. **No compat code;
> wipe-and-regen on upgrade** for any persisted advice/concern wire shape.
> **Worktree port** `http://127.0.0.1:5101` (5000 reserved for main checkout).

---

## 0. Motivation

The briefing record + derivation landed, but nothing reads it. Willie Rules
Slice A is the deterministic skeleton that turns `WillieBriefing` fields into
player-facing advice — the first time Willie produces output. Per AGENTS,
`Rules.cs` is the first file in a minister directory and must compile + have
tests before the LLM is wired.

**Hard constraint discovered from the landed code.** The landed
`WillieConcernSummaries` (`Src/Common/Briefings/WillieConcernSummaries.cs`) is
**minimal** — it carries presence counts and aggregates, not the full S1 field
set. So the achievable Slice-A rule cut is **narrower** than
`willie-briefing-schema.md` §S4's theoretical "real-now" list. Rules Slice A
ships only the rules whose summary fields are populated **today**; the rest
become derivation-extension follow-ups (§4 below), not dead rules.

Populated today (shippable rules):

| concern | landed summary fields | Slice-A rule(s) |
|---|---|---|
| `power_stability` | `ProductionW/ConsumptionW/StoredWd/CapacityWd/NetW/GeneratorCount/BatteryCount` (full) | `power_net_deficit` (`NetW < threshold`), `low_battery_reserve` (`StoredWd/CapacityWd < 0.25`) |
| `material_bottleneck` | `MissingMaterials`, `BacklogGroups`, `BlockedCount` | `backlog_material_gap` (`MissingMaterials` non-empty) |
| `stalled_builds` | `BacklogGroups`, `PendingBuildCount`, `BlockedCount` | `frame_blocked_by_material` (`BlockedCount > 0`) |
| `functional_rooms` | `RoomCountsByClass` | `kitchen_missing` / `hospital_missing` / `storage_room_missing` (presence) |
| `thermal_control` | `CoolerCount/HeaterCount/FreezerAnchorCount` + inbound flag | `freezer_request_active` (inbound `building_request`), `cooler_missing` (`CoolerCount == 0 && FreezerAnchorCount > 0`) |

Populated but **too coarse for any §S4 rule** (no Slice-A rule; not a gap):

| concern | landed summary fields | why no rule yet |
|---|---|---|
| `storage_placement` | `StockpileZones`, `StockpileCells` (presence/count only) | every §S4 storage rule needs a *distance* or *saturation* metric (`NearestKitchenDistanceCells`, `CoolerAdjacentFoodUnits`, SlotGroup saturation) that the summary does not carry — see §4 `willie-storage-distance`. |
| `fire_risk` | `WoodStructureCount` (map-wide count) | §S4 lists `fire_risk` with **zero** real-now rules; per-room wall material / membership is `need-fork` (`rimapi-building-detail-read` / `rimapi-room-detail-read`). A map-wide wood count cannot localize the risk. |
| `base_layout` | `RoomCount`, `BuildingCount` (totals only) | the real-now §S4 rule `food_chain_loop_length_long` needs per-anchor *centroid distance* (euclidean over kitchen/freezer/dining anchors), not room/building totals — the "anchor-to-anchor loop distance" derivation in §4. |

Not populated at all → **deferred** to derivation extensions (§4): per-room
temperature, storage distance metrics, room cells/quality, wall material,
frame age, constructor work-priority, anchor-to-anchor loop distance. The §S4
rules that depend on these (`room_too_hot`, `food_stockpile_far_from_kitchen`,
`room_undersized_for_purpose`, `room_quality_low`, `wood_wall_in_critical_room`,
`frame_age_exceeded`, `no_assigned_constructor`, `food_chain_loop_length_long`)
wait for those fields.

Per-slice motivation:

- **WR1** — `WillieConcern` enum + flag-request helper. The concern enum is
  the unit `Rules.cs` returns; mirror `FoodConcern`. Must exist before any
  rule.
- **WR2** — `Rules.cs`. The deterministic threshold/presence rules against
  populated fields + the Food-style empty-state success trace.
- **WR3** — `MinisterOfWillie` + registry/DI/cabinet order + dashboard scope,
  so the briefing + advice actually render (read-only minister, suggest-mode).
- **WR4** — tests + fixtures (rules compile + tested before any LLM).

---

## 1. Slices (gimp independently, in order)

Minister namespace `RimBob.Ministers.Willie`; minister directory
`Src/Ministers/Willie/`.

### WR1 — `WillieConcern` enum + `WillieFlagRequests` helper  *(CONTRACT)*

Files (new):

- `Src/Common/Advice/WillieConcern.cs`
  - Closed enum, snake_cased on the wire, mirroring `FoodConcern.cs` style.
    8 members: `PowerStability`, `ThermalControl`, `FunctionalRooms`,
    `StoragePlacement`, `MaterialBottleneck`, `FireRisk`, `StalledBuilds`,
    `BaseLayout` (per `willie-advice-types.md` §1; `basic_shelter` is
    Welfare per §4.5 — do **not** add it).
- `Src/Ministers/Willie/WillieFlagRequests.cs`
  - Helper mirroring `FoodFlagRequests`: `Building(...)`, `Labor(...)`,
    `Item(...)` builders that produce the typed request arrays on an
    `AgentFlag` (the request taxonomy is already landed in schema S2).

Tests (new):

- `Src/Tests/Willie/WillieConcernTests.cs` — enum ↔ snake_case wire round-trip.

Docs to touch: `Docs/design/ministers/construction.md` — record the 8-member
concern enum is now code (replace the stale concern list with a pointer to
`willie-advice-types.md`); `Docs/design/advice.md` if it enumerates
per-minister concern enums.

Risk: **low.** Additive enum + helper.

### WR2 — `Rules.cs` deterministic cut  *(CORE)*

Files (new):

- `Src/Ministers/Willie/Rules.cs`
  - `IMinisterRules<WillieBriefing>` (`Src/Common/Ministers/IMinisterRules.cs`).
  - `Evaluate(WillieBriefing briefing, ColonyContext context) → RulesResult`.
    The interface `Evaluate` keeps the landed two-arg shape; **do not change
    `IMinisterRules`** (Food depends on it — no-compat clause).
  - Rule families (populated-field cut from §0). Order by priority: power
    deficit → material/stalled → missing functional rooms → thermal.
  - **Empty-state success** mirrors Food: a clean base publishes a
    successful empty snapshot (trace e.g. `maintain_build_program`), not a
    noisy rule.
  - `freezer_request_active`: detect an inbound `building_request` with
    `Temperature.TargetBand == Freezing`. **Correction (verified against
    landed code):** `ColonyContext` (`Src/Common/Ministers/ColonyContext.cs`)
    carries only Mayor posture — it has **no** flags. Inbound requests live on
    `AgentFlag`s in `FlagChannel.Active()` (`Src/Coordination/FlagChannel.cs`),
    which the *minister* holds, not `Evaluate`. So WR3's `MinisterOfWillie`
    reads `flags.Active()`, extracts the freezing `BuildingRequest`s, and hands
    them to the rules — e.g. a small `WillieRulesInput`/overload
    `Evaluate(briefing, context, inboundRequests)` that the public
    `IMinisterRules.Evaluate` delegates to with an empty list. Do **not** try
    to read flags off `context`. Slice A emits prose advice + a
    `place_blueprint` action **without** an executable Apply.
    - `// TODO: call PlacementSolver.SolveAsync (placement-solver-1.md)
      once Solver1 lands to attach options[] + a fork-validated blueprint_group;
      until then the freezer rule is prose-only.`

Tests: see WR4.

Docs to touch: none beyond WR1.

Risk: **medium.** First Willie rule logic; thresholds need calibration TODOs.

### WR3 — `MinisterOfWillie` + registration  *(WIRING)*

Files (new):

- `Src/Ministers/Willie/MinisterOfWillie.cs`
  - Minister class mirroring `Src/Ministers/Food/Chef.cs`: holds the rules,
    publishes the `WillieBriefing`, exposes suggest-mode advice. Read-only
    (no Apply path this slice).
  - Takes `FlagChannel` as a constructor dependency (as `Chef` does) and, per
    the WR2 correction, reads `flags.Active()` to source inbound freezing
    `building_request`s, then passes them to the rules' freezing-request
    overload. `Evaluate(briefing, ColonyContext.Default)` is the call site for
    everything else.

Files (modified):

- Minister registry / DI / cabinet-order registration (mirror where `Chef`
  registers — `refactor-minister-registry` added the descriptor pattern;
  find the registration site at touch-time). Cabinet order: Construction
  lands **after Food** (meta-plan milestone note).
- Dashboard scope: `Dashboard/src/` minister tab + briefing view so the
  Willie briefing + advice render. Mirror the Food tab. Per AGENTS, a new
  feature notes its dashboard surface — Willie gets a **Willie tab** with
  Briefing + Advice + Rules/debug views.
  - `// TODO: Anchor Inventory / Construction Backlog / Data Coverage panels
    per willie-briefing-derivation.md §4 dashboard note.`

Tests: see WR4.

Docs to touch: `Docs/design/dashboard.md` if it enumerates minister tabs;
`Docs/design/ministers/construction.md` (minister now wired, suggest-mode).

Risk: **medium–high.** Touches registry + dashboard (live surfaces).
Keep read-only; no Apply.

### WR4 — tests + fixtures  *(VERIFICATION)*

Files (new):

- `Src/Tests/Willie/WillieRulesTests.cs` — one test per rule family:
  - power deficit fires on `NetW < 0`; low-battery on reserve ratio.
  - `backlog_material_gap` fires on non-empty `MissingMaterials`.
  - `frame_blocked_by_material` fires on `BlockedCount > 0`.
  - `kitchen_missing` fires when `RoomCountsByClass` has no `Kitchen` key
    (dict is keyed by `RoomClass.ToString()`, compared case-insensitively).
  - `freezer_request_active` fires when an inbound freezing `building_request`
    is fed in via the rules' freezing-request overload (not via
    `ColonyContext`) and emits prose (no Apply).
  - clean base → empty success trace (`maintain_build_program`).
- `Src/Tests/Willie/Fixtures/` — canned `WillieBriefing` JSON per case
  (AGENTS: fixtures under `Src/Tests/<MinisterName>/Fixtures/`).
- `Src/Tests/Willie/MinisterOfWillieTests.cs` — minister publishes a briefing
  + advice end-to-end against a fixture state.

Risk: **low.** Test addition.

---

## 2. Keep-green (every slice)

- Sync worktree with `master` before verification builds.
- `dotnet build Src/RimBob.sln`; `dotnet test Src/Tests/RimBob.Tests.csproj`.
- `npm.cmd run build` from `Dashboard` (WR3 touches the dashboard).
- `.\run-rimbob.ps1 -ListenUrl http://127.0.0.1:5101`; verify
  `/api/system/health` + the Willie tab renders the briefing.

---

## 3. Recommended gimp order

1. **WR1** — enum + helper (mechanical).
2. **WR2** — rules against populated fields (logic; calibration TODOs).
3. **WR4 tests** can land with WR2 (rules tested before LLM, per AGENTS).
4. **WR3** — minister + registry + dashboard last (live surfaces; biggest
   blast radius).

---

## 4. Derivation-extension follow-ups (separate captures)

The landed `WillieConcernSummaries` is minimal. To unlock the deferred §S4
rules, `WillieBriefingDerivation` must populate richer fields. Each is a
small follow-on (capture in HumanTodo when scheduled):

- **`willie-thermal-room-temps`** — add per-room temperature + role to
  `WillieThermalControlSummary` (reads landed `RoomRecord.Temperature` /
  `RoomRecord.RoleLabel`; `freezer_lost_seal` also reads landed
  `RoomRecord.OpenRoofCount`). Unlocks `room_too_hot` / `room_too_cold` /
  `freezer_lost_seal`.
- **`willie-storage-distance`** — add nearest-stockpile-to-kitchen distance +
  cooler-adjacency to `WillieStoragePlacementSummary` (mirror
  `FoodStorageSummary`). Unlocks `food_stockpile_far_from_kitchen` /
  `food_storage_split_from_freezer`.
- **`willie-room-quality-fields`** — add `CellsCount` + quality stats per
  room class to `WillieFunctionalRoomsSummary` (reads landed
  `RoomRecord.CellsCount`, `RoomRecord.Impressiveness`, `RoomRecord.Beauty` —
  the quality fields are nullable, so the rule must null-guard). Unlocks
  `room_undersized_for_purpose` / `room_quality_low`.
- **`willie-frame-age`** — state-store snapshot diff for frame first-seen
  cycle. Unlocks `frame_age_exceeded`.
- **`willie-constructor-priority`** — count pawns with `Construction` work
  enabled into `WillieStalledBuildsSummary`. Unlocks `no_assigned_constructor`.

(These were `real-now` in §S4 but the derivation shipped minimal; they are
"real after a derivation extension", not blocked on any fork endpoint.)

---

## 5. Out of scope (separate plans)

- **Placement Solver Solver1** — [`placement-solver-1.md`](placement-solver-1.md).
  Rules Slice A calls `SolveAsync` only after Solver1 lands (TODO in WR2).
- **LLM escalation (Slice C)** — Willie prompt + RAG; after rules + tests.
- **Apply path** (`place_blueprint_group` executor) — bundles with the
  Assisted Apply work, not this slice.
- **`fire_risk` / `base_layout` rules** — blocked on fork reads
  (`rimapi-building-detail-read`) or topology derivations; deferred per §S4.
- The derivation extensions in §4 — each its own slice.

---

## 6. Verification (per slice, before commit)

- WR1: enum wire round-trip green.
- WR2: each rule family fires on its fixture; clean base → empty success
  trace; no rule reads a `need-fork`/unpopulated field.
- WR3: minister registered; Willie tab renders briefing + advice in suggest
  mode (no Apply button); cabinet order = after Food.
- WR4: full rules + minister test pass; fixtures cover fire and empty cases.

---

## 7. HumanTodo capture (append in same commit as this plan)

```
- [ ] willie-rules-slice-a [2026-05-28] #construction #willie #rules First deterministic Willie rule cut: WillieConcern enum (WR1), Rules.cs against populated WillieConcernSummaries fields (WR2), MinisterOfWillie + registry + dashboard tab (WR3), tests + fixtures (WR4). Scoped to landed-minimal briefing fields; richer rules wait on derivation extensions (§4). [plan](.plans/willie-rules-slice-a.md)
```
