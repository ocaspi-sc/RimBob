# Test suite: AdviceForNewColony1

Status: PLAN (design-only). Reach-target suite — many assertions are expected to FAIL today. They encode where new-colony day-1 advice SHOULD land, not where it is.

Owner-on-land: Codex (via `bring-out-the-gimp`). Claude writes plan + target map; never edits source on master.

Execution note 2026-06-05: Codex implemented the suite in `codex/advice-for-new-colony-1` and kept the default gate green by moving newly proven current-code gaps into `[Trait("kind","reach")]`. The full snapshot restored through current code derives `resources.total_nutrition=2.45` / `reported` / ~0.51 food-days because all 57 `MealSurvivalPack` stacks in `things` are forbidden, while the reference briefing under `.plans/advice-for-new-colony-1/briefing.food.json` targeted `item_def_catalog` / ~10.7 food-days; the suite pins the target in `Snapshot_DerivesTargetFoodBuffer` as reach. Welfare currently emits shelter only from this fixture, so temperature comfort is reach. Willie standalone `kitchen_missing` is also reach because snapshot mode has no functional-room/anchor evidence. The CI-safe gate is `dotnet test Src\Tests\RimBob.Tests.csproj --filter "kind!=reach&Category!=Live"`.

## Goal

Freeze one real new-colony world, commit the **entire** captured state, and assert what Chef, Welfare, and Willie *should* advise from it — driven through the real pipeline (snapshot → restore → derive briefings → rules → advice). Tests are a golden/reach target: they pin the desired day-1 advice so we can drive rules toward it and catch regressions once they pass.

The user's three asks map to the three sections below: (1) **Save the entire state relevant to briefings** → one committed `ColonyStateSnapshot` (via Git LFS); (2) **Map target advice** → the per-minister target tables; (3) **Write tests for the target** → the end-to-end test design.

## Decision: commit the whole snapshot via Git LFS (not gitignore, not trim)

- The capture is an 8.4 MB `ColonyStateSnapshot` (schema 6). 43% is the full wild-`plants` array; the rest is `thing_defs`/`things`/`terrain`.
- **LFS, whole file.** Git LFS is installed (git-lfs 2.13.2) and already used by this repo (`Docs/guides/.../images/*` in `.gitattributes`). Committing the snapshot through LFS keeps git history light while putting the real state in-repo and reproducible from a clean checkout.
- **Not gitignore** (v1 mistake): a gitignored fixture can't drive a committed test and doesn't actually "save the state" in-repo.
- **Not trim:** trimming `plants` to only crop + edible-forage defs would drop it <1 MB without LFS, but it is lossy and risks silent derivation drift, and contradicts "save the entire state." (The sibling `state-rooted-replay-corpus` effort deliberately builds *compact* hand-trimmed snapshots for a different purpose; this suite keeps the whole capture.)
- **Cost, stated honestly:** a raw snapshot fixture is coupled to `ColonyStateSnapshot.CurrentSchemaVersion`. A schema bump makes `LoadAsync` soft-fail (mismatch) → recapture required. Acceptable for a scenario golden; the test must surface a clear "recapture needed" failure rather than a silent skip.

## 1. Captured state (provenance + committed fixture)

Captured live from the running Host on 2026-06-05, RimWorld `Playing`, paused, `colony_state_origin: snapshot`.

- World: Y1 D1, Aprimay 2, 6h, `game_tick: 538`, 3 colonists (Susskind, Leblanc, Levia). Early growing season, 44 days to winter.
- **Committed fixture (LFS):** `Src/Tests/NewColony/Fixtures/new-colony-1.colony-state.json` — the full `ColonyStateSnapshot`, schema 6, 8.4 MB.
- **Provenance source for the land:** the capture currently sits at `C:\dev\RimBob\var\scenarios\new-colony-1\colony-state.json` (gitignored `var/`, lives in the main checkout). Codex copies it from that absolute path into the fixture path during the land (a worktree does not inherit gitignored files, so an absolute-path copy is required).
- Reference-only (NOT committed): derived briefings + current advice under `.plans/advice-for-new-colony-1/` (`briefing.{food,welfare,willie,mayor}.json`, `snapshot.{chef,welfare,willie}.json`). These are what I read to map the target; the test regenerates briefings from the snapshot rather than committing them.

### State digest (what the three ministers see, derived from the snapshot)

