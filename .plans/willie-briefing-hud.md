# Willie Briefing HUD — Concern Tiles + Signal Bars

> **High-level plan.** Lands **dashboard code** (React + TS + Vite, `Dashboard/`).
> Replaces the raw-JSON render of the Willie briefing with a game-like HUD. Chosen
> first cut: **concern status tiles** + **data-coverage signal bars**. Other HUD
> ideas (anchor-inventory minimap, power/thermal gauges, solver-trace filmstrip) are
> named follow-ups.
>
> Pure **frontend**; the briefing data already flows. No server work.

---

## 0. What already exists (this is a promotion, not a greenfield build)

The Willie Briefing view is **already half-HUD**:
[`MinisterBriefingView.tsx`](../Dashboard/src/components/minister/MinisterBriefingView.tsx)
renders a `WillieBriefingReadout` (lines 149–299) — a `MetricCard` grid + four
`willie-readout-panel` articles + a coverage grid — **above** the raw
`DisclosureSection`+`JsonTree` groups (lines 57–66). The CSS classes (`willie-briefing-metrics`,
`willie-readout-panel`, `willie-fact-grid`, `willie-coverage-grid`) already exist in
`styles.css`.

So this plan **promotes** the readout to the primary surface, makes it
**concern-aligned**, upgrades coverage to **signal bars**, and **demotes the raw JSON**
to a collapsed debug disclosure. It is not a rewrite.

Inputs already on the client (the briefing record):
- Concern sub-records: `powerStability`, `thermalControl`, `functionalRooms`,
  `storagePlacement`, `materialBottleneck`, `stalledBuilds`, `baseLayout`, `fireRisk`
  — the **8 canonical Willie concerns** (meta-plan §3).
- `dataCoverage` — a **bool** map today, rendered as `CoverageBadge`
  (available/missing) via
  [`DataCoverage.tsx`](../Dashboard/src/components/shared/DataCoverage.tsx).
- `anchorInventory.anchors[]`, `constructionBacklog.groups[]` (for later follow-ups).

---

## 1. Concern tiles (HUD1)

A tile strip of the **8 concerns**, each tile = icon + short label + a **headline
value** + a **severity tone** (ok / neutral / warn / error). Tone is a tiny per-concern
rule over the existing sub-record fields, e.g.:

| Concern | Headline | Tone rule (example) |
|---|---|---|
| power_stability | net W | `netW < 0` → error |
| thermal_control | cooler / freezer-anchor count | no freezer anchor while a freezer request is active → warn |
| functional_rooms | room-anchor count | missing expected room class → warn |
| storage_placement | zones / cells | zero stockpile cells → warn |
| material_bottleneck | material-gap count | gaps > 0 → warn |
| stalled_builds | pending / blocked | blocked > 0 → warn |
| fire_risk | wood-structure count | (info) |
| base_layout | room / building count | (info) |

Clicking a tile scrolls to / expands the matching detail panel (the existing
`willie-readout-panel` articles, generalized to one per concern). Replaces the current
ad-hoc 6-`MetricCard` grid with a concern-complete set. Reuse `MetricCard` /
`SemanticLabel` / `iconForField`. Risk: low–medium (the tone rules + concern↔field map
are the only judgement).

## 2. Signal bars (HUD2)

Replace the `dataCoverage` `CoverageBadge` pills with **signal-strength bars** — a new
`CoverageBars` component (extend `DataCoverage.tsx`, keep `CoverageBadge` for other
callers). MVP renders the existing **2-state bool** as full/empty bars.

**Note the wire limit:** the briefing-schema availability model is **3-tier**
(`have` / `need-fork` / `defer`), but the `dataCoverage` wire field is a plain bool, so
a true 3-tier bar needs a **briefing wire change** — flag as a follow-up, do not fake
the middle tier from a bool. Risk: low.

## 3. Demote raw JSON (HUD3)

Move the `DisclosureSection`+`JsonTree` groups behind a single **collapsed "Raw
briefing"** disclosure at the bottom (kept for debugging, not the default surface).
Scope the change to the **`willie` branch** of `groupBriefing` so the food/welfare/mayor
briefing views are untouched. Risk: low.

---

## 4. Follow-ups (the other HUD ideas)

- **Anchor-inventory minimap** — anchors as labeled dots on a base mini-map. Wants a
  **shared base-map render component** — the **Build Queue variant D**
  (`willie-build-queue-tab.md`) wants the same one. **Build it once**, both consume it.
- **Power/thermal gauges** — upgrade the `Fact` rows to dial/gauge widgets (battery
  reserve %, net-W gauge, freezer temp).
- **Solver-trace candidate filmstrip** — drafts as thumbnails with score bars + hard-gate
  reject reasons. Needs the solver `PlacementTrace` surfaced to the client (ties into
  the meta-plan §4 `DASH` solver-trace panel + the analytics `candidates` view that
  already exists in `scopes.ts`).
- **3-tier coverage** — the `have/need-fork/defer` wire change that unlocks real signal
  bars.

## 5. Out of scope

- The shared base-map render component (follow-up, shared with Build Queue D).
- Any briefing **wire-shape** change (3-tier coverage, solver trace) — separate slices;
  honor no-compat / wipe-and-regen if/when they land.
- Food/Welfare/Mayor briefing HUDs (this is Willie-only).

## 6. Verification

- Willie Briefing tab leads with the concern-tile strip + per-concern panels + signal
  bars; the raw JSON is collapsed at the bottom.
- Tone reflects live state (e.g. negative net power → error tile; blocked builds → warn).
- No regression to other ministers' Briefing views (shared `CoverageBars` stays additive;
  `CoverageBadge` unchanged for existing callers).

---

## 7. HumanTodo capture (already in Tasks.md — relink to this plan)

```
- [ ] willie-briefing-hud [2026-05-29] #dashboard #willie #ui Replace the raw-JSON Willie briefing render with a HUD: promote the existing WillieBriefingReadout to primary, add an 8-concern tile strip (severity tone) + data-coverage signal bars, demote JsonTree groups to a collapsed "Raw briefing" disclosure (willie branch only). MVP signal bars from the current bool; 3-tier have/need-fork/defer needs a wire change (follow-up). Minimap + gauges + solver-trace filmstrip are follow-ups; minimap shares a base-map render component with Build Queue D. [plan](.plans/willie-briefing-hud.md)
```
