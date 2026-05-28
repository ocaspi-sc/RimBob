# Willie - Meta-Plan

> **Master index + roadmap** for the Willie minister effort. Willie owns the
> construction domain; schema/code names use `Willie*` rather than `Construction*`.
>
> This doc ties together the design anchors, implementation plans, RIMAPI work,
> locked decisions, remaining work, and open decisions. It is the single place to
> see where the Willie effort stands. Detail lives in the linked plans;
> exact C# lives in source after landing.
>
> **Milestone:** M4 (first cabinet wave); Willie lands **first** after Food
> (Food's first live dependencies are cooler/power/room/storage builds). Design
> phase ~complete; **no Willie minister code landed yet.**

---

## 1. North-star chain (the vertical slice everything serves)

```
Food emits building_request
  { target_class: freezer,
    capacity_need: {food_units, 200},
    adjacency: [near kitchen],
    temperature: {freezing},
    deadline: {by_day, 15} }
-> Willie Rules see an active building_request (deterministic; no LLM)
-> Placement Solver:
     anchor (near kitchen) -> size (200 food -> ~5x5)
     -> precompute map evidence
     -> run bounded candidate generators
     -> hard-gate, cheap-score, dedupe, pick diverse survivors
     -> fork-validate survivors, estimate materials
-> AdviceItem (concern=thermal_control) with options[] (top 1-3 layouts)
-> player picks one in Willie's dashboard tab
     (sends advice-id + option-id)
-> Host wraps chosen blueprint_group as a place_blueprint_group apply;
   fork-validates
-> player clicks Apply -> fork validate -> place
     -> read the group (floor/walls/door/cooler)
```

This is the milestone-exit demo. It crosses the schema, the request taxonomy, the
Placement Solver, the fork group endpoints, and the dashboard pick UI.

---

## 2. Artifact map

### Design anchors - the "what"

| Plan | Holds |
|---|---|
| [`willie-advice-types.md`](willie-advice-types.md) | The 9 canonical Willie concerns + first-slice rules-vs-LLM split. |
| [`willie-advice-schema.md`](willie-advice-schema.md) | Advice/output side: flatten (drop icon/reason), per-kind apply split, `options[]`, `blueprint_group`, `place_blueprint_group`. |
| [`willie-request-taxonomy.md`](willie-request-taxonomy.md) | Request/input side: typed request arrays, rich `BuildingRequest`, inbound ask-map -> concern. |
| [`willie-briefing-schema.md`](willie-briefing-schema.md) + [`willie-briefing-fields.md`](willie-briefing-fields.md) | Willie Briefing Schema: per-field `signal/source/availability/consumers/notes` for the 8 canonical concerns + anchor inventory contract. **S1–S5 landed 2026-05-27.** |

### Engine designs - the "how Willie decides where to build"

| Plan | Holds |
|---|---|
| [`placement-solver.md`](placement-solver.md) | Deterministic engine: `building_request`/intent -> `PlacementSpec` -> bounded candidate generator registry -> shared gates/scoring -> top 1-3 validated `AdviceOption`s. **No LLM.** |
| [`wfc-variant-generator.md`](wfc-variant-generator.md) | Optional/experimental Wave-Function-Collapse candidate generator for internal layouts inside a fixed rectangle. Not the core solver. |

### Implementation plans - land code in RimBob

| Plan | Holds |
|---|---|
| [`schema-landing.md`](schema-landing.md) | Lands the schema anchors as code. **S1** additive cleanup + dormant types; **S2** typed flag request arrays (Food emits `building_request`); **S3** apply per-kind split (bundle with fork). Gimp one slice at a time. |

### RIMAPI work (lands in `C:\dev\RIMAPI-for-RimBob`, HTTP-only integration)

| Plan | Holds |
|---|---|
| [`rimapi-blueprint-placement-endpoint.md`](rimapi-blueprint-placement-endpoint.md) | Single-asset blueprint `validate` / `place` / `read` triplet (the primitive). |
| [`rimapi-blueprint-groups-and-planning-overlay.md`](rimapi-blueprint-groups-and-planning-overlay.md) | Capability A: blueprint **groups** (room shell + contents). Capability B (future): planning overlay via vanilla `Designator_Plan`. |
| [`rimapi-map-reach-and-path-cost.md`](rimapi-map-reach-and-path-cost.md) | In-map reachability + path cost (`/map/reach`, `/map/path-cost`, batch). Region-BFS for coarse rank, A* for tiebreak. Unblocks real `near:<class>` solver scoring beyond euclidean-from-centroid. |

### Background / supporting plans

| Plan | Holds | Kind |
|---|---|---|
| [`base-construction-layout-agent.md`](base-construction-layout-agent.md) | Original 10-phase Willie implementation plan + Slices A/B/C (mirror-Food file list). | Primary source for the Willie-minister implementation phase. |
| [`base-layout-construction-tips.md`](base-layout-construction-tips.md) | Community base-building heuristics -> spatial-lint signals + dashboard panels. | Research input for solver heuristics. |
| [`deterministic-cos-cabinet-issue-solver.md`](deterministic-cos-cabinet-issue-solver.md) | Issue-report model + CoS routing (Willie issue families map onto the 9 concerns). | Cross-minister design. |

### Grounding

- RAG corpus: `Docs/guides/Base/*` (rooms, colony-building, defense-structures,
  efficient layouts, killbox, planner, more-planning-mod) + `Docs/guides/beginner/*`.
- Canonical design docs: [`../Docs/design/ministers/construction.md`](../Docs/design/ministers/construction.md),
  [`../Docs/design/ministers.md`](../Docs/design/ministers.md), [`../Docs/DESIGN.md`](../Docs/DESIGN.md).
- Reference implementation: `Src/Ministers/Food/` (the only fully-built feeder;
  Willie mirrors it).

---

## 3. Locked decisions

- **Minister name Willie.** No separate Base Layout minister; layout is a
  capability inside Willie's construction domain. Schema/code names use
  `WillieBriefing`, `WillieAdvice`, and `MinisterOfWillie`.
- **8 concerns** ([`willie-advice-types.md`](willie-advice-types.md)):
  `power_stability`, `thermal_control`, `functional_rooms`,
  `storage_placement`, `material_bottleneck`, `fire_risk`, `stalled_builds`,
  `base_layout`. `base_topology` folded into `base_layout` (dashboard grouping
  only). `basic_shelter` moved to Welfare 2026-05-27 — survival-floor bedrooms
  reach Willie via `BuildingRequest{ room_class: bedroom | barracks }` under
  `functional_rooms`, same shape as Food's freezer request (see
  `willie-advice-types.md` §4.5). `functional_rooms` and `storage_placement`
  both kept (orthogonal). No generic `build_structure` concern - that is the
  `place_blueprint` action, not a category.
