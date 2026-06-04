# Dashboard — Rule Card (visualize one rule-as-data row)

> A single reusable **Rule Card** that visualizes one evaluated rule, rendered **one-per-row** as a full-width horizontal band. It replaces the all-rules table section on the Rules tab of **every** minister (Chef, Welfare, Willie, and any future feeder) because all ministers emit the same `RuleEvaluationTrace` shape through the shared `MinisterRuleTableEvaluator`.
>
> **Key decision (changed from v1 of this plan):** the wire fields are **not** enough. The card shows each rule's **real emitted decisions** (advice title/priority, typed requests with their target def, escalate reason) using rich game-asset icons — not a parsed summary string. That data already exists at trace-build time but is currently thrown away into a lossy `outputAction` string, so the trace is enriched first (small, server-side), then the card consumes it.

---

## 1. Grounding — verified by read

### What a "rule" is, as data

Post `minister-rules-table-refactor`, each minister's rules are a declarative table of `MinisterRule<TBriefing>(Id, Matches, Reason, Build)` ([MinisterRule.cs](../Src/Common/Ministers/MinisterRule.cs)):

- `Id` — `RuleId` string (e.g. `shelter_floor`).
- `Matches` — `Func<TBriefing,bool>` predicate. **No static condition string exists in the model.**
- `Reason` — `Func<TBriefing,string>` → live evidence (e.g. `"bed_deficit=2; unroofed_bedrooms=0"`).
- `Build` — `Func<TBriefing, IReadOnlyList<Decision>>` → typed effects: `Advise` / `RequestBuild` / `RequestLabor` / `RequestItem` / `RequestAttention` / `Escalate` ([Decision.cs](../Src/Common/Ministers/Decision.cs)).

### What reaches the dashboard today

`MinisterRuleTableEvaluator.BuildTrace` ([:84](../Src/Common/Ministers/MinisterRuleTableEvaluator.cs)) projects each rule to one `RuleEvaluationTrace`; `RuleTraceDetails` wraps the catalog; `/api/ministers/{minister}/trace/latest` returns the whole `MinisterTraceSnapshot` verbatim ([MinisterEndpoints.cs:445](../Src/ApiHost/Endpoints/MinisterEndpoints.cs), [MinisterTraceStore.cs:190](../Src/Coordination/MinisterTraceStore.cs)). TS mirror ([system.ts:193](../Dashboard/src/types/system.ts)):

```ts
interface RuleTraceDetails { selectedRule: string | null; allRules: RuleEvaluationTrace[]; }
interface RuleEvaluationTrace {
  rule: string;         // id
  outcome: string;      // 'selected' | 'not_matched' | 'escalated'
  conditions: string;   // live evidence  ── duplicate of reason today
  outputAction: string; // lossy summary, e.g. "actions: place_blueprint; requests: 1; escalate"
  reason: string | null;// live evidence
}
```

### Why "wire fields" are not enough — the answer to the review question

Three facts force the enrichment:

1. **`conditions` === `reason`.** `BuildTrace` writes the same `Reason(briefing)` string into both positions (`new RuleEvaluationTrace(rule.Id, outcome, reason, OutputActionFor(...), reason)`, [:110](../Src/Common/Ministers/MinisterRuleTableEvaluator.cs)). One of the four columns the current table renders is pure duplication. So a rule really only carries **id + outcome + evidence + a lossy emission summary**.
2. **`outputAction` is lossy.** `OutputActionFor` ([:170](../Src/Common/Ministers/MinisterRuleTableEvaluator.cs)) collapses the emitted decisions into a joined string: `actions: <kinds>; requests: <n>; escalate`. It cannot tell the operator *which* building (a bed? a table?), *to whom*, at what priority, or the advice title / escalate reason. A card built only on this is no richer than the table it replaces.
3. **The emitted detail is already in hand at build time.** `BuildTrace` line 101 computes `ruleDecisions = decisions.Where(d => d.Rule == rule.Id)` — the exact per-rule `Decision` list — then discards it into the summary. The trace (`MinisterTraceSnapshot`) carries only `AdviceCount`/`FlagCount` (counts) and `Flag` (the inbound **wake** flag, not emissions). So surfacing real emissions is a **cheap, local** server change at the one place the data already exists — not a new pipeline.