- **Food**: 10.7 days of food (106 units / 57 meals / 0 raw). NO kitchen (0 cooking buildings), NO butcher, NO cooler, NO stockpile zone (0 cells, meals unpositioned). 49 `RawFungus` **forbidden** at (146,0,119), excluded from the buffer. 78 `Plant_Berry` forage at ~26 cells ("moderate"). 48 wild animals but every hunt target is FAR (Tortoise×3 @119, Rat×4 @122, Squirrel×3 @139), all low-risk. Skills: best Plants 8 (1 grower), best Cooking 6 (1 cook). 52,020 growable cells, rich soil present.
- **Welfare**: avg mood 0.526; 0 break-risk / 0 stressed. Beds 0, deficit 3, 0 bedrooms. Thought digest: Health (Sick/Pain, -5, 3 pawns), Temperature ("Chilly" `EnvironmentCold`, -4, 2 pawns: Leblanc+Levia), plus "Sleeping alone" (-4) and "Undergrounder outdoors" (-3). Leblanc fresh_air 0. No recreation source but 0 low-joy pawns. No dining-table thought.
- **Willie**: no power net (0 W, 0 generators/batteries), no thermal control, `functionalRooms: {}`, 0 stockpile zones, 0 buildings, empty backlog. `hasLiveState:false`, `hasAnchorInventory:false`, `anchors: []` (snapshot mode — solver has no anchor to place against).

### Current advice (the baseline to beat — 6 items)

- Chef (4): `expand_growing_capacity` **High** (36 "emergency rice" tiles + starter kitchen) · `wild_harvest_available` Med · `hunt_low_risk_animals` Med (+butcher+campfire builds for a 119-cell hunt) · `freezer_missing` Med.
- Welfare (1): `shelter_floor` High. (Live snapshot is stale v2 — predates `temperature_comfort` Slice D `29efd3b`; no temperature advice despite 2 cold pawns. The committed fixture is current schema, so derivation produces the temperature group.)
- Willie (1): `building_request_active` "Butcher request" — solver **no-fit** ("no requested anchor is available"). The day-1 build that reached Willie was a far-hunt butcher, and it failed to place.

## 2. Target advice map (what it SHOULD be)

Day-1 doctrine (from `Tasks.md` → "About Chef's Advice / Alerts"): surface *important and currently-possible short-term* actions; let the Mayor own grand strategy; forage advice names the nearest plants and marks them; labor requests name a work type/skill; no caravans/trade day-1; basic hauling/cleaning is the game's job.

Tags: **PASS** = current rules likely already produce this · **REACH** = expected to fail today (the point of the suite).

### Chef (Food)

| Target | Priority | Tag | Why / current gap |
|---|---|---|---|
| `food_stockpile_missing` — designate a reachable food stockpile zone near the meals | Medium–High | **REACH** | 57 meals sit unpositioned, 0 stockpile cells. `SetStockpileZone` only fires today inside `nutrition_signal_gap`/`emergency` branches, which don't trigger at a known 10.7-day buffer. Highest-value currently-possible day-1 basic; `Tasks.md` explicitly flags "why not tiles marked as storage?". |
| `starter_kitchen` build request → Willie | Medium | PASS-ish | 57 meals deplete with no cooking building. Request exists today but rides on the `expand_growing_capacity` flag; target wants it legible as its own driver. |
| `unforbid_forbidden_food` — unforbid 49 raw fungus @ (146,0,119) | Medium | **REACH** | Free food currently excluded. `ForbiddenMealUnforbidAction` filters `UnforbidTargets` to `kind=="meal"`, so raw-food fungus is dropped; the unknown/forbidden gap rule needs an unknown food-state that doesn't fire at a known buffer. Tracks the `chef-unforbid-in-rules` todo. |
| `wild_harvest_available` — mark nearest 6 of 78 berries (~26 cells) | Low–Medium | PASS | Already correct (names cluster + distance, attaches `mark_harvest` apply). |
| `expand_growing_capacity` — plant a rice food zone | **Medium** (not High) | **REACH** | Healthy 10.7-day buffer, 44 days to winter — sensible long-game, not an emergency. Demote from High; drop the "emergency rice" framing. |
| `hunt_low_risk_animals` | **Low or suppressed**, no premature butcher build | **REACH** | All targets 119–139 cells away with a healthy buffer — not a "currently-possible short-term" day-1 action. Should demote/suppress and NOT emit butcher+campfire builds for a far hunt. |
| `freezer_missing` | **Low or deferred** | **REACH** | 57 packaged meals don't spoil and there's no raw/foraged surplus yet to protect. Premature at Medium on day 1. |

Net target ordering: stockpile → kitchen → unforbid → forage → plant-zone → (hunt low/none) → (freezer low/none).

### Welfare