- **MVP posture = suggest + player-confirmed apply** (not suggest-only). Every
  write is player-click-gated; coverage partial; not `Auto`.
- **Typed request arrays** ([`willie-request-taxonomy.md`](willie-request-taxonomy.md)):
  `building_requests[]` / `labor_requests[]` / `item_requests[]` / `attention[]`
  replace the generic `requests[]`. `BuildingRequest` is rich (`target_class`,
  `room_class`, `capacity_need`, `adjacency`, `power`, `temperature`, `deadline`,
  etc.). `requested_from` stays a **string** (not an enum) for now.
- **Advice flatten:** drop `AdviceAction.icon` (dashboard derives) and `.reason`
  (redundant with advice-level rationale).
- **`AdviceActionApply` per-kind split** (STJ polymorphic) + new
  `place_blueprint_group` kind. A single placement is a length-1 group (no
  separate single kind).
- **`AdviceItem.options[]`** for multi-option advice; each `AdviceOption` carries
  a `blueprint_group`. Dashboard pick sends only advice-id + option-id; the
  Apply click stays the consent boundary.
- **LLM scope:** never authors exact cells; reserved for genuine judgment
  (intent, not placement). Request-driven build = deterministic Placement Solver
  path; LLM only invoked when the input is unstructured. Placement Solver
  computes placement; LLM proposes intent only.
- **Candidate generation is competitive, validation is shared.** Several bounded
  generators can propose drafts, but one shared solver owns hard gates, score
  vector, dedupe/diversity, fork validation, and final top 1-3 option selection.
  Generators do not get private scoring or bypass RIMAPI validation.
- **Score traces carry units.** Measured metrics keep raw values with explicit
  units (`tiles`, `watts`, `celsius`, item counts, `silver_value`, etc.) beside
  normalized 0-1 ranking values. Example shape:
  `{metric:"distance_to_kitchen", raw:7, unit:"tiles", normalized:0.82}`.
  Boolean/enum readiness and validation facts stay typed status fields, not
  fake metrics.
- **Blueprint groups** placed atomically-ish on one player Apply. **Planning
  overlay** (future) uses the vanilla `Designator_Plan`, not forbidden
  blueprints.
- **WFC** is an optional future generator inside a validated rectangle, never the
  core solver, never bypassing live-state validation.
