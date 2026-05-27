# Construction Briefing + Data-Gap Anchor (the GATE)

> Design doc for the **one undesigned piece** flagged in
> [`willie-meta-plan.md`](willie-meta-plan.md) §4. Resolves which Slice-A rules
> are real vs aspirational and feeds the Placement Solver's anchor resolution.
>
> Pure design output. No code lands here.

---

## Goal

Produce a per-field map of the `ConstructionBriefing` (Willie minister briefing
record) so every downstream consumer can tell **what's already readable, what
needs RIMAPI work, and what to defer**. Without this, PS1 and Willie Rules
Slice A are guessing about their inputs.

For each candidate briefing field, decide:

- **Signal** — what the field means (one short sentence).
- **Source** — RIMAPI endpoint, state-store derivation, or RimMind part.
- **Availability** — `have` / `need-fork` / `defer`.
- **Consumers** — which Slice-A rule(s), which solver gate(s).
- **Notes** — known gaps, RimMind reuse opportunities, follow-up todos.

---

## Context

- Meta-plan: [`willie-meta-plan.md`](willie-meta-plan.md) — GATE node in §4.
- Schema landed: S1 `7d0818c`, S2 `8676d59`, S3 `4927741`. Food emits typed
  `building_request`.
- FORK1 landed: RIMAPI single-asset triplet `3eb1d84`. Validate/place/read
  primitive available. RimBob meta `300f5ce`.
- FORK2 pending: blueprint groups (Capability A). Some signals may need it.
- RimMind candidates to mine:
  `C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/ConstructionBacklogPart.cs`,
  `.../StorageSaturationPart.cs`.
- Known data-gap todos:
  - `source-todo-building-condition-read` (HumanTodo)
  - `source-todo-room-quality-read` (landed — see HumanTodo line 40)
  - `source-todo-mayor-briefing-context` (parallel briefing work)
- Mirror reference: `Src/StateStore/Derivations/FoodBriefingDerivation.cs` is
  the only fully-built feeder briefing; Willie's briefing follows its shape.

---

## Candidate field set (first pass)

Group by the 9 Construction concerns
([`willie-advice-types.md`](willie-advice-types.md)). Each field needs the
signal/source/availability/consumers/notes treatment in the body of the doc.

- **power_stability** — total generation, total draw, battery reserve,
  outage-prone net IDs.
- **thermal_control** — room temperatures by purpose, freezing-room status,
  cooler placements, deadline windows.
- **basic_shelter** — uncovered colonist sleeping spots, open roofs, missing
  doors.
- **functional_rooms** — kitchen/bedroom/storage purpose detection + room
  quality stats.
- **storage_placement** — stockpile saturation per category, distance to
  consumer, spoilage adjacency (kitchen-near, cooler-near).
- **material_bottleneck** — pending blueprint material need vs current stock
  vs production rate.
- **fire_risk** — flammable adjacency, missing firefoam, conduit-near-wood
  flags.
- **stalled_builds** — frames open > N days, blocked-by-material vs
  blocked-by-colonist, work-priority gap.
- **base_layout** — corridor widths, choke-points, blueprint density, planning
  overlay state (Capability B).

Cross-cutting:

- **map / region topology** — region IDs, navmesh-reachable cells, indoor/outdoor
  segmentation. Needed by solver anchor resolution.
- **anchor inventory** — discovered named anchors (`kitchen`, `bedroom`,
  `freezer`, `storage`). Feeds `near:<class>` request resolution.

---

## Slices

1. **S1 — table draft.** One markdown table per concern. Fill
   signal/source/availability with current best knowledge. Mark `need-fork`
   rows with the missing RIMAPI endpoint name. No commitments yet.
2. **S2 — RimMind cross-walk.** Read `ConstructionBacklogPart` and
   `StorageSaturationPart`; map their fields onto our briefing rows. Where
   RimMind already computes something useful, propose state-store ingestion
   instead of fresh derivation. Record gaps and overlaps.
3. **S3 — anchor inventory contract.** Define the `AnchorInventory` record
   (name, room-class, centroid cell, supporting evidence). This is the input
   the Placement Solver's `near:<class>` resolution will read. Mark dependent
   RIMAPI room reads as `have` (landed via `source-todo-room-quality-read`) or
   `need-fork`.
4. **S4 — Slice-A rule-vs-aspiration cut.** Per concern, per draft rule:
   "real now (have)" / "real after FORK2 (need-fork)" / "aspirational
   (defer)". This is the actionable output — Willie Rules Slice A will be
   written against the `real now` set first.
5. **S5 — follow-up todos.** Promote each `need-fork` row into a HumanTodo
   `source-todo-*` entry if one doesn't already exist. Link back to this doc.

---

## Validation

- Every briefing field has all five columns filled. No `TBD` left.
- Every `need-fork` row names an explicit endpoint (or marks it as a new RIMAPI
  todo with a sibling plan).
- Slice-A rules list (S4) is non-empty for at least 3 concerns and matches the
  `have` subset.
- Anchor inventory contract (S3) covers `near:kitchen` end-to-end for the
  north-star freezer demo in `willie-meta-plan.md` §1.

---

## Open questions / dependencies

- Does `ConstructionBacklogPart` already group material need + work need per
  blueprint? If yes, ingest. If no, design our own derivation.
- Anchor centroid: median of room cells vs first-discovered? Affects solver
  distance scoring. Lean median; revisit if PS1 needs it tighter.
- Recompute cadence: every cycle vs delta-driven? Mirrors Food's choice — start
  per-cycle, optimize later if hot.
- Power-net IDs from RIMAPI: have, or need-fork? Confirm before S1 ships.
- Floor-fill rep for `base_layout` (per-cell vs compressed rect) is open in
  advice-schema Q2; coordinate.

---

## Out of scope

- Code landing — this is a design doc only.
- Placement Solver internals — covered by `placement-solver.md` once the GATE
  unblocks it.
- Promoting design into `Docs/design/ministers/construction.md` — separate
  phase 7 in the meta-plan.
- FORK2 blueprint-group endpoint specification — owned by
  `rimapi-blueprint-groups-and-planning-overlay.md`.
