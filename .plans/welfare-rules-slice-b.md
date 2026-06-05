# Welfare — Rules Slice B (implementation plan)

> **LANDED `319930f`, then refactored.** Historical plan. The `concern`/`WelfareConcern` enum was **deleted** and rules ported to the **table-driven all-hits** engine ([`welfare-rules-table-port.md`](welfare-rules-table-port.md), `b8189f3`); "independent-concern emission" below is now the shared all-hits hit-policy, and emissions are `Advise` + `RequestBuild`/`RequestItem`/`RequestAttention` decisions keyed by trace id (no concern). The Slice-B *data* work (Sleep/Recreation summaries, `WelfareThoughtTaxonomy`, `ThoughtDigest`, mood HUD) landed and is current. Architecture: [`welfare-meta-plan.md`](welfare-meta-plan.md) §0. Source of truth: `Src/Ministers/Welfare/Rules.cs`.

> Second Welfare slice: wire the two remaining Slice-A-candidate concerns (`recreation_gap`, `comfort_beauty`), add the briefing derivation they need, generalize the thought classifier (research R3), refine `shelter_floor`/`break_risk`, and convert Welfare to **independent-concern emission** so simultaneous mood pressures all surface. Plus a Welfare mood HUD.
>
> **Builds on Slice A** (landed `eee91c1`): `WelfareConcern { BreakRisk, ShelterFloor, RecreationGap, ComfortBeauty }`, `Rules : IMinisterRules<WelfareSourceBriefing>` (first-match `break_risk` → `shelter_floor` → `needs_stable`), `shelter_floor` flag → Willie barracks path. See [`welfare-rules-slice-a.md`](welfare-rules-slice-a.md) and [`welfare-meta-plan.md`](welfare-meta-plan.md).
>
> **Aligns with** the in-flight [`food-independent-concerns`](food-rules-independent-concerns.md) direction: each matched concern emits its own advice + flag, priority-sorted by the bus; no dedup/cap/folding for now (simplest full independence).
>
> Rules-only, `Suggest`-mode, no LLM, no Welfare-owned Apply. Welfare requests builds via flags; Willie owns placement/Apply.

---

## 1. Grounding — landed Slice A + what Slice B needs

**Landed (verified):**
- `Rules.Evaluate` is a **first-match cascade** returning one `Decision`: `break_risk` (colonists>0 && `Mood.BreakRiskCount`>0) → `shelter_floor` (`HasRooms` && colonists>0 && `Rooms.BedroomCount`==0) → empty `needs_stable`.
- `shelter_floor` emits `AdviceItem`(note) + one `AgentFlag`(`welfare:shelter_floor`) carrying `BuildingRequest{ RoomClass=Barracks, CapacityNeed=(Beds, ColonistCount), RequestedFrom="Willie" }`.
- `break_risk` emits advice only (no flag); names worst at-risk pawn + dominant driver from `TopNegativeThoughts[0]`.
- A crude inline thought check exists: `IsShelterSleepThought` substring-matches `sleptoutside`/`sleptonground`. **This is the seed for the WB2 taxonomy.**
- `RecreationGap` + `ComfortBeauty` enum values exist but **no rule emits them**.

**Available briefing fields** (`WelfareSourceBriefing`, derived in `WelfareBriefingDerivation`): `ColonistCount`; `Mood{Average,BreakRisk,Stressed,Content}`; `WorstPawns[]{ needs + TopNegativeThoughts[] }`; `NeedLows[]{PawnId,Need,Value<0.35}`; `Rooms{Count,BedroomCount,PrisonCellCount,AvgImpressiveness,WorstRooms[]{…,CellsCount,IsPrisonCell,OpenRoofCount}}`; `DataCoverage`.