- **No compat code for proposal/trace persistence changes.** Scope: local
  on-disk state only (no external consumers). If the solver option or trace
  wire shape changes, wipe-and-regen on upgrade.

---

## 4. Remaining work (ordered, with dependencies)

```mermaid
flowchart TD
  S1["Schema S1 ✓ LANDED 7d0818c<br/>(cleanup + dormant types)"] --> S2["Schema S2 ✓ LANDED 8676d59<br/>(typed request arrays;<br/>Food emits building_request)"]
  FORK1["RIMAPI single-asset triplet ✓ LANDED 3eb1d84<br/>(RIMAPI repo; RimBob meta 300f5ce)"] --> FORK2["RIMAPI blueprint groups Cap A<br/>✓ Slice A LANDED (group validate/place)"]
  S2 --> SCHEMA["Willie Briefing Schema ✓ LANDED 2026-05-27<br/>(willie-briefing-schema.md +<br/>willie-briefing-fields.md)"]
  FORK2 --> SCHEMA
  FORK2 --> ROOM["Room/anchor detection<br/>(room-purpose inference)"]
  ROOM --> SCHEMA
  SCHEMA --> DERIV["Willie briefing derivation ✓ LANDED 3235902<br/>(WillieBriefing + backlog ingest +<br/>anchor inventory + FORK3 client)"]
  DERIV --> Solver1["Placement Solver Solver1<br/>(template freezer skeleton + generator registry)<br/>← UNBLOCKED, next active"]
  S2 --> Solver1
  FORK2 --> Solver1
  FORK3["RIMAPI map reach + path-cost ✓ LANDED<br/>(rimapi-map-reach-and-path-cost.md)"] -.solver scoring.-> Solver1
  DERIV --> WILLIE["Willie minister ✓ LANDED e902e96<br/>(Rules.cs + MinisterOfWillie + registry/DI;<br/>rules read briefing directly, no solver dep)"]
  S1 --> S3["Schema S3 ✓ LANDED 4927741<br/>(apply per-kind split +<br/>place_blueprint_group)"]
  FORK2 --> S3
  S3 --> WILLIE
  Solver1 -.freezer options[].-> WILLIE
  WILLIE --> DASH["Willie dashboard ✓ LANDED ddef5f3<br/>(readout tab; anchor/backlog/coverage/<br/>solver-trace panels = follow-on)"]
  DERIV -.feeds panels.-> DASH
  Solver1 -.solver trace.-> DASH
  Solver1 --> Solver2["Solver2 generator competition<br/>(template + rectangle + pattern)"] --> Solver3["Solver3 breadth + reuse existing rooms"]
  WILLIE --> PROMOTE["Promote design -> construction.md"]
  Solver3 --> WFC["(optional) WFC generator spike"]
  Solver3 --> SOLVER4B["Solver4 + planning overlay (Cap B)"]

  classDef done fill:#1f3a1f,stroke:#3fa83f,color:#cfe8cf;
  class S1,S2,S3,FORK1,FORK3,SCHEMA,DERIV,WILLIE,DASH done;
  classDef partial fill:#3a341f,stroke:#a8993f,color:#e8e0cf;
  class FORK2 partial;
```

Note: FORK3 endpoints + the RimBob client wrappers (`GetReachAsync` /
`PostPathCostAsync` / `PostPathCostBatchAsync`) both landed (briefing-derivation
commit `3235902`). Solver1 can ship Slice A with euclidean-from-centroid scoring and
swap in walkable distance for Slice B with no new wiring. FORK2 Cap A: group
`validate` / `place` (Slice A) landed; remaining Cap A slices + Cap B (planning
overlay) pending.

**Phase ordering:**

1. **Schema landing** [✓ LANDED — S1 `7d0818c`, S2 `8676d59`, S3 `4927741`].
   Source of truth: `schema-landing.md`. Food emits typed `building_request`;
   `AdviceActionApply` poly-split with `place_blueprint_group`; `options[]` and
   `BlueprintGroup` dormant in `Src/Common/Advice/`.
2. **RIMAPI** - single-asset triplet [✓ LANDED — RIMAPI `3eb1d84`, RimBob
   meta `300f5ce`] -> blueprint groups Capability A: group `validate` / `place`
   [✓ Slice A LANDED]; remaining Cap A slices + Cap B planning overlay pending.
   Group `validate` is what the Placement Solver calls on candidate survivors.
