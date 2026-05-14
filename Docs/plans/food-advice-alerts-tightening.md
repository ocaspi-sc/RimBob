# Food Advice and Alerts Tightening Plan

## Goal

Make Food advice behave like useful in-game coaching instead of a noisy strategy dump. Food should produce a small number of concrete, currently possible or short-term actions; use `priority` consistently; request resources with enough structure for the player, Mayor, and future Labor/Construction ministers to understand; and keep briefings compact by summarizing spatial data.

## Design Decisions Captured

- All ministers should prefer near-term, actionable, currently possible advice. The Mayor can reason strategically, but should still prioritize concrete next moves.
- `AdviceItem.priority` is the single urgency field for advice: `low | medium | high | critical`.
- `AgentFlag.severity` remains a semantic signal for Mayor/CoS routing and tactical alert behavior.
- Rules may calculate advice `priority` dynamically from live state.
- Labor/resource requests must use canonical RimWorld work-tab types, not vague phrases like "labor capacity."
- Work types and skills are distinct. Example: `Cook` is a work type; `Cooking` is the related skill.
- Tile/zone requests should specify amount plus constraints/proximity; Construction/Base Layout owns exact placement.
- Briefings should use compact spatial aggregations instead of raw coordinate dumps.
- Food should only flag trade/procurement pressure in M3; Economy/Trade or the Mayor owns trade/caravan framing.
- The dashboard is a debugging surface and should render what it receives. Filtering vague/bad output belongs in producer-side rules, prompts, and normalization.

## Phase 1 - Advice Schema

1. Add `priority` to `AdviceItem`.
   - Wire name: `priority`.
   - Type: enum.
   - Valid values: `low | medium | high | critical`.
   - Tolerant parsing may repair older `severity` / `priority_score` LLM values.

2. Add canonical work type support.
   - Add a common `WorkType` enum that mirrors RimWorld Work tab concepts.
   - MVP enum draft: `Firefight`, `Patient`, `Doctor`, `BedRest`, `Basic`, `Warden`, `Handle`, `Cook`, `Hunt`, `Construct`, `Grow`, `Mine`, `PlantCut`, `Smith`, `Tailor`, `Art`, `Craft`, `Haul`, `Clean`, `Research`.
   - Keep room to extend from live defs/modded work types later.

3. Extend `ResourceRequest`.
   - Add optional `work_type`.
   - Add optional `skill`.
   - Keep `requested_from`.
   - Do not add pawn assignment fields for MVP.

4. Update shared LLM parsing/normalization.
   - Require or synthesize `priority`.
   - Normalize common work type strings.
   - Reject or downgrade vague labor requests that do not specify a work type when `kind == labor`.

## Phase 2 - Food Briefing Data, Aggregated

Status: implemented for source-supported fields. RIMAPI DTOs expose plant/animal/building positions and zone cells, so Phase 2 preserves those in aggregates and derives compact Food briefing summaries. Work-priority state and trade/caravan availability remain explicit coverage gaps.

1. Investigate live RIMAPI DTOs for spatial fields.
   - Plants: determine whether wild edible plants and crop plants expose map coordinates.
   - Buildings/zones: determine whether stockpiles, kitchen benches, coolers, and freezers expose position or room data.
   - Pawns/work: determine whether current work priorities or disabled work types are available.
   - Trade: determine whether current trader/caravan availability is exposed.

2. Add compact Food briefing summaries only where source data supports them.
   - `WildHarvestClusters`: count, edible defs, readiness, rough direction/distance from base/kitchen/freezer if derivable.
   - `CropZoneSummaries`: crop def, count, readiness, zone id/label, rough proximity bucket if derivable.
   - `FoodStorageSummary`: food stockpile cells, freezer/cold-room signal, rough kitchen-to-storage distance if derivable.
   - `KitchenSummary`: has campfire/stove, has butcher table, known cook bill state if exposed.
   - `WorkReadinessSummary`: best skill plus whether relevant work type appears available/enabled if exposed.

3. Avoid raw dumps.
   - Do not put every plant, animal, tile, building, or coordinate in `FoodBriefing`.
   - If exact positions are unavailable, advice should state the supported aggregate instead of inventing precision.

## Phase 3 - Food Rules

Status: implemented. Food rules now compute advice priority dynamically, use stable same-issue advice ids, suppress day-one trade/caravan advice, emit concrete bill/harvest/growing/freezer suggestions, and only request labor when urgent or coverage is missing.

1. Add dynamic priority helpers.
   - Inputs: days of food, nutrition confidence, colonist count, season/growing window, active threat, immediate action availability, missing infrastructure.
   - Reserve `Critical` for immediate starvation/hunger risk evidence.
   - Use `priority` to rank advice urgency.

2. Tighten output volume.
   - Normal Food cycle should emit the most important few advice items.
   - Do not make the cap strict for Critical/complex states.
   - Prefer updating/superseding same-issue advice over emitting duplicates.

3. Fix day-one/bootstrap behavior.
   - Bootstrap escalation should still happen, but it should produce a concise actionable memo.
   - Suppress `TradeForFood` advice on day one unless trade/caravan context is actually present or starvation is immediate.
   - Food may flag procurement pressure upward instead.

