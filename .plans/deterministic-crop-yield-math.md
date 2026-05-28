# Deterministic Food Crop-Yield Math Plan

## Goal

Replace Food's hardcoded "emergency rice" crop advice with deterministic crop
candidates that Food rules can use directly and Food LLM escalation can inspect.

## Implementation Status

Slice A is implemented: rules use `FoodCropMath`, crop candidates account for
season and compact terrain-fertility context, and the Food briefing carries a
small growing-terrain summary. Prompt payload exposure remains a follow-up.

The first slice should answer:

- Which of rice, potatoes, or corn is the best next food crop for the current
  buffer and season?
- How many tiles should be requested to reach a practical near-term buffer?
- Why was that crop chosen, in one concise player-facing reason?

## Current State

- `Tasks.md` tracks this as a post-M3 Food follow-up.
- `Rules.cs` currently emits `expand_growing_capacity` when food is below 20
  days and `CanSowBeforeWinter()` is true.
- `GrowingZoneStep()` always says "emergency rice growing tiles".
- `GrowingTileRequest()` is `colonists * 12`, clamped to 12-72.
- `FoodBriefing` already exposes the inputs this slice needs for a first pass:
  days of food, colonist count, season/days-to-winter, crop breakdown, crop-zone
  summaries, skills, and data coverage.
- RIMAPI `/def/all` exposes item nutrition and terrain fertility/affordances,
  but not plant grow days, yield, fertility sensitivity, or sow tags.
- Local RimWorld Core XML exposes the crop constants for the initial table:
  `Plants_Cultivated_Farm.xml` has `Plant_Rice`, `Plant_Potato`, and
  `Plant_Corn` with grow days, harvest yield, harvested item, and fertility
  constraints.

## Non-Goals

- Do not add RIMAPI writes or Auto behavior.
- Do not solve exact zone placement, hydroponics, or biome growing-season
  calendars in this slice.
- Do not expand Food into textile/drug crops.
- Do not require live RimWorld XML parsing at runtime.

## Target Shape

### 1. Add a Small Crop Math Module

Add a pure Food-domain helper, likely under `Src/Ministers/Food/`:

- `FoodCropMath`
- `FoodCropCandidate`
- `FoodCropProfile`
- `FoodCropRecommendation`

Start with a static profile table for:

| Crop | Def | Harvested item | Grow days | Yield | Notes |
|---|---|---|---:|---:|---|
| Rice | `Plant_Rice` | `RawRice` | 3.0 | 6 | Fastest emergency crop, fertility-sensitive via inherited plant defaults |
| Potato | `Plant_Potato` | `RawPotatoes` | 5.8 | 11 | Lower fertility sensitivity, safer poor-soil fallback |
| Corn | `Plant_Corn` | `RawCorn` | 11.3 | 22 | High yield, bad when winter is close or buffer is urgent |

Use `FoodNutrition.NutritionPerRawFood` to translate harvest yield into
nutrition and days-of-food contribution.

### 2. Scoring Rules

Produce ranked candidates with explicit reason codes:

- `crop_def`
- `label`
- `tiles`
- `projected_raw_food`
- `projected_nutrition`
- `projected_days_added`
- `grow_days`
- `days_to_winter_margin`
- `urgency_fit`
- `soil_fit`
- `score`
- `reason`

Initial scoring:

- If current buffer is below 7 days, strongly prefer crops that can finish soon;
  rice should usually win unless a poor-soil signal exists.
- If days to winter cannot fit the grow time plus a safety margin, mark the crop
  unsuitable instead of recommending it.
- If terrain fertility is unavailable, assume normal soil and say so in the
  candidate reason rather than pretending fertility is known.
- If terrain fertility is available, adjust grow time by crop fertility
  sensitivity before scoring candidates.
- Corn should win only when the buffer and season are safe enough for a long
  crop and the colony needs storage-efficient yield.

Keep the math intentionally explainable rather than physically complete.

### 3. Integrate Rules First

Replace `GrowingZoneStep()` and the related tile `ResourceRequest` text with
the top crop candidate:

- Step instruction names the chosen crop.
- Step icon uses the chosen crop def.
- Quantity uses the candidate tile count.
- Reason uses the candidate reason.
- Existing `expand_growing_capacity` advice id and concern stay stable.

Keep `CanSowBeforeWinter()` conservative, but make it candidate-aware:

- If at least one crop can fit before winter, rules may recommend growing.
- If no crop fits and food pressure remains, fall through to the existing
  escalation path for winter/crop/freezer/labor tradeoff.

### 4. Expose Candidates To LLM Escalation

Do not rely on Gemini to invent crop math. Provide candidates in the prompt
payload as deterministic context.

Preferred narrow path:

- Add `crop_candidates` to `FoodPromptPayload` in `PromptBuilder`, computed from
  the current `FoodBriefing`.
- Keep `FoodBriefing` stable unless rule code also needs the candidates from
  non-prompt consumers.
- Update `food.system.md` to say crop choice must use `crop_candidates` when
  present and must not override unsuitable candidate constraints without naming
  the uncertainty.

If rules and prompt code need the same output, keep the shared helper pure and
call it from both places.

### 5. Dashboard And Replay

No dashboard UI change is required for the first slice because Advice steps and
raw prompt/debug views already expose the chosen crop and prompt payload.

Replay impact:

- Existing rules replay fixtures may need a one-line update if the growing-zone
  step reason/instruction changes.
- Add a specific replay-style or focused unit test for the new crop path so
  future crop-tuning changes are visible.

## Test Plan

Add focused unit coverage before broad integration:

1. `FoodCropMathTests`
   - emergency low buffer ranks rice first on normal soil.
   - poor-soil signal ranks potatoes first once such a signal exists.
   - corn is unsuitable when days-to-winter is too low.
   - corn can rank first when buffer is safe and season is long.
   - tile count produces enough projected nutrition for the requested target
     band without exceeding existing clamps.

2. `FoodRulesTests`
   - `LowBufferDuringGrowingSeason_RequestsFoodGrowingTiles` should assert the
     chosen crop through the candidate, not a hardcoded "rice" assumption.
   - add a winter-near case where growing is not recommended if no crop fits.
   - preserve stable `expand_growing_capacity` id/concern.

3. `FoodPromptTests`
   - prompt payload includes `crop_candidates`.
   - prompt/system instructions require using deterministic candidates.

4. Replay
   - rerun `FoodReplayCorpusTests`; update fixture only if the new advice text
     is intentionally better.

Verification commands:

```powershell
dotnet test Src\Tests\RimBob.Tests.csproj --no-restore
dotnet build Src\RimBob.sln --no-restore
```

If a live Host is running and locks DLLs, stop/restart it using the repo's
normal Host workflow, then smoke-check:

```powershell
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:5000/api/system/health
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:5000/api/briefings/food/latest
```

## Suggested Slices

### Slice A - Pure Math And Rule Wiring

- Status: implemented.
- Added crop profiles and candidate scoring.
- Replaced hardcoded rice advice with the top candidate.
- Added compact terrain fertility into Food briefing and crop scoring.
- Kept prompt payload exposure as a follow-up.

### Slice B - Prompt Exposure

- Add deterministic `crop_candidates` to Food prompt payload.
- Update `food.system.md`.
- Add prompt tests.

### Slice C - Tune From Live Output

- Trigger Food against the current live save.
- Confirm the advice does not overclaim fertility or exact placement.
- If the recommendation is awkward, tune weights using replay before changing
  rule behavior again.

## Open Questions

- Do we want to include strawberries or haygrass later, or keep this first pass
  strictly rice/potato/corn?
- Should crop candidates become part of `FoodBriefing`, or remain prompt/rules
  derived context?
- What source should eventually provide soil/fertility summary: zone terrain,
  map terrain, or a future RIMAPI storage/zone endpoint?
- What target buffer should crop tiles aim for: 7 emergency days, 20 safety days,
  or a season-aware target?