| Target | Priority | Tag | Why |
|---|---|---|---|
| `shelter_floor` — 3 beds in a roofed barracks → Willie | High | PASS | Bed deficit 3; rule already fires. |
| `temperature_comfort` — Heater → Willie (cold direction) | High | PASS (on the committed fixture) | Temperature group worst-offset -4 ≤ -3 match threshold; "Chilly" sniffs Cold → Heater; 2 pawns >1 → High. Live snapshot missed it only because it predates Slice D; deriving from the current-schema fixture should emit it. |
| (no spurious escalation for Health Sick/Pain) | — | PASS | Health is unwired (medical); `unexplained_mood_pressure` must NOT fire because shelter+temperature matched. |
| (no recreation/comfort advice) | — | PASS | 0 low-joy pawns; no dining-table thought. |

Target trace: `rules:shelter_floor+temperature_comfort`.

### Willie

Two angles (both worth a test):

- **Standalone** (`Rules.Evaluate(willieBriefing)`, no inbound): `functionalRooms` empty → `kitchen_missing` fires. Assert at least `kitchen_missing` selected. **PASS-ish.**
- **Cross-minister** (inbound = the requests Chef + Welfare emit from this world: beds-barracks **High**, heater, starter-kitchen, food-stockpile): Willie should emit `building_request_active` for the **highest-priority** request (beds/barracks, not a far-hunt butcher) and the solver should produce ≥1 placement option. **REACH** — today it no-fits because the snapshot-mode Willie briefing has empty `anchorInventory` (`hasAnchorInventory:false`), so the solver has nothing to place against. Exposes a real data-coverage gap: day-1 placement needs an anchor (or buildable-region fallback) even in snapshot mode.

## 3. Test design (end-to-end from the snapshot)

New scenario suite that runs the **real pipeline** from the one committed snapshot, mirroring `ColonyStateSnapshotStoreTests.SaveLoadRestore_RoundTripsCuratedSnapshotAndBriefings`.

### Shared load (once per suite)

xUnit class/collection fixture:

1. `ColonyStateSnapshotStore store = await ColonyStateSnapshotStore.LoadAsync(fixturePath, logger);`
2. `ColonyState state = new(); store.RestoreInto(state);`
3. `BriefingCache cache = new(state, logger);`
4. `FoodBriefing food = cache.GetFoodBriefing();` · `WelfareSourceBriefing welfare = cache.GetWelfareBriefing();` · `WillieBriefing willie = cache.GetWillieBriefing();`

Then per minister: `new Rules().Evaluate(briefing)` → `RuleRun.ProjectFor(...)` (a fixed clock, like the existing rules tests). If `LoadAsync` returns no snapshot (schema mismatch / LFS not pulled), fail with an explicit "recapture or `git lfs pull`" message so the failure mode is obvious.

Path discovery: walk up from `Directory.GetCurrentDirectory()` to `Src/Tests/NewColony/Fixtures/new-colony-1.colony-state.json` (same pattern as `FindFixturePath`).

### Test class

`Src/Tests/NewColony/AdviceForNewColony1Tests.cs` (new `RimBob.Tests.NewColony` namespace). Cases (assert target; tag reach cases via `[Trait("kind","reach")]` + a comment so the red is intentional and CI can filter):

- Chef:
  - `Chef_NewColony1_DesignatesFoodStockpile` (REACH) — an advice/action requests a food stockpile zone (meals unpositioned, 0 cells).
  - `Chef_NewColony1_RequestsStarterKitchen` — a `place_blueprint` owned by Willie for a cooking station.
  - `Chef_NewColony1_UnforbidsForbiddenFungus` (REACH) — an `unforbid` action / `ItemRequest` covering the 49 `RawFungus`.
  - `Chef_NewColony1_ForagesNearestBerries` (PASS) — `mark_harvest` for nearest berries with a `MarkHarvestAreaApply`.
  - `Chef_NewColony1_GrowingCapacityIsNotHigh` (REACH) — `expand_growing_capacity` priority ≤ Medium.
  - `Chef_NewColony1_DoesNotPushFarHunt` (REACH) — no `hunt_low_risk_animals` at Medium with a butcher build while buffer is healthy and targets are >100 cells.
- Welfare:
  - `Welfare_NewColony1_EmitsShelterAndTemperature` — trace `rules:shelter_floor+temperature_comfort`; bed `BuildingRequest` (3 beds, barracks) + heater `BuildingRequest`, both → Willie.
  - `Welfare_NewColony1_DoesNotEscalateHealthThoughts` (PASS) — no `Escalate`.
- Willie:
  - `Willie_NewColony1_Standalone_FlagsMissingKitchen` (PASS-ish) — `kitchen_missing` selected.
  - `Willie_NewColony1_PlacesHighPriorityBeds` (REACH) — given inbound beds-barracks High request, `building_request_active` targets the barracks/beds and the solver yields ≥1 option (currently no-fit).