4. Fix concrete Food advice cases.
   - Low meals with raw food: suggest simple meal bill target and cooking work type only if urgent or cook coverage is missing.
   - Wild harvest: suggest marking nearest edible cluster(s) for harvest when cluster data exists; otherwise say only that edible wild harvest candidates exist.
   - Stockpile/freezer: request tile count/proximity or building need; do not represent basic zone marking as generic labor.
   - Growing expansion: request N growing tiles with constraints/proximity, not exact layout.
   - Routine hauling/cleaning: assume RimWorld automation unless there is urgent evidence it blocks food access or spoilage prevention.

## Phase 4 - Food Prompt and RAG Contract

Status: implemented. The Food system prompt now requires sparse near-term output, `priority`, concrete actions, work-type-qualified labor requests, live-state-first RAG usage, and trade/procurement as flag-only unless live trade context exists.

1. Update `food.system.md`.
   - Require near-term actionable output.
   - Require `priority`.
   - Require concrete `suggested_actions`.
   - Require work type on labor requests.
   - Ban vague labels such as "attention" and "labor capacity" unless a specific subsystem need is named.
   - Treat trade as flag-only in M3 unless live context proves an actual trade opportunity.

2. Keep RAG subordinate to live state.
   - Guides may shape crop choice, bill targets, freezer policy, and seasonal timing.
   - Guides must not override live impossibility.

3. Keep advice sparse.
   - Ask for top items, ranked by `priority`.
   - Allow extra items only for Critical or multi-bottleneck states.

## Phase 5 - Dashboard Rendering

Status: implemented. Alerts render advice `priority`, sort active advice by that field, and show all resource request metadata without filtering producer output.

1. Render `priority`.
   - Show the enum value directly.
   - Sort active advice by priority, but keep raw payload visible.

2. Render resource request details without filtering.
   - Show `kind`, `request`, `reason`, `quantity`, `priority`, `requested_from`, `work_type`, and `skill` when present.
   - Do not hide vague requests; visible bad output is useful for debugging.

3. Improve labels only.
   - Make resource/action blocks readable.
   - Do not rewrite producer output in the frontend.

## Phase 6 - Tests

Status: implemented for this slice. Backend tests cover priority/work metadata schema, Food rule behavior, compact briefing summaries, prompt contract language, shared LLM response normalization, and existing SSE/advice paths. Dashboard coverage is build-time TypeScript verification for this slice.

1. Schema tests.
   - `AdviceItem` serializes/deserializes `priority`.
   - `ResourceRequest` serializes/deserializes `work_type` and `skill`.
   - Invalid LLM priority scores are handled deterministically.

2. Rules tests.
   - Stable food produces no advice.
   - Day-one bootstrap does not suggest caravans/trade without trade context.
   - Urgent shortage gets High/Critical only when evidence warrants it.
   - Priority changes as days of food, season, and action availability change.
   - Labor requests name work type and skill when applicable.
   - Tile requests use quantity plus proximity/constraint text.
   - Wild harvest advice uses cluster/proximity data when available and avoids fake specificity when unavailable.

3. Prompt/LLM tests.
   - Food prompt includes closed advice types, `priority`, work type requirements, and sparse-output instruction.
   - Normalizer rejects or flags vague labor requests.
   - Gemini simplified output still normalizes through shared parsing.

4. Integration/dashboard tests.
   - SSE advice includes `priority`.
   - Alerts tab renders priority and resource request details.
   - Mayor prompt/flag digest still sees flag severity and does not depend on dashboard-only priority rendering.

## Phase 7 - Verification

Status: verified on 2026-05-13. `dotnet build Src\RimAI.sln --no-restore`, `dotnet test Src\Tests\RimAI.Tests.csproj --no-restore --no-build`, and `npm.cmd run build` passed. RimAI was restarted with `run-rimai.ps1` and `/api/health` returned OK. Live Food output against a real save could not be confirmed in this pass because RIMAPI at `localhost:8765` was not reachable; `/api/briefings/food/latest` returned the empty fallback briefing.

1. Stop any running `RimAI.Host` before build if DLLs are locked.
2. Run backend build.
3. Run backend tests.
4. Run dashboard build.
5. Run RimAI with the repo launcher or from `Src/ApiHost`.
6. Inspect `./logs/` and the dashboard Alerts tab.
7. Confirm Food emits fewer, more actionable cards.
8. Confirm live Food output does not invent trade, caravan, exact coordinates, or vague labor capacity.

## Open Implementation Questions

- Should Mayor Agenda priorities need their own urgency field, or is ordered free text enough?
- Should labor request `skill` be free text for MVP or a second canonical enum?
- Should Food advice be role-based only in MVP, or may it name colonists when the briefing clearly identifies a best candidate?
- Which RIMAPI endpoint is the source of truth for map positions and room/zone proximity?
- How should advice supersession/dedup be keyed: by `(minister, advice_type)`, a normalized issue id, or explicit LLM/rule-provided `issue_key`?
