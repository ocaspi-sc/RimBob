# Milestone: NC1 vertical slice

Index for the NC1 milestone — status + routing only. Detail lives in the linked subplans. Living doc; update on each landing.

## Goal

NC1 = canonical dev world (user loads `new-colony-1` save each RimWorld boot). Done when, on that one world: Chef + Welfare each emit full correct advice, Willie solves every emitted request, every Apply works end-to-end, Mayor is minimal+appropriate, architecture stays clean, NC1 test coverage strong, dashboard curated.

Fixture: [new-colony-1.colony-state.json](../Src/Tests/NewColony/Fixtures/new-colony-1.colony-state.json) (Y1 D1, tick 538, 3 colonists, LFS). Suite: [AdviceForNewColony1Tests.cs](../Src/Tests/NewColony/AdviceForNewColony1Tests.cs). Real pipeline: snapshot→restore→briefing→rules.

## Goals & status — 2026-06-10

Legend: ✅ done · 🟢 healthy · 🟡 gap · ⚪ unscoped.

| # | Goal | Plan(s) | Status | Gap (1 line) |
|---|---|---|---|---|
| 1 | World state = NC1 | (fixture) | ✅ | Frozen + loads. But fixture diverges from live save → [nc1-fixture-recapture.md](nc1-fixture-recapture.md). |
| 2 | **Chef** full advice | [target](advice-for-new-colony-1.md) · [fixes](new-colony-1-reach-fixes.md) · [r4](reach4-chef-priority-demote.md) | 🟢 | All NC1 reach reds green (set empty): latent-food split `fde7d64`, growing/far-hunt demote `3f009dd`. Over-production curation = loose note, deferred. Live dashboard verify rides the batch. |
| 3 | **Welfare** full advice | [rec slice](welfare-recreation-anticipatory.md) · [meta](welfare-meta-plan.md) | 🟢 | shelter + temperature + **recreation (anticipatory, Low)** all emit on NC1 ; comfort/dining verified quiet. Rec slice LANDED `8131285`. Live dashboard verify rides the batch. |
| 4 | Willie solves all requests | [packing](nc1-fixture-recapture.md) | 🟡 | Heater/Cooler + Recreation templates registered (verified 2026-06-10). Non-rect packing DESCOPED 2026-06-11 (Home auto-expands). Remaining: real-packing tests on recaptured fixture. |
| 5 | Apply actions | [router](s3-apply-chain.md) · [grow-zone](grow-zone-apply-500-mainthread.md) · [forage](forage-apply-harvestable-readiness.md) · [hunt](mark-hunt-apply-per-animal-designation.md) | 🟡 | All 6 apply methods exist. Remaining = grow-zone S6/S7 + hunt S1/S2 truthfulness (RIMAPI-fork + RimBob) + live verify. RIMAPI-first, live-gated. |
| 6 | Mayor minimal+appropriate | [mayor-minimal-nc1.md](mayor-minimal-nc1.md) | ⚪ | Spec drafted; test strategy undecided. |
| 7 | Good architecture | (close gate) | 🟢 | Rules-as-data/all-hits, typed `Decision`, concern deleted, solver dedup landed. Close gate: `/simplify` + `/code-review` over **all major subsystems** + named-debt list. |
| 8 | NC1 test coverage | [fixes](new-colony-1-reach-fixes.md) · [apply-seam](state-rooted-replay-corpus.md) · [recapture](nc1-fixture-recapture.md) | 🟢 | Reach set EMPTY — all 8 targets green plain Facts. `kind!=reach` 672/672. Bed test synthetic until recapture. |
| 9 | Dashboard: everything, good UX, no clutter | [dashboard-nc1-declutter.md](dashboard-nc1-declutter.md) · [rule-card](dashboard-rule-card.md) | 🟡 | Decided: HOME default; demote raw-briefing + rule-tables + LLM-traces + SYSTEM-diagnostics behind SYSTEM/INFO toggle. Rule-card pending. |
| 10 | Cabinet integration (NC1 end-to-end) | [nc1-cabinet-integration.md](nc1-cabinet-integration.md) | ⚪ | New goal 2026-06-09. Full cabinet run on fixture → Mayor reflects all 3 feeders, flags no dupes; one integration test. |

## Sequence

Cheap+plan-backed first, design-gated last. Lane: gimp = RimBob worktree; codex = RIMAPI fork (rebuild `-c Release-1.6` + reload).