**Available raw signals** the derivation can pull but the briefing does not yet expose: `RoomRecord.ContainedBedIds` (→ total bed count), `RoomRecord.RoleLabel`/`OpenRoofCount` per room (→ recreation-room presence, unroofed sleeping), per-pawn `Joy`/`Comfort`/`Beauty` (already in `WorstPawns`/`NeedLows`), `MoodThoughts` def/label (→ taxonomy). A recreation-**source building** signal (chess table / TV / horseshoes defs) needs the building registry Willie already ingests; if not cleanly reachable, recreation's build-request half degrades gracefully (see WB3).

**The cascade limit:** mood pressures are independent and simultaneous — a colony can need a barracks *and* a rec source *and* a table at once, but first-match surfaces only the top one. Slice B fixes this (WB3a).

---

## 2. Steps

### WB1 — Welfare briefing derivation extensions

Extend `WelfareSourceBriefing` (+ `WelfareBriefingDerivation.Compute`) with the rule-facing derived fields. New sub-records:

- `WelfareSleepSummary { int BedCount, int ColonistCount, int BedDeficit, int UnroofedBedroomCount }`
  - `BedCount` = distinct `RoomRecord.ContainedBedIds` across rooms; `BedDeficit = max(0, ColonistCount - BedCount)`; `UnroofedBedroomCount` = bedrooms with `OpenRoofCount > 0`.
- `WelfareRecreationSummary { int JoyLowCount, int RecreationRoomCount, int JoySourceBuildingCount, bool HasRecreationSource }`
  - `JoyLowCount` = pawns with `Joy < threshold`; `RecreationRoomCount` from `RoleLabel` ~ recreation; `JoySourceBuildingCount` from a seed joy-building def set (chess table, billiards, horseshoes, TV, etc.); `HasRecreationSource = RecreationRoomCount>0 || JoySourceBuildingCount>0`. If the building registry is not reachable for Welfare this slice, set `JoySourceBuildingCount=0` and flag it in `DataCoverage` (recreation build-request half then waits — WB3 ships the need-only note).
- `WelfareThoughtDigest { IReadOnlyList<WelfareThoughtGroup> ByCategory }` where `WelfareThoughtGroup { ThoughtCategory Category, int PawnCount, float WorstOffset, string ExampleLabel }` — colony-wide grouping of negative thoughts by the WB2 taxonomy (mirrors RimMind `MoodRiskPart` top-negative-thought grouping; ties to Tasks `rimmind-mood-risk-causes`).
- Extend `WelfareDataCoverage` with `HasBuildings` (recreation-source reachability) and keep existing flags.

Add tunable thresholds as named consts beside the existing ones (`BreakRiskMood=0.35` etc.): `JoyLowThreshold`, `ComfortLowThreshold`, `BeautyLowThreshold`.

**Compat:** this is a briefing schema change. Per AGENTS — **no compat code; wipe-and-regen persisted snapshots on upgrade.** State it in the slice.

### WB2 — Thought taxonomy (research R3 → code)

New `Src/Ministers/Welfare/WelfareThoughtTaxonomy.cs` (or `Src/Common/Briefings/` if shared): a deterministic static classifier.

```csharp
public enum ThoughtCategory { ShelterSleep, Recreation, ComfortBeauty, Hunger, Temperature, Social, Health, Ideology, Other }

public static class WelfareThoughtTaxonomy
{
    public static ThoughtCategory Classify(string defName, string? label);
    public static string? SuggestedOwner(ThoughtCategory category); // "Willie" | "Chef" | "Medical"(future) | "Industry"(future) | null=player
}
```

- Seed the def→category map from a **captured day-1..early replay corpus** (R3): mine real `WelfareMoodThought.DefName`s from Welfare replay records / a fresh save. Cover at least: `SleptOutside`, `SleptInCold`/`Heat`, `SleptOnGround`, `SleptInBarracks`; `Bored`/recreation; `AteWithoutTable`, `AteOnFloor`, ugly-environment/`Darkness`; hunger; `Cold`/`Heat`/`SoakingWet`; social-fight/insulted; pain/sickness; ideology. Unknown defs → `Other` (never crash).
- **Replace** the inline `IsShelterSleepThought` with `Classify(...) == ShelterSleep`. Single source of truth for thought meaning, used by WB1 digest, WB3 rules, and `break_risk` driver routing.