Conclusion: enrich `RuleEvaluationTrace` with a structured, per-rule `Emissions` list (Slice 1, backend). Then the card renders the genuine decisions with real icons (Slice 2, frontend). This also satisfies dashboard.md §Rules, which already asks diagnostics to show *"which rule … produced each action row."*

(Outcome is exactly three values — `selected` / `not_matched` / `escalated` ([RuleOutcome.cs](../Src/Common/Ministers/RuleOutcome.cs)). The dashboard's `ruleOutcomeOrder`/`ruleOutcomeIcons` still list dead `matched`/`suppressed` ([semanticIcons.ts:332](../Dashboard/src/dashboard/semanticIcons.ts)); pruned in Slice 1.)

### What we replace

`MinisterRulesView` → `RuleDiagnosticsPanel` ([MinisterRulesView.tsx:159](../Dashboard/src/components/minister/MinisterRulesView.tsx)) renders the catalog as per-outcome `DynamicTable`s with `min-width:1040px` ([styles.css:2628](../Dashboard/src/styles.css)) → horizontal scroll on every Rules tab. **The card stack replaces this entire "All rules" table section.** The rest of `RuleDiagnosticsPanel` (the `selectedRule` field, the surrounding disclosure) and `MinisterRulesView` (trigger summary, escalation callout, JSON `InspectorSurface`, recent events, active advice) stay.

### One card, every minister — for free

`MinisterRulesView` is the single Rules view for all scopes; `RuleDiagnosticsPanel` consumes only `RuleTraceDetails`, which every minister produces identically via the shared evaluator. A new card in that panel applies to Chef, Welfare, Willie, and every future minister with **no per-minister code**.

### Icon machinery already available (we lean on it hard)

- `iconForRuleOutcome('selected'|'not_matched'|'escalated')` — outcome glyph.
- `iconForField('rule'|'reason'|'priority'|'owner'|'labor_requests'|'attention'|'llm_escalation'|'advice')` — all resolve today.
- **`iconForFieldValue('target_def', 'Bed')`** → real RimWorld **game sprite** (the `*_def` branch, [semanticIcons.ts:450](../Dashboard/src/dashboard/semanticIcons.ts)). This is how the existing flag table shows a bed/table/horseshoe icon ([MinisterAdviceView.tsx:481](../Dashboard/src/components/minister/MinisterAdviceView.tsx) `buildingRequestRow`). The card reuses this exact path for request targets.
- `GameIcon` + `SemanticLabel` + `IconizedText` render those refs and inline-iconize evidence text.

---

## 2. The Rule Card — design

### 2.1 Layout: one rule per row, full-width horizontal band

Not a grid of tiles — a **vertical stack of full-width rows**, one rule each, in catalog (declaration) order. Each row is an internal 3-column CSS grid so it **fills the horizontal space instead of overflowing**:

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ ✔ selected        │ 🛈 bed_deficit=2; unroofed_bedrooms=0         │ 🛏 +2 beds, roofed   │
│ shelter_floor  ⬆H │                                               │    barracks → Willie⬆H│
└────────────────────────────────────────────────────────────────────────────────────────┘
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ ✔ selected        │ 🛈 thought=ate without table; pawns=1          │ 💬 "Colonists need a  │
│ comfort_beauty ▾L │                                               │   table"  🪑 table→Willie│
└────────────────────────────────────────────────────────────────────────────────────────┘
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ ○ not matched     │ 🛈 break_risk_count=0                          │ —                     │
│ break_risk        │                                               │                       │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

- **Col 1 — Identity** (`minmax(170px, auto)`): outcome icon+label (`iconForRuleOutcome`), rule id in `<code>`, priority chip (`iconForField('priority')`) when the rule emits one. A short outcome-colored left accent bar.
- **Col 2 — Evidence** (`minmax(220px, 1fr)`): the live `reason`, via `IconizedText` (inline def/skill icons). One line, wraps; never truncated to ellipsis-overflow.
- **Col 3 — Emits** (`minmax(260px, 1.6fr)`): the structured emissions as compact icon+label rows (see 2.2). `not_matched` rows have none → a muted `—`, and the whole row renders **thin** (less vertical space), so the catalog stays scannable and space is spent where there's signal.

Grid columns use `minmax(..,fr)` + `flex-wrap` semantics so at narrow widths the three columns **stack vertically** within the card — no fixed `min-width`, **no horizontal scroll** anywhere (the headline fix over the 1040px table). Container is plain `display:grid; gap` of full-width rows.

### 2.2 Emissions — the real decisions, with real icons

Each entry in the new `emissions[]` renders as one dense row `[icon] [label] [routing/priority chips]`, icon chosen by kind:

| Decision kind | Icon | Label shown |
|---|---|---|
| `advise` | `iconForField(title)` (domain flavor) ?? `iconForField('advice')` | advice **title** (the human headline) |
| `request_build` | **`iconForFieldValue('target_def', targetDef)`** → game sprite (bed/table/cooler…) | request text, e.g. `+2 beds, roofed barracks` |
| `request_labor` | `iconForField('labor_requests')` | request text + `work_type`/`skill` chip |
| `request_item` | `iconForFieldValue('item_def', itemDef)` → game sprite | request text + qty |
| `request_attention` | `iconForField('attention')` | request text |
| `escalate` | `iconForField('llm_escalation')` (warn tone) | escalate **reason** |

Each emission also shows a **routing chip** (`→ Willie`, via `iconForField('owner')`) and a **priority chip** (`iconForField('priority')`) when present — mirroring the existing flag-request routing cells so the two surfaces feel consistent. Reuse the `buildingRequestRow`/`itemRequestRow` icon-key logic verbatim where possible.

This is the whole point of the enrichment: the operator sees *"`shelter_floor` selected → emits a build request for a **bed** (real bed icon) to **Willie** at **High**"* at a glance, not `"requests: 1"`.

### 2.3 Outcome theming (`rule-card ${outcome}`)

Mirror the priority/tone-class pattern of `advice-card`/`escalation-callout`:

| Outcome | Tone | Treatment |
|---|---|---|
| `selected` | affirmed | colored accent bar + filled outcome badge; the row is the operator's focus |
| `escalated` | warn (reuse escalation-callout amber) | amber accent + the Escalate emission carries the reason |
| `not_matched` | muted | dim surface, low-contrast, no accent, thin row |
| _unknown_ | neutral | defensive default; never blank-render a future outcome |

`details.selectedRule` (single authoritative pick, or `null` for multi-rule all-hits runs) adds an `is-selected` marker; when null, no row claims selection (honest).

### 2.4 Icons are mandatory, everywhere

Every visual element carries a semantic icon — outcome, rule, priority, evidence label, each emission kind, request target (real game sprite), routing owner, escalate. No bare text labels. This is both a house-style rule (dashboard.md "semantic icon cues") and the explicit ask. `GameIcon` fallbacks (two-letter) cover any missing sprite so a row never renders icon-less.

---

## 3. Slice 1 — backend trace enrichment (the only C# slice)

Surface the per-rule decisions the evaluator already computes.

- **New record** `RuleEmission` in [RuleTraceDetails.cs](../Src/Common/Ministers/RuleTraceDetails.cs):
  ```csharp
  public sealed record RuleEmission(
      string Kind,        // "advise" | "request_build" | "request_labor" | "request_item" | "request_attention" | "escalate"
      string Label,       // advice Title / request text / escalate Reason
      Priority? Priority, // emission priority where applicable
      string? To,         // routing owner (request To)
      string? TargetDef,  // request_build/request_item def for the game-sprite icon
      string? WorkType);  // request_labor work type/skill
  ```
- **Extend** `RuleEvaluationTrace` with `IReadOnlyList<RuleEmission> Emissions`. Populate in `BuildTrace` from the `ruleDecisions` already grouped at [:101](../Src/Common/Ministers/MinisterRuleTableEvaluator.cs) (and from the escalate descriptor path / `RuleTraceDetails.Escalated`). Keep `outputAction` for now (back-compat); it can derive from `Emissions`.
- **Prune** dead `matched`/`suppressed` from `ruleOutcomeOrder`/`ruleOutcomeIcons` (frontend) — no rule emits them.
- **TS mirror:** add `RuleEmission` + `emissions` to `system.ts`.
- **Replay corpus:** the trace is persisted in replay records → **wipe-and-regen** (note in plan; mechanical). Tests: rule-trace assertions gain `emissions` shape for Welfare (multi-rule fixture already exists: [multi-rule.json](../Src/Tests/Welfare/Fixtures/multi-rule.json)) + Chef + Willie.
- Repo convention: evaluator/`Rules` compile + tests green before any frontend work.

Leaving the redundant `conditions` field on the wire is fine for now (card ignores it, renders `reason`); collapsing it is optional cleanup, not required.

## 4. Slice 2 — the Rule Card frontend

- **New** `Dashboard/src/components/minister/RuleCard.tsx`: `function RuleCard({ rule, selected }: { rule: RuleEvaluationTrace; selected: boolean })`. Renders the 3-column row (2.1), emissions (2.2), theming (2.3). Minister-agnostic.
- **Rewire** `RuleDiagnosticsPanel` ([MinisterRulesView.tsx:159](../Dashboard/src/components/minister/MinisterRulesView.tsx)): replace the per-group `DynamicTable` blocks with a single flat `<div className="rule-card-list">` mapping every `allRules` row to `<RuleCard rule={row} selected={row.rule === details.selectedRule} />`. Drop the per-outcome **table grouping** (the `DynamicTable`s, group headers, and the `groupRuleCatalogByOutcome`/`compareRuleOutcomes`/`ruleOutcomeSortIndex`/`RuleOutcomeGroup`/`formatRuleGroupMeta` helpers it fed); outcome is now shown on each card. **Sort the flat list by outcome rank** (`selected`, `escalated`, `not_matched` — reuse `ruleOutcomeOrder`) then stable catalog order within a rank, so emitting rules sit on top, one per row. Keep the `selectedRule` summary field, the `All rules` count meta, and the disclosure wrappers. Remove helpers left dead by the grouping removal so `tsc`/lint stay clean.
- **CSS** ([styles.css](../Dashboard/src/styles.css) near ~2591): `.rule-card-list` (stacked full-width rows), `.rule-card` (the internal 3-col `minmax` grid + accent bar), `.rule-card.selected`/`.escalated`/`.not_matched`/`.is-selected` (tones via existing `--text`/`--muted` + the escalation warn palette), `.rule-card-evidence`, `.rule-card-emits`, `.rule-card-emit` (icon+label+chips row). **Retire** `.rule-all-rules-table` (table gone). No `min-width` anywhere → no overflow.
- Graceful: empty `emissions` → muted `—` and a thin row; unknown `outcome` → neutral.

---

## 5. Accessibility & space discipline

- Outcome conveyed by icon **plus** text label, never color alone.
- One-per-row rows reflow their 3 columns to a vertical stack at narrow widths; no horizontal scroll, no `min-width` overflow.
- `not_matched` rows are thin/dim → vertical space is spent on rules that actually emitted.
- Rule id in `<code>` for copy-into-grep.

## 6. Acceptance

- Every minister's Rules tab renders the catalog as a **one-per-row** card stack, no horizontal scroll; the old all-rules table section is gone.
- Each card shows: outcome badge (icon+label), rule id, evidence line, and **real emissions** — advice titles and typed requests with their **game-sprite icon**, routing owner, and priority — sourced from `emissions[]`.
- `selected` rules are distinguished and carry the `is-selected` marker when `selectedRule` matches; `not_matched` rows are dim and thin.
- Backend: `dotnet test` green incl. updated rule-trace `emissions` assertions; replay corpus regenerated.
- Verify on a live/fixtured Welfare run (multiple `selected` + `not_matched`) per memory: confirm `emissions` in the trace JSON first, then the dashboard.

## 7. Out of scope / follow-ups

- Collapsing the redundant `conditions` field (optional; card already ignores it).
- Static predicate descriptions / which briefing fields a `Matches` read — needs an "explainable rules" model that doesn't exist; not this card.
- Player-facing styling — Rules is an operator/debug surface; the card stays compact/technical.

## 8. Files

**Slice 1 (backend):** `Src/Common/Ministers/RuleTraceDetails.cs` (+`RuleEmission`), `Src/Common/Ministers/MinisterRuleTableEvaluator.cs` (`BuildTrace` populate), `Dashboard/src/types/system.ts` (mirror), `Dashboard/src/dashboard/semanticIcons.ts` + `MinisterRulesView.tsx` (drop dead outcomes), rule-trace tests, replay corpus regen.
**Slice 2 (frontend):** `Dashboard/src/components/minister/RuleCard.tsx` (new), `Dashboard/src/components/minister/MinisterRulesView.tsx` (swap table→card list), `Dashboard/src/styles.css` (`.rule-card*`, retire `.rule-all-rules-table`).

## 9. Suggested gimp slicing

- **Slice 1** first (backend must compile + test per repo conventions): trace `Emissions` + TS mirror + dead-outcome prune + corpus regen.
- **Slice 2** next: `RuleCard` + render swap + CSS, consuming `emissions`. Verifiable on the regenerated fixtures/replay.

---

## Summary — Slice 1 (landed 2026-06-04)

**Motivation.** A reviewer asked "why only wire fields — is that enough?" It wasn't: the rule trace's `outputAction` is a lossy `"requests: 1"` summary, so a card built on it would be no richer than the table it replaces. Slice 1 surfaces each rule's **real emitted decisions** on the wire so the upcoming Rule Card can show advice titles and typed requests (with game-sprite icons), routing, priority, and escalate reasons — not a string.

**Context.** Follows the landed `minister-rules-table-refactor` (rules-as-data) and `a711649` priority decisions. The emitted `Decision` list per rule already existed in hand at `MinisterRuleTableEvaluator.BuildTrace` (the `decisions.Where(d => d.Rule == rule.Id)` group) but was discarded into `OutputActionFor`. This slice exposes it structurally. Backbone for the frontend in Slice 2 (`RuleCard`, one-per-row, icons everywhere) — still pending.

**Scope (shipped).**
- New `RuleEmission(Kind, Priority?, Label, To?, TargetDef?, WorkType?)` record; `RuleEvaluationTrace.Emissions` (defaults to `[]`, so old persisted replay records load clean — additive, no compat reader).
- `BuildTrace` populates `Emissions` from the per-rule decisions (`Advise`/`RequestBuild`/`RequestLabor`/`RequestItem`/`RequestAttention`/`Escalate` → kind/label/priority/to/targetDef/workType); `RuleTraceDetails.Escalated` carries an escalate emission. `outputAction` and `conditions`===`reason` left unchanged.
- TS mirror: `RuleEmission` interface + `emissions` on `RuleEvaluationTrace`.
- Pruned dead `matched`/`suppressed` from `ruleOutcomeOrder` (MinisterRulesView) and `ruleOutcomeIcons` (semanticIcons) — the enum only emits 3 outcomes.
- Tests: emissions assertions in Food/Welfare/Willie rules tests + `ReplayCorpusWriterTests` (incl. a missing-`emissions` back-compat deserialize test).
- No Slice 2 (no `RuleCard`/CSS/render swap). Replay corpus is gitignored under `var/` — runtime regen only, nothing committed.

**How to verify (human).**
- This is a wire-shape change with **no visible dashboard surface yet** — the Rules tab still renders the old table (Slice 2 adds the card). Confirm via the trace JSON: `GET /api/ministers/welfare/trace/latest` → each `ruleDiagnostics.allRules[]` row now carries an `emissions[]` array (e.g. a selected `shelter_floor` row shows an `advise` + a `request_build` with `targetDef: "Bed"`, `to: "Willie"`).
- Commands: `dotnet test Src\Tests\RimBob.Tests.csproj`; `cd Dashboard && npm.cmd run build`.
- Files: `Src/Common/Ministers/RuleTraceDetails.cs`, `Src/Common/Ministers/MinisterRuleTableEvaluator.cs`, `Dashboard/src/types/system.ts`.

**Codex run:** `20260604-215602-dashboard-rule-card-s1` · branch `codex/prompt-20260604-215602-dashboard-rule-card-s1` · build 0/0, 561 tests green (after merging Welfare LLM slice C), Dashboard build green · landed commit `68f2fbb`.

---

## Summary — Slice 2 (landed 2026-06-04)

**Motivation.** The visible payoff: the Rules tab of every minister now renders the all-rules catalog as a **Rule Card** stack — one full-width row per rule — replacing the 1040px horizontal-scroll table. Built directly on the Slice 1 `emissions` wire, so each card shows the rule's **real decisions** (advice titles, typed requests with their RimWorld sprite, routing, priority, escalate reason), not a `"requests: 1"` summary.

**Context.** Consumes `RuleEvaluationTrace.emissions` landed in Slice 1 (`68f2fbb`). Card style mirrors the existing `advice-card`/`agent-flag-card`/`minister-escalation-callout` house patterns and reuses the `GameIcon` + `iconForFieldValue('target_def', …)` game-sprite path from `MinisterAdviceView`.

**Scope (shipped).**
- New `Dashboard/src/components/minister/RuleCard.tsx` — one full-width band per rule, internal 3-column grid (Identity: outcome badge + rule id `<code>` + priority chip · Evidence: live `reason` via `IconizedText` · Emits: each emission as `[game icon] label [to/priority/workType chips]`). Icons on every element; `request_build`/`request_item` resolve real sprites via `iconForFieldValue`; `escalate` carries a warn tone; empty emissions → muted `—`; unknown outcome tolerated.
- Rewired `RuleDiagnosticsPanel` (`MinisterRulesView.tsx`) — replaced the per-outcome `DynamicTable` blocks with one flat `.rule-card-list`, sorted by outcome rank (selected → escalated → not_matched) then stable order. Deleted the now-dead grouping helpers; kept the `selectedRule` field, the disclosure wrappers, and an `X selected / N rules` count meta.
- `styles.css` — added `.rule-card*` (3-col `minmax` grid + outcome tones via existing `--text`/`--muted`/warn vars; `not_matched` dim/thin; `@media(max-width:720px)` stacks to one column, **no fixed min-width**); removed the dead `.rule-all-rules-table*` and `.rule-outcome-group*` rules.
- Frontend only — no backend/test changes.

**How to verify (human).**
- Dashboard: any minister → **Rules** tab → `Rule diagnostics` → `All rules`. Now a card-per-row list (selected rules on top), no horizontal scroll. Welfare on a new colony shows `shelter_floor` selected with a `request_build` emission carrying the **bed** sprite, `to Willie`, `High`.
- Command: `cd Dashboard && npm.cmd run build` (tsc strict + vite, green).
- Files: `Dashboard/src/components/minister/RuleCard.tsx`, `Dashboard/src/components/minister/MinisterRulesView.tsx`, `Dashboard/src/styles.css`.

**Codex run:** `20260604-232026-dashboard-rule-card-s2` · branch `codex/prompt-20260604-232026-dashboard-rule-card-s2` · Dashboard build green (tsc + vite) · landed commit `9f28044`.
