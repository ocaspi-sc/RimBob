# RimBob - Human Todo

> Single task inbox and execution board for RimBob.
> Big milestone sequencing belongs in `Docs/ROADMAP.md`.
> `/todo` entries go under "Captured by /todo"; promote serious items into the execution board when they become active work.
> Lifecycle: completed `/todo` captures move to "Done"; promoted items leave the inbox for the board; stale or duplicate captures are deleted.

---

## Captured by /todo

<!-- entries go here -->
- [ ] [2026-05-19] #git #ops #multiagent Fix cross-agent staged-commit contamination on master. [plan](.plans/multi-agent-git-race.md)
- [ ] [2026-05-19] #design #briefing #advice **`set_priority` advice — briefing read (near-term, Suggest-only).** Read current work priorities into Food's briefing so `set_priority` advice is accurate against live state. Action vocabulary already exists; gap is the input. Gated on a RIMAPI Pawn Edit Controller GET. [plan](.plans/set-priority-advice-briefing.md)
- [ ] [2026-05-19] #design #auto #labor **Auto knob-write shim (deferred, M7+).** At Auto graduation: validate → one declarative policy write → readback; Labor recommends the knob, RimWorld's job-giver allocates. Depends on the briefing-read plan landing first; design at re-engagement. [plan](.plans/auto-knob-write-shim.md)
- [ ] [2026-05-19] #rimmind #idea #defs **RimMind: Structured Def Knowledge Index.** Investigate a persistent RimWorld Def index for minister grounding; refs [index-defs.ts](<C:/dev/RimSage/src/scripts/index-defs.ts>), [db.ts](<C:/dev/RimSage/src/utils/db.ts>).
- [ ] [2026-05-19] #rimmind #idea #defs **RimMind: Def Inheritance Resolver.** Investigate raw/merged XML Def handling for active/modded game truth; refs [def-resolver.ts](<C:/dev/RimSage/src/utils/def-resolver.ts>), [get-def-details.ts](<C:/dev/RimSage/src/tools/get-def-details.ts>).
- [ ] [2026-05-19] #rimmind #idea #lookup **RimMind: Label-To-Def Resolution.** Investigate label and `defName` lookup for odd live labels, icons, and advice grounding; ref [search-defs.ts](<C:/dev/RimSage/src/tools/search-defs.ts>).
- [ ] [2026-05-19] #rimmind #idea #refine **RimMind: Source Symbol Lookup.** Investigate dev-only C# symbol browsing for `minister-refine` and RimWorld/RIMAPI code archaeology; refs [index-csharp.ts](<C:/dev/RimSage/src/scripts/index-csharp.ts>), [read-csharp-symbol.ts](<C:/dev/RimSage/src/tools/read-csharp-symbol.ts>).
- [ ] [2026-05-19] #rimmind #idea #tools **RimMind: Bounded Knowledge Tools.** Investigate sandboxed, output-limited read/search tools for dashboard SYSTEM/INFO and dev-agent workflows; refs [server.ts](<C:/dev/RimSage/src/server.ts>), [search-source.ts](<C:/dev/RimSage/src/tools/search-source.ts>), [path-sandbox.ts](<C:/dev/RimSage/src/utils/path-sandbox.ts>).
- [ ] [2026-05-19] #rimmind #idea #mcp **RimMind: Persistent Knowledge Service.** Investigate whether a RimSage-style MCP/HTTP service should remain dev-only or become bounded Host knowledge endpoints; refs [http.ts](<C:/dev/RimSage/src/http.ts>), [stdio.ts](<C:/dev/RimSage/src/stdio.ts>).
- [ ] [2026-05-19] #research #rimmind #assisted Investigate exact-ID targeting/read-back for Assisted Apply. [RimMind](C:/dev/RimMind) [DesignationTools.cs](C:/dev/RimMind/Source/RimMind/Tools/DesignationTools.cs)
- [ ] [2026-05-19] #research #rimmind #spatial Investigate spatial evidence tools for location-backed advice. [RimMind](C:/dev/RimMind) [MapTools.cs](C:/dev/RimMind/Source/RimMind/Tools/MapTools.cs)
- [ ] [2026-05-19] #research #rimmind #construction Investigate player-approved construction proposal patterns. [RimMind](C:/dev/RimMind) [ProposalTracker.cs](C:/dev/RimMind/Source/RimMind/Core/ProposalTracker.cs)
- [ ] [2026-05-19] #research #rimmind #events Investigate event-driven wakeups and cooldowns for ministers. [RimMind](C:/dev/RimMind) [Automation](C:/dev/RimMind/Source/RimMind/Automation/README.md)
- [ ] [2026-05-19] #research #rimmind #history Investigate trend history as minister briefing fuel. [RimMind](C:/dev/RimMind) [MoodHistoryTracker.cs](C:/dev/RimMind/Source/RimMind/Core/MoodHistoryTracker.cs)
- [ ] [2026-05-19] #research #rimmind #defense Investigate Defense minister algorithms from RimMind. [RimMind](C:/dev/RimMind) [CombatTools.cs](C:/dev/RimMind/Source/RimMind/Tools/CombatTools.cs)
- [ ] [2026-05-19] #research #rimmind #construction Investigate Construction minister algorithms from RimMind. [RimMind](C:/dev/RimMind) [SemanticTools.cs](C:/dev/RimMind/Source/RimMind/Tools/SemanticTools.cs)
- [ ] [2026-05-19] #research #rimmind #economy Investigate Economy minister algorithms from RimMind. [RimMind](C:/dev/RimMind) [TradeTools.cs](C:/dev/RimMind/Source/RimMind/Tools/TradeTools.cs)
- [ ] [2026-05-19] #rimmind #research #state RimMind: Domain Snapshot Parts - compare RimAI world-data parts against RimBob state-store and briefing boundaries. [IWorldDataService](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/IWorldDataService.cs) [WorldDataService](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/WorldDataService.cs)
- [ ] [2026-05-19] #rimmind #research #food #hunt RimMind: Food Wildlife Risk Scoring - inspect species-group value/risk scoring for Food hunt advice and Defense veto inputs. [WildlifeOpportunitiesPart](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/WildlifeOpportunitiesPart.cs)
- [ ] [2026-05-19] #rimmind #research #storage #construction RimMind: Storage Saturation - investigate stockpile/storage utilization as a Food, Construction, and Mayor briefing signal. [StorageSaturationPart](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/StorageSaturationPart.cs)
- [ ] [2026-05-19] #rimmind #research #construction #resources RimMind: Construction Backlog - inspect blueprint/frame/material-gap grouping for the first Construction minister briefing. [ConstructionBacklogPart](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/ConstructionBacklogPart.cs)
- [ ] [2026-05-19] #rimmind #research #rimapi RimMind: Research Dependency Snapshot - inspect available/locked research, prerequisites, benches, techprints, and ETA fields for a Research minister slice. [ResearchPart](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/ResearchPart.cs)
- [ ] [2026-05-19] #rimmind #research #medical #welfare RimMind: Medical Risk Digest - inspect injury, infection, bleeding, operation, capacity, and risk-score summaries for future Medical/Welfare work. [MedicalOverviewPart](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/MedicalOverviewPart.cs)
- [ ] [2026-05-19] #rimmind #research #welfare #mood RimMind: Mood Risk Causes - inspect mood thresholds and top negative thought grouping for a compact Welfare briefing. [MoodRiskPart](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/MoodRiskPart.cs)
- [ ] [2026-05-19] #rimmind #research #defense #threats RimMind: Defense Posture Coverage - inspect perimeter, turret/trap coverage, and raid-readiness scoring for later Defense scope. [SecurityPosturePart](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/SecurityPosturePart.cs) [RaidReadinessPart](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/World/Parts/RaidReadinessPart.cs)
- [ ] [2026-05-19] #rimmind #research #tooling #llm RimMind: Tool Registry And TopK Index - evaluate whether tool schemas and embedding-ranked tool selection belong in debug/refinement tooling only. [ToolRegistryService](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/Tooling/ToolRegistryService.cs) [IRimAITool](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/Tooling/IRimAITool.cs)
- [ ] [2026-05-19] #rimmind #research #cos #alerts RimMind: Stage Cooldown And Ticket Kernel - inspect cooldowns, leases, coalescing, and idempotency for CoS alert suppression. [StageKernel](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/Stage/Kernel/StageKernel.cs) [StageService](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/Stage/StageService.cs)
- [ ] [2026-05-19] #rimmind #research #prompts #ministers RimMind: Prompt Composer Blocks - inspect scoped prompt block composition if RimBob minister prompt builders become repetitive. [PromptService](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/Prompting/PromptService.cs) [IPromptComposer](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/Prompting/IPromptComposer.cs)
- [ ] [2026-05-19] #rimmind #research #replay #feedback RimMind: History Recap Windows - inspect idempotent recap windows and stale-summary handling for replay and Pushback refinement. [HistoryService](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/History/HistoryService.cs) [RecapService](C:/dev/Rimworld_AI_Core/RimAI.Core/Source/Modules/History/Recap/RecapService.cs)
- [ ] [2026-05-19] #rimapi #construction Add RIMAPI fork endpoints for blueprint validate/place/read (coordinates supplied by caller; minister wiring deferred). [plan](.plans/rimapi-blueprint-placement-endpoint.md)
- [ ] [2026-05-19] #dashboard #food #ux Advice chain route cards (Grow/Hunt/Forage) above the Food snapshot. [plan](.plans/advice-chain-visualization.md)
- [ ] [2026-05-18] #ops #cleanup Remove legacy repo-local icon/log artifacts after AppData cache/log paths stay verified. [plan](.plans/icons-warm-fixes.md)
- [ ] [2026-05-17] #git #debt Migrate dirty legacy worktrees after their active slices land.
- [ ] [2026-05-16] #skill #debt Fix local skill validator Python dependency.
- [ ] [2026-05-16] #dashboard #markdown Add restricted player-facing Markdown rendering when advice bodies or guide snippets need rich formatting; keep raw/debug views unrendered.
- [ ] [2026-05-15] #idea #llm #rag Provide ministers with more RAG knowledge.
- [ ] [2026-05-09] #spike #llm #test Benchmark optional TOON prompt encoding. [plan](.plans/toon-prompt-encoding-spike.md)