### WB3 — Independent-concern emission + new/refined rules

**WB3a — emission mechanism.** Convert `Rules.Evaluate` from return-on-first-match to **collect-all**: evaluate every concern predicate, gather each match's `AdviceItem` (+ optional `AgentFlag`) into one `Decision(advice[], flags[], trace, diagnostics)`, ordered by priority (the bus already priority-sorts; keep a stable secondary order). `needs_stable` stays the empty fallthrough when nothing matches. Keep each item's single `priority`. Trace becomes the set of selected rules (e.g. join, or a primary + `AllRules` already lists all). Mirror the shape `food-independent-concerns` is landing; **no dedup, no cap, no folding** this slice.

**WB3b — rules** (each independent; build concerns emit a Willie flag, mood-only concerns emit advice only):

1. `break_risk` (refine) — keep; route the dominant driver to its owner via `WelfareThoughtTaxonomy.SuggestedOwner`: emit a cross-minister flag/request only for **live** owners (Chef hunger, Willie shelter/temperature-shell); non-live owners (Medical/Industry) stay a player-attention note until those ministers exist.
2. `shelter_floor` (refine) — trigger on `Sleep.BedDeficit > 0` (covers both "0 beds" and "grew past beds") **or** `UnroofedBedroomCount > 0`. `CapacityNeed.Beds = BedDeficit` (not raw ColonistCount). Unroofed-only case → request roof/enclosure framing rather than new beds. Keep `RequestedFrom="Willie"`, `RoomClass=Barracks`.
3. `recreation_gap` (new) — fires on `Recreation.JoyLowCount > 0` (or colony-wide low joy). If `!HasRecreationSource`: emit advice + a Willie `BuildingRequest{ RoomClass=Recreation or TargetClass=joy-source bench, RequestedFrom="Willie", Priority=Low/Medium }`. If a source exists but joy is still low: advice-only note (schedule/joy-time — Suggest-only, no flag). Priority below shelter.
4. `comfort_beauty` (new) — fires on `ComfortBeauty` thought category present (e.g. `AteWithoutTable`) **or** colony comfort/beauty need lows. Build-fix → Willie `BuildingRequest{ RoomClass=Dining or TargetClass=Table, RequestedFrom="Willie", Priority=Low }`; pure-beauty pressure with no concrete build → advice-only note. Lowest of the build concerns.

Keep the `DecisionFor` helper; generalize it to append N advice/flags. Update `RuleMatches`/`AllRuleEvaluations` to list the new rules with conditions + output.

**Solver-run cost (accepted, noted):** each build concern that fires emits a building_request flag; `CabinetCycle` runs Willie once per distinct build flag, so up to 3 Welfare build flags → 3 Willie solver runs/cycle. This matches the cost `food-independent-concerns` explicitly accepts. A per-cycle build-flag cap is a deferred option, not this slice.

### WB4 — Welfare mood HUD (dashboard)

Mirror the Willie briefing HUD (`willie-briefing-hud`): promote a Welfare readout — a concern-tile strip (4 concerns, severity tone), mood distribution gauge (content/stressed/break-risk), per-colonist mini mood/needs rows (sleep/comfort/beauty/joy + dominant driver), shelter/recreation status lines, and a data-coverage signal bar. Demote raw JSON to a collapsed "Raw briefing" disclosure. Welfare branch only; reuse shared components. (Separable tail — see WB-gimp slicing.)

### WB5 — tests + fixtures