1. **S1 Chef calibration** — **LANDED 2026-06-10**: reach-5 latent-food `fde7d64` (CloseOut) + reach-4 growing/far-hunt demote `3f009dd` (gimp Sonnet lane). All 3 reds flipped; NC1 reach set EMPTY. Over-production curation = loose note, deferred.
1b. **Welfare recreation (anticipatory)** — [welfare-recreation-anticipatory.md](welfare-recreation-anticipatory.md). **LANDED `8131285`** (642 offline tests green ; NC1 emits shelter+temp+rec).
2. **S2 Willie Heater/Cooler template** — **DONE** (verified 2026-06-10: `HeaterTemplate`/`CoolerTemplate` registered in `RoomTemplateSet.Default`; task `willie-heater-cooler-placement-template` stale). Live-pack verify rides recapture.
3. **S3 Apply chain** — [router](s3-apply-chain.md). All 6 apply methods exist; remaining = grow-zone **S6/S7** + hunt **S1/S2** truthfulness (RIMAPI-fork + RimBob); forage/unforbid/cook-bill/Willie-build coded → live-verify. **2 repos, RIMAPI-first, live-gated** (RIMAPI down now; recapture L1 in another session). **~4 code slices + 1 live batch.**
4. **S4 Mayor minimal** — [mayor-minimal-nc1.md](mayor-minimal-nc1.md). **Design-gated** (test strategy).
5. **S5 Dashboard declutter** — [dashboard-nc1-declutter.md](dashboard-nc1-declutter.md). **Design-gated** (important-views taste).
6. **S6 Test polish + recapture** — [newcolony-test-polish] ; [nc1-fixture-recapture.md](nc1-fixture-recapture.md) **ACTIVE 2026-06-11** (save final; capture running).

## Human-gated steps & JSON state

**JSON-state regen.** Schema/wire change → wipe-and-regen (AGENTS.md, no compat). Two kinds:
- *Auto / deterministic in-slice* (codex, no human): replay-corpus regen (capture-helper from fixtures) ; persisted runtime state (`%LOCALAPPDATA%\RimBob\…`) wiped → auto-regen on next Host run ; briefing-shaped committed fixtures.
- *Human-gated* (only 2): **(1) NC1 colony-state fixture** — changes ONLY on recapture (live, HOLD). Briefings are DERIVED from the snapshot at test time, so a briefing schema change (reach-5 latent-food) does NOT need recapture. **(2) Mayor golden snapshot** — capture real LLM output + user approval.

**Live-game-gated — batch in ONE RimWorld-on-NC1 session (cannot pre-decide):**
- L1 Fixture recapture (after save finalized) → unblocks Willie real-packing tests.
- L2 RIMAPI fork rebuild `-c Release-1.6` + RimWorld restart (no hot-reload) for S3 grow-zone marshal-and-wait + hunt per-thing endpoint + def-resolution fix.
- L3 Live apply verify all paths + truthful readback.
- L4 Delete 2 stray grow zones left in-game from earlier diagnosis.
- L5 Mayor live agenda eyeball.

**Recurring approval:** Mayor golden re-baseline on every prompt/model change.

**Up-front decisions:**
- D1 — apply paths that don't fire on NC1-Day-1 (hunt suppressed, cook-bill no-kitchen): **decouple** — each path gets unit + standalone-live coverage; NC1-Day-1 live-verifies only firing paths (unforbid/harvest/grow-zone). *(decided 2026-06-10)*
- D2 — Mayor golden: **user approves each baseline** (Gemini capture, subagent fallback; re-baseline needs sign-off). *(confirmed 2026-06-10)*
- D3 — Welfare rec: **decided B (anticipatory)** 2026-06-10 → [welfare-recreation-anticipatory.md](welfare-recreation-anticipatory.md). Widen `recreation_gap` to fire on missing rec source, Low priority. *(done)*

## Decisions log

