# Welfare — Rules Slice A (implementation plan)

> **LANDED `eee91c1`, then refactored.** Historical plan. Vocabulary below predates two repo-wide refactors: the `concern`/`WelfareConcern` enum was **deleted** ([`delete-concern.md`](delete-concern.md)) and rules were ported to the **table-driven all-hits** engine ([`welfare-rules-table-port.md`](welfare-rules-table-port.md), `b8189f3`). Where this plan says `WelfareConcern`/`concern`/`AdvicePriority`/first-match cascade, the live code uses trace ids / `Priority` / `MinisterRule` table / `EvaluateAllHits`. Current architecture: [`welfare-meta-plan.md`](welfare-meta-plan.md) §0. Source of truth: `Src/Ministers/Welfare/Rules.cs`.

> First deterministic rule cut for the Welfare (Mood & Needs) minister. Ships the **north-star vertical slice** from [`welfare-meta-plan.md`](welfare-meta-plan.md): on a new colony, Welfare detects the missing shelter floor and emits a `building_request` to Willie for a basic barracks with beds — reusing Willie's `functional_rooms` + Placement Solver with **zero new placement code**.
>
> Mirrors [`willie-rules-slice-a.md`](willie-rules-slice-a.md) (WR1–WR4), which mirrored Food. `Rules.cs` is the first file and must compile + have tests before any LLM wiring (Repo Conventions).
>
> **Scope discipline:** rules-only, `Suggest`-mode, no LLM, no Welfare-owned Apply. Two wired rules: `shelter_floor` (the headline) and `break_risk` (mood safety net). `recreation_gap` / `comfort_beauty` are defined in the enum but deferred to Slice B (need the R3 thought taxonomy + joy-threshold tuning).

---

## 1. Grounding — what already exists (verified by read, not assumed)

- **Source briefing exists and is live.** `WelfareSourceBriefing` (`Src/Common/Briefings/WelfareBriefing.cs`) is derived by `WelfareBriefingDerivation.Compute` (`Src/StateStore/Derivations/`), cached via `BriefingCache.GetWelfareBriefing()`, and served at `GET /api/briefings/welfare/latest`. Slice-A rules read it **directly** — no new rule-facing briefing record this slice.
- **Fields available to Slice-A rules** (`WelfareSourceBriefing`):
  - `ColonistCount`
  - `Mood { AverageMood, BreakRiskCount, StressedCount, ContentCount }` (derivation thresholds: break `< 0.35`, stressed `0.35–0.50`, content `> 0.65`)
  - `WorstPawns[] { Id, Name, Mood, Sleep, Comfort, Beauty, Joy, FreshAir, DrugsDesire, TopNegativeThoughts[] { DefName, Label, MoodOffset, StageIndex } }` (≤8 pawns, worst mood first)
  - `NeedLows[] { PawnId, PawnName, Need, Value }` (needs `< 0.35`)
  - `Rooms { Count, BedroomCount, PrisonCellCount, AverageImpressiveness, WorstRooms[] { …, CellsCount, IsPrisonCell, OpenRoofCount } }` — `BedroomCount` = rooms whose role contains "bedroom" **or** that contain ≥1 bed.
  - `DataCoverage { HasNeedLevels, HasMoodThoughts, HasRooms, HasRoomQuality }`
- **No single total-bed-count field.** `BedroomCount == 0` is the honest day-1 "no sleeping shelter" signal. Bed-deficit-by-count (`beds < colonists` when some beds exist) is a Slice-B derivation extension — out of scope here.
- **Registry slot exists.** `MinisterRegistry` (`Src/Coordination/MinisterRegistry.cs`) already has `new("welfare", "Welfare", "minister", false, MinisterViews)` — `Ready:false`, no `CabinetOrder`. Activating Welfare = editing this one descriptor.
- **Mirror minister.** `Src/Ministers/Willie/` — `MinisterOfWillie.cs` (skeleton + `PublishSnapshot` + replay), `Rules.cs` (`IMinisterRules<T>`, first-match cascade, `DecisionFor` → `AdviceItem` + `AgentFlag` with `requests.BuildingRequestsOrNull`), `WillieFlagRequests.cs` (request-bundle builder), `WillieStateSummary.cs` (factual summary line). Welfare's minister is **simpler** — no `PlacementSolver`, `ColonyState`, or `SolverStore` deps.

