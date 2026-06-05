# Welfare — Rules Slice D (implementation plan)

> **Rule breadth, signal-ready cut.** Promote the one signal-ready thought category that passes the meta-plan **promotion test** (concrete, routable advice) into a deterministic rule: **`temperature_comfort`**. Then tighten the escalation bucket so a now-wired category no longer double-counts toward `unexplained_mood_pressure`. See [`welfare-meta-plan.md`](welfare-meta-plan.md) §0, §4, §5, §6.2.
>
> **Builds on** landed A (`eee91c1`) + B (`319930f`) + table port (`b8189f3`) + **C — LLM escalation (`5de6012`)**. `Suggest`-mode. Welfare emits `Advise` + `RequestBuild`; **Willie owns placement** (no new Welfare Apply). Small, focused slice — rule breadth is incremental.
>
> **✅ LANDED `29efd3b` (2026-06-05).** Implemented as planned: `temperature_comfort` rule (cold/hot sniff → `Heater`/`Cooler` `RequestBuild`, ambiguous → `RequestAttention`, Priority High at `WorstOffset ≤ -8` or multi-pawn), `IsEscalationCategory` excludes `Temperature`, 6 fixtures + 7 `WelfareRulesTests` cases. **WD3 follow-up resolved:** at `29efd3b` `RoomTemplateSet` had no heater/cooler template (graceful no-fit); the Willie-side template (`HeaterTemplate`/`CoolerTemplate`) landed `adfda67`, so the request now resolves to real `options[]` end-to-end.

---

## 1. Why temperature, why only temperature

The post-C data reality (verified against `Src/Common/Briefings/WelfareThoughtTaxonomy.cs` + `Rules.cs`):

- `ThoughtCategory` already enumerates `Social`, `Ideology`, `Temperature`, `Health`, `Hunger`, and `ThoughtDigest.ByCategory` already groups every pawn thought into them. So **Social / Ideology / Temperature / Health rules are signal-ready today** — no new RIMAPI read, no derivation change. They currently fall straight into the escalation bucket (`Rules.IsEscalationCategory` = anything not ShelterSleep/Recreation/ComfortBeauty).
- **Promotion test** (meta-plan §5): a signal-ready category earns a deterministic rule only when the rule yields *concrete, routable* advice. Apply it:

| Category | `SuggestedOwner` | Live owner? | Concrete physical fix? | Verdict |
|---|---|---|---|---|
| `Temperature` | `Willie` | ✅ | ✅ `Heater`/`Cooler` exist in `BuildingClass` | **Promote → rule (this slice)** |
| `Social` | `null` | — | ❌ (relationships/schedule, nuanced per-pawn) | Keep **LLM-served** |
| `Ideology` | `null` | — | ❌ | Keep **LLM-served** |
| `Health` | `Medical` | ❌ not live | route-only | **Defer** (Welfare/Medical sequencing, meta §7) |
| `Hunger` | `Chef` | ✅ | Chef's domain; `break_risk` already routes it | Out (no standalone Welfare rule) |

- `guest_hospitality` / `animal_welfare` / `schedule_balance` are **signal-blocked** — no `ThoughtCategory` covers guest-lodging quality, animal living conditions, or schedule slack; each needs a **new** briefing signal (RIMAPI read + derivation). Out of this slice.

So Slice D promotes exactly one rule and leaves escalation to do what it's good at (nuanced social/ideology, novel categories, mood-floor catch-all).

---

## 2. The escalation interaction (the subtle part)

Escalation only fires when `ruleRun.Decisions.Count == 0` (`Rules.Evaluate`). Once `temperature_comfort` is a rule, a colony with a real temperature problem produces decisions → no escalation. Good. **But** `HasUnexplainedMoodPressure` / `DominantEscalationGroup` still scan *all* non-shelter/rec/comfort categories, including `Temperature`. Without a fix, a temperature thought **below** the rule's match threshold but **above** the escalation `MaterialThoughtOffset` would escalate as "unexplained" — even though temperature is now a wired, understood category. That's noisy and wrong.

