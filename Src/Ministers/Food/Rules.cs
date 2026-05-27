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

    public RulesResult Evaluate(FoodBriefing briefing, ColonyContext context)
    {
        if (briefing.EstimatedDaysOfFood is null)
        {
            if (briefing.UnclassifiedFoodUnits > 0)
                return DecisionFor(briefing, "nutrition_signal_gap",
                    FoodConcern.ManageFoodStockpile,
                    AdvicePriority.Medium,
                    "Food stockpile categories need verification",
                    FoodRemainderBody(briefing),
                    "Missing nutrition would make days-of-food unreliable; use the fallback counts until upstream data is fixed.",
                    [new(AdviceActionKind.SetStockpileZone, "Confirm unknown or excluded food is edible and reachable before counting it as buffer.")],
                    FoodFlagRequests.Building(StockpileVisibilityRequest(
                        "reachable food stockpile visibility",
                        "food units outside meals/raw-food counts cannot be converted into days-of-food",
                        quantity: null,
                        priority: AdvicePriority.Medium)),
                    false);

            return DecisionFor(briefing, "unknown_food_state",
                FoodConcern.FoodSecurity,
                AdvicePriority.High,
                "Food state unknown",
                "No reliable food stockpile signal is available. Treat this as a food-security check, not confirmed starvation.",
                "The food chain cannot safely decide without stockpile visibility.",
                [new(AdviceActionKind.SetStockpileZone, "Create or expose a reachable food stockpile, then refresh RimBob once food is visible.")],
                FoodFlagRequests.Building(StockpileVisibilityRequest(
                    "visible reachable food stockpile",
                    "food_units and nutrition are both unavailable",
                    quantity: null,
                    priority: AdvicePriority.High)),
                true);
        }

        float days = briefing.EstimatedDaysOfFood.Value;

        if (days < 7f)
        {
            AdvicePriority priority = FoodBufferPriority(briefing, days);
            return DecisionFor(briefing, "emergency_food_flag",
                FoodConcern.FoodSecurity,
                priority,
                "Food crisis within a week",
                EmergencyBody(briefing, days),
                "Food below 7 days is an urgent survival risk. Chef owns the next food-chain actions; cross-minister requests carry build, item, attention, and labor needs.",
                EmergencyActions(briefing, days),
                EmergencyRequests(briefing, days, priority),
                true);
        }

        if (briefing.ReadyToHarvest > 0)
        {
            AdvicePriority priority = days < 15f ? AdvicePriority.High : AdvicePriority.Medium;
            bool needsFreezerSupport = NeedsFreezerSupport(briefing, days, incomingPerishableFood: true);
            return DecisionFor(briefing, "harvest_mature_crops",
                FoodConcern.HarvestNow,
                priority,
                "Mature crops are ready",
                $"{briefing.ReadyToHarvest} crop tiles are ready to harvest. Pull them in before weather, rot, or task drift wastes the buffer.",
                "Mature crops are a deterministic food-chain opportunity.",
                ActionsWithFreezerSupport(briefing, days, [HarvestAction(briefing, days)], incomingPerishableFood: true),
                RequestsWithFreezerSupport(briefing, days, PlantLaborIfNeeded(briefing, days, "PlantCut work for ready crops", "mature crops only help once harvested"), incomingPerishableFood: true),
                days < 15f || needsFreezerSupport);
        }

        if (briefing.MealsCount < briefing.ColonistCount * 2 &&
            days > 7f &&
            briefing.RawFoodCount > 0 &&
            (ShouldSuggestCookBill(briefing) || ShouldRequestCookingLabor(briefing, days)))
        {
            bool needsFreezerSupport = NeedsFreezerSupport(briefing, days, incomingPerishableFood: true);
            bool needsCookingLabor = ShouldRequestCookingLabor(briefing, days);
            return DecisionFor(briefing, "meals_understocked",
                FoodConcern.ManageCookBills,
                AdvicePriority.Medium,
                "Cooked meals are understocked",
                $"Only {briefing.MealsCount} meals are reported for {briefing.ColonistCount} colonists while raw food exists.",
                "A raw-food buffer still needs cooking throughput to become safe daily nutrition.",
                ActionsWithFreezerSupport(briefing, days, CookBillActions(briefing, days), incomingPerishableFood: true),
                RequestsWithFreezerSupport(briefing, days, CookingLaborIfNeeded(briefing, days, AdvicePriority.Medium), incomingPerishableFood: true),
                needsFreezerSupport || needsCookingLabor);
        }

        if (days < 20f && briefing.WildHarvestCandidates > 0 && briefing.ReadyToHarvest == 0)
        {
            bool needsFreezerSupport = NeedsFreezerSupport(briefing, days, incomingPerishableFood: true);
            return DecisionFor(briefing, "wild_harvest_available",
                FoodConcern.WildHarvest,
                days < 10f ? AdvicePriority.High : AdvicePriority.Medium,
                "Forage can extend the buffer",
                WildHarvestBody(briefing, days),
                "Foraging edible plants is lower-risk than hunting when no mature crops are ready.",
                ActionsWithFreezerSupport(briefing, days, [WildHarvestAction(briefing, days)], incomingPerishableFood: true),
                RequestsWithFreezerSupport(briefing, days, PlantLaborIfNeeded(briefing, days, "PlantCut work for forage", "edible forage requires plant work"), incomingPerishableFood: true),
                days < 10f || needsFreezerSupport);
        }

        if (days < 20f && CanSuggestHunting(briefing) && briefing.ReadyToHarvest == 0)
        {
            AdvicePriority priority = days < 10f ? AdvicePriority.High : AdvicePriority.Medium;
            bool needsFreezerSupport = NeedsFreezerSupport(briefing, days, incomingPerishableFood: true);
            return DecisionFor(briefing, "hunt_low_risk_animals",
                FoodConcern.HuntForFood,
                priority,
                "Mark low-risk animals for hunting",
                HuntingBody(briefing, days),
                "The briefing has healthy wild animals and no lower-risk harvest path; hunting is a concrete local food-acquisition action.",
                ActionsWithFreezerSupport(briefing, days, HuntingActions(briefing), incomingPerishableFood: true),
                RequestsWithFreezerSupport(briefing, days, HuntingRequests(briefing, priority), incomingPerishableFood: true),
                days < 10f || needsFreezerSupport);
        }

        FoodCropCandidate? cropCandidate = FoodCropMath.Recommend(briefing).BestCandidate;
        if (days < 20f && cropCandidate is not null && ShouldRecommendNewGrowingZone(briefing, cropCandidate))
        {
            bool needsFreezerSupport = NeedsFreezerSupport(briefing, days, incomingPerishableFood: true);
            return DecisionFor(briefing, "expand_growing_capacity",
                FoodConcern.ExpandGrowingCapacity,
                days < 12f ? AdvicePriority.High : AdvicePriority.Medium,
                "Expand food growing capacity",
                $"Food covers about {days:F1} days and {cropCandidate.Label} still fits the growing window: {cropCandidate.Reason}. Add a compact food crop zone instead of waiting for hunting or trade.",
                "A concrete growing-zone action is more actionable than a vague labor request; cross-minister flags carry the growing-zone dependency separately.",
                ActionsWithFreezerSupport(briefing, days, [GrowingZoneAction(cropCandidate)], incomingPerishableFood: true),
                RequestsWithFreezerSupport(briefing, days,
                    FoodFlagRequests.AttentionRequest(
                        $"{cropCandidate.Tiles} {cropCandidate.Label} growing tiles near fertile soil and food storage",
                        cropCandidate.Reason,
                        quantity: cropCandidate.Tiles,
                        priority: days < 12f ? AdvicePriority.High : AdvicePriority.Medium,
                        requestedFrom: "Willie"),
                    incomingPerishableFood: true),
                days < 12f || needsFreezerSupport);
        }

        if (ShouldEscalateHuntTargetsBlockedByRisk(briefing, days))
            return new Escalate(
                "Food below 20 days with visible animals, but the hunting path is blocked by the current safety/risk filters.",
                new { briefing.WildAnimalCount, briefing.ActiveThreat, LowRiskTargetCount = briefing.WildHuntTargets.Count, briefing.Skills.BestCooking, briefing.HuntRiskSummaries },
                DiagnosticsFor(briefing, "hunt_targets_blocked_by_risk"));

        if (briefing.Season.DaysToWinter is < 20 && days < 30f)
            return new Escalate(
                "Winter is close and food buffer is below target; crop/freezer/labor tradeoff needs guide-grounded judgment.",
                new { briefing.Season.DaysToWinter, DaysOfFood = days, briefing.CropBreakdown },
                DiagnosticsFor(briefing, "winter_food_tradeoff"));

        if (briefing.Infrastructure.Coolers == 0 && days >= 20f && briefing.FoodUnits > 0)
            return DecisionFor(briefing, "freezer_missing",
                FoodConcern.ManageFreezer,
                AdvicePriority.Medium,
                "Food storage needs freezer support",
                "Food exists but no cooler is visible. Preserve surplus before warm weather or large harvests.",
                "Chef owns freezer need; Willie owns the actual build work.",
                FreezerSupportActions(briefing, days, incomingPerishableFood: false),
                FreezerSupportRequests(briefing, days, incomingPerishableFood: false),
                true);

        if (days >= 30f)
            return new Decision([], [], "maintain_security_threshold", DiagnosticsFor(briefing, "maintain_security_threshold"));

        return new Escalate(
            "Food state is below ideal but no deterministic rule cleanly chooses the next action.",
            new { DaysOfFood = days, briefing.ReadyToHarvest, briefing.WildHarvestCandidates, briefing.WildAnimalCount },
            DiagnosticsFor(briefing, "unresolved_food_gap"));
    }

    private static Decision DecisionFor(
        FoodBriefing briefing,
        string trace,
        FoodConcern type,
        AdvicePriority priority,
        string title,
        string body,
        string rationale,
        IReadOnlyList<AdviceAction> actions,
        FoodFlagRequests requests,
        bool emitFlag)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string concern = ToSnakeCase(type.ToString());
        AdviceItem advice = new(
            Id: $"{MinisterName.ToLowerInvariant()}_{trace}",
            Minister: MinisterName,
            Concern: concern,
            Priority: priority,
            Title: title,
            Body: body,
            Rationale: rationale,
            Actions: actions,
            GuideCitationIds: [],
            IssuedAt: now,
            ExpiresAt: now.AddHours(priority >= AdvicePriority.High ? 4 : 24),
            IssuedGameDate: briefing.Date,
            IssuedGameTick: briefing.GameTick,
            ExpiresGameTick: AdviceFreshness.ExpiresGameTick(briefing.GameTick, priority),
            BriefingRef: new BriefingRef(MinisterName, briefing.BriefingVersion, $"food:{briefing.BriefingVersion}")
        );

        IReadOnlyList<AgentFlag> flags = emitFlag
            ? [new AgentFlag(
                Id: $"{Domain}:{trace}",
                SourceMinister: MinisterName,
                Severity: ToFlagSeverity(priority),
                Domain: Domain,
                Summary: title,
                BuildingRequests: requests.BuildingRequestsOrNull,
                LaborRequests: requests.LaborRequestsOrNull,
                ItemRequests: requests.ItemRequestsOrNull,
                Attention: requests.AttentionOrNull,
                Detail: trace,
                ExpiresAt: now.AddHours(24))]
            : [];

        RuleTraceDetails diagnostics = DiagnosticsFor(briefing, trace)
            .WithEmissions("rules", trace, [advice], flags);
        return new Decision([advice], flags, trace, diagnostics);
    }

    private static IReadOnlyList<AdviceAction> ActionsWithFreezerSupport(
        FoodBriefing briefing,
        float days,
        IReadOnlyList<AdviceAction> actions,
        bool incomingPerishableFood)
    {
        List<AdviceAction> result = [.. actions];
        result.AddRange(FreezerSupportActions(briefing, days, incomingPerishableFood));
        return result;
    }

    private static FoodFlagRequests RequestsWithFreezerSupport(
        FoodBriefing briefing,
        float days,
        FoodFlagRequests requests,
        bool incomingPerishableFood)
    {
        return requests.Add(FreezerSupportRequests(briefing, days, incomingPerishableFood));
    }

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
                Urgency: FreezerSupportPriority(briefing) >= AdvicePriority.High ? Urgency.BeforeDeadline : Urgency.Soon,
                Deadline: FreezerDeadline(briefing, incoming),
                Priority: FreezerSupportPriority(briefing),
                RequestedFrom: "Willie"));
    }

    private static bool NeedsFreezerSupport(FoodBriefing briefing, float days, bool incomingPerishableFood) =>
        briefing.Infrastructure.Coolers == 0 &&
        (incomingPerishableFood || (days >= 7f && briefing.FoodUnits > 0));

    private static bool HasPotentialPerishableFoodPath(FoodBriefing briefing) =>
        briefing.FoodUnits > 0 ||
        briefing.RawFoodCount > 0 ||
        briefing.ReadyToHarvest > 0 ||
        briefing.WildHarvestCandidates > 0 ||
        CanSuggestHunting(briefing) ||
        FoodCropMath.Recommend(briefing).BestCandidate is not null;

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

    private static AdvicePriority FreezerSupportPriority(FoodBriefing briefing) =>
        briefing.Season.DaysToWinter is < 20 ? AdvicePriority.High : AdvicePriority.Medium;

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
            briefing.FoodUnits >= briefing.ColonistCount * 20)
            return "surplus freezer";

        return days >= 20f ? "buffer freezer" : "starter freezer";
    }

    private static int FreezerTargetDays(FoodBriefing briefing, float days)
    {
        if (briefing.Season.DaysToWinter is < 20)
            return 45;

        if (days >= 20f ||
            briefing.ReadyToHarvest >= briefing.ColonistCount * 6 ||
            briefing.FoodUnits >= briefing.ColonistCount * 20)
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

    private static RuleTraceDetails DiagnosticsFor(FoodBriefing briefing, string selectedRule)
    {
        List<RuleTraceEntry> matches = RuleMatches(briefing);
        int selectedIndex = matches.FindIndex(match =>
            string.Equals(match.Rule, selectedRule, StringComparison.OrdinalIgnoreCase));

        if (selectedIndex < 0)
        {
            matches.Add(new RuleTraceEntry(selectedRule, "selected", FallbackRuleReason(selectedRule, briefing)));
            selectedIndex = matches.Count - 1;
        }

        List<RuleTraceEntry> annotated = new(matches.Count);
        for (int i = 0; i < matches.Count; i++)
        {
            RuleTraceEntry match = matches[i];
            string outcome = i == selectedIndex
                ? SelectedOutcome(selectedRule)
                : i > selectedIndex
                    ? "suppressed"
                    : "matched";
            annotated.Add(match with { Outcome = outcome });
        }

        IReadOnlyList<RuleTraceEntry> suppressed = annotated
            .Where(match => string.Equals(match.Outcome, "suppressed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new RuleTraceDetails(selectedRule, annotated, suppressed)
        {
            AllRules = AllRuleEvaluations(annotated)
        };
    }

    private static IReadOnlyList<RuleEvaluationTrace> AllRuleEvaluations(IReadOnlyList<RuleTraceEntry> annotated)
    {
        Dictionary<string, RuleTraceEntry> outcomes = annotated.ToDictionary(
            entry => entry.Rule,
            StringComparer.OrdinalIgnoreCase);

        return
        [
            RuleEvaluation("nutrition_signal_gap", outcomes,
                "EstimatedDaysOfFood is null; UnclassifiedFoodUnits > 0",
                "ManageFoodStockpile advice; stockpile visibility action/request"),
            RuleEvaluation("unknown_food_state", outcomes,
                "EstimatedDaysOfFood is null; UnclassifiedFoodUnits == 0",
                "FoodSecurity advice; visible reachable stockpile action/request"),
            RuleEvaluation("emergency_food_flag", outcomes,
                "EstimatedDaysOfFood < 7",
                "FoodSecurity advice; immediate food-chain actions; typed flag requests; high/critical flag"),
            RuleEvaluation("harvest_mature_crops", outcomes,
                "ReadyToHarvest > 0",
                "HarvestNow advice; mark_harvest action; optional PlantCut labor/freezer support"),
            RuleEvaluation("meals_understocked", outcomes,
                "MealsCount < ColonistCount * 2; days > 7; RawFoodCount > 0; cook bill or labor needed",
                "ManageCookBills advice; production_bill action; optional Cook labor/freezer requests"),
            RuleEvaluation("wild_harvest_available", outcomes,
                "days < 20; WildHarvestCandidates > 0; ReadyToHarvest == 0",
                "WildHarvest advice; forage mark_harvest action; optional freezer support"),
            RuleEvaluation("hunt_low_risk_animals", outcomes,
                "days < 20; low-risk hunt target visible; ReadyToHarvest == 0",
                "HuntForFood advice; mark_hunt action; optional butcher/cooking/freezer support"),
            RuleEvaluation("expand_growing_capacity", outcomes,
                "days < 20; FoodCropMath best candidate exists; active matching crop coverage is below candidate tile target",
                "ExpandGrowingCapacity advice; designate_zone action; attention/freezer support"),
            RuleEvaluation("hunt_targets_blocked_by_risk", outcomes,
                "days < 20; WildAnimalCount > 0; ReadyToHarvest == 0; hunt advice blocked by safety/risk gate",
                "Escalate to LLM with hunt safety/risk context"),
            RuleEvaluation("winter_food_tradeoff", outcomes,
                "DaysToWinter < 20; days < 30",
                "Escalate to LLM with crop/freezer/labor tradeoff context"),
            RuleEvaluation("freezer_missing", outcomes,
                "Coolers == 0; days >= 20; FoodUnits > 0",
                "ManageFreezer advice; Willie freezer/cooler request"),
            RuleEvaluation("maintain_security_threshold", outcomes,
                "days >= 30",
                "No advice; food security threshold is maintained"),
            RuleEvaluation("unresolved_food_gap", outcomes,
                "days < 30; no deterministic action predicate selected",
                "Escalate to LLM for unresolved Food gap")
        ];
    }

    private static RuleEvaluationTrace RuleEvaluation(
        string rule,
        IReadOnlyDictionary<string, RuleTraceEntry> outcomes,
        string conditions,
        string outputAction)
    {
        if (outcomes.TryGetValue(rule, out RuleTraceEntry? trace))
            return new RuleEvaluationTrace(rule, trace.Outcome, conditions, outputAction, trace.Reason);

        return new RuleEvaluationTrace(rule, "not_matched", conditions, outputAction, null);
    }

    private static List<RuleTraceEntry> RuleMatches(FoodBriefing briefing)
    {
        List<RuleTraceEntry> matches = [];
        if (briefing.EstimatedDaysOfFood is null)
        {
            if (briefing.UnclassifiedFoodUnits > 0)
            {
                matches.Add(Match(
                    "nutrition_signal_gap",
                    $"{briefing.UnclassifiedFoodUnits} unclassified food units exist but days-of-food is unavailable"));
            }
            else
            {
                matches.Add(Match(
                    "unknown_food_state",
                    "food units and days-of-food are both unavailable"));
            }

            return matches;
        }

        float days = briefing.EstimatedDaysOfFood.Value;
        if (days < 7f)
        {
            matches.Add(Match(
                "emergency_food_flag",
                $"food buffer {days:F1}d is below the 7d emergency threshold"));
        }

        if (briefing.ReadyToHarvest > 0)
        {
            matches.Add(Match(
                "harvest_mature_crops",
                $"{briefing.ReadyToHarvest} mature crop tiles are ready"));
        }

        if (briefing.MealsCount < briefing.ColonistCount * 2 &&
            days > 7f &&
            briefing.RawFoodCount > 0 &&
            (ShouldSuggestCookBill(briefing) || ShouldRequestCookingLabor(briefing, days)))
        {
            matches.Add(Match(
                "meals_understocked",
                $"{briefing.MealsCount} meals for {briefing.ColonistCount} colonists while raw food exists"));
        }

        if (days < 20f && briefing.WildHarvestCandidates > 0 && briefing.ReadyToHarvest == 0)
        {
            matches.Add(Match(
                "wild_harvest_available",
                $"{briefing.WildHarvestCandidates} forage candidates are visible below the 20d target"));
        }

        if (days < 20f && CanSuggestHunting(briefing) && briefing.ReadyToHarvest == 0)
        {
            matches.Add(Match(
                "hunt_low_risk_animals",
                $"{briefing.WildHuntTargets.Count} low-risk hunt target summaries are visible"));
        }

        FoodCropCandidate? cropCandidate = FoodCropMath.Recommend(briefing).BestCandidate;
        if (days < 20f && cropCandidate is not null && ShouldRecommendNewGrowingZone(briefing, cropCandidate))
        {
            matches.Add(Match(
                "expand_growing_capacity",
                $"{cropCandidate.Label} can add about {cropCandidate.ProjectedDaysAdded:F1}d from {cropCandidate.Tiles} tiles"));
        }

        if (ShouldEscalateHuntTargetsBlockedByRisk(briefing, days))
        {
            matches.Add(Match(
                "hunt_targets_blocked_by_risk",
                HuntTargetsBlockedByRiskReason(briefing)));
        }

        if (briefing.Season.DaysToWinter is < 20 && days < 30f)
        {
            matches.Add(Match(
                "winter_food_tradeoff",
                $"{briefing.Season.DaysToWinter}d to winter with {days:F1}d food"));
        }

        if (briefing.Infrastructure.Coolers == 0 && days >= 20f && briefing.FoodUnits > 0)
        {
            matches.Add(Match(
                "freezer_missing",
                $"{briefing.FoodUnits} food units exist but no cooler is visible"));
        }

        if (days >= 30f)
        {
            matches.Add(Match(
                "maintain_security_threshold",
                $"food buffer {days:F1}d meets the 30d security threshold"));
        }

        return matches;
    }

    private static RuleTraceEntry Match(string rule, string reason) =>
        new(rule, "matched", reason);

    private static string SelectedOutcome(string selectedRule) =>
        selectedRule is "hunt_targets_blocked_by_risk" or "winter_food_tradeoff" or "unresolved_food_gap"
            ? "escalated"
            : "selected";

    private static string FallbackRuleReason(string selectedRule, FoodBriefing briefing) =>
        selectedRule == "unresolved_food_gap" && briefing.EstimatedDaysOfFood is float days
            ? $"food buffer {days:F1}d is below target, but no deterministic action predicate matched"
            : "selected rule did not have a matching predicate entry";

    private static AdvicePriority FoodBufferPriority(FoodBriefing briefing, float days)
    {
        bool noImmediateLocalFood = briefing.MealsCount == 0 &&
                                    briefing.RawFoodCount == 0 &&
                                    briefing.ReadyToHarvest == 0 &&
                                    briefing.WildHarvestCandidates == 0;
        return days < 1f && noImmediateLocalFood ? AdvicePriority.Critical : AdvicePriority.High;
    }

    private static BuildingRequest StockpileVisibilityRequest(
        string request,
        string reason,
        int? quantity,
        AdvicePriority? priority) =>
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

    private static string? FirstForbiddenFoodDef(FoodBriefing briefing) =>
        briefing.UnclassifiedFoodItems
            .Where(item => item.IsForbidden)
            .Select(item => item.Def)
            .FirstOrDefault(def => !string.IsNullOrWhiteSpace(def));

    private static FoodFlagRequests EmergencyRequests(FoodBriefing briefing, float days, AdvicePriority priority)
    {
        FoodFlagRequests requests = FoodFlagRequests.Empty;
        int forbiddenMealCount = ForbiddenMealCount(briefing);
        if (forbiddenMealCount > 0)
            requests = requests.Add(new ItemRequest(
                $"{forbiddenMealCount} forbidden {ForbiddenMealLabel(briefing, forbiddenMealCount)}",
                "visible meals are forbidden and excluded from the reachable food buffer",
                ItemDef: FirstForbiddenFoodDef(briefing),
                Quantity: forbiddenMealCount,
                Priority: priority));
        if (briefing.UnknownFoodUnits > 0 && briefing.MealsCount == 0 && briefing.RawFoodCount == 0)
            requests = requests.Add(StockpileVisibilityRequest(
                $"reachable stockpile visibility for {briefing.UnknownFoodUnits} unknown food units",
                "unknown_food_units exists, but no meal or raw-food category is visible to Food",
                quantity: briefing.UnknownFoodUnits,
                priority: priority));
        if (briefing.ReadyToHarvest > 0 || briefing.WildHarvestCandidates > 0)
            requests = requests.Add(new LaborRequest(
                "PlantCut work today",
                "food buffer is below 7 days and harvestable food exists",
                WorkType.PlantCut,
                Skill: "Plants",
                Priority: priority,
                RequestedFrom: "Labor"));
        if (CanSuggestHunting(briefing))
        {
            requests = requests.Add(new LaborRequest(
                "Hunt work for selected animals",
                "food buffer is below 7 days and low-risk wild animals are visible",
                WorkType.Hunt,
                Skill: "Shooting",
                Priority: priority,
                RequestedFrom: "Labor"));
            if (!briefing.Kitchen.HasButcherTable)
                requests = requests.Add(new BuildingRequest(
                    "butcher table for hunted animals",
                    "hunted animals must be butchered before they become usable meals",
                    BuildingClass.ProductionBench,
                    TargetDef: "TableButcher",
                    RoomClass: RoomClass.Butcher,
                    Priority: priority,
                    RequestedFrom: "Willie"));
        }
        FoodCropCandidate? cropCandidate = FoodCropMath.Recommend(briefing).BestCandidate;
        if (cropCandidate is not null && ShouldRecommendNewGrowingZone(briefing, cropCandidate))
        {
            requests = requests.Add(new AttentionRequest(
                $"{cropCandidate.Tiles} emergency food growing tiles",
                "food buffer is below 7 days and the growing window is still open",
                Priority: priority,
                RequestedFrom: "Willie"));
            requests = requests.Add(new LaborRequest(
                "Grow work for emergency food zone",
                "new food growing tiles only help once sown",
                WorkType.Grow,
                Skill: "Plants",
                Priority: priority,
                RequestedFrom: "Labor"));
        }
        if (!briefing.Kitchen.HasCookingBuilding)
            requests = requests.Add(new BuildingRequest(
                "campfire or stove for simple meals",
                "the food chain cannot turn raw food into meals without a cooking building",
                BuildingClass.ProductionBench,
                TargetDef: "Campfire",
                RoomClass: RoomClass.Kitchen,
                Priority: priority,
                RequestedFrom: "Willie"));
        if (briefing.RawFoodCount > 0)
        {
            requests = requests.Add(new AttentionRequest(
                $"cook simple meals until {SimpleMealTarget(briefing)}",
                "raw food must become meals during an urgent shortage",
                Priority: priority,
                RequestedFrom: MinisterName));
            requests = requests.Add(new LaborRequest(
                "Cook work today",
                "raw food must become meals during an urgent shortage",
                WorkType.Cook,
                Skill: "Cooking",
                Priority: priority,
                RequestedFrom: "Labor"));
        }
        if (requests.Count == 0)
            requests = requests.Add(new AttentionRequest(
                "emergency food acquisition path",
                "no stored, harvestable, cookable, or sowable food path is visible in the briefing",
                Priority: priority,
                RequestedFrom: "Mayor"));
        return requests.Add(FreezerSupportRequests(briefing, days, HasPotentialPerishableFoodPath(briefing)));
    }

    private static IReadOnlyList<AdviceAction> EmergencyActions(FoodBriefing briefing, float days)
    {
        List<AdviceAction> actions = [];
        int forbiddenMealCount = ForbiddenMealCount(briefing);
        if (forbiddenMealCount > 0)
            actions.Add(new AdviceAction(
                AdviceActionKind.Unforbid,
                ForbiddenMealActionText(briefing, forbiddenMealCount),
                Quantity: forbiddenMealCount,
                Apply: UnforbidApply(briefing)));
        if (briefing.UnknownFoodUnits > 0 && briefing.MealsCount == 0 && briefing.RawFoodCount == 0)
            actions.Add(new AdviceAction(
                AdviceActionKind.SetStockpileZone,
                $"Make {briefing.UnknownFoodUnits} unknown food units visible in a reachable food stockpile; treat the buffer as zero if they are not edible.",
                Quantity: briefing.UnknownFoodUnits));
        if (briefing.ReadyToHarvest > 0)
            actions.Add(HarvestAction(briefing, days));
        if (briefing.WildHarvestCandidates > 0)
            actions.Add(WildHarvestAction(briefing, days));
        if (CanSuggestHunting(briefing))
            actions.Add(HuntingAction(briefing));
        if (!briefing.Kitchen.HasCookingBuilding)
            actions.Add(new AdviceAction(
                AdviceActionKind.PlaceBlueprint,
                "Place a campfire or stove so raw food can become meals.",
                Owner: "Willie"));
        if (briefing.RawFoodCount > 0)
        {
            if (ShouldSuggestCookBill(briefing))
            {
                actions.Add(new AdviceAction(
                    AdviceActionKind.ProductionBill,
                    CookBillActionText(briefing),
                    Quantity: SimpleMealTarget(briefing),
                    Apply: CookBillApply(briefing)));
            }
        }
        FoodCropCandidate? cropCandidate = FoodCropMath.Recommend(briefing).BestCandidate;
        if (cropCandidate is not null && ShouldRecommendNewGrowingZone(briefing, cropCandidate))
            actions.Add(GrowingZoneAction(cropCandidate));
        if (actions.Count == 0)
            actions.Add(new AdviceAction(
                AdviceActionKind.Trade,
                "Open an emergency food acquisition path because no stored, harvestable, cookable, or sowable food path is visible.",
                Owner: "Mayor"));
        IReadOnlyList<AdviceAction> freezerActions = FreezerSupportActions(briefing, days, HasPotentialPerishableFoodPath(briefing));
        actions.AddRange(freezerActions);
        int limit = forbiddenMealCount > 0 ? 4 : 3;
        if (freezerActions.Count > 0)
            limit++;
        return actions.Take(limit).ToList();
    }

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
        AdvicePriority priority)
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

    private static FoodFlagRequests HuntingRequests(FoodBriefing briefing, AdvicePriority priority)
    {
        FoodFlagRequests requests = FoodFlagRequests.Labor(
            new LaborRequest(
                "Hunt work for selected animals",
                "low-risk wild animals are the best visible local food-acquisition path",
                WorkType.Hunt,
                Skill: "Shooting",
                Priority: priority,
                RequestedFrom: "Labor"));
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
        return requests;
    }

    private static IReadOnlyList<AdviceAction> HuntingActions(FoodBriefing briefing)
    {
        List<AdviceAction> actions =
        [
            HuntingAction(briefing)
        ];
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
        new(AdviceActionKind.DesignateZone,
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

    private static AdviceActionApply? UnforbidApply(FoodBriefing briefing)
    {
        IReadOnlyList<FoodUnforbidTarget> targets = briefing.UnforbidTargets
            .Where(target => string.Equals(target.Kind, "meal", StringComparison.OrdinalIgnoreCase))
            .Take(AssistedApplyLimits.MaxUnforbidTargets)
            .ToList();
        if (targets.Count == 0)
            return null;

        int count = targets.Sum(target => target.Count);
        string label = count == 1 ? "Unforbid meal" : "Unforbid meals";
        string summary = $"{count} {ForbiddenMealLabel(briefing, count)} across {targets.Count} stack{(targets.Count == 1 ? "" : "s")}";

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
            ? $"{sentence} {forbiddenMealCount} forbidden {ForbiddenMealLabel(briefing, forbiddenMealCount)} are visible but not counted as reachable food."
            : sentence;
    }

    private static string FoodRemainderBody(FoodBriefing briefing)
    {
        if (briefing.UnknownFoodUnits > 0 && briefing.ExcludedFoodUnits > 0)
        {
            return $"RIMAPI reports {briefing.UnknownFoodUnits} unknown food units plus {briefing.ExcludedFoodUnits} food units excluded from the reachable buffer. Make the unknown items visible as reachable meals or raw food before treating them as a safe buffer.";
        }

        if (briefing.UnknownFoodUnits > 0)
        {
            return $"RIMAPI reports {briefing.UnknownFoodUnits} unknown food units but no usable nutrition signal. Make those items visible as reachable meals or raw food before treating this as a safe buffer.";
        }

        return $"RIMAPI reports {briefing.ExcludedFoodUnits} food units excluded from the reachable buffer and no usable nutrition signal. Make them reachable before treating this as a safe buffer.";
    }

    private static int ForbiddenMealCount(FoodBriefing briefing) =>
        briefing.UnclassifiedFoodItems
            .Where(item => item.IsForbidden && string.Equals(item.Kind, "meal", StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.Count);

    private static string ForbiddenMealActionText(FoodBriefing briefing, int forbiddenMealCount)
    {
        IReadOnlyList<string> positions = briefing.UnclassifiedFoodItems
            .Where(item => item.IsForbidden && string.Equals(item.Kind, "meal", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Position)
            .Where(position => !string.IsNullOrWhiteSpace(position))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Cast<string>()
            .ToList();
        string location = positions.Count == 0 ? "" : $" at {string.Join(", ", positions)}";
        return $"Unforbid {forbiddenMealCount} {ForbiddenMealLabel(briefing, forbiddenMealCount)}{location}; then let haulers bring them into the food stockpile.";
    }

    private static string ForbiddenMealLabel(FoodBriefing briefing, int count)
    {
        FoodUnclassifiedItem? first = briefing.UnclassifiedFoodItems
            .FirstOrDefault(item => item.IsForbidden && string.Equals(item.Kind, "meal", StringComparison.OrdinalIgnoreCase));
        string label = first?.Label ?? "meals";
        if (count == 1 || label.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            return label;
        return $"{label}s";
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

    private static FlagSeverity ToFlagSeverity(AdvicePriority priority) => priority switch
    {
        AdvicePriority.Critical => FlagSeverity.Critical,
        AdvicePriority.High => FlagSeverity.High,
        AdvicePriority.Medium => FlagSeverity.Medium,
        _ => FlagSeverity.Low
    };

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
        IReadOnlyList<AttentionRequest> Attention)
    {
        public static FoodFlagRequests Empty => new([], [], [], []);

        public int Count =>
            BuildingRequests.Count +
            LaborRequests.Count +
            ItemRequests.Count +
            Attention.Count;

        public IReadOnlyList<BuildingRequest>? BuildingRequestsOrNull => NullIfEmpty(BuildingRequests);
        public IReadOnlyList<LaborRequest>? LaborRequestsOrNull => NullIfEmpty(LaborRequests);
        public IReadOnlyList<ItemRequest>? ItemRequestsOrNull => NullIfEmpty(ItemRequests);
        public IReadOnlyList<AttentionRequest>? AttentionOrNull => NullIfEmpty(Attention);

        public static FoodFlagRequests Building(BuildingRequest request) =>
            Empty.Add(request);

        public static FoodFlagRequests Labor(LaborRequest request) =>
            Empty.Add(request);

        public static FoodFlagRequests AttentionRequest(
            string request,
            string reason,
            int? quantity,
            AdvicePriority? priority,
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
                Attention.Concat(other.Attention).ToArray());

        public FoodFlagRequests Add(BuildingRequest request) =>
            this with { BuildingRequests = BuildingRequests.Append(request).ToArray() };

        public FoodFlagRequests Add(LaborRequest request) =>
            this with { LaborRequests = LaborRequests.Append(request).ToArray() };

        public FoodFlagRequests Add(ItemRequest request) =>
            this with { ItemRequests = ItemRequests.Append(request).ToArray() };

        public FoodFlagRequests Add(AttentionRequest request) =>
            this with { Attention = Attention.Append(request).ToArray() };

        private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T> values) =>
            values.Count == 0 ? null : values;
    }
}