### The reuse that makes this cheap

Willie consumes inbound building requests where `RequestedFrom == "Willie"` (`MinisterOfWillie.IsRequestedFromWillie`, case-insensitive), via flags it reads from `FlagChannel.Active()`. A request with `RoomClass != null` routes to `WillieConcern.FunctionalRooms` → `BuildingRequestActiveTrace` → `PlacementSolver.SolveAsync` → `options[]`. **So Welfare's entire job is to publish an `AgentFlag` carrying a `BuildingRequest { RoomClass = Barracks, …, RequestedFrom = "Willie" }`.** This is the exact path Chef's freezer already uses.

---

## 2. Steps

### WS1 — `WelfareConcern` enum

New `Src/Common/Advice/WelfareConcern.cs`, mirroring `WillieConcern.cs` (same `[JsonConverter(typeof(SnakeCaseLowerEnumConverter<WelfareConcern>))]` attribute, snake_case wire):

```csharp
public enum WelfareConcern
{
    BreakRisk,       // wired Slice A
    ShelterFloor,    // wired Slice A — new-colony headline
    RecreationGap,   // defined; wired Slice B
    ComfortBeauty,   // defined; wired Slice B
}
```

Closed per-minister enum; the autonomy dial graduates one at a time ([`advice.md`](../docs/design/advice.md) "Concern"). Defining the two Slice-B values now signals direction at no cost; only `BreakRisk` + `ShelterFloor` are emitted this slice. (Final/full candidate set lives in `welfare-advice-types.md`; this slice does not need it locked.)

### WS2 — `Rules.cs`