---

## Execution Board

> Moved to done when shipped, not when coded. Keep this list short.
> Big items belong in `Docs/ROADMAP.md`. This is day-to-day work.

### Now (live-state gaps)

- [ ] **Medical hediff severity wiring.** If Mayor or a future Medical minister needs life-threatening condition awareness, preserve the relevant `Hediffs` signal into state/briefing instead of inferring severity from generic `Health`.
- [ ] **`total_nutrition == 0` upstream investigation.** RIMAPI returns 0 nutrition even when `food_total > 0` and meals exist on map. Check whether this is a bug we can patch around (e.g. compute from `meals_count * 0.9 + raw_food_count * 0.05`) or a deeper RIMAPI gap.

### Current implementation order

1. [ ] **Investigate and fix/patch `total_nutrition == 0`.** Decide whether to patch RIMAPI upstream or compute a documented RimBob fallback from stored meal/raw-food counts.
2. [ ] **Add minimal CoS handling.** Implement the Mayor-side helper for dedupe, lead framing, and tactical-alert vs digest routing before multiple feeders exist.
3. [ ] **Add Construction.** Food's first live dependencies are cooler / power / room / storage recommendations, not Defense coupling.
4. [ ] **Add Defense.**
5. [ ] **Resolve Welfare / Medical sequencing.** Decide whether they land together or whether Medical becomes its own follow-on slice (`M4.5` / second wave), then implement Welfare.
6. [ ] **Wire Pushback feedback.** Land M5 only after multiple ministers are emitting advice.

