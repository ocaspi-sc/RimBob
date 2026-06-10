using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;

namespace RimBob.Ministers.Food;

public sealed class Rules : IMinisterRules<FoodBriefing>
{
    private const string MinisterName = "Chef";
    private const string Domain = "food";
    private const string SimpleMealRecipeSelector = "simple_meal";
    private const string BillRepeatModeTargetCount = "TargetCount";
    private static readonly IReadOnlyList<string> SimpleMealRecipeDefs = ["CookMealSimple", "CookMealSimpleBulk"];

    public RuleRun Evaluate(FoodBriefing briefing)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<MinisterRule<FoodBriefing>> rules = RuleTable(now);
        IReadOnlyList<MinisterRuleTraceDescriptor<FoodBriefing>> fallbackRuleDescriptors = FallbackRuleDescriptors();
        RuleRun ruleRun = MinisterRuleTableEvaluator.EvaluateAllHits(
            rules,
            briefing,
            additionalRuleDescriptors: fallbackRuleDescriptors);
        if (ruleRun.Decisions.Count > 0)
            return ruleRun;

        if (briefing.EstimatedDaysOfFood is not float days)
            return new RuleRun([], DiagnosticsForFallback(rules, fallbackRuleDescriptors, briefing, "no_food_signal"));

        if (ShouldEscalateHuntTargetsBlockedByRisk(briefing, days))
            return EscalationRun(
                rules,
                fallbackRuleDescriptors,
                briefing,
                "hunt_targets_blocked_by_risk",
                "Food below 20 days with visible animals, but the hunting path is blocked by the current safety/risk filters.",
                new { briefing.WildAnimalCount, briefing.ActiveThreat, LowRiskTargetCount = briefing.WildHuntTargets.Count, briefing.Skills.BestCooking, briefing.HuntRiskSummaries });

        if (briefing.Season.DaysToWinter is < 20 && days < 30f)
            return EscalationRun(
                rules,
                fallbackRuleDescriptors,
                briefing,
                "winter_food_tradeoff",
                "Winter is close and food buffer is below target; crop/freezer/labor tradeoff needs guide-grounded judgment.",
                new { briefing.Season.DaysToWinter, DaysOfFood = days, briefing.CropBreakdown });

        if (days >= 30f)
            return new RuleRun([], DiagnosticsForFallback(rules, fallbackRuleDescriptors, briefing, "maintain_security_threshold"));