**Fix:** add `Temperature` to the exclusion set in `IsEscalationCategory`, so escalation covers only genuinely-unmapped pressure (`Social`, `Ideology`, `Health`, `Other`) + the `AverageMood` floor catch-all. After Slice D the escalation bucket = `{Social, Ideology, Health, Other}` + mood-floor. (Social/Ideology stay there **by design** — that's their owner.)

---

## 3. Steps

### WD1 — `temperature_comfort` rule in `Rules.cs`

Add one `MinisterRule<WelfareSourceBriefing>` row to `RuleTable(now)` (after `comfort_beauty`, keeping shelter/break-risk highest). Mirror the existing `comfort_beauty` emission shape.

- **Match** (`MatchesTemperatureComfort`): a `Temperature` `ThoughtDigest` group exists with `WorstOffset <= TemperatureThoughtOffset` (a tunable const; start at `MaterialThoughtOffset` = `-3f` for parity with escalation, tune from R7 telemetry). Gate on `briefing.DataCoverage.HasMoodThoughts`.
- **Reason** (`TemperatureComfortReason`): `thought={group.ExampleLabel}; pawns={group.PawnCount}; worst_offset={…}`.
- **Emit** (`TemperatureComfortEmission`):
  - **Cold vs hot** decides `Heater` vs `Cooler`. Sniff `group.ExampleLabel`/defNames (mirror `IsDiningTablePressure`): tokens `cold`/`snap`/`freez`/`hypothermia` → `Heater`; `hot`/`heat`/`wave`/`heatstroke` → `Cooler`. **Ambiguous/mixed → no `RequestBuild`, emit `RequestAttention` to Willie** ("review temperature control for the affected room") so Welfare never guesses the wrong appliance.
  - **Priority:** `High` when `WorstOffset` is severe (≤ a severe floor, e.g. `-8f`) or multiple pawns; else `Medium`.
  - **`Advise`** (trace `temperature_comfort`): title e.g. "Colonists are too cold" / "Colonists are overheating"; body cites pawn count + worst offset + example thought; rationale ties to the `Heater`/`Cooler` route; one `AdviceAction` — `PlaceBlueprint` owned by `"Willie"` when the appliance is known, else `Note` owned by Welfare.
  - **`RequestBuild`** via `BuildRequestDecisions("temperature_comfort", priority, buildingRequests:[…])`:
    ```
    new BuildingRequest(
        Request: $"add a {heaterOrCooler} to the affected sleeping/work area",
        Reason:  $"temperature thought pressure: {group.ExampleLabel} ({group.PawnCount} pawn{…})",
        TargetClass: BuildingClass.Heater | BuildingClass.Cooler,
        TargetDef: "Heater" | "Cooler",
        RoomClass: RoomClass.Barracks,            // best available room hint; no Temperature RoomClass
        CapacityNeed: new CapacityNeed(CapacityMeasure.Occupants, Math.Max(1, briefing.ColonistCount)),
        Priority: priority,
        RequestedFrom: "Willie")
    ```
  - **Apparel angle is out of scope** (Industry not live): if the dominant temperature driver is apparel-shaped, keep it a player `Note` in body/rationale — do **not** invent an item request to a dead owner.

> Reuse the existing `EmitAdvice` / `BuildRequestDecisions` / `Plural` / `ToSnakeCase` / `ThoughtGroup` helpers. No new emission machinery.

### WD2 — Tighten the escalation bucket

In `Rules.cs`, extend `IsEscalationCategory` to also exclude `ThoughtCategory.Temperature`:

```
private static bool IsEscalationCategory(ThoughtCategory category) =>
    category is not ThoughtCategory.ShelterSleep and
        not ThoughtCategory.Recreation and
        not ThoughtCategory.ComfortBeauty and
        not ThoughtCategory.Temperature;        // NEW: temperature is now a wired rule
```

`HasUnexplainedMoodPressure` / `DominantEscalationGroup` / `BuildEscalationContext` inherit this automatically (they all route through `IsEscalationCategory` or order the full digest). Escalation now triggers only on `{Social, Ideology, Health, Other}` material pressure or the `AverageMood` floor. **No prompt change required** — `welfare.system.md` already lists temperature under Welfare's domain and tells the LLM to route physical fixes to Willie; the deterministic rule simply intercepts the clear cases first.

### WD3 — Willie solve-path dependency check ✅ RESOLVED

Welfare's contract ends at emitting `RequestBuild(Heater|Cooler)`. **Finding (at `29efd3b`):** `BuildingClass.Heater`/`Cooler` existed but `RoomTemplateSet` had **no** heater/cooler placement template → requests produced a graceful no-fit. Filed as a Willie-side follow-up (out of Welfare scope — no placement built here).

**Follow-up landed `adfda67`:** `HeaterTemplate.cs` + `CoolerTemplate.cs` + `RoomTemplateSet` wiring + `RoomTemplateSetTests`. Welfare's `temperature_comfort` `RequestBuild` now solves to real `options[]` in Willie's Build Queue. Recorded in meta-plan §3 reused-machinery.

### WD4 — Fixtures + tests

- **Rules:** `too-cold` fixture (Temperature group, cold labels, no shelter gap) → `RuleRun` contains `Advise("temperature_comfort")` + `RequestBuild(Heater, RequestedFrom:"Willie")`, **no** `Escalate`. `too-hot` → `Cooler`. `temperature-ambiguous` (mixed cold+hot) → `RequestAttention`, no `RequestBuild`. `mild-temperature` (offset above match threshold, below escalation floor) → **no rule, no escalate** (proves WD2 closed the double-fire). `social-only` (Social dominant, no temperature) → still `Escalate("unexplained_mood_pressure")` (proves social stays LLM-served).
- **Multi-pressure:** shelter gap + cold → both `shelter_floor` and `temperature_comfort` decisions (all-hits), priority-sorted, no escalate.
- **Severity:** severe cold → `Priority.High`; mild → `Medium`.
- Extend the existing `WelfareRulesTests` table-trace assertions for the new rule + the shrunken escalation set.

### WD5 — Docs

- Append a Slice D row to meta-plan §3 (done in this change set) — no further doc here beyond the plan Summary on land.
- The **promote → `welfare.md`** canonical-doc sweep (meta §6.3) stays a **separate** step; do not fold it in.

---

## 4. Acceptance

- `dotnet test Src/Tests/RimBob.Tests.csproj` green incl. the new temperature + escalation-shrink fixtures.
- A cold/hot colony → deterministic `temperature_comfort` advice + a Willie `Heater`/`Cooler` `RequestBuild` (or `RequestAttention` when the appliance is ambiguous); **no** LLM escalation for that cycle.
- A mild-temperature colony neither emits a temperature rule nor escalates (no double-fire).
- A social/ideology-dominant colony still escalates to the LLM (unchanged) — confirming the promotion boundary.
- Willie solve-path outcome recorded (solves / needs template follow-up).
- Every cycle replayable as before (rules path; escalation path unchanged).

---

## 5. Out of scope

- `social_pressure` / `ideology_pressure` deterministic rules — **kept LLM-served by design** (promotion test fails: no physical fix, no live owner).
- `health_pressure` — deferred to the Welfare/Medical sequencing decision (meta §7).
- `guest_hospitality` / `animal_welfare` / `schedule_balance` + a deeper apparel/exposure temperature signal — **signal-blocked**, each a later slice gated on a new RIMAPI read.
- Adding a Willie `Heater`/`Cooler` placement template (Willie-side follow-up if WD3 finds it missing).
- Promote → `welfare.md` (separate step).
- Any new Welfare-owned Apply.

---

## 6. Files

**Edited:** `Src/Ministers/Welfare/Rules.cs` (one `RuleTable` row + emission helpers + `IsEscalationCategory` exclusion + temperature consts), `Src/Tests/Welfare/WelfareRulesTests.cs` (+ new fixtures under `Src/Tests/Welfare/Fixtures/`).
**Verify-only:** Willie placement templates / `placement-solver-*` (WD3) — no edit unless a Willie follow-up is opened.
**No change:** `MinisterOfWelfare.cs`, `welfare.system.md`, registry, DI, RAG, parser — Slice C wiring already carries everything Slice D needs.

## 7. Suggested gimp slicing

Single small slice — land in one pass:
- **D1:** WD1 (`temperature_comfort` rule) + WD2 (escalation tighten) + WD4 tests. The whole behavioral change.
- WD3 (Willie verify) runs first as a read-only check; its outcome only adds a follow-up task, not code here.

Land D1.

---

## Summary (landed `29efd3b`, 2026-06-05)

**WD1 + WD2 + WD4 landed in one pass.**

`temperature_comfort` rule added after `comfort_beauty` in `RuleTable`. Match threshold `TemperatureThoughtOffset = -3f` (parity with `MaterialThoughtOffset`). Cold/hot sniff via token matching on `ExampleLabel`; ambiguous labels (e.g. `SoakingWet`) route `RequestAttention` to Willie instead of guessing the wrong appliance. Priority is `High` when `WorstOffset <= -8f` or `PawnCount > 1`, else `Medium`. `IsEscalationCategory` now excludes `ThoughtCategory.Temperature` — a sub-threshold temperature thought no longer double-fires as `unexplained_mood_pressure`. Escalation bucket post-Slice D: `{Social, Ideology, Health, Other}` + `AverageMood` floor.

**WD3 outcome:** `BuildingClass.Heater`/`Cooler` both exist. `RoomTemplateSet` has no Heater or Cooler placement template — `RequestBuild(Heater|Cooler)` produces a graceful no-fit from Willie's solver. Welfare still emits the request correctly. Willie-side follow-up filed as `willie-heater-cooler-placement-template` in `Tasks.md`.

**Tests:** 6 new fixtures (`too-cold`, `too-hot`, `temperature-ambiguous`, `mild-temperature`, `social-only`, `cold-with-shelter-gap`) + 7 new `WelfareRulesTests` cases. 19/19 welfare tests green. `AssertAllRulesIncludeTableAndStableFallback` updated to include `temperature_comfort`.