3. **Willie Briefing Schema** [✓ LANDED 2026-05-27 —
   [`willie-briefing-schema.md`](willie-briefing-schema.md) +
   [`willie-briefing-fields.md`](willie-briefing-fields.md)]. Per
   `WillieBriefing` field: signal, RIMAPI/state-store source,
   availability (`have` / `need-fork` / `defer`), consumers, notes. Grounded
   by mining RimMind (`ConstructionBacklogPart`, `StorageSaturationPart`) and
   the known gaps (`source-todo-building-condition-read`,
   `rimapi-room-detail-read`, etc.). Gates which Slice-A rules are real vs
   aspirational; feeds solver anchor resolution.
   - **Power-net availability resolved (2026-05-27):** `/api/v1/map/power/info`
     is aggregate-only (gen/draw/battery totals + building id lists); per-net
     split + outage flag = `need-fork`. **`PowerInfoDto` fix landed** so
     aggregate fields surface end-to-end.
   - **Anchor scoring resolved (2026-05-27):** drop centroid-as-primary;
     anchor = `{room_id, entry_cells[], region_id}`. Solver does region-tier
     batch rank → A* top-K tiebreak. Both rely on **FORK3 endpoints (landed)**
     plus a RimBob client method (pending); Slice A ships with
     euclidean-from-centroid approximation as fallback.
4. **Room/anchor detection** - room-purpose inference from RIMAPI reads (resolve
   `near:kitchen` -> map location). The hard sub-problem; may gate Solver1. Depends
   on building/room reads (`source-todo-building-condition-read`,
   `source-todo-room-quality-read`) — i.e. on FORK2.
5. **Placement Solver** [← NEXT ACTIVE; deps green] - Solver1 skeleton (freezer,
   near-kitchen, `TemplateAnchoredGenerator`, one candidate) -> Solver2 competing
   generators (templates + rectangle + local patterns, top 1-3 options) -> Solver3
   more room classes and reuse-existing-footprint logic -> Solver4 base planning.
   Deps satisfied: S2 ✓, group `validate` ✓ (FORK2 Slice A), `WillieBriefing` +
   `WillieAnchorInventory` ✓ (commit `3235902`), FORK3 client ✓. Solver1 reads the
   briefing's anchor inventory; Slice-A scoring euclidean, Slice-B walkable.
6. **Willie minister** [✓ LANDED `e902e96`] - mirror Food: `Rules.cs`
   (rules-first; reads `WillieBriefing` directly) -> `MinisterOfWillie` ->
   registry/DI/cabinet order. Rules cut:
   [`willie-rules-slice-a.md`](willie-rules-slice-a.md) (WR1–WR4). Landed
   independent of the solver — the freezer rule's solver-backed `options[]`
   is a TODO until Solver1 lands (rules-only, suggest-mode, no Apply).
