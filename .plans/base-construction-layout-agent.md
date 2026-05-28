# Base / Willie / Layout Agent Plan

## Suggested Name

**Willie**

The official minister name is **Willie**. Construction remains the domain/work category, not the schema or class-name prefix.

Rationale: the name covers base layout, rooms, power, material flow, and build feasibility without creating a separate Base Layout minister. Current design says layout belongs inside Willie unless it becomes noisy enough to split later.

---

## Current Repo / Design Constraints

- Willie construction-domain work is already planned in:
  - `Docs/design/ministers/construction.md`
  - `Docs/design/ministers.md`
  - `Tasks.md`
- Existing design says:
  - Willie owns built infrastructure, power, rooms, material/component flow, and layout efficiency.
  - No separate Base Layout minister for the first pass.
  - Willie should ship before Defense because Food already emits freezer / cooler / power / storage requests that Willie should own.
- Implementation pattern to mirror:
  - `Src/Ministers/Food/Rules.cs`
  - `Src/Ministers/Food/MinisterOfFood.cs`
  - `FoodBriefing`
  - `FoodBriefingDerivation`
  - `BriefingCache`
  - `MinisterRegistry`
  - Dashboard minister tabs already expect planned `construction`.

---

## Proposed Implementation Plan

### Phase 0 - Keep Scope Tight

Implement **Willie** as one feeder minister for the construction domain.

Do **not** implement:

- autonomous building placement
- RIMAPI writes
- pawn assignment
- broad HTN planning
- exact room blueprint generation
- killbox/defense-specific fortification strategy beyond build feasibility
- zone writes / stockpile writes via Assisted Apply

The first version should emit **Suggest-mode advice only**.

---

### Phase 1 - Durable Design / Doc Updates

Files to update during implementation:

- `Docs/design/ministers/construction.md`
- possibly `Docs/DESIGN.md`
- possibly `Docs/design/dashboard.md`

Doc changes should capture:

- Minister/schema name: **Willie**.
- First implementation slice focuses on:
  1. power deficit / fragile power margin
  2. missing freezer/cooler support requested by Food
  3. missing beds / basic shelter
  4. material/component bottlenecks
  5. wood/fire-risk warnings for critical rooms
  6. layout hygiene signals: storage/workshop/freezer distance, if data supports it
- Explicit limitation: no exact blueprint placement in MVP; advice can say "place a cooler-backed freezer near food storage," not compute perfect coordinates.
- Dashboard expectation: Willie should become a live minister scope with Prompt, Briefing, RAG, Rules, Raw LLM Output, and Advice tabs.

---

### Phase 2 - Core Contracts

Add Willie-specific contracts under shared core.

Likely files:

- `Src/Common/Advice/WillieAdvice.cs`
- `Src/Common/Briefings/WillieBriefing.cs`

#### Proposed `WillieAdvice` Concern Values

Keep them stable and execution-facing:

- `PowerStability`
- `ThermalControl`
- `BasicShelter`
- `FunctionalRooms`
- `MaterialBottleneck`
- `FireRisk`
- `StalledBuilds`
- `BaseLayout`

Wire serialization like Food does, using snake-case output.

#### Proposed `WillieBriefing` Shape

Design-level groups:

- identity/version:
  - briefing version
  - date
  - game tick
  - map id
- colony context:
  - colonist count
  - current Mayor posture/context
- power:
  - production W
  - consumption W
  - net W
  - battery count / stored power if available
  - known power buildings
  - power data coverage
- rooms/shelter:
  - beds count
  - colonist bed deficit
  - enclosed-room/basic-shelter signal if available
  - critical missing assets
- food/build dependencies:
  - active Food flags requesting freezer/cooler/storage/building work
  - current cooler count
  - food storage/freezer signals already available from Food-derived state where safe
- materials:
  - wood / steel / components / stone blocks / cloth if available
  - bottleneck summary
- buildings/build queue:
  - current major building counts
  - blocked construction signals if available
- layout:
  - compact proximity summaries only: freezer-to-kitchen, storage-to-workshop, material stockpile availability
  - no raw coordinate dumps unless a rule needs it
- risks:
  - wood-heavy critical infrastructure
  - active threat/fire/emergency context
- data coverage:
  - missing and unimplemented signals

---

### Phase 3 - State-Store Derivation