        return EscalationRun(
            rules,
            fallbackRuleDescriptors,
            briefing,
            "unresolved_food_gap",
            "Food state is below ideal but no deterministic rule cleanly chooses the next action.",
            new { DaysOfFood = days, briefing.ReadyToHarvest, briefing.WildHarvestCandidates, briefing.WildAnimalCount });
    }

    private static RuleRun EscalationRun(
        IReadOnlyList<MinisterRule<FoodBriefing>> rules,
        IReadOnlyList<MinisterRuleTraceDescriptor<FoodBriefing>> fallbackRuleDescriptors,
        FoodBriefing briefing,
        RuleId rule,
        string reason,
        object context) =>
        new(
            [new Escalate(rule, reason, context)],
            DiagnosticsForFallback(rules, fallbackRuleDescriptors, briefing, rule));

    private static IReadOnlyList<MinisterRule<FoodBriefing>> RuleTable(DateTimeOffset now) =>
    [
        new("nutrition_signal_gap", MatchesNutritionSignalGap, NutritionSignalGapReason, briefing => BuildNutritionSignalGap(briefing, now)),
        new("unknown_food_state", MatchesUnknownFoodState, UnknownFoodStateReason, briefing => BuildUnknownFoodState(briefing, now)),
        new("food_stockpile_missing", MatchesFoodStockpileMissing, FoodStockpileMissingReason, briefing => BuildFoodStockpileMissing(briefing, now)),
        new("emergency_food_flag", MatchesEmergencyFoodFlag, EmergencyFoodFlagReason, briefing => BuildEmergencyFoodFlag(briefing, now)),
        new("harvest_mature_crops", MatchesHarvestMatureCrops, HarvestMatureCropsReason, briefing => BuildHarvestMatureCrops(briefing, now)),
        new("meals_understocked", MatchesMealsUnderstocked, MealsUnderstockedReason, briefing => BuildMealsUnderstocked(briefing, now)),
        new("wild_harvest_available", MatchesWildHarvestAvailable, WildHarvestAvailableReason, briefing => BuildWildHarvestAvailable(briefing, now)),
        new("hunt_low_risk_animals", MatchesHuntLowRiskAnimals, HuntLowRiskAnimalsReason, briefing => BuildHuntLowRiskAnimals(briefing, now)),
        new("expand_growing_capacity", MatchesExpandGrowingCapacity, ExpandGrowingCapacityReason, briefing => BuildExpandGrowingCapacity(briefing, now)),
        new("freezer_missing", MatchesFreezerMissing, FreezerMissingReason, briefing => BuildFreezerMissing(briefing, now))
    ];

    private static bool MatchesNutritionSignalGap(FoodBriefing briefing) =>
        briefing.EstimatedDaysOfFood is null && briefing.UnclassifiedFoodUnits > 0;

    private static string NutritionSignalGapReason(FoodBriefing briefing) =>
        MatchesNutritionSignalGap(briefing)
            ? $"{briefing.UnclassifiedFoodUnits} unclassified food units exist but days-of-food is unavailable"
            : "days-of-food is available or no unclassified food units need stockpile-category verification";

    private static IReadOnlyList<Decision> BuildNutritionSignalGap(FoodBriefing briefing, DateTimeOffset now)
    {
        FoodFlagRequests nutritionGapRequests = FoodFlagRequests.Building(StockpileVisibilityRequest(
            "reachable food stockpile visibility",
            "food units outside meals/raw-food counts cannot be converted into days-of-food",
            quantity: null,
            priority: Priority.Medium));
        IReadOnlyList<ItemRequest> forbiddenMealRequests = ForbiddenMealItemRequests(briefing, Priority.Medium);
        foreach (ItemRequest forbiddenMealRequest in forbiddenMealRequests)
            nutritionGapRequests = nutritionGapRequests.Add(forbiddenMealRequest);

        return EmitAdvice(briefing, now, "nutrition_signal_gap",
            Priority.Medium,
            "Food stockpile categories need verification",
            FoodRemainderBody(briefing),
            "Missing nutrition would make days-of-food unreliable; use the fallback counts until upstream data is fixed.",
            NutritionSignalGapActions(briefing),
            nutritionGapRequests,
            forbiddenMealRequests.Count > 0);
    }

    private static bool MatchesUnknownFoodState(FoodBriefing briefing) =>
        briefing.EstimatedDaysOfFood is null && briefing.UnclassifiedFoodUnits <= 0;

    private static string UnknownFoodStateReason(FoodBriefing briefing) =>
        MatchesUnknownFoodState(briefing)
            ? "food units and days-of-food are both unavailable"
            : "food visibility has either a days-of-food signal or unclassified units for the stockpile-category rule";

    private static IReadOnlyList<Decision> BuildUnknownFoodState(FoodBriefing briefing, DateTimeOffset now) =>
        EmitAdvice(briefing, now, "unknown_food_state",
            Priority.High,
            "Food state unknown",
            "No reliable food stockpile signal is available. Treat this as a food-security check, not confirmed starvation.",
            "The food chain cannot safely decide without stockpile visibility.",
            [new(AdviceActionKind.SetStockpileZone, "Create or expose a reachable food stockpile, then refresh RimBob once food is visible.")],
            FoodFlagRequests.Building(StockpileVisibilityRequest(
                "visible reachable food stockpile",
                "food_units and nutrition are both unavailable",
                quantity: null,
                priority: Priority.High)),
            true);

    private static bool MatchesFoodStockpileMissing(FoodBriefing briefing) =>
        briefing.EstimatedDaysOfFood is not null &&
        FoodStockpileVisibleFoodUnits(briefing) > 0 &&
        briefing.StockpileCells == 0;

    private static string FoodStockpileMissingReason(FoodBriefing briefing)
    {
        int visibleFoodUnits = FoodStockpileVisibleFoodUnits(briefing);
        if (MatchesFoodStockpileMissing(briefing))
            return briefing.Storage.UnpositionedFoodUnits > 0
                ? $"{briefing.Storage.UnpositionedFoodUnits} food units are unpositioned with 0 reachable stockpile cells"
                : $"{visibleFoodUnits} visible food units exist with 0 reachable stockpile cells";

        if (briefing.EstimatedDaysOfFood is null)
            return "days-of-food is unavailable, so nutrition visibility rules own stockpile setup";

        if (visibleFoodUnits <= 0)
            return "no visible food units need stockpile placement";

        return $"{briefing.StockpileCells} reachable stockpile cell(s) are visible";
    }

    private static IReadOnlyList<Decision> BuildFoodStockpileMissing(FoodBriefing briefing, DateTimeOffset now)
    {
        float days = RequiredFoodDays(briefing, "food_stockpile_missing");
        int visibleFoodUnits = FoodStockpileVisibleFoodUnits(briefing);
        Priority priority = days < 7f ? Priority.High : Priority.Medium;
        return EmitAdvice(briefing, now, "food_stockpile_missing",
            priority,
            "Food needs a reachable stockpile",
            FoodStockpileMissingBody(briefing, days, visibleFoodUnits),
            "Known food still needs reachable storage; Chef owns the stockpile requirement and Willie owns spatial setup.",
            [
                new AdviceAction(
                    AdviceActionKind.SetStockpileZone,
                    "Designate a reachable food stockpile near the visible meals/raw food, then let haulers move the food into storage.",
                    Quantity: visibleFoodUnits,
                    Owner: "Willie")
            ],
            FoodFlagRequests.Building(StockpileVisibilityRequest(
                $"reachable food stockpile for {visibleFoodUnits} visible food units",
                "known or visible food exists but no reachable food stockpile cells are visible",
                quantity: visibleFoodUnits,
                priority: priority)),
            true);
    }

    private static int FoodStockpileVisibleFoodUnits(FoodBriefing briefing)
    {
        int classifiedFoodUnits = briefing.MealsCount + briefing.RawFoodCount;
        if (classifiedFoodUnits > 0)
            return classifiedFoodUnits;

        if (briefing.Storage.UnpositionedFoodUnits > 0)
            return briefing.Storage.UnpositionedFoodUnits;

        int forbiddenMealCount = ForbiddenMealCount(briefing);
        return forbiddenMealCount > 0 ? forbiddenMealCount : briefing.FoodUnits;
    }

    private static string FoodStockpileMissingBody(FoodBriefing briefing, float days, int visibleFoodUnits)
    {
        string placementSignal = briefing.Storage.UnpositionedFoodUnits > 0
            ? $"{briefing.Storage.UnpositionedFoodUnits} food unit(s) are unpositioned"
            : ForbiddenMealCount(briefing) > 0
                ? $"{ForbiddenMealCount(briefing)} forbidden {ForbiddenMealLabel(briefing, ForbiddenMealCount(briefing))} are visible"
                : $"{visibleFoodUnits} food unit(s) are visible";
        return $"Food covers about {days:F1} days and {placementSignal}, but there are 0 reachable food stockpile cells. Designate a small food stockpile near the meals so haulers have a valid storage target.";
    }

    private static bool MatchesEmergencyFoodFlag(FoodBriefing briefing) =>
        briefing.EstimatedDaysOfFood is float days && days < 7f;

    private static string EmergencyFoodFlagReason(FoodBriefing briefing) =>
        briefing.EstimatedDaysOfFood is float days
            ? days < 7f
                ? $"food buffer {days:F1}d is below the 7d emergency threshold"
                : $"food buffer {days:F1}d is at or above the 7d emergency threshold"
            : "days-of-food is unavailable for the emergency threshold";

    private static IReadOnlyList<Decision> BuildEmergencyFoodFlag(FoodBriefing briefing, DateTimeOffset now)
    {
        float days = RequiredFoodDays(briefing, "emergency_food_flag");
        Priority priority = FoodBufferPriority(briefing, days);
        return EmitAdvice(briefing, now, "emergency_food_flag",
            priority,
            "Food crisis within a week",
            EmergencyBody(briefing, days),
            "Food below 7 days is an urgent survival risk. Chef flags the survival pressure; separate Food advice cards carry each concrete intervention.",
            EmergencyActions(briefing, days),
            EmergencyRequests(briefing, days, priority),
            true);
    }

    private static bool MatchesHarvestMatureCrops(FoodBriefing briefing) =>
        briefing.EstimatedDaysOfFood is not null && briefing.ReadyToHarvest > 0;

    private static string HarvestMatureCropsReason(FoodBriefing briefing) =>
        briefing.ReadyToHarvest > 0
            ? $"{briefing.ReadyToHarvest} mature crop tiles are ready"
            : "no mature crop tiles are ready";

    private static IReadOnlyList<Decision> BuildHarvestMatureCrops(FoodBriefing briefing, DateTimeOffset now)
    {
        float days = RequiredFoodDays(briefing, "harvest_mature_crops");
        Priority priority = days < 15f ? Priority.High : Priority.Medium;
        return EmitAdvice(briefing, now, "harvest_mature_crops",
            priority,
            "Mature crops are ready",
            $"{briefing.ReadyToHarvest} crop tiles are ready to harvest. Pull them in before weather, rot, or task drift wastes the buffer.",
            "Mature crops are a deterministic food-chain opportunity.",
            ActionsWithCookingBuildingSupport(briefing, [HarvestAction(briefing, days)]),
            RequestsWithCookingBuildingSupport(briefing, PlantLaborIfNeeded(briefing, days, "PlantCut work for ready crops", "mature crops only help once harvested"), priority),
            days < 15f || NeedsCookingBuildingSupport(briefing));
    }

    private static bool MatchesMealsUnderstocked(FoodBriefing briefing) =>
        briefing.EstimatedDaysOfFood is float days &&
        briefing.MealsCount < briefing.ColonistCount * 2 &&
        briefing.RawFoodCount > 0 &&
        (ShouldSuggestCookBill(briefing) || ShouldRequestCookingLabor(briefing, days));

    private static string MealsUnderstockedReason(FoodBriefing briefing)
    {
        if (briefing.EstimatedDaysOfFood is not float days)
            return "days-of-food is unavailable for the cooking-throughput check";

        if (MatchesMealsUnderstocked(briefing))
            return $"{briefing.MealsCount} meals for {briefing.ColonistCount} colonists while raw food exists";

        if (briefing.MealsCount >= briefing.ColonistCount * 2)
            return $"{briefing.MealsCount} meals already cover at least two meals per colonist";

        if (briefing.RawFoodCount <= 0)
            return "no raw food is available for meal production";

        if (!ShouldSuggestCookBill(briefing) && !ShouldRequestCookingLabor(briefing, days))
            return "cook bill and cooking labor signals are already sufficient";

        return "meal stock is below target but no cooking intervention is selected";
    }

    private static IReadOnlyList<Decision> BuildMealsUnderstocked(FoodBriefing briefing, DateTimeOffset now)
    {
        float days = RequiredFoodDays(briefing, "meals_understocked");
        bool needsCookingLabor = ShouldRequestCookingLabor(briefing, days);
        bool needsCookingBuildingSupport = NeedsCookingBuildingSupport(briefing);
        return EmitAdvice(briefing, now, "meals_understocked",
            Priority.Medium,
            "Cooked meals are understocked",
            $"Only {briefing.MealsCount} meals are reported for {briefing.ColonistCount} colonists while raw food exists.",
            "A raw-food buffer still needs cooking throughput to become safe daily nutrition.",
            CookBillActions(briefing, days),
            RequestsWithCookingBuildingSupport(briefing, CookingLaborIfNeeded(briefing, days, Priority.Medium), Priority.Medium),
            needsCookingLabor || needsCookingBuildingSupport);
    }

    private static bool MatchesWildHarvestAvailable(FoodBriefing briefing) =>
        briefing.EstimatedDaysOfFood is float days && days < 20f && briefing.WildHarvestCandidates > 0;

    private static string WildHarvestAvailableReason(FoodBriefing briefing) =>
        briefing.EstimatedDaysOfFood is float days
            ? days < 20f && briefing.WildHarvestCandidates > 0
                ? $"{briefing.WildHarvestCandidates} forage candidates are visible below the 20d target"
                : $"food buffer {days:F1}d and {briefing.WildHarvestCandidates} forage candidates do not select forage"
            : "days-of-food is unavailable for forage selection";

    private static IReadOnlyList<Decision> BuildWildHarvestAvailable(FoodBriefing briefing, DateTimeOffset now)
    {
        float days = RequiredFoodDays(briefing, "wild_harvest_available");
        Priority priority = days < 10f ? Priority.High : Priority.Medium;
        return EmitAdvice(briefing, now, "wild_harvest_available",
            priority,
            "Forage can extend the buffer",
            WildHarvestBody(briefing, days),
            "Foraging edible plants is a concrete local food-acquisition action.",
            ActionsWithCookingBuildingSupport(briefing, [WildHarvestAction(briefing, days)]),
            RequestsWithCookingBuildingSupport(briefing, PlantLaborIfNeeded(briefing, days, "PlantCut work for forage", "edible forage requires plant work"), priority),
            days < 10f || NeedsCookingBuildingSupport(briefing));
    }

    private static bool MatchesHuntLowRiskAnimals(FoodBriefing briefing) =>
        briefing.EstimatedDaysOfFood is float days && days < 20f && CanSuggestHunting(briefing);

    private static string HuntLowRiskAnimalsReason(FoodBriefing briefing) =>
        briefing.EstimatedDaysOfFood is float days
            ? days < 20f && CanSuggestHunting(briefing)
                ? $"{briefing.WildHuntTargets.Count} low-risk hunt target summaries are visible"
                : $"food buffer {days:F1}d, active threat={briefing.ActiveThreat}, low-risk hunt targets={briefing.WildHuntTargets.Count}"
            : "days-of-food is unavailable for hunting selection";

    private static IReadOnlyList<Decision> BuildHuntLowRiskAnimals(FoodBriefing briefing, DateTimeOffset now)
    {
        float days = RequiredFoodDays(briefing, "hunt_low_risk_animals");
        // Cap at Low when the latent reserve covers near-term starvation; far-hunt infra is premature.
        Priority priority = HasHealthyLatentFoodReserve(briefing) ? Priority.Low : (days < 10f ? Priority.High : Priority.Medium);
        return EmitAdvice(briefing, now, "hunt_low_risk_animals",
            priority,
            "Mark low-risk animals for hunting",
            HuntingBody(briefing, days),
            "The briefing has healthy wild animals that pass the current safety filter.",
            ActionsWithCookingBuildingSupport(briefing, HuntingActions(briefing, HasHealthyLatentFoodReserve(briefing))),
            HuntingRequests(briefing, priority, HasHealthyLatentFoodReserve(briefing)),
            true);
    }

    private static bool MatchesExpandGrowingCapacity(FoodBriefing briefing) =>
        briefing.EstimatedDaysOfFood is float days &&
        days < 20f &&
        FoodCropMath.Recommend(briefing).BestCandidate is FoodCropCandidate cropCandidate &&
        ShouldRecommendNewGrowingZone(briefing, cropCandidate);

    private static string ExpandGrowingCapacityReason(FoodBriefing briefing)
    {
        if (briefing.EstimatedDaysOfFood is not float days)
            return "days-of-food is unavailable for growing-zone selection";

        FoodCropCandidate? cropCandidate = FoodCropMath.Recommend(briefing).BestCandidate;
        if (cropCandidate is null)
            return "FoodCropMath has no viable food crop candidate";

        if (days >= 20f)
            return $"food buffer {days:F1}d is at or above the 20d growing-zone trigger";

        if (!ShouldRecommendNewGrowingZone(briefing, cropCandidate))
        {
            int activeTiles = ActiveMatchingCropTiles(briefing, cropCandidate.CropDef);
            return $"{cropCandidate.Label} candidate already has {activeTiles} active matching crop tiles for the {cropCandidate.Tiles}-tile target";
        }

        return $"{cropCandidate.Label} can add about {cropCandidate.ProjectedDaysAdded:F1}d from {cropCandidate.Tiles} tiles";
    }

    private static IReadOnlyList<Decision> BuildExpandGrowingCapacity(FoodBriefing briefing, DateTimeOffset now)
    {
        float days = RequiredFoodDays(briefing, "expand_growing_capacity");
        FoodCropCandidate cropCandidate = FoodCropMath.Recommend(briefing).BestCandidate
            ?? throw new InvalidOperationException("expand_growing_capacity matched without a crop candidate");
        // Cap at Medium when the latent reserve covers near-term starvation; growing is a long-game action.
        Priority priority = (days < 12f && !HasHealthyLatentFoodReserve(briefing)) ? Priority.High : Priority.Medium;
        return EmitAdvice(briefing, now, "expand_growing_capacity",
            priority,
            "Expand food growing capacity",
            $"Food covers about {days:F1} days and {cropCandidate.Label} still fits the growing window: {cropCandidate.Reason}. Add a compact food crop zone instead of waiting for hunting or trade.",
            "A concrete growing-zone action is more actionable than a vague labor request; cross-minister flags carry the growing-zone dependency separately.",
            ActionsWithCookingBuildingSupport(briefing, [GrowingZoneAction(cropCandidate)]),
            RequestsWithCookingBuildingSupport(
                briefing,
                FoodFlagRequests.Zone(new ZoneRequest(
                    Request: $"{cropCandidate.Tiles} {cropCandidate.Label} growing tiles near fertile soil and food storage",
                    Reason: cropCandidate.Reason,
                    ZoneClass: ZoneClass.Growing,
                    PlantDef: cropCandidate.CropDef,
                    TileCount: cropCandidate.Tiles,
                    Adjacency: [new AdjacencyHint(AdjacencyRelation.Near, "storage")],
                    Terrain: new TerrainNeed(
                        MustSupportGrowing: true,
                        PreferredFertility: cropCandidate.TerrainFertility),
                    Priority: priority,
                    RequestedFrom: "Willie")),
                priority),
            true);
    }

    private static bool MatchesFreezerMissing(FoodBriefing briefing) =>
        briefing.EstimatedDaysOfFood is float days &&
        NeedsFreezerSupport(briefing, days, HasIncomingPerishableFoodPath(briefing));

    private static string FreezerMissingReason(FoodBriefing briefing)
    {
        if (briefing.EstimatedDaysOfFood is not float days)
            return "days-of-food is unavailable for freezer selection";

        bool incomingPerishableFood = HasIncomingPerishableFoodPath(briefing);
        if (NeedsFreezerSupport(briefing, days, incomingPerishableFood))
            return $"{briefing.FoodUnits} food units exist but no cooler is visible";

        if (briefing.Infrastructure.Coolers > 0)
            return $"{briefing.Infrastructure.Coolers} cooler(s) are visible";

        return incomingPerishableFood
            ? "incoming perishable food exists, but freezer support is not needed under current buffer thresholds"
            : "no incoming perishable path or stored-food buffer currently needs cold storage";
    }

    private static IReadOnlyList<Decision> BuildFreezerMissing(FoodBriefing briefing, DateTimeOffset now)
    {
        float days = RequiredFoodDays(briefing, "freezer_missing");
        bool incomingPerishableFood = HasIncomingPerishableFoodPath(briefing);
        return EmitAdvice(briefing, now, "freezer_missing",
            FreezerSupportPriority(briefing),
            "Food storage needs freezer support",
            FreezerAdviceBody(briefing, days, incomingPerishableFood),
            "Chef owns freezer need; Willie owns the actual build work.",
            FreezerSupportActions(briefing, days, incomingPerishableFood),
            FreezerSupportRequests(briefing, days, incomingPerishableFood),
            true);
    }

    // A ≥1-week forbidden-but-edible reserve means near-term food is secured via one unforbid-click,
    // so long-game growing and optional hunting are not emergencies.
    private const float LatentReserveComfortDays = 7f;

    private static bool HasHealthyLatentFoodReserve(FoodBriefing briefing) =>
        briefing.LatentFoodDays is float latent && latent >= LatentReserveComfortDays;

    private static float RequiredFoodDays(FoodBriefing briefing, string rule) =>
        briefing.EstimatedDaysOfFood
        ?? throw new InvalidOperationException($"{rule} matched without days-of-food");

    private static RuleTraceDetails DiagnosticsForFallback(
        IReadOnlyList<MinisterRule<FoodBriefing>> rules,
        IReadOnlyList<MinisterRuleTraceDescriptor<FoodBriefing>> fallbackRuleDescriptors,
        FoodBriefing briefing,
        string selectedRule)
    {
        return MinisterRuleTableEvaluator.BuildTrace(
            rules,
            briefing,
            decisions: [],
            matchedRules: new HashSet<RuleId> { selectedRule },
            additionalRuleDescriptors: fallbackRuleDescriptors,
            selectedRuleOverride: selectedRule);
    }

    private static IReadOnlyList<MinisterRuleTraceDescriptor<FoodBriefing>> FallbackRuleDescriptors() =>
    [
        new("hunt_targets_blocked_by_risk", HuntTargetsBlockedByRiskTraceReason, (_, selectedRule) => EscalatedWhenSelected("hunt_targets_blocked_by_risk", selectedRule)),
        new("winter_food_tradeoff", WinterFoodTradeoffTraceReason, (_, selectedRule) => EscalatedWhenSelected("winter_food_tradeoff", selectedRule)),
        new("maintain_security_threshold", MaintainSecurityThresholdTraceReason, (_, selectedRule) => SelectedWhenSelected("maintain_security_threshold", selectedRule)),
        new("unresolved_food_gap", UnresolvedFoodGapTraceReason, (_, selectedRule) => EscalatedWhenSelected("unresolved_food_gap", selectedRule))
    ];

    private static string HuntTargetsBlockedByRiskTraceReason(FoodBriefing briefing)
    {
        if (briefing.EstimatedDaysOfFood is not float days)
            return "days-of-food is unavailable for hunt-risk escalation";

        if (ShouldEscalateHuntTargetsBlockedByRisk(briefing, days))
            return HuntTargetsBlockedByRiskReason(briefing);

        if (days >= 20f)
            return $"food buffer {days:F1}d is at or above the 20d hunt-risk escalation trigger";

        if (briefing.WildAnimalCount <= 0)
            return "no visible wild animals are blocking the hunt path";

        if (briefing.ReadyToHarvest > 0)
            return $"{briefing.ReadyToHarvest} mature crop tiles are ready before hunt-risk escalation";

        if (CanSuggestHunting(briefing))
            return $"{briefing.WildHuntTargets.Count} low-risk hunt target summaries are visible";

        return "hunt-risk escalation is not selected";
    }

    private static string WinterFoodTradeoffTraceReason(FoodBriefing briefing)
    {
        if (briefing.EstimatedDaysOfFood is not float days)
            return "days-of-food is unavailable for winter food tradeoff escalation";

        if (briefing.Season.DaysToWinter is not int daysToWinter)
            return "winter timing is unavailable for winter food tradeoff escalation";

        if (daysToWinter < 20 && days < 30f)
            return $"{daysToWinter}d to winter with {days:F1}d food";

        if (daysToWinter >= 20)
            return $"{daysToWinter}d to winter is outside the close-winter tradeoff window";

        return $"food buffer {days:F1}d is at or above the 30d winter tradeoff buffer";
    }

    private static string MaintainSecurityThresholdTraceReason(FoodBriefing briefing) =>
        briefing.EstimatedDaysOfFood is float days
            ? days >= 30f
                ? $"food buffer {days:F1}d meets the 30d security threshold"
                : $"food buffer {days:F1}d is below the 30d security threshold"
            : "days-of-food is unavailable for the security threshold";

    private static string UnresolvedFoodGapTraceReason(FoodBriefing briefing)
    {
        if (briefing.EstimatedDaysOfFood is not float days)
            return "days-of-food is unavailable for unresolved food gap escalation";

        if (AnyTableRuleApplies(briefing))
            return "a deterministic Chef rule matched the food state";

        if (ShouldEscalateHuntTargetsBlockedByRisk(briefing, days))
            return "hunt-risk escalation owns the unresolved low-food state";

        if (briefing.Season.DaysToWinter is < 20 && days < 30f)
            return "winter tradeoff escalation owns the unresolved low-food state";

        if (days >= 30f)
            return "food security threshold maintenance owns the terminal state";

        return $"food buffer {days:F1}d is below target, but no deterministic action predicate matched";
    }

    private static bool AnyTableRuleApplies(FoodBriefing briefing) =>
        RuleTable(DateTimeOffset.UnixEpoch).Any(rule => rule.Matches(briefing));

    private static RuleOutcome EscalatedWhenSelected(RuleId rule, RuleId? selectedRule) =>
        selectedRule == rule ? RuleOutcome.Escalated : RuleOutcome.NotMatched;

    private static RuleOutcome SelectedWhenSelected(RuleId rule, RuleId? selectedRule) =>
        selectedRule == rule ? RuleOutcome.Selected : RuleOutcome.NotMatched;

    private static IReadOnlyList<Decision> EmitAdvice(
        FoodBriefing briefing,
        DateTimeOffset now,
        string trace,
        Priority priority,
        string title,
        string body,
        string rationale,
        IReadOnlyList<AdviceAction> actions,
        FoodFlagRequests requests,
        bool emitFlag)
    {
        List<Decision> decisions =
        [
            new Advise(
                trace,
                priority,
                title,
                body,
                rationale,
                actions)
        ];

        if (emitFlag)
            decisions.AddRange(requests.ToDecisions(trace, priority));

        return decisions;
    }

    private static IReadOnlyList<AdviceAction> ActionsWithCookingBuildingSupport(
        FoodBriefing briefing,
        IReadOnlyList<AdviceAction> actions)
    {
        List<AdviceAction> result = [.. actions];
        if (NeedsCookingBuildingSupport(briefing) && !HasCookingBuildingAction(result))
            result.Add(CookingBuildingSupportAction());
        return result;
    }

    private static FoodFlagRequests RequestsWithCookingBuildingSupport(
        FoodBriefing briefing,
        FoodFlagRequests requests,
        Priority priority)
    {
        if (NeedsCookingBuildingSupport(briefing) && !HasKitchenCookingRequest(requests))
            return requests.Add(CookingBuildingRequest(
                "starter kitchen cooking station",
                "the selected food path cannot become meals without a cooking building",
                priority));

        return requests;
    }

    private static AdviceAction CookingBuildingSupportAction() =>
        new(
            AdviceActionKind.PlaceBlueprint,
            "Plan a starter kitchen cooking station so raw or foraged food can become meals.",
            Owner: "Willie");

    private static bool HasCookingBuildingAction(IReadOnlyList<AdviceAction> actions) =>
        actions.Any(action =>
            action.Kind == AdviceActionKind.PlaceBlueprint &&
            string.Equals(action.Owner, "Willie", StringComparison.OrdinalIgnoreCase) &&
            (action.Instruction.Contains("campfire", StringComparison.OrdinalIgnoreCase) ||
             action.Instruction.Contains("stove", StringComparison.OrdinalIgnoreCase) ||
             action.Instruction.Contains("kitchen cooking station", StringComparison.OrdinalIgnoreCase)));

    private static IReadOnlyList<AdviceAction> FreezerSupportActions(
        FoodBriefing briefing,
        float days,
        bool incomingPerishableFood)
    {
        if (!NeedsFreezerSupport(briefing, days, incomingPerishableFood))
            return [];

        string capacity = FreezerCapacityClass(briefing, days);
        string incoming = FreezerIncomingPhrase(briefing, incomingPerishableFood);
        return
        [
            new AdviceAction(
                AdviceActionKind.PlaceBlueprint,
                $"Plan a {capacity} near food storage before {incoming} spoils.",
                Owner: "Willie")
        ];
    }

    private static FoodFlagRequests FreezerSupportRequests(
        FoodBriefing briefing,
        float days,
        bool incomingPerishableFood)
    {
        if (!NeedsFreezerSupport(briefing, days, incomingPerishableFood))
            return FoodFlagRequests.Empty;

        string capacity = FreezerCapacityClass(briefing, days);
        int targetDays = FreezerTargetDays(briefing, days);
        string incoming = FreezerIncomingPhrase(briefing, incomingPerishableFood);
        return FoodFlagRequests.Building(
            new BuildingRequest(
                $"{capacity} cold storage for {briefing.ColonistCount} colonists, {targetDays} days",
                $"no cooler is visible; {incoming} needs cold storage before spoilage",
                BuildingClass.Freezer,
                TargetDef: "Cooler",
                RoomClass: RoomClass.Freezer,
                CapacityNeed: new CapacityNeed(CapacityMeasure.FoodUnits, briefing.ColonistCount * targetDays, "colonist_days"),
                Adjacency: [new AdjacencyHint(AdjacencyRelation.Near, "kitchen")],
                Power: new PowerNeed(NeedsPower: true, ApproxWatts: 200),
                Temperature: new TempNeed(TemperatureBand.Freezing, MustHold: true),
                Urgency: FreezerSupportPriority(briefing) >= Priority.High ? Urgency.BeforeDeadline : Urgency.Soon,
                Deadline: FreezerDeadline(briefing, incoming),
                Priority: FreezerSupportPriority(briefing),
                RequestedFrom: "Willie"));
    }

    private static BuildingRequest CookingBuildingRequest(
        string request,
        string reason,
        Priority priority) =>
        new(
            request,
            reason,
            BuildingClass.ProductionBench,
            TargetDef: "Campfire",
            RoomClass: RoomClass.Kitchen,
            CapacityNeed: new CapacityNeed(CapacityMeasure.WorkSlots, 1),
            Adjacency: [new AdjacencyHint(AdjacencyRelation.Near, "storage")],
            Priority: priority,
            RequestedFrom: "Willie");

    private static bool NeedsCookingBuildingSupport(FoodBriefing briefing) =>
        !briefing.Kitchen.HasCookingBuilding;

    private static bool HasKitchenCookingRequest(FoodFlagRequests requests) =>
        requests.BuildingRequests.Any(request =>
            request.TargetClass == BuildingClass.ProductionBench &&
            request.RoomClass == RoomClass.Kitchen);

    private static bool NeedsFreezerSupport(FoodBriefing briefing, float days, bool incomingPerishableFood) =>
        briefing.Infrastructure.Coolers == 0 &&
        (incomingPerishableFood || (days >= 7f && briefing.FoodUnits > 0));

    private static bool HasIncomingPerishableFoodPath(FoodBriefing briefing) =>
        briefing.RawFoodCount > 0 ||
        briefing.ReadyToHarvest > 0 ||
        briefing.WildHarvestCandidates > 0 ||
        CanSuggestHunting(briefing) ||
        FoodCropMath.Recommend(briefing).BestCandidate is not null;

    private static string FreezerAdviceBody(
        FoodBriefing briefing,
        float days,
        bool incomingPerishableFood)
    {
        string incoming = FreezerIncomingPhrase(briefing, incomingPerishableFood);
        if (incomingPerishableFood)
            return $"No cooler is visible while {incoming} is part of the active food path. Add cold storage before spoilage erases the gain.";

        return $"Food covers about {days:F1} days but no cooler is visible. Preserve the stored buffer before warm weather or large harvests.";
    }

    private static bool ShouldRecommendNewGrowingZone(FoodBriefing briefing, FoodCropCandidate candidate) =>
        ActiveMatchingCropTiles(briefing, candidate.CropDef) < candidate.Tiles;

    private static int ActiveMatchingCropTiles(FoodBriefing briefing, string cropDef)
    {
        int zoneTiles = briefing.CropZoneSummaries
            .Where(zone => SameCropDef(zone.Def, cropDef))
            .Sum(zone => Math.Max(0, zone.Count));
        int cropBreakdownTiles = briefing.CropBreakdown
            .Where(crop => SameCropDef(crop.Def, cropDef))
            .Sum(crop => Math.Max(0, crop.Count));

        return Math.Max(zoneTiles, cropBreakdownTiles);
    }

    private static bool SameCropDef(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static Priority FreezerSupportPriority(FoodBriefing briefing) =>
        briefing.Season.DaysToWinter is < 20 ? Priority.High : Priority.Medium;

    private static Deadline FreezerDeadline(FoodBriefing briefing, string incoming)
    {
        if (briefing.Season.DaysToWinter is < 20)
            return new Deadline(DeadlineKind.BeforeEvent, "winter");

        return new Deadline(DeadlineKind.BeforeEvent, incoming);
    }

    private static string FreezerCapacityClass(FoodBriefing briefing, float days)
    {
        if (briefing.Season.DaysToWinter is < 20)
            return "winter freezer";

        if (briefing.ReadyToHarvest >= briefing.ColonistCount * 6 ||
            briefing.WildHarvestCandidates >= briefing.ColonistCount * 6 ||
            (briefing.MealsCount + briefing.RawFoodCount) >= briefing.ColonistCount * 20)
            return "surplus freezer";

        return days >= 20f ? "buffer freezer" : "starter freezer";
    }

    private static int FreezerTargetDays(FoodBriefing briefing, float days)
    {
        if (briefing.Season.DaysToWinter is < 20)
            return 45;

        if (days >= 20f ||
            briefing.ReadyToHarvest >= briefing.ColonistCount * 6 ||
            (briefing.MealsCount + briefing.RawFoodCount) >= briefing.ColonistCount * 20)
            return 30;

        return 20;
    }

    private static string FreezerIncomingPhrase(FoodBriefing briefing, bool incomingPerishableFood)
    {
        if (briefing.ReadyToHarvest > 0)
            return "the next harvest";

        if (briefing.WildHarvestCandidates > 0)
            return "foraged food";

        if (CanSuggestHunting(briefing))
            return "hunted meat";

        if (briefing.RawFoodCount > 0)
            return "raw food and cooked meals";

        if (incomingPerishableFood)
            return "the planned food intake";

        return "stored food";
    }

    private static Priority FoodBufferPriority(FoodBriefing briefing, float days)
    {
        bool noImmediateLocalFood = briefing.MealsCount == 0 &&
                                    briefing.RawFoodCount == 0 &&
                                    briefing.ReadyToHarvest == 0 &&
                                    briefing.WildHarvestCandidates == 0;
        return days < 1f && noImmediateLocalFood ? Priority.Critical : Priority.High;
    }

    private static BuildingRequest StockpileVisibilityRequest(
        string request,
        string reason,
        int? quantity,
        Priority? priority) =>
        new(
            request,
            reason,
            BuildingClass.Stockpile,
            RoomClass: RoomClass.Storage,
            CapacityNeed: quantity is null
                ? null
                : new CapacityNeed(CapacityMeasure.StorageStacks, quantity, "food_units"),
            Quantity: quantity,
            Priority: priority,
            RequestedFrom: "Willie");

    private static FoodFlagRequests EmergencyRequests(FoodBriefing briefing, float days, Priority priority)
    {
        FoodFlagRequests requests = FoodFlagRequests.Empty;
        IReadOnlyList<ItemRequest> forbiddenMealRequests = ForbiddenMealItemRequests(briefing, priority);
        foreach (ItemRequest forbiddenMealRequest in forbiddenMealRequests)
            requests = requests.Add(forbiddenMealRequest);

        if (briefing.UnknownFoodUnits > 0 && briefing.MealsCount == 0 && briefing.RawFoodCount == 0)
            requests = requests.Add(StockpileVisibilityRequest(
                $"stockpile visibility for {briefing.UnknownFoodUnits} food units not classified as meals or raw food",
                "reported food units exist, but no meal or raw-food category is visible to Food",
                quantity: briefing.UnknownFoodUnits,
                priority: priority));

        if (!HasVisibleLocalFoodPath(briefing) && requests.Count == 0)
            requests = requests.Add(new AttentionRequest(
                "emergency food acquisition path",
                "no stored, harvestable, cookable, or sowable food path is visible in the briefing",
                Priority: priority,
                RequestedFrom: "Mayor"));

        if (requests.Count == 0)
            requests = requests.Add(new AttentionRequest(
                "active food-chain advice",
                "food buffer is below 7 days; resolve the separate Food advice cards before routine work",
                Priority: priority,
                RequestedFrom: MinisterName));

        return requests;
    }

    private static IReadOnlyList<AdviceAction> EmergencyActions(FoodBriefing briefing, float days)
    {
        List<AdviceAction> actions = [];
        AdviceAction? forbiddenMealAction = ForbiddenMealUnforbidAction(briefing);
        if (forbiddenMealAction is not null)
            actions.Add(forbiddenMealAction);

        if (briefing.UnknownFoodUnits > 0 && briefing.MealsCount == 0 && briefing.RawFoodCount == 0)
            actions.Add(new AdviceAction(
                AdviceActionKind.SetStockpileZone,
                $"Make {briefing.UnknownFoodUnits} food units visible as meals or raw food in a reachable stockpile; treat the buffer as zero if they are not edible.",
                Quantity: briefing.UnknownFoodUnits));

        if (!HasVisibleLocalFoodPath(briefing) && actions.Count == 0)
            actions.Add(new AdviceAction(
                AdviceActionKind.Trade,
                "Open an emergency food acquisition path because no stored, harvestable, cookable, or sowable food path is visible.",
                Owner: "Mayor"));

        if (actions.Count == 0)
            actions.Add(new AdviceAction(
                AdviceActionKind.RequestResource,
                "Resolve the active Food advice cards before routine work until the buffer is above 7 days.",
                Owner: MinisterName));

        return actions;
    }

    private static bool HasVisibleLocalFoodPath(FoodBriefing briefing) =>
        briefing.MealsCount > 0 ||
        briefing.RawFoodCount > 0 ||
        briefing.ReadyToHarvest > 0 ||
        briefing.WildHarvestCandidates > 0 ||
        CanSuggestHunting(briefing) ||
        FoodCropMath.Recommend(briefing).BestCandidate is not null;

    private static IReadOnlyList<AdviceAction> CookBillActions(FoodBriefing briefing, float days)
    {
        List<AdviceAction> actions = [];
        if (ShouldSuggestCookBill(briefing))
            actions.Add(new AdviceAction(AdviceActionKind.ProductionBill,
                CookBillActionText(briefing),
                Quantity: SimpleMealTarget(briefing),
                Apply: CookBillApply(briefing)));
        if (!briefing.Kitchen.HasCookingBuilding)
            actions.Add(new AdviceAction(
                AdviceActionKind.PlaceBlueprint,
                "Place a campfire or stove before relying on cooked-meal advice.",
                Owner: "Willie"));
        return actions;
    }

    private static FoodFlagRequests CookingLaborIfNeeded(
        FoodBriefing briefing,
        float days,
        Priority priority)
    {
        if (!ShouldRequestCookingLabor(briefing, days))
            return FoodFlagRequests.Empty;

        return FoodFlagRequests.Labor(
            new LaborRequest(
                "Cook work today",
                "raw food has to become meals and cook coverage is urgent or weak",
                WorkType.Cook,
                Skill: "Cooking",
                Priority: priority,
                RequestedFrom: "Labor"));
    }

    private static FoodFlagRequests HuntingRequests(FoodBriefing briefing, Priority priority, bool suppressBuildRequests = false)
    {
        FoodFlagRequests requests = FoodFlagRequests.Labor(
            new LaborRequest(
                "Hunt work for selected animals",
                "low-risk wild animals are the best visible local food-acquisition path",
                WorkType.Hunt,
                Skill: "Shooting",
                Priority: priority,
                RequestedFrom: "Labor"));
        // Suppress Butcher/ProductionBench and campfire build requests when a healthy latent reserve
        // means building hunting infrastructure on day 1 would be premature waste.
        if (!suppressBuildRequests)
        {
            if (!briefing.Kitchen.HasButcherTable)
                requests = requests.Add(new BuildingRequest(
                    "butcher table for hunted animals",
                    "hunting only helps the food chain after animals can be butchered",
                    BuildingClass.ProductionBench,
                    TargetDef: "TableButcher",
                    RoomClass: RoomClass.Butcher,
                    Priority: priority,
                    RequestedFrom: "Willie"));
            if (!briefing.Kitchen.HasCookingBuilding)
                requests = requests.Add(new BuildingRequest(
                    "campfire or stove for meat meals",
                    "meat must be cooked into safe meals once butchered",
                    BuildingClass.ProductionBench,
                    TargetDef: "Campfire",
                    RoomClass: RoomClass.Kitchen,
                    Priority: priority,
                    RequestedFrom: "Willie"));
        }
        return requests;
    }

    private static IReadOnlyList<AdviceAction> HuntingActions(FoodBriefing briefing, bool suppressBuildActions = false)
    {
        List<AdviceAction> actions =
        [
            HuntingAction(briefing)
        ];
        // Suppress butcher/campfire build actions when a healthy latent reserve makes far-hunt
        // infrastructure premature; keep the hunt designation itself.
        if (!suppressBuildActions)
        {
            if (!briefing.Kitchen.HasButcherTable)
                actions.Add(new AdviceAction(
                    AdviceActionKind.PlaceBlueprint,
                    "Place a butcher table so hunted animals can become meat.",
                    Owner: "Willie"));
            if (!briefing.Kitchen.HasCookingBuilding)
                actions.Add(new AdviceAction(
                    AdviceActionKind.PlaceBlueprint,
                    "Place a campfire or stove so butchered meat can become meals.",
                    Owner: "Willie"));
        }
        return actions.Take(3).ToList();
    }

    private static FoodFlagRequests PlantLaborIfNeeded(FoodBriefing briefing, float days, string what, string why) =>
        ShouldRequestPlantLabor(briefing, days)
            ? FoodFlagRequests.Labor(new LaborRequest(what, why, WorkType.PlantCut, Skill: "Plants", RequestedFrom: "Labor"))
            : FoodFlagRequests.Empty;

    private static AdviceAction HarvestAction(FoodBriefing briefing, float days) =>
        new(AdviceActionKind.MarkHarvest,
            HarvestActionText(briefing),
            Owner: ShouldRequestPlantLabor(briefing, days) ? "Labor" : null,
            WorkType: WorkType.PlantCut,
            Skill: "Plants",
            Apply: HarvestApply(briefing, "crop"));

    private static AdviceAction WildHarvestAction(FoodBriefing briefing, float days) =>
        new(AdviceActionKind.MarkHarvest,
            WildHarvestActionText(briefing),
            Owner: ShouldRequestPlantLabor(briefing, days) ? "Labor" : null,
            WorkType: WorkType.PlantCut,
            Skill: "Plants",
            Apply: HarvestApply(briefing, "wild"));

    private static AdviceAction HuntingAction(FoodBriefing briefing) =>
        new(AdviceActionKind.MarkHunt,
            HuntingActionText(briefing),
            Owner: "Labor",
            WorkType: WorkType.Hunt,
            Skill: "Shooting",
            Apply: HuntApply(briefing));

    private static AdviceAction GrowingZoneAction(FoodCropCandidate candidate) =>
        new(AdviceActionKind.DesignateZoneReq,
            $"Create about {candidate.Tiles} emergency {candidate.Label} growing tiles; use fertile soil near storage when possible.",
            Quantity: candidate.Tiles,
            Owner: MinisterName,
            WorkType: WorkType.Grow,
            Skill: "Plants");

    private static AdviceActionApply? HarvestApply(FoodBriefing briefing, string source)
    {
        FoodHarvestTarget? target = source == "wild"
            ? SelectedWildHarvestTarget(briefing)
            : SelectedCropHarvestTarget(briefing);
        if (target is null)
            return null;

        string label = source == "wild" ? "Mark forage" : "Mark harvest";
        string zone = string.IsNullOrWhiteSpace(target.ZoneId) ? "" : $" in {target.ZoneId}";
        string location = string.IsNullOrWhiteSpace(target.Proximity) ? "" : $" ({target.Proximity})";
        string summary = $"{target.Count} {target.Def} plants{zone}{location}";

        return new MarkHarvestAreaApply(
            Label: label,
            TargetSummary: summary,
            MapId: briefing.MapId,
            Rect: target.Rect,
            TargetIds: target.PlantIds,
            TargetCount: target.Count);
    }

    private static AdviceActionApply? HuntApply(FoodBriefing briefing)
    {
        if (briefing.WildHuntTargets.Count == 0)
            return null;

        WildHuntTarget summaryTarget = briefing.WildHuntTargets[0];
        FoodHuntTarget? target = briefing.HuntTargets
            .Where(candidate => string.Equals(candidate.Def, summaryTarget.Def, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.Count)
            .FirstOrDefault();
        if (target is null)
            return null;

        string location = string.IsNullOrWhiteSpace(target.Proximity) ? "" : $" ({target.Proximity})";
        string targetLabel = LabelDef(target.Def);
        string summary = $"{target.Count} {targetLabel} hunt target{(target.Count == 1 ? "" : "s")}{location}";

        return new MarkHuntAreaApply(
            Label: "Mark hunt",
            TargetSummary: summary,
            MapId: briefing.MapId,
            Rect: target.Rect,
            TargetIds: target.AnimalIds,
            TargetCount: target.Count);
    }

    private static FoodHarvestTarget? SelectedCropHarvestTarget(FoodBriefing briefing)
    {
        FoodCropZoneSummary? zone = briefing.CropZoneSummaries
            .Where(z => z.ReadyCount > 0)
            .OrderByDescending(z => z.ReadyCount)
            .FirstOrDefault();
        if (zone is null)
            return briefing.HarvestTargets
                .Where(target => string.Equals(target.Source, "crop", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(target => target.Count)
                .FirstOrDefault();

        return briefing.HarvestTargets
            .Where(target => string.Equals(target.Source, "crop", StringComparison.OrdinalIgnoreCase))
            .Where(target => string.Equals(target.Def, zone.Def, StringComparison.OrdinalIgnoreCase))
            .Where(target => string.Equals(target.ZoneId ?? "", zone.ZoneId ?? "", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(target => target.Count)
            .FirstOrDefault();
    }

    private static FoodHarvestTarget? SelectedWildHarvestTarget(FoodBriefing briefing)
    {
        WildHarvestCluster? cluster = briefing.WildHarvestClusters.FirstOrDefault();
        if (cluster is null)
            return briefing.HarvestTargets
                .Where(target => string.Equals(target.Source, "wild", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(target => target.Count)
                .FirstOrDefault();

        return briefing.HarvestTargets
            .Where(target => string.Equals(target.Source, "wild", StringComparison.OrdinalIgnoreCase))
            .Where(target => string.Equals(target.Def, cluster.Def, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
    }

    private static AdviceActionApply? UnforbidApply(FoodBriefing briefing) =>
        UnforbidApply(briefing, ForbiddenMealUnforbidTargets(briefing));

    private static AdviceActionApply? UnforbidApply(
        FoodBriefing briefing,
        IReadOnlyList<FoodUnforbidTarget> targets)
    {
        if (targets.Count == 0)
            return null;

        int count = targets.Sum(target => target.Count);
        string label = targets.All(IsMealUnforbidTarget)
            ? count == 1 ? "Unforbid meal" : "Unforbid meals"
            : "Unforbid food";
        string summary = $"{count} {ForbiddenMealLabel(targets, count)} across {targets.Count} stack{(targets.Count == 1 ? "" : "s")}";

        return new UnforbidThingsApply(
            Label: label,
            TargetSummary: summary,
            MapId: briefing.MapId,
            ThingIds: targets.Select(target => target.Id).ToList(),
            ThingTargets: targets.Select(target => new AdviceThingApplyTarget(
                Id: target.Id,
                Def: target.Def,
                Kind: target.Kind,
                Source: target.Source,
                Position: target.Position)).ToList(),
            TargetCount: targets.Count);
    }

    private static IReadOnlyList<AdviceAction> NutritionSignalGapActions(FoodBriefing briefing)
    {
        AdviceAction setStockpileZoneAction = new(
            AdviceActionKind.SetStockpileZone,
            "Confirm remaining food units are classified as meals or raw food and reachable before counting them as buffer.");
        AdviceAction? forbiddenMealAction = ForbiddenMealUnforbidAction(briefing);
        return forbiddenMealAction is null
            ? [setStockpileZoneAction]
            : [forbiddenMealAction, setStockpileZoneAction];
    }

    private static AdviceActionApply? CookBillApply(FoodBriefing briefing)
    {
        if (briefing.RawFoodCount <= 0 || !briefing.Kitchen.HasCookingBuilding)
            return null;

        string? workbenchId = briefing.Kitchen.SingleCookingBuildingId;
        if (string.IsNullOrWhiteSpace(workbenchId))
            return null;

        int target = Math.Min(SimpleMealTarget(briefing), AssistedApplyLimits.MaxProductionBillTarget);
        return new UpsertProductionBillApply(
            Label: "Set simple meal bill",
            TargetSummary: $"simple meal bill on {CookingStationTarget(briefing)} until {target} meals",
            MapId: briefing.MapId,
            WorkbenchBuildingId: workbenchId,
            RecipeSelectorKey: SimpleMealRecipeSelector,
            RepeatMode: BillRepeatModeTargetCount,
            TargetCount: target);
    }

    private static bool ShouldSuggestCookBill(FoodBriefing briefing) =>
        briefing.RawFoodCount > 0 &&
        !HasSatisfiedSimpleMealBill(briefing);

    private static bool HasSatisfiedSimpleMealBill(FoodBriefing briefing)
    {
        int target = Math.Min(SimpleMealTarget(briefing), AssistedApplyLimits.MaxProductionBillTarget);
        return briefing.Kitchen.CookingBills.Any(bill =>
            !bill.Suspended &&
            string.Equals(bill.RepeatMode, BillRepeatModeTargetCount, StringComparison.OrdinalIgnoreCase) &&
            bill.TargetCount >= target &&
            IsSimpleMealBill(bill));
    }

    private static bool IsSimpleMealBill(FoodCookingBillSummary bill) =>
        !string.IsNullOrWhiteSpace(bill.RecipeDefName) &&
        SimpleMealRecipeDefs.Any(recipeDef =>
            string.Equals(recipeDef, bill.RecipeDefName, StringComparison.OrdinalIgnoreCase));

    private static bool ShouldRequestCookingLabor(FoodBriefing briefing, float days) =>
        days < 10f || briefing.MealsCount == 0 || briefing.Skills.QualifiedCooks == 0;

    private static bool ShouldRequestPlantLabor(FoodBriefing briefing, float days) =>
        days < 10f || briefing.Skills.QualifiedGrowers == 0;

    private static string HarvestActionText(FoodBriefing briefing)
    {
        FoodCropZoneSummary? zone = briefing.CropZoneSummaries
            .Where(z => z.ReadyCount > 0)
            .OrderByDescending(z => z.ReadyCount)
            .FirstOrDefault();
        if (zone is null)
            return $"Mark/prioritize harvest for {briefing.ReadyToHarvest} ready food crop tiles.";

        FoodHarvestTarget? target = SelectedCropHarvestTarget(briefing);
        string location = string.IsNullOrWhiteSpace(zone.Proximity) ? "" : $" ({zone.Proximity})";
        if (target is not null && target.Count < zone.ReadyCount)
            return $"Mark/prioritize harvest for {target.Count} of {zone.ReadyCount} ready {zone.Def} crop tiles{location}.";

        return $"Mark/prioritize harvest for {zone.ReadyCount} ready {zone.Def} crop tiles{location}.";
    }

    private static string WildHarvestBody(FoodBriefing briefing, float days)
    {
        WildHarvestCluster? cluster = briefing.WildHarvestClusters.FirstOrDefault();
        if (cluster is null)
            return $"{briefing.WildHarvestCandidates} edible forage plants are visible while food is at {days:F1} days. Position data is unavailable, so do not assume exact location.";
        return $"{cluster.Count} harvestable {cluster.Def} plants are {cluster.Proximity ?? "visible"} while food is at {days:F1} days.";
    }

    private static string WildHarvestActionText(FoodBriefing briefing)
    {
        WildHarvestCluster? cluster = briefing.WildHarvestClusters.FirstOrDefault();
        if (cluster is null)
            return $"Mark edible forage for harvest; {briefing.WildHarvestCandidates} candidates are visible but no location summary is available.";

        FoodHarvestTarget? target = SelectedWildHarvestTarget(briefing);
        if (target is not null && target.Count < cluster.Count)
            return $"Mark {target.Count} of {cluster.Count} {cluster.Def} for harvest ({target.Proximity ?? cluster.Proximity ?? "location unknown"}).";

        return $"Mark the nearest {cluster.Count} {cluster.Def} for harvest ({cluster.Proximity ?? "location unknown"}).";
    }

    private static string HuntingBody(FoodBriefing briefing, float days)
    {
        WildHuntTarget target = briefing.WildHuntTargets[0];
        string location = string.IsNullOrWhiteSpace(target.Proximity)
            ? "with no location summary available"
            : target.Proximity;
        string score = string.IsNullOrWhiteSpace(target.ScoreReason) ? "" : $" {target.ScoreReason}.";
        return $"Food covers about {days:F1} days and {target.Count} {LabelDef(target.Def)} are visible {location}.{score} Mark a small, low-risk hunting batch and avoid predators, bonded animals, or anything the colony cannot cover safely.";
    }

    private static string HuntingActionText(FoodBriefing briefing)
    {
        WildHuntTarget target = briefing.WildHuntTargets[0];
        int count = Math.Min(target.Count, Math.Max(1, briefing.ColonistCount * 2));
        string location = string.IsNullOrWhiteSpace(target.Proximity) ? "location unknown" : target.Proximity;
        return $"Mark up to {count} {LabelDef(target.Def, count)} for hunting ({location}).";
    }

    private static string CookBillActionText(FoodBriefing briefing) =>
        $"Set/check simple meal bill on {CookingStationTarget(briefing)} around {SimpleMealTarget(briefing)} meals; keep fine meals off until the buffer is stable.";

    private static string CookingStationTarget(FoodBriefing briefing)
    {
        FoodCookingBuildingSummary? building = briefing.Kitchen.SingleCookingBuilding;
        if (building is null)
            return briefing.Kitchen.CookingBuildings > 1 ? "a cooking station" : "one cooking station";

        string label = string.IsNullOrWhiteSpace(building.Label)
            ? LabelBuildingDef(building.Def)
            : building.Label.Trim();
        string position = FormatPosition(building.Position);
        return string.IsNullOrWhiteSpace(position) ? label : $"{label} at {position}";
    }

    private static string FormatPosition(MapPosition? position) =>
        position is null ? "" : $"({position.X}, {position.Y}, {position.Z})";

    private static string EmergencyBody(FoodBriefing briefing, float days)
    {
        string sentence = $"Food covers about {days:F1} days for {briefing.ColonistCount} colonists. Set up immediate intake, cooking, and growing capacity before routine work.";
        int forbiddenMealCount = ForbiddenMealCount(briefing);
        return forbiddenMealCount > 0
            ? $"{sentence} {forbiddenMealCount} forbidden {ForbiddenMealLabel(briefing, forbiddenMealCount)} are visible but not counted in the current food buffer."
            : sentence;
    }

    private static string FoodRemainderBody(FoodBriefing briefing)
    {
        int forbiddenMealCount = ForbiddenMealCount(briefing);
        if (briefing.UnknownFoodUnits > 0 && briefing.ExcludedFoodUnits > 0)
        {
            string excluded = forbiddenMealCount > 0
                ? $"{forbiddenMealCount} forbidden {ForbiddenMealLabel(briefing, forbiddenMealCount)}"
                : $"{briefing.ExcludedFoodUnits} forbidden food units";
            return $"RIMAPI reports {briefing.UnknownFoodUnits} food units still not classified as meals or raw food plus {excluded} outside the current food buffer. Unforbid the visible food, then make any remaining items visible as meals or raw food before treating this as a safe buffer.";
        }

        if (briefing.UnknownFoodUnits > 0)
        {
            return $"RIMAPI reports {briefing.UnknownFoodUnits} food units but no usable meal/raw-food classification. Make those items visible as reachable meals or raw food before treating this as a safe buffer.";
        }

        if (forbiddenMealCount > 0)
        {
            return $"RIMAPI reports {forbiddenMealCount} forbidden {ForbiddenMealLabel(briefing, forbiddenMealCount)} outside the current food buffer. Unforbid them before treating this as a safe buffer.";
        }

        return $"RIMAPI reports {briefing.ExcludedFoodUnits} forbidden food units outside the current food buffer and no usable nutrition signal. Make them reachable before treating this as a safe buffer.";
    }

    private static int ForbiddenMealCount(FoodBriefing briefing) =>
        ForbiddenMealUnforbidTargets(briefing)
            .Sum(target => target.Count);

    private static IReadOnlyList<FoodUnforbidTarget> ForbiddenMealUnforbidTargets(FoodBriefing briefing) =>
        briefing.UnforbidTargets
            .Where(IsEdibleUnforbidTarget)
            .Take(AssistedApplyLimits.MaxUnforbidTargets)
            .ToList();

    private static bool IsEdibleUnforbidTarget(FoodUnforbidTarget target) =>
        IsMealUnforbidTarget(target) ||
        string.Equals(target.Kind, "raw_food", StringComparison.OrdinalIgnoreCase);

    private static bool IsMealUnforbidTarget(FoodUnforbidTarget target) =>
        string.Equals(target.Kind, "meal", StringComparison.OrdinalIgnoreCase);

    private static AdviceAction? ForbiddenMealUnforbidAction(FoodBriefing briefing)
    {
        IReadOnlyList<FoodUnforbidTarget> targets = ForbiddenMealUnforbidTargets(briefing);
        int forbiddenMealCount = targets.Sum(target => target.Count);
        if (forbiddenMealCount <= 0)
            return null;

        return new AdviceAction(
            AdviceActionKind.Unforbid,
            ForbiddenMealActionText(targets, forbiddenMealCount),
            Quantity: forbiddenMealCount,
            Apply: UnforbidApply(briefing, targets));
    }

    private static IReadOnlyList<ItemRequest> ForbiddenMealItemRequests(FoodBriefing briefing, Priority priority)
    {
        return ForbiddenMealUnforbidTargets(briefing)
            .GroupBy(target => target.Def, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                IReadOnlyList<FoodUnforbidTarget> targets = group.ToList();
                int count = targets.Sum(target => target.Count);
                return new ItemRequest(
                    $"{count} forbidden {ForbiddenMealLabel(targets, count)}",
                    ForbiddenMealRequestReason(targets),
                    ItemDef: group.Key,
                    Quantity: count,
                    Priority: priority);
            })
            .Where(request => request.Quantity is > 0)
            .ToList();
    }

    private static string ForbiddenMealRequestReason(IReadOnlyList<FoodUnforbidTarget> targets) =>
        targets.All(IsMealUnforbidTarget)
            ? "visible meals are forbidden and not counted in the current food buffer"
            : "visible edible food is forbidden and not counted in the current food buffer";

    private static string ForbiddenMealActionText(
        IReadOnlyList<FoodUnforbidTarget> targets,
        int forbiddenMealCount)
    {
        IReadOnlyList<string> positions = targets
            .Select(target => FormatPosition(target.Position))
            .Where(position => !string.IsNullOrWhiteSpace(position))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        string location = positions.Count == 0 ? "" : $" at {string.Join(", ", positions)}";
        return $"Unforbid {forbiddenMealCount} {ForbiddenMealLabel(targets, forbiddenMealCount)}{location}; then let haulers bring them into the food stockpile.";
    }

    private static string ForbiddenMealLabel(FoodBriefing briefing, int count)
    {
        return ForbiddenMealLabel(ForbiddenMealUnforbidTargets(briefing), count);
    }

    private static string ForbiddenMealLabel(IReadOnlyList<FoodUnforbidTarget> targets, int count)
    {
        string label = ForbiddenMealBaseLabel(targets.FirstOrDefault());
        if (count == 1 || label.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            return label;
        return $"{label}s";
    }

    private static string ForbiddenMealBaseLabel(FoodUnforbidTarget? target)
    {
        string? label = target?.Label;
        if (string.IsNullOrWhiteSpace(label))
            return "meals";

        string trimmed = label.Trim();
        int stackSuffixIndex = trimmed.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
        if (stackSuffixIndex > 0)
        {
            string suffix = trimmed[(stackSuffixIndex + 2)..];
            if (suffix.Length > 0 && suffix.All(char.IsDigit))
                return trimmed[..stackSuffixIndex].Trim();
        }

        return trimmed;
    }

    private static int SimpleMealTarget(FoodBriefing briefing) =>
        Math.Clamp(briefing.ColonistCount * 4, 4, 30);

    private static bool CanSuggestHunting(FoodBriefing briefing) =>
        !briefing.ActiveThreat && briefing.WildHuntTargets.Count > 0;

    private static bool ShouldEscalateHuntTargetsBlockedByRisk(FoodBriefing briefing, float days) =>
        days < 20f &&
        briefing.WildAnimalCount > 0 &&
        briefing.ReadyToHarvest == 0 &&
        !CanSuggestHunting(briefing);

    private static string HuntTargetsBlockedByRiskReason(FoodBriefing briefing)
    {
        if (briefing.ActiveThreat)
            return $"{briefing.WildAnimalCount} wild animals exist but an active threat blocks hunting advice";

        if (briefing.WildHuntTargets.Count == 0)
            return $"{briefing.WildAnimalCount} wild animals exist but the risk calculator found no low-risk hunt target";

        return $"{briefing.WildAnimalCount} wild animals exist but hunting advice is blocked by the safety gate";
    }

    private static string LabelDef(string def)
    {
        string label = def;
        if (label.StartsWith("Animal_", StringComparison.OrdinalIgnoreCase))
            label = label["Animal_".Length..];
        return label.Replace('_', ' ').Trim().ToLowerInvariant();
    }

    private static string LabelDef(string def, int count)
    {
        string label = LabelDef(def);
        if (count == 1 || label.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            return label;
        return $"{label}s";
    }

    private static string LabelBuildingDef(string def)
    {
        string normalized = def.Replace('_', ' ').Trim();
        List<char> chars = new(normalized.Length + 4);
        for (int i = 0; i < normalized.Length; i++)
        {
            char current = normalized[i];
            if (i > 0 &&
                char.IsUpper(current) &&
                !char.IsWhiteSpace(normalized[i - 1]) &&
                (char.IsLower(normalized[i - 1]) || (i + 1 < normalized.Length && char.IsLower(normalized[i + 1]))))
            {
                chars.Add(' ');
            }

            chars.Add(current);
        }

        string label = new string(chars.ToArray()).Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(label) ? "cooking station" : label;
    }

    private static string ToSnakeCase(string value)
    {
        List<char> chars = new(value.Length + 4);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsUpper(c) && i > 0) chars.Add('_');
            chars.Add(char.ToLowerInvariant(c));
        }
        return new string(chars.ToArray());
    }

    private sealed record FoodFlagRequests(
        IReadOnlyList<BuildingRequest> BuildingRequests,
        IReadOnlyList<LaborRequest> LaborRequests,
        IReadOnlyList<ItemRequest> ItemRequests,
        IReadOnlyList<ZoneRequest> ZoneRequests,
        IReadOnlyList<AttentionRequest> Attention)
    {
        public static FoodFlagRequests Empty => new([], [], [], [], []);

        public int Count =>
            BuildingRequests.Count +
            LaborRequests.Count +
            ItemRequests.Count +
            ZoneRequests.Count +
            Attention.Count;

        public IReadOnlyList<BuildingRequest>? BuildingRequestsOrNull => NullIfEmpty(BuildingRequests);
        public IReadOnlyList<LaborRequest>? LaborRequestsOrNull => NullIfEmpty(LaborRequests);
        public IReadOnlyList<ItemRequest>? ItemRequestsOrNull => NullIfEmpty(ItemRequests);
        public IReadOnlyList<ZoneRequest>? ZoneRequestsOrNull => NullIfEmpty(ZoneRequests);
        public IReadOnlyList<AttentionRequest>? AttentionOrNull => NullIfEmpty(Attention);

        public static FoodFlagRequests Building(BuildingRequest request) =>
            Empty.Add(request);

        public static FoodFlagRequests Labor(LaborRequest request) =>
            Empty.Add(request);

        public static FoodFlagRequests Zone(ZoneRequest request) =>
            Empty.Add(request);

        public static FoodFlagRequests AttentionRequest(
            string request,
            string reason,
            int? quantity,
            Priority? priority,
            string? requestedFrom) =>
            Empty.Add(new AttentionRequest(
                Request: quantity is null ? request : $"{request} ({quantity.Value})",
                Reason: reason,
                Priority: priority,
                RequestedFrom: requestedFrom));

        public FoodFlagRequests Add(FoodFlagRequests other) =>
            new(
                BuildingRequests.Concat(other.BuildingRequests).ToArray(),
                LaborRequests.Concat(other.LaborRequests).ToArray(),
                ItemRequests.Concat(other.ItemRequests).ToArray(),
                ZoneRequests.Concat(other.ZoneRequests).ToArray(),
                Attention.Concat(other.Attention).ToArray());

        public FoodFlagRequests Add(BuildingRequest request) =>
            this with { BuildingRequests = BuildingRequests.Append(request).ToArray() };

        public FoodFlagRequests Add(LaborRequest request) =>
            this with { LaborRequests = LaborRequests.Append(request).ToArray() };

        public FoodFlagRequests Add(ItemRequest request) =>
            this with { ItemRequests = ItemRequests.Append(request).ToArray() };

        public FoodFlagRequests Add(ZoneRequest request) =>
            this with { ZoneRequests = ZoneRequests.Append(request).ToArray() };

        public FoodFlagRequests Add(AttentionRequest request) =>
            this with { Attention = Attention.Append(request).ToArray() };

        public IReadOnlyList<Decision> ToDecisions(RuleId rule, Priority fallbackPriority)
        {
            List<Decision> decisions = [];
            decisions.AddRange(BuildingRequests.Select(request => new RequestBuild(
                rule,
                request,
                request.RequestedFrom ?? "Willie",
                request.Priority ?? fallbackPriority)));
            decisions.AddRange(LaborRequests.Select(request => new RequestLabor(
                rule,
                request,
                request.RequestedFrom ?? "Labor",
                request.Priority ?? fallbackPriority)));
            decisions.AddRange(ItemRequests.Select(request => new RequestItem(
                rule,
                request,
                request.RequestedFrom ?? "Chief of Staff",
                request.Priority ?? fallbackPriority)));
            decisions.AddRange(ZoneRequests.Select(request => new RequestZone(
                rule,
                request,
                request.RequestedFrom ?? "Willie",
                request.Priority ?? fallbackPriority)));
            decisions.AddRange(Attention.Select(request => new RequestAttention(
                rule,
                request,
                request.RequestedFrom ?? "Chief of Staff",
                request.Priority ?? fallbackPriority)));
            return decisions;
        }

        private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T> values) =>
            values.Count == 0 ? null : values;
    }
}
