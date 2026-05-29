# Willie Build Queue Tab - Stacked Sections + Option Cards (B + C)

> **High-level plan.** Lands **dashboard code** (React + TS + Vite, `Dashboard/`).
> Adds a new **"Build Queue"** view tab under the Willie minister. Chosen shape:
> **B - stacked collapsible sections** as the shell, **C - option-thumbnail cards** as the section
> contents. Variants **A** (flat ordered list) and **D** (pending-footprint
> minimap overlay) are named follow-ups, not this slice.
>
> Mostly a **frontend** job: the data already flows. Flag any server gap as a
> blocker rather than silently widening scope.

---

## 0. What already exists (don't rebuild it)

- **Tab wiring is a known seam.** Minister view tabs are declared in [`Dashboard/src/dashboard/scopes.ts`](../Dashboard/src/dashboard/scopes.ts) (`MinisterViewKey` union + `ministerViews[]` + per-scope `enabledViews`) and rendered via [`Dashboard/src/dashboard/ministerViewRegistry.tsx`](../Dashboard/src/dashboard/ministerViewRegistry.tsx) (`ministerViewRenderers`). Willie currently enables only `['briefing', 'rules', 'advice']` (`rulesOnlyMinisterViews`, `scopes.ts:86`).
- **The section data is already on the client:**
  - **Proposed options** - `AdviceItem.options?: AdviceOption[]` ([`types/advice.ts:311`](../Dashboard/src/types/advice.ts)). Each `AdviceOption` carries `label`, `summary`, `tradeoff_note`, `est_materials[]`, `readiness{draftable, placement_valid, materials_ready, apply_ready}`, and a `blueprint_group{label, map_id, assets[]}`. This is the freezer solver output the wiring slice (`fbad240`) attached.
  - **Backlog** - `constructionBacklog.groups[]` + `stalledBuilds` (`pendingBuildCount`/`blockedCount`/`disallowedCount`) already surfaced in the Willie briefing readout's `DynamicTable` ([`MinisterBriefingView.tsx:247`](../Dashboard/src/components/minister/MinisterBriefingView.tsx)).
  - **Requested** - inbound `AgentFlag.building_requests[]` ([`types/advice.ts:270`](../Dashboard/src/types/advice.ts)).
- **Apply path exists** - `place_blueprint_group` is a wired `AdviceActionApply` kind (`types/advice.ts:109`); the advice apply round-trip already lives in `api/advice.ts` + `MinisterAdviceView`. Reuse it; do not invent a new executor.
- **Reusable building blocks:** `MetricCard`, `StatusPill`, `CoverageBadge`, `DynamicTable` (`Inspector.tsx`), `SemanticLabel`, the advice-feed hooks, and the `willie-*` CSS classes in `styles.css`.

So the genuinely new code is: a tab registration, a stacked disclosure layout, and a **footprint thumbnail** renderer for option cards.

---

## 1. Sections (the B shell)

Map the existing data to a small, honest set of stacked collapsible sections - do **not** invent states the data can't back:

| Section | Source | Notes |
|---|---|---|
| **Requested** | `AgentFlag.building_requests[]` (Willie-domain flags) | the asks Willie has noticed but not yet solved |
| **Proposed** | `AdviceItem.options[]` on Willie advice | solver layouts awaiting a player pick (freezer today) |
| **In backlog** | `constructionBacklog.groups[]` + `stalledBuilds` | blueprinted/building/blocked frames already on the map |

A `Done` section is **deferred** - the briefing only exposes *pending* backlog, not a completed-build history. Add it only if a completion feed lands.

## 2. Cards (the C contents)

- **Option card** (Proposed section): `label` + `summary`, a **footprint thumbnail** (render `blueprint_group.assets[]` as a tiny SVG/canvas grid colored by `role` - floor/wall/door/cooler), `est_materials` chips, `readiness` as `StatusPill`s, a `tradeoff_note`, and an **Apply** button wired to the existing `place_blueprint_group` apply. Multiple options per advice item sit side by side.
- **Backlog card / row**: reuse the `DynamicTable` columns already proven in the readout (`kind`, `defName`, `count`, `blockedCount`, `totalWorkLeft`).
- **Request card**: `target_class`/`room_class`, `capacity_need`, `adjacency`, `urgency`, `requested_from`.

---

## 3. Slices (coarse)

- **BQ1 - tab + shell + data-backed sections.** Add `'build_queue'` to `MinisterViewKey` + `ministerViews` + Willie's `enabledViews` (`scopes.ts`); add a renderer + `MinisterBuildQueueView` (`ministerViewRegistry.tsx`). Render the three stacked disclosure sections from data already in `MinisterViewContext` (`activeAdvice`, `flags`) + a briefing fetch for the backlog. Requested + In-backlog sections use existing table/pill components. Risk: low.
- **BQ2 - option-thumbnail cards.** Build the footprint thumbnail renderer + the option card; wire Apply through the existing advice-apply path; show readiness + materials + tradeoff. This is the slice with real new UI. Risk: medium (the thumbnail renderer is the new primitive).
- **BQ3 - polish (optional).** Sort by urgency/age, empty states per section, live refresh on the advice SSE feed, count metadata per section. Risk: low.

---

## 4. Follow-ups (the other variants)

- **Variant A - flat ordered list** as a density toggle on the same view (cheap once BQ1 lands).
- **Variant D - pending-footprint minimap overlay.** Wants a **shared base-map render component**. The **HUD anchor-inventory minimap** (`willie-briefing-hud.md`) wants the same component - **build it once**, then both D and the HUD minimap consume it.
- **Non-freezer options** - the Proposed section only fills for freezer until the missing-room-to-solver wiring pass lands (`willie-placement-wiring.md` section 3). Sections degrade gracefully (empty Proposed) until then.

## 5. Out of scope

- Server/RIMAPI changes - **confirm first** whether a pending-blueprint/per-frame feed is needed for richer "building" granularity; if so it's a separate RIMAPI todo, not this slice.
- Group-apply executor changes (the Apply path is reused as-is).
- A completed-build `Done` section (needs a completion feed).

## 6. Verification

- "Build Queue" tab appears for Willie only (not other ministers).
- A live freezer `building_request` populates Requested + Proposed; an option card renders a footprint thumbnail and **Apply round-trips** through the existing path.
- Backlog section matches the readout table; no regression to Briefing/Rules/Advice tabs.

---

## 7. HumanTodo capture (already in Tasks.md - relink to this plan)

```text
- [ ] willie-build-queue-tab [2026-05-29] #dashboard #willie #ui Dashboard "Build Queue" Willie view tab: B stacked collapsible sections (Requested / Proposed / In-backlog) + C option-thumbnail cards (footprint render + readiness pills + Apply). Reuses scopes.ts/ministerViewRegistry tab seam, AdviceOption.blueprint_group, construction backlog, existing apply path. Variants A (flat list) + D (minimap overlay) are follow-ups; D shares a base-map render component with the HUD minimap. [plan](.plans/willie-build-queue-tab.md)
```