7. **Willie dashboard surfacing** [✓ readout LANDED `ddef5f3`] - the Willie
   tab + Willie state on the dashboard (AGENTS: "dashboard reflects exact
   state"). Readout tab landed. **Follow-on:** dedicated panels for **anchor
   inventory**, **construction backlog**, **data coverage**, and the **solver
   trace** (`generator_id`, hard-gate reasons, metric rows, readiness
   booleans) — the trace panel needs Solver1 first.
8. **Promote design -> `construction.md`** - keep canonical docs aligned once
   anchors settle.
9. **Optional/future** - WFC variant generator; planning overlay (Capability B);
   whole-base planning (Solver4).

**Tests:** Placement Solver tests live in `Src/Tests/PlacementSolver/`;
seed-based replay for solver determinism (same inputs -> same options ranking).
Per-minister tests live alongside their minister directory under `Src/Tests/`.

**Critical path:** [S1/S2/S3 ✓] [FORK1 ✓] [FORK2 group validate/place ✓]
[FORK3 ✓] [briefing schema + derivation ✓ `3235902`] -> **Solver1** ->
Willie Rules Slice A -> Willie minister wiring -> dashboard. The gates that
blocked everything spatial (briefing/data-gap + room/anchor detection) are
**cleared** — `WillieAnchorInventory` ships from the derivation. Solver1 is the
next active node.

---

## 5. Open decisions (need a human call)

| Decision | Where | Lean |
|---|---|---|
| Group atomicity on partial fresh-state failure + `MaxBlueprintGroupAssets` | advice-schema Q1 (see also rimapi-groups §4) | validate-all gate, then best-effort place + per-asset report; cap ~64 |
| Anchor/room-purpose detection approach | placement-solver Q1 | the hard gate; templated room detection first; RIMAPI `/api/v1/map/rooms` available today |
| Anchor scoring representation | placement-solver / willie-briefing-schema S3 | **resolved 2026-05-27:** `{room_id, entry_cells[], region_id}` + region-BFS scoring (FORK3); euclidean-from-centroid only as Slice-A fallback |
| Generator budgets and diversity thresholds | placement-solver Q3 | start with tiny per-generator caps; validate only a diverse top survivor set |
| Floor-fill representation (per-cell vs compressed rect) | advice-schema Q2 | per-cell now; cap room size; revisit if payloads bloat |
| `ResourceRequest` vs `AdviceAction` shared-shape refactor | both anchors flagged | partly mooted - S2 retires `ResourceRequest` from the flag path |
| Solver run cadence (per cycle vs on-demand) | placement-solver Q4 | on-demand when a `building_request` appears; bound fork `validate` calls |
| Picked-not-applied option survives a fresh snapshot? | advice-schema Q5, placement-solver Q5 | open |
| Feedback records *which* option was picked | advice-schema Q6 | yes - high-value refinement signal |

**Resolved:** `requested_from` = string; `base_topology` folded; `functional_rooms`
and `storage_placement` both kept; `build_structure` dropped; `basic_shelter`
moved to Welfare (2026-05-27 — see `willie-advice-types.md` §4.5);
suggest+apply posture; deterministic request -> solver path; candidate
generation uses a bounded generator registry with one shared validator/scorer.

---

## 6. Entry points for the next session

> Active items only — see §4 for the full phase graph.

- **Willie Briefing Schema ✓ LANDED 2026-05-27** —
  [`willie-briefing-schema.md`](willie-briefing-schema.md) +
  [`willie-briefing-fields.md`](willie-briefing-fields.md). 8 concerns;
  `basic_shelter` reconciled into Welfare (`willie-advice-types.md` §4.5).
- **Willie briefing derivation ✓ LANDED `3235902`** —
  [`willie-briefing-derivation.md`](willie-briefing-derivation.md) (WB1–WB4).
  `WillieBriefing` record, `WillieBacklog` aggregate + construction-backlog
  ingestion, `WillieAnchorInventoryDerivation`, FORK3 RimApiClient wrappers,
  docs (state-store / RimAPI / construction), tests. DTOs landed under
  `Src/GameStateSync/Dtos/`.
- **Willie minister (Rules Slice A) ✓ LANDED `e902e96`** +
  **dashboard readout ✓ LANDED `ddef5f3`** —
  [`willie-rules-slice-a.md`](willie-rules-slice-a.md) (WR1–WR4).
  `WillieConcern` enum, `Rules.cs`, `MinisterOfWillie`, registry, Willie tab.
  Rules-only, suggest-mode, no Apply; freezer rule's solver `options[]` is a
  TODO awaiting Solver1.
- **← NEXT ACTIVE: Placement Solver Solver1.** All deps green (S2, FORK2 group
  validate, briefing + anchor inventory, FORK3 client). Skeleton:
  freezer / near-kitchen / `TemplateAnchoredGenerator` / one candidate /
  group-validate / single option. Plan:
  [`placement-solver-1.md`](placement-solver-1.md) (Solver1a–d; refreshed +
  review-hardened). Adds the group-validate client method (not yet landed).
- **RIMAPI track (parallel, separate repo):**
  - `FORK1` triplet ✓ landed.
  - `FORK2` blueprint groups Capability A — group `validate` / `place`
    ✓ Slice A landed; remaining Cap A slices + Cap B planning overlay pending.
  - `FORK3` map reach + path-cost
    ([`rimapi-map-reach-and-path-cost.md`](rimapi-map-reach-and-path-cost.md))
    ✓ endpoints landed; RimBob client wrappers ✓ landed (`3235902`).
  - `rimapi-power-info-dto` ✓ landed.
  - New captures from briefing schema S5: `rimapi-map-region-at`,
    `rimapi-room-entry-cells`.
- **Remaining after Solver1:**
  - Wire the freezer rule to `PlacementSolver.SolveAsync` (the `options[]`
    TODO in `Rules.cs`) once Solver1 lands.
  - **Willie dashboard panels** (§4 `DASH` follow-on) — dedicated
    anchor-inventory / backlog / data-coverage / solver-trace panels beyond
    the landed readout tab; solver-trace panel needs Solver1.
  - **Derivation extensions** (`willie-rules-slice-a.md` §4) — thermal
    room-temps, storage distance, room quality/cells, frame age, constructor
    priority — each unlocks more Slice-A rules against richer briefing fields.
  - `construction.md` promotion (§4 `PROMOTE`).