Add Willie briefing derivation using existing aggregate patterns.

Likely files:

- `Src/StateStore/Derivations/WillieBriefingDerivation.cs`
- update `Src/StateStore/BriefingCache.cs`
- update `Src/StateStore/ColonyState.cs` if Willie needs its own aggregate dependency version list

Reuse existing derivation helpers where possible:

- `PawnDeriver`
- `SeasonDeriver`
- `ThreatDeriver`
- `BuildingClassifier`
- `MapDistance`

Important: keep the briefing compact. For layout, summarize distances and deficits; do not dump every building/tile.

---

### Phase 4 - Rules-First Willie Minister

Add minister directory and start with `Rules.cs`, matching repo convention.

Likely files:

- `Src/Ministers/Willie/Rules.cs`
- `Src/Ministers/Willie/MinisterOfWillie.cs`
- later: `Src/Ministers/Willie/scope.md`

#### Initial Deterministic Rules

Priority order should be:

1. **Power deficit**
   - If net power is negative, emit `power_stability`.
   - Steps:
     - `place_blueprint`: build additional generation or reduce load
     - maybe `place_blueprint`: battery/conduit if needed
   - Flag if Food/freezer is affected.

2. **Food freezer build request**
   - If active Food flag requests cooler/freezer/building and Willie has enough rough materials, emit `thermal_control`.
   - Steps:
     - `place_blueprint`: cooler-backed freezer/cold room near food storage
   - Request:
     - Construct labor
     - components/steel if bottlenecked

3. **Missing beds / shelter**
   - If colonists > beds, emit `basic_shelter`.
   - Steps:
     - `place_blueprint`: simple beds/sleeping spots/basic barracks
   - Keep it simple, no exact room planner.

4. **Material bottleneck**
   - If steel/components/wood are too low for visible construction needs, emit `material_bottleneck`.
   - Steps:
     - mine/chop/stonecut recommendation as advice only
   - Resource requests should use canonical work types where possible:
     - `Construct`
     - `Mine`
     - `Haul`
     - maybe `Craft` for stonecutting if appropriate

5. **Fire risk**
   - If critical structures are wood-heavy and stone/materials are available, emit `fire_risk`.
   - Good first target: kitchen/freezer/power room if building classification supports it.

6. **No urgent issue**
   - Return empty `Decision` with a stable trace like `maintain_build_program`.

7. **Escalate**
   - If multiple build needs compete, or layout tradeoff is ambiguous, escalate to LLM.

---

### Phase 5 - LLM Prompt And Parsing

To be a full agent like Food, add Willie LLM path.

Likely files:

- `Src/LlmGateway/prompts/construction.system.md`
- update `Src/LlmGateway/PromptBuilder.cs`
- update `Src/LlmGateway/LlmClient.cs`
- add parser/normalizer if Food parser is too Food-specific:
- maybe `WillieLlmResponseParser.cs`
  - or extract generic minister advice parsing first if low-risk

Prompt should tell **Willie**:

- You own build feasibility, room/base layout, power, materials, and non-defense infrastructure.
- Other ministers own why they need a thing; you own how feasible the build is.
- Emit sparse, concrete `AdviceItem`s.
- Use `actions[]` only; no old suggested action schema.
- Do not allocate pawns.
- Do not invent exact coordinates.
- If placement is unknown, describe constraints: "near kitchen," "adjacent to food stockpile," "outside traffic choke," etc.
- Respect data coverage gaps.

---

### Phase 6 - RAG Retrieval

Willie should eventually get guide context, but this can be a second slice if needed.

Likely files:

- `Src/KnowledgeBase/WillieRagQueryBuilder.cs`
- `Src/KnowledgeBase/WillieRagRetriever.cs`

Useful query topics:

- freezer design
- power redundancy
- early base build order
- room size / bedrooms / barracks
- fire-safe materials
- workshop/storage layout

The existing guide corpus already has relevant beginner and strategic-plan content.

---

### Phase 7 - Runtime Registration

Wire Willie into the cabinet.

Likely files:

- `Src/Coordination/MinisterRegistry.cs`
- `Src/ApiHost/Program.cs`
- any DI/endpoint code that currently branches for Food only

Changes:

- Mark Willie `Ready = true`.
- `CabinetOrder` should probably be:
  - Food first
  - Willie second
  - Mayor after feeder ministers