### Next (post-M3 follow-ups)

- [ ] Add polished guide-citation footnotes on feeder memo cards.
- [ ] Extend replay corpus persistence beyond Food and attach future Pushback/outcome fields once feedback is wired.
- [ ] Improve Food hunting target risk/value scoring from live animal data.
- [ ] Expand Food briefing with freezer room temperature and spoilage timers when RIMAPI exposes them.

### Later (M5 - Feedback loop)

- [ ] Minister snapshot feedback route writes a `FeedbackEvent` to the decision log.
- [ ] Wire dashboard Accept / Dismiss / Pushback buttons (Pushback modal opens an editable text field, posts `pushback_text`).
- [ ] `Decision Log` tab renders the last N `FeedbackEvent`s with the originating Agenda bullet.
- [ ] Per-minister persistent pushback list (each minister owns + carries forward player corrections).

### Design TODOs (deferred)

#### Doc alignment

- [ ] Extend the action ownership sketch into a full RIMAPI action/endpoint catalogue, preserving owner/requester/executor labels.
- [ ] Reconcile the Medical/Welfare boundary across docs (`Docs/DESIGN.md` still describes Welfare as owning medical sub-blocks, while `Docs/design/ministers.md` splits Medical into its own subsystem).
- [ ] Expand the roadmap beyond the first cabinet wave: explicitly schedule Industry, Medical, Research, and Economy instead of leaving them only in post-MVP notes.

#### Cabinet rollout planning