- **2026-06-11** — recapture **UNBLOCKED**: user confirms canonical NC1 save live + final; capture subagent dispatched (Host restart → fresh ingest → fixture overwrite → suite run). **Non-rect packing DESCOPED from NC1** (user): Area_Home auto-expands when rooms built outside it; bounding-box clamp suffices. Cell-mask = post-NC1 option. [nc1-fixture-recapture.md](nc1-fixture-recapture.md).
- **2026-06-10** — **LANDED `3f009dd`** (S1 reach-4): Chef day-1 priority demote via the **gimp Sonnet lane** (Sonnet 4.6 sub-agent; gimp-verifier Adherent:yes). `HasHealthyLatentFoodReserve` (`LatentFoodDays>=7`) caps growing→Medium, hunt→Low + suppresses far-hunt butcher/campfire build. **NC1 reach set now EMPTY.** [reach4-chef-priority-demote.md](reach4-chef-priority-demote.md).
- **2026-06-10** — **LANDED `fde7d64`** (S1 reach-5): Option-C latent-food. `LatentFoodDays` on FoodBriefing/Mayor; `EstimatedDaysOfFood` stays honest (~0.5d edible). Landed the paused Codex branch (re-merged master clean, `kind!=reach` 668/668, CloseOut) per user "land r5 now". Unblocked reach-4.
- **2026-06-10** — added a **Sonnet implementer lane** to `bring-out-the-gimp` (Codex stays default; a Sonnet 4.6 sub-agent implements when asked, Claude owns the worktree + squash-land). First drafted as a separate `diy-sonnet-4.6` skill, then folded into the gimp skill per user (DRY — the two shared ~80%). Session directive (this session): Sonnet sub-agent instead of Codex for gimping.
- **2026-06-10** — **LANDED `8131285`** (S1b): Welfare anticipatory recreation. NC1 Welfare = shelter+temperature+recreation ; 642 offline non-reach tests green. Implemented directly in a worktree (no gimp, per user).
- **2026-06-10** — verify: Heater/Cooler + Recreation Willie templates already registered in `RoomTemplateSet.Default` → **S2 done** (stale `willie-heater-cooler-placement-template`) ; Welfare rec request will solve end-to-end.
- **2026-06-10** — D3: Welfare recreation = **B (anticipatory)** — rec fires Day-1 on missing rec source (not just low joy), Low priority ; parallels `shelter_floor`. Slice [welfare-recreation-anticipatory.md](welfare-recreation-anticipatory.md).
- **2026-06-10** — D1: apply verify = decouple path from NC1 trigger (unit + standalone-live per path ; NC1-Day-1 live-verifies firing paths only).
- **2026-06-10** — interview cont.: dashboard demote-list = raw-briefing + rule-tables + LLM/escalation traces + SYSTEM/probe diagnostics behind a SYSTEM/INFO toggle ; **all** allowlisted applies (incl. hunt + cook-bill) in the NC1 gate ; architecture close-gate = `/simplify` + `/code-review` + debt list over **all major subsystems**.
- **2026-06-09** — interview: (a) cabinet integration = **goal #10 + integration test** ; (b) Welfare Day-1 target = **shelter + temperature + recreation**, comfort/dining suppressed ; (c) Mayor test = **recorded LLM-output snapshot, re-baselined on prompt/model change** ; (d) dashboard default landing = **HOME**.
- **2026-06-09** — split Chef and Welfare into separate goals (#2, #3).
- **2026-06-09** — fixture recapture **HOLD**: user still setting up canonical save; Willie placement tests stay synthetic meanwhile. See [nc1-fixture-recapture.md](nc1-fixture-recapture.md).
- **2026-06-09** — stale Tasks.md checkboxes (code ahead): `new-colony-1-reach-fixes` reach-1/2/3/6 DONE (green plain Facts), only reach-4/5 remain ; `state-rooted-replay-corpus` seam closed (`AssessHarvest` green) ; `forage-apply-readiness` verdict green at unit level, live HTTP unverified. (Tasks.md not edited — carried a human edit at session start.)

## Verified test state (ran 2026-06-10)

- Full `--filter "kind!=reach"` → **672 pass / 0 fail** (worktree, pre-land; master HEAD byte-identical via squash). Live-farm + IconCache trio env/flake-dependent (passed this run; tracked by `live-farm-gate-fix`/`newcolony-test-polish`).
- `--filter "kind=reach"` → **0 matched** — reach set empty. All 8 `new-colony-1` targets are green plain `[Fact]`.

## Done gate

- `dotnet test --filter "kind!=reach"` green AND NC1 reach set empty (3 flipped to green plain Facts).
- Live on NC1 save: Chef + Welfare advice cards correct (Welfare incl. recreation) ; Willie solves every emitted request incl. Heater ; every allowlisted Apply (unforbid/harvest/grow-zone/stockpile/hunt/cook-bill) succeeds end-to-end with truthful readback.
- Mayor Day-1 agenda matches the minimal spec (posture stable, ≤3 short-term, no trade).
- Dashboard: HOME default, important views curated, rule-card landed, no horizontal scroll, all 4 debug surfaces behind SYSTEM/INFO toggle.
- Cabinet-integration test green ; architecture close-gate (`/simplify` + `/code-review` over all major subsystems) clean.