Extend `Src/Tests/Welfare/Fixtures/` + `WelfareRulesTests`:
- `grew-past-beds` — `BedCount=3`, `ColonistCount=5` → `shelter_floor` with `CapacityNeed.Beds==2`.
- `unroofed-bedroom` — bedroom with `OpenRoofCount>0`, beds sufficient → `shelter_floor` roof framing.
- `low-joy-no-rec` — `JoyLowCount>0`, `HasRecreationSource=false` → `recreation_gap` + Willie rec flag.
- `low-joy-has-rec` — rec source present → `recreation_gap` advice-only (no flag).
- `ate-without-table` — comfort thought present → `comfort_beauty` + Willie table flag.
- `multi-concern` — shelter gap **and** low joy **and** comfort thought simultaneously → **all three** advice items + their flags emitted in one snapshot, priority-sorted (proves WB3a independence).
- `WelfareThoughtTaxonomy` unit tests: known defs classify correctly; unknown → `Other`; owner routing.
- `needs_stable` still empty when nothing matches.

---

## 3. Acceptance (slice exit)

- `dotnet test Src/Tests/RimBob.Tests.csproj` green incl. new fixtures.
- Multi-concern colony surfaces **all** matched mood concerns in one Welfare snapshot, priority-sorted; each carries one `priority`; build concerns each emit a `RequestedFrom="Willie"` flag.
- `recreation_gap` + `comfort_beauty` emit correct advice + (when a build fixes it) Willie flags that drive the existing solver path to rec/dining/table options.
- `shelter_floor` handles grew-past-beds (deficit) and unroofed cases; `break_risk` routes drivers to live owners.
- Thought meaning is centralized in `WelfareThoughtTaxonomy`; no inline substring checks remain.
- Welfare HUD renders concern tiles + mood distribution + per-pawn drivers; raw JSON collapsed.
- Briefing schema change shipped with wipe-and-regen, no compat code.

---

## 4. Out of scope

- LLM escalation (Slice C / `welfare-llm-slice-c.md`) — the judgment cases in `welfare.md` Escalation Boundaries.
- `social_pressure`, `guest_hospitality`, `animal_welfare`, `temperature_comfort` concerns, schedule/`set_priority` — later slices.
- Welfare-owned Assisted Apply (still none; Willie owns build Apply).
- Per-cycle build-flag dedup/cap/folding (deferred, matching `food-independent-concerns`).
- Welfare/Medical sequencing decision (meta-plan §7).
- Prisoner mood concern (meta-plan open question).

---

## 5. Files

**New:** `Src/Ministers/Welfare/WelfareThoughtTaxonomy.cs`; `Src/Tests/Welfare/Fixtures/*` (+ taxonomy tests).
**Edited:** `Src/Common/Briefings/WelfareBriefing.cs` (new sub-records + coverage), `Src/StateStore/Derivations/WelfareBriefingDerivation.cs` (derive them), `Src/Ministers/Welfare/Rules.cs` (independent emission + 2 new rules + 2 refinements), `Src/Ministers/Welfare/WelfareStateSummary.cs` (recreation/comfort/sleep lines), `Src/Tests/Welfare/WelfareRulesTests.cs`, dashboard Welfare view (WB4). Possibly a Willie `RoomClass.Recreation`/`Dining` + table/joy-bench template alias check (only if those classes don't already resolve in the solver — same §3.3 check as Slice A did for Barracks).

## 6. Suggested gimp slicing

- **B1 — data:** WB1 (briefing extensions) + WB2 (taxonomy) + their tests. Pure/deterministic, no rule behavior change yet; `Rules` still compiles against new fields. Land first (briefing-before-rules, mirrors Willie order).
- **B2 — rules:** WB3a (independent emission) + WB3b (new/refined rules) + WB5 rule/fixture tests. The behavior slice.
- **B3 — dashboard:** WB4 HUD. Separable tail; pure frontend + readout.

Land B1→B2→B3; B1 and B3 are independently shippable, B2 depends on B1.