- [ ] Add scope docs for Industry, Medical, Research, and Economy once their first slices are scheduled.
- [ ] Per-minister `advice_type` enums - define in each minister's session.
- [ ] Per-minister scope docs (`RimBob.Ministers/<name>/scope.md`) - write after first slice ships.
- [ ] Construction: placement / layout strategy (Base Layout Minister candidate). [plan](.plans/base-construction-layout-agent.md)
- [ ] Define the minimum viable CoS arbitration rule set for M4: dedupe same-issue flags, choose lead framing when multiple ministers point at the same problem, and decide when a flag becomes a tactical alert versus Mayor-digest input.
- [ ] Decide whether Medical should stay coupled to Welfare in the rollout plan or become its own slice after Welfare.

#### Longer-tail design

- [ ] Candidate minister promotion criteria (CMO, Research, Trade, Treasury).
- [ ] Memo cadence calibration: flag-severity-gated tactical alerts vs. strict daily.
- [ ] Dashboard: notification UX (in-page only vs. browser notifications).
- [ ] Retire or narrow legacy `ColonyContext` on the rules path now that minister wake reasons moved to `PlayCycleContext` and feeder ministers use agenda-derived briefing context.

### Auto epic (M7 - defer until then)

- [ ] Auto write shim (one validated declarative knob write + readback) per [`Docs/design/planning.md`](Docs/design/planning.md).
- [ ] Bulletin board (`Coordination/BulletinBoard.cs`) — queues policy-change requests for Labor, not per-pawn tickets — per [`Docs/design/communication.md`](Docs/design/communication.md).
- [ ] Labor policy recommender per [`Docs/design/ministers/labor.md`](Docs/design/ministers/labor.md).
- [ ] RIMAPI policy-knob write-endpoint coverage map (Pawn Edit Controller etc.).
- [ ] Per-(minister, advice_type) autonomy dial wired with real Auto execution.
- [ ] Autonomy dial UI with confirmation step.

### Claude skills to build (see ROADMAP)

- [ ] `fixture-gen` skill (can synthesize from Pushback events).
- [ ] `briefing-check` skill.
- [ ] `rule-promote` skill.

### Known open questions

See [`Docs/DESIGN.md`](Docs/DESIGN.md) decision log and Open Questions sections in sub-docs, especially [`Docs/design/advice.md`](Docs/design/advice.md) and [`Docs/design/dashboard.md`](Docs/design/dashboard.md).

---

## Loose Notes

Refine system prompts for agents - how systematically?

### Fill out guide corpus

Download and create guides.

### Get dashboard inspiration from existing mods

### Simple actions that may be straightforward enough for MVP Assisted Apply

Moved into the Assisted Apply roadmap slice. Keep the implementation narrow: `unforbid` known item stacks, `mark_harvest` validated safe plant clusters, `mark_hunt` deterministic low-risk animal batches, and one single-workbench simple-meal bill upsert; work priorities, broad bill editing, zones, pawn assignment, medical/prisoner actions, and combat controls stay out of the first slice.

### Scan GitHub repos for reference ideas

### Older notes

Is the agenda saved to a file that the dashboard reads from? Is there history?

Migrate from Codex Chrome plugin to Playwright for frontend.

### About the Food minister's Advice / Alerts

- The first alert "Resource requests attention food" - what does attention mean? Why not Trade?
- It is bad day-one advice; do not suggest sending caravans to traders.
- Too many things are generated. Focus on important and currently possible short-term actions; let the Mayor handle grand strategy.
- Alerts priorities use numbers 1-10.
- "Manage Food Stockpile" requested labor; why not tiles to be marked as storage?
- The most basic actions like hauling and cleaning should be automated by the game, and only request labor for urgent cases.
- LABOR requests should specify what kind of work type or skill is required.
- Forage advice should say exactly where the nearest edible plants are and suggest marking them for harvest.

### Dashboard v2

Full redesign of the dashboard. First brainstorm; do not make changes yet. [plan](.plans/dashboard_v2.md)

- Left side: tab for each minister, with emojis.
- Add a SYSTEM tab for info about LLM usage, logs, etc.
- Top of main area: tabs for System Prompt, Briefing, RAG, Rules, Advice.
- Each view is structured into sections for readability and navigation.
- Remove the pushback buttons for now.
- Right sidebar: no need to break up into sections when there are just a few items in each. Add a small panel for each colonist.
- Header is fine.
- Read the design docs to see if something important is missing, e.g. whether we show triggers.

Then research either/or:

- Frontend dashboard skill for AI agents.
- React library for dashboards with good support for collapsible panels and data.