New `Src/Ministers/Welfare/Rules.cs` implementing `IMinisterRules<WelfareSourceBriefing>` (same shape as Willie's `Rules`). First-match cascade, highest-value rule first, fallthrough to a successful empty `Decision`. Pure, deterministic, no I/O.

Rule order:

1. **`break_risk`** (priority `High`; `Critical` if `BreakRiskCount` ≥ half the colony) — fires when `ColonistCount > 0 && Mood.BreakRiskCount > 0`.
   - Advice: names the count at risk + the dominant driver = the worst pawn's most-negative `TopNegativeThoughts[0]` (`Label ?? DefName`).
   - Actions: one `note` ("address <driver> for <pawn> before a mental break"). **No flag** — pure player-facing mood advice in Slice A. (Routing the driver to Chef/Medical/Willie is Slice B once the R3 thought→owner map exists.)
   - Why first: imminent break is the highest-urgency Mood & Needs state; on a fresh colony this is empty (correct).
2. **`shelter_floor`** (priority `High`) — fires when `DataCoverage.HasRooms && ColonistCount > 0 && Rooms.BedroomCount == 0`. **The new-colony headline.**
   - Advice: concern `shelter_floor`, body "All N colonists have no beds and will sleep unsheltered — mood and rest will suffer." Rationale cites the missing bedroom + (if present) any `slept_outside`/`slept_on_ground` thought already in `WorstPawns`.
   - Action: one `note` — "Colonists need a roofed barracks with N beds; sent to Willie for placement." (Welfare never authors `place_blueprint`; the Apply lives on Willie's card.)
   - **Flag (the load-bearing output):** one `AgentFlag` (`Domain: "welfare"`, `Id: "welfare:shelter_floor"`, `SourceMinister: "Welfare"`, severity from priority) carrying `BuildingRequests = [ shelterRequest ]`:
     ```
     new BuildingRequest(
         Request:       "basic barracks with N beds",
         Reason:        "colony has no bedroom/beds; colonists will sleep unsheltered",
         TargetClass:   BuildingClass.Bed,
         TargetDef:     "Bed",
         RoomClass:     RoomClass.Barracks,
         CapacityNeed:  new CapacityNeed(CapacityMeasure.Beds, ColonistCount),
         Adjacency:     null,                       // no anchor preference day 1; Willie home-area fallback handles it
         Priority:      AdvicePriority.High,
         RequestedFrom: "Willie")
     ```
     (`Temperature` left null for Slice A — roof/enclosure is the survival floor; a temperature-band ask is a Slice-B refinement. `Adjacency` null relies on Willie's landed Home-area buildable-region fallback anchor.)
3. **fallthrough** — `Decision([], [], "needs_stable", …)`: successful **empty** snapshot when no rule matches (a colony with beds + no break risk). Mirrors Willie's `maintain_build_program`.

Build `AdviceItem` + `AgentFlag` with a `DecisionFor`-style helper copied from Willie `Rules` (id `welfare_<trace>`, `Concern = ToSnakeCase(concern)`, `BriefingRef`, game-time fields from the briefing, expiry by priority). A `WelfareFlagRequests` bundle helper (mirror `WillieFlagRequests`) is optional for one request — a direct single-element `BuildingRequests` list is fine; prefer the helper only if it reads cleaner.

Also emit the rule-trace diagnostics (matched/suppressed/all-rules) like Willie `Rules.DiagnosticsFor` so the dashboard rules view and replay records attribute behavior.

### WS3 — `MinisterOfWelfare` + state summary + registration

- New `Src/Ministers/Welfare/MinisterOfWelfare.cs : IMinister` — mirror `MinisterOfWillie.RunPlayCycle` **minus** all placement-solver logic. Deps: `BriefingCache`, `Rules`, `MinisterOutputStore`, `AdviceBus`, `FlagChannel`, `MinisterTraceStore`, `ILogger<MinisterOfWelfare>`, `MinisterReplayRecorder?`. Flow: `briefings.GetWelfareBriefing()` → `rules.Evaluate(briefing, ColonyContext.Default)` → on `Decision`: `PublishSnapshot(advice, flags, summary)` (= `bus.ReplaceMinisterAdvice` + `flags.Publish` per flag) + `PersistReplayAsync`; on `Escalate`: publish empty snapshot + replay (no LLM wired, so escalate should not occur in Slice A — keep the arm for symmetry). `Name => "Welfare"`. `RunRefinement` → `Task.CompletedTask`.
- New `Src/Ministers/Welfare/WelfareStateSummary.cs` — factual one-block summary (mirror `WillieStateSummary.Build`): mood distribution (`content/stressed/break-risk` counts, avg), shelter status (`BedroomCount`, beds present y/n), top driver thought if any, and a data-coverage caveat when `HasRooms`/`HasMoodThoughts` is false. No advice, no LLM prose.
- **DI** (`Src/ApiHost/Program.cs` or wherever `MinisterOfWillie` + Willie `Rules` are registered): register `Welfare.Rules` and `MinisterOfWelfare` the same way.
- **Registry** (`MinisterRegistry.cs`): replace the dormant welfare descriptor with an active rules-only one:
  ```csharp
  new(
      Key: "welfare",
      Label: "Welfare",
      Kind: "minister",
      Ready: true,
      EnabledViews: ["briefing", "rules", "advice"],   // no build_queue/solver — those are Willie's
      CabinetOrder: 12,                                 // after Chef(10), BEFORE Willie(15) — see §3
      CanManualTrigger: true,
      CanRunRules: true)
  ```
- **Cabinet wiring:** ensure `MinisterOfWelfare` is added to the cabinet run set the same place `MinisterOfWillie` is (so `CabinetOrder: 12` actually schedules it). Confirm the manual-trigger and `Run Rules` paths resolve the new minister.

### WS4 — tests + fixtures

`Src/Tests/Welfare/` (mirror `Src/Tests/Willie/` layout; fixtures under `Src/Tests/Welfare/Fixtures/`):

- **`new-colony` fixture (this is R4 — the headline regression):** a captured/synthesized day-1 `WelfareSourceBriefing` — `ColonistCount: 3`, `Rooms.BedroomCount: 0`, `HasRooms: true`, no break risk. Assert: exactly one `shelter_floor` `AdviceItem` (priority `High`); exactly one `AgentFlag` with one `BuildingRequest` where `RoomClass == Barracks`, `CapacityNeed == (Beds, 3)`, `RequestedFrom == "Willie"`, `Priority == High`; `break_risk` absent; trace `shelter_floor`.
- **`has-beds` fixture:** `BedroomCount: 1`, no break risk → empty `Decision`, trace `needs_stable`, zero flags.
- **`break-risk` fixture:** `BreakRiskCount: 1` with a worst pawn carrying a negative thought → one `break_risk` `AdviceItem` naming the driver, **no** flag.
- **Rules-trace test:** matched/suppressed/all-rules diagnostics shape parity with Willie.

---

## 3. Cross-minister integration (verify; expected to need NO new Willie code)

The slice is only "done" when the new-colony flag actually drives Willie to barracks options. Three pre-flight checks (each is a verification, not anticipated new work):

1. **Cabinet ordering / flag consumption.** Welfare at `CabinetOrder: 12` runs before Willie (`15`), so its `welfare:shelter_floor` flag is in `FlagChannel.Active()` when Willie runs the same cycle. Confirm `CabinetCycle` triggers/permits Willie on a building-request flag regardless of source minister (the per-build-flag Willie run at `CabinetCycle.cs:~328` must not be Chef-specific). If it is source-gated, generalize it — flag as a dependency.
2. **`RequestedFrom` match.** Welfare sets `RequestedFrom: "Willie"`; `IsRequestedFromWillie` matches case-insensitively. ✓ by construction.
3. **`RoomClass.Barracks` resolves to a template.** placement-solver-3 landed bedroom/barracks templates + a `RoomClass` alias map. Confirm `Barracks` maps to a bed-bearing template (`BedroomTemplate` via `CapacitySizing` on `CapacityMeasure.Beds`). If `Barracks` is unmapped, add the alias (small Willie change) — call it out as the one possible Willie-side edit.

If all three hold, the only new code is Welfare-side (WS1–WS4). Live/integration verification: run the new colony, confirm Welfare publishes the flag and Willie's Build Queue shows barracks `options[]` with an Apply payload. Verify via the snapshot/replay JSON first (per memory: JSON evidence before dashboard screenshot).

---

## 4. Acceptance (slice exit)

- `dotnet test Src/Tests/RimBob.Tests.csproj` green, including the three Welfare fixtures.
- On the new-colony fixture/live save: Welfare emits exactly one `High` `shelter_floor` advice + one barracks `building_request` flag (`RequestedFrom: Willie`, `Beds == ColonistCount`); `break_risk` empty; factual state summary present.
- Integration: that flag drives Willie's existing `functional_rooms` path to ≥1 validated barracks option (no new placement code, modulo the §3.3 alias check).
- Dashboard: Welfare tab is `Ready`, Briefing view renders the source briefing, Advice view renders the shelter card. (Dedicated mood HUD = Slice B.)
- Replay record persisted for the Welfare cycle (briefing + trace + advice + flags + summary).

---

## 5. Out of scope (explicit)

- LLM escalation (Slice C / `welfare-llm-slice-c.md`).
- `recreation_gap`, `comfort_beauty`, and any thought→owner routing (needs R3 taxonomy) — Slice B.
- Bed-deficit-by-count, enclosure/open-roof rules, temperature-band asks, room-quality/impressiveness rules — Slice B derivation extensions.
- Welfare-owned Assisted Apply — none; Welfare requests, Willie owns the build Apply.
- Schedule/`set_priority`, social/ideology/guest/animal concerns — later slices.
- Welfare/Medical sequencing decision (meta-plan §7) — unblocked by this slice but not decided here.
- **No compat code:** Slice A reads existing `WelfareSourceBriefing` fields, so no schema/wire change and no wipe-regen. If Slice B adds derived fields, that plan carries the wipe-and-regen note.

---

## 6. Files

**New:** `Src/Common/Advice/WelfareConcern.cs`, `Src/Ministers/Welfare/Rules.cs`, `Src/Ministers/Welfare/MinisterOfWelfare.cs`, `Src/Ministers/Welfare/WelfareStateSummary.cs`, `Src/Tests/Welfare/*` (+ `Fixtures/`).
**Edited:** `Src/Coordination/MinisterRegistry.cs` (activate welfare descriptor), `Src/ApiHost/Program.cs` (DI + cabinet wiring), possibly `Src/.../CabinetCycle.cs` (§3.1, only if Willie's build-flag run is source-gated) and a Willie `RoomClass` alias (§3.3, only if `Barracks` is unmapped).

## 7. Suggested gimp slicing

One Codex slice is feasible (mirrors a landed pattern), but if split: **WS1+WS2+WS4-rules** (enum + Rules + rules tests, pure/deterministic, no wiring) first; then **WS3 + integration** (minister + registry + DI + cabinet + cross-minister verify). Land WS1/WS2 independently since `Rules.cs` must compile + test before wiring per Repo Conventions.