- Enable:
  - manual trigger
  - prompt
  - raw LLM output
  - RAG if Phase 6 lands
  - manual LLM ingestion only if matching endpoint is wired

Potential order:

1. Food emits freezer/power/storage flags.
2. Willie reads active flags and emits build feasibility advice.
3. Mayor reads active flags/advice context.

---

### Phase 8 - Host Endpoints / Dashboard Inspection

Most dashboard structure already supports planned ministers. Need to ensure backend endpoints return Willie data.

Likely files:

- `Src/ApiHost/Endpoints/MinisterEndpoints.cs`
- `Dashboard/src/dashboard/scopes.ts`
- `Dashboard/src/components/minister/MinisterBriefingView.tsx`
- maybe `Dashboard/src/api/ministers.ts`

Dashboard should show Willie as live and support:

- System Prompt
- Briefing
- RAG
- Rules
- Raw LLM Output
- Advice

Add a Willie-specific briefing grouping in `MinisterBriefingView.tsx`:

- Power
- Rooms / shelter
- Materials
- Build requests
- Layout
- Data coverage

Keep raw/debug fields under the original backend contract names.

---

### Phase 9 - Replay And Observability

Willie should follow the newer rule:

> Every minister LLM attempt must be replayable.

Likely code path:

- use `MinisterReplayRecorder`
- write replay records under the Host-resolved logs root at
  `replay/willie-YYYYMMDD.jsonl`
- include:
  - briefing
  - play cycle context
  - rule trace
  - escalation reason/context
  - guide citations
  - advice/flags
  - raw/normalized LLM output metadata
  - error summaries

This should also show up in `/api/system/health` metadata if existing replay/log discovery is generic. If not, extend it.

---

### Phase 10 - Tests

Add tests before/with implementation.

Likely folders:

- `Src/Tests/Willie/`
- `Src/Tests/Willie/Fixtures/`

Test groups:

1. **Rules tests**
   - negative power emits `power_stability`
   - Food freezer request emits `thermal_control`
   - missing beds emits `basic_shelter`
   - material deficit emits `material_bottleneck`
   - stable base emits no advice
   - ambiguous competing needs escalates

2. **Briefing derivation tests**
   - computes power margin
   - computes bed deficit
   - summarizes material counts
   - includes Food build requests from flags if implemented
   - reports missing data coverage honestly

3. **Minister tests**
   - rules path publishes advice snapshot
   - bootstrap tries LLM first, then rules fallback on failure
   - flags are published
   - replay records are written

4. **Prompt tests**
   - prompt includes Willie briefing
   - prompt includes Mayor cabinet direction
   - prompt forbids coordinates/blueprint precision when unsupported
   - prompt requires current schema

5. **Registry/API tests**
   - Willie is ready/live
   - manual trigger works
   - prompt/briefing endpoints return Willie data
   - planned-scope behavior changes correctly from 501/not-wired to live response

6. **Dashboard build**
   - TypeScript compiles after Willie becomes live.

Verification commands later:

- `dotnet test Src/Tests/RimBob.Tests.csproj`
- `dotnet build Src/RimBob.sln`
- `npm.cmd run build` from `Dashboard`
- preferably `./run-rimbob.ps1` after build, then smoke-check Host health.

---

## Recommended Implementation Slicing

### Slice A - Rules-Only Willie Skeleton

- contracts
- briefing
- derivation
- `Rules.cs`
- tests
- dashboard briefing visibility
- registry still maybe not cabinet-live until tests pass

This gives deterministic value without LLM complexity.

### Slice B - Live Minister Wiring

- `MinisterOfWillie`
- DI
- `MinisterRegistry` ready/live
- cabinet order
- manual trigger
- active advice snapshot
- replay records for rules path

### Slice C - LLM/RAG Escalation

- prompt
- Gemini call
- parser
- RAG retriever
- bootstrap escalation
- raw output/manual fallback if desired

If fastest visible agent behavior is desired, combine A+B and defer C. If parity with Food from day one is desired, do A+B+C together.

---

## Recommendation

Build **Willie** in two steps:

1. First implementation: rules-first, live dashboard, no LLM yet except maybe planned prompt docs.
2. Second implementation: LLM/RAG escalation once the deterministic briefing and first rule set prove useful.

This avoids spending LLM work on weak/immature briefing fields and keeps Willie grounded.