- Pipeline sanity (PASS): `Snapshot_Loads_And_DerivesThreeBriefings` — schema matches, 3 colonists, food 10.7d, welfare bed-deficit 3, willie functionalRooms empty. Guards the fixture and the load path.

### LFS wiring (Codex, at land)

- Copy `C:\dev\RimBob\var\scenarios\new-colony-1\colony-state.json` → `Src/Tests/NewColony/Fixtures/new-colony-1.colony-state.json`.
- Add to `.gitattributes`: `Src/Tests/NewColony/Fixtures/*.colony-state.json filter=lfs diff=lfs merge=lfs -text` (path-scoped, not a broad `*.json`).
- `git lfs track` is implied by the attribute; stage the `.gitattributes` change + the LFS pointer; verify with `git lfs ls-files` before commit.

## Scope / out of scope

- IN: 1 LFS-committed snapshot fixture, the `.gitattributes` LFS line, a shared load fixture, 1 scenario test class with the assertions above.
- OUT: any rule/source change to make reach tests pass (separate slices — `chef-unforbid-in-rules`, a new stockpile-zone rule, hunt/freezer demotion, Willie snapshot-mode anchor coverage). This suite only *defines* the target.
- OUT: cabinet-cycle integration (the "far-hunt butcher beat beds for the day-1 solve" priority bug lives in `CabinetCycle`, not per-minister rules) — note it, don't test it here.
- No wire/persistence/format change; no compat code; no replay-corpus touch. (Fixture is pinned to the current snapshot schema; recapture on schema bump.)

## Verify

`git lfs pull` then `dotnet test` builds and runs. The pipeline-sanity + PASS cases must be green; the named REACH cases are expected RED. Confirm the red set equals exactly the documented reach target. No Host/RIMAPI run needed (pure fixture→store→derive→rules).

**REACH cases MUST be excluded from the default/CI test gate** via `[Trait("kind","reach")]` + a `--filter "kind!=reach"` (or equivalent) on the gating run. This is a hard requirement, not a preference: the follow-on `state-rooted-replay-corpus` plan lands a real forage-regression test against this same snapshot, and an already-red master would mask its failures. The reach reds stay runnable on demand (`--filter "kind=reach"`) as the living target.

## Relationship to `state-rooted-replay-corpus`

Partial overlap, complementary, **land this first**. Both consume one captured world + the `RestoreInto`/`BriefingCache` harness; this suite cuts at `state→briefing→advice`, the sibling extends the same chain to `advice→apply-verdict` (`AssessHarvest`) to catch the `ebfaef5` forage-drift class. The sibling hard-depends on this plan's deliverables (the LFS snapshot, the `.gitattributes` LFS rule, the `Src/Tests/NewColony/` scaffold) and reuses this snapshot as its headline regression fixture (it already carries forage plants `is_harvestable:true` at `growth<0.85`). The sibling's `AssessHarvest` extraction is snapshot-independent and may land anytime/first as its own slice. No file conflict — this suite does not touch `AssistedApplyService.cs`.

## Dashboard

No new panel. Once reach tests drive real rule changes, the HOME new-colony advice for Chef/Welfare/Willie should visibly shift (stockpile + unforbid + warmth surface; far-hunt/freezer recede) — verify in the dashboard at land time per the worktree-launch rule.

## Open questions

1. Fixture placement — chosen `Src/Tests/NewColony/Fixtures/` (one cross-minister scenario dir, single state file) deviates from the per-minister `Src/Tests/<MinisterName>/Fixtures/` convention. Accept the deviation for a multi-minister scenario, or split? Default: scenario dir.
2. ~~Reach-test visibility~~ — DECIDED (see Verify): `[Trait("kind","reach")]`, left RED, hard-excluded from the default/CI gate so `state-rooted-replay-corpus` can land a real regression against the same snapshot without masking.
3. Schema-bump maintenance — recapture-by-hand vs. a small `dotnet run` capture helper / skill. Default: document manual recapture now; revisit a helper if it churns.

## Decisions

- Save the **entire** state: one full `ColonyStateSnapshot` committed via Git LFS (repo already uses LFS). No gitignore, no trimming.
- Test the **full pipeline** end-to-end (snapshot → restore → derive → rules → advice), reusing `ColonyStateSnapshotStore` + `BriefingCache`.
- Suite is reach-target: assertions encode desired day-1 advice; failures are intentional, trait-tagged, and documented.
- Rule changes to turn reach tests green are separate, already-tracked slices.
