using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;

namespace RimBob.Ministers.Food;

public sealed class Rules : IMinisterRules<FoodBriefing>
{
    private const string MinisterName = "Food";
    private const string Domain = "food";
    private const string SimpleMealRecipeSelector = "simple_meal";
    private const string BillRepeatModeTargetCount = "TargetCount";
    private static readonly IReadOnlyList<string> SimpleMealRecipeDefs = ["CookMealSimple", "CookMealSimpleBulk"];
    private static readonly IconRef CampfireIcon = ItemIcon("Campfire");
    private static readonly IconRef CoolerIcon = ItemIcon("Cooler");
    private static readonly IconRef SimpleMealIcon = ItemIcon("MealSimple");

    public RulesResult Evaluate(FoodBriefing briefing, ColonyContext context)
    {
        if (briefing.EstimatedDaysOfFood is null)
        {
            if (briefing.UnclassifiedFoodUnits > 0)
                return DecisionFor(briefing, "nutrition_signal_gap",
                    FoodAdviceType.ManageFoodStockpile,
                    AdvicePriority.Medium,
                    "Food stockpile categories need verification",
                    FoodRemainderBody(briefing),
                    "Missing nutrition would make days-of-food unreliable; use the fallback counts until upstream data is fixed.",
                    [new(AdviceActionKind.SetStockpileZone, "Confirm unknown or excluded food is edible and reachable before counting it as buffer.", Reason: "food units outside meals/raw-food counts cannot be converted into days-of-food", Icon: SimpleMealIcon)],
                    [new(ResourceRequestKind.StockpileSpace, "reachable food stockpile visibility", "food units outside meals/raw-food counts cannot be converted into days-of-food", Icon: SimpleMealIcon)],
                    false);

            return DecisionFor(briefing, "unknown_food_state",
                FoodAdviceType.FoodSecurity,
                AdvicePriority.High,
                "Food state unknown",
                "No reliable food stockpile signal is available. Treat this as a food-security check, not confirmed starvation.",
                "The food chain cannot safely decide without stockpile visibility.",
                [new(AdviceActionKind.SetStockpileZone, "Create or expose a reachable food stockpile, then refresh RimBob once food is visible.", Reason: "food_units and nutrition are both unavailable", Icon: SimpleMealIcon)],
                [new(ResourceRequestKind.StockpileSpace, "visible reachable food stockpile", "food_units and nutrition are both unavailable", Icon: SimpleMealIcon)],
                true);
        }

        float days = briefing.EstimatedDaysOfFood.Value;

        if (days < 7f)
        {
            AdvicePriority priority = FoodBufferPriority(briefing, days);
            return DecisionFor(briefing, "emergency_food_flag",
                FoodAdviceType.FoodSecurity,
                priority,
                "Food crisis within a week",
                EmergencyBody(briefing, days),
                "Food below 7 days is an urgent survival risk. Food owns the next food-chain actions; cross-minister requests carry only the build, tile, and labor needs.",
                EmergencyActions(briefing),
                EmergencyRequests(briefing, priority),
                true);
        }

        if (briefing.ReadyToHarvest > 0)
        {
            AdvicePriority priority = days < 15f ? AdvicePriority.High : AdvicePriority.Medium;
            return DecisionFor(briefing, "harvest_mature_crops",
                FoodAdviceType.HarvestNow,
                priority,
                "Mature crops are ready",
                $"{briefing.ReadyToHarvest} crop tiles are ready to harvest. Pull them in before weather, rot, or task drift wastes the buffer.",
                "Mature crops are a deterministic food-chain opportunity.",
                [HarvestAction(briefing, days)],
                PlantLaborIfNeeded(briefing, days, "PlantCut work for ready crops", "mature crops only help once harvested"),
                days < 15f);
        }

        if (briefing.MealsCount < briefing.ColonistCount * 2 &&
            days > 7f &&
            briefing.RawFoodCount > 0 &&
            (ShouldSuggestCookBill(briefing) || ShouldRequestCookingLabor(briefing, days)))
        {
            return DecisionFor(briefing, "meals_understocked",
                FoodAdviceType.ManageCookBills,
                AdvicePriority.Medium,
                "Cooked meals are understocked",
                $"Only {briefing.MealsCount} meals are reported for {briefing.ColonistCount} colonists while raw food exists.",
                "A raw-food buffer still needs cooking throughput to become safe daily nutrition.",
                CookBillActions(briefing, days),
                [],
                false);
        }

        if (days < 20f && briefing.WildHarvestCandidates > 0 && briefing.ReadyToHarvest == 0)
            return DecisionFor(briefing, "wild_harvest_available",
                FoodAdviceType.WildHarvest,
                days < 10f ? AdvicePriority.High : AdvicePriority.Medium,
                "Forage can extend the buffer",
                WildHarvestBody(briefing, days),
                "Foraging edible plants is lower-risk than hunting when no mature crops are ready.",
                [WildHarvestAction(briefing, days)],
                PlantLaborIfNeeded(briefing, days, "PlantCut work for forage", "edible forage requires plant work"),
                days < 10f);

        if (days < 20f && CanSuggestHunting(briefing) && briefing.ReadyToHarvest == 0)
        {
            AdvicePriority priority = days < 10f ? AdvicePriority.High : AdvicePriority.Medium;
            return DecisionFor(briefing, "hunt_low_risk_animals",
                FoodAdviceType.HuntForFood,
                priority,
                "Mark low-risk animals for hunting",
                HuntingBody(briefing, days),
                "The briefing has healthy wild animals and no lower-risk harvest path; hunting is a concrete local food-acquisition action.",
                HuntingActions(briefing),
                HuntingRequests(briefing, priority),
                days < 10f);
        }

        FoodCropCandidate? cropCandidate = FoodCropMath.Recommend(briefing).BestCandidate;
        if (days < 20f && cropCandidate is not null)
            return DecisionFor(briefing, "expand_growing_capacity",
                FoodAdviceType.ExpandGrowingCapacity,
                days < 12f ? AdvicePriority.High : AdvicePriority.Medium,
                "Expand food growing capacity",
                $"Food covers about {days:F1} days and {cropCandidate.Label} still fits the growing window. Add a compact food crop zone instead of waiting for hunting or trade.",
                "A concrete growing-zone action is more actionable than a vague labor request; cross-minister flags carry tile needs separately.",
                [GrowingZoneAction(cropCandidate, "current food buffer is below the 20-day safety band")],
                [
                    new(ResourceRequestKind.Tile,
                        $"{cropCandidate.Tiles} {cropCandidate.Label} growing tiles near fertile soil and food storage",
                        cropCandidate.Reason,
                        Quantity: cropCandidate.Tiles,
                        RequestedFrom: "Construction",
                        Icon: ItemIcon(cropCandidate.CropDef))
                ],
                days < 12f);

        if (days < 20f && briefing.WildAnimalCount > 0 && briefing.ReadyToHarvest == 0)
            return new Escalate(
                "Food below 20 days with possible hunting path; target risk/value needs judgment.",
                new { briefing.WildAnimalCount, briefing.ActiveThreat, briefing.Skills.BestCooking, briefing.HuntRiskSummaries },
                DiagnosticsFor(briefing, "hunting_ambiguity"));

        if (briefing.Season.DaysToWinter is < 20 && days < 30f)
            return new Escalate(
                "Winter is close and food buffer is below target; crop/freezer/labor tradeoff needs guide-grounded judgment.",
                new { briefing.Season.DaysToWinter, DaysOfFood = days, briefing.CropBreakdown },
                DiagnosticsFor(briefing, "winter_food_tradeoff"));

        if (briefing.Infrastructure.Coolers == 0 && days >= 20f && briefing.FoodUnits > 0)
            return DecisionFor(briefing, "freezer_missing",
                FoodAdviceType.ManageFreezer,
                AdvicePriority.Medium,
                "Food storage needs freezer support",
                "Food exists but no cooler is visible. Preserve surplus before warm weather or large harvests.",
                "The Food minister owns freezer need; Construction owns the actual build work.",
                [new(AdviceActionKind.PlaceBlueprint, "Plan a freezer/cold-room upgrade near food storage.", Owner: "Construction", Reason: "stored food can spoil without temperature control", Icon: CoolerIcon)],
                [new(ResourceRequestKind.Building, "cooler-backed freezer or cold room", "stored food can spoil without temperature control", RequestedFrom: "Construction", Icon: CoolerIcon)],
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
        FoodAdviceType type,
        AdvicePriority priority,
        string title,
        string body,
        string rationale,
        IReadOnlyList<AdviceAction> actions,
        IReadOnlyList<ResourceRequest> requests,
        bool emitFlag)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string adviceType = ToSnakeCase(type.ToString());
        AdviceItem advice = new(
            Id: $"{MinisterName.ToLowerInvariant()}_{trace}",
            Minister: MinisterName,
            AdviceType: adviceType,
            Priority: priority,
            Title: title,
            Body: body,
            Rationale: rationale,
            Actions: actions,
            GuideCitationIds: [],
            IssuedAt: now,
            ExpiresAt: now.AddHours(priority >= AdvicePriority.High ? 4 : 24),
            IssuedInGameTick: FormatTick(briefing),
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
                Requests: requests,
                Detail: trace,
                ExpiresAt: now.AddHours(24))]
            : [];

        RuleTraceDetails diagnostics = DiagnosticsFor(briefing, trace)
            .WithEmissions("rules", trace, [advice], flags);
        return new Decision([advice], flags, trace, diagnostics);
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

        return new RuleTraceDetails(selectedRule, annotated, suppressed);
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
        if (days < 20f && cropCandidate is not null)
        {
            matches.Add(Match(
                "expand_growing_capacity",
                $"{cropCandidate.Label} can add about {cropCandidate.ProjectedDaysAdded:F1}d from {cropCandidate.Tiles} tiles"));
        }

        if (days < 20f && briefing.WildAnimalCount > 0 && briefing.ReadyToHarvest == 0)
        {
            matches.Add(Match(
                "hunting_ambiguity",
                $"{briefing.WildAnimalCount} wild animals exist but target risk/value needs judgment"));
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
        selectedRule is "hunting_ambiguity" or "winter_food_tradeoff" or "unresolved_food_gap"
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

    private static IReadOnlyList<ResourceRequest> EmergencyRequests(FoodBriefing briefing, AdvicePriority priority)
    {
        List<ResourceRequest> requests = [];
        int forbiddenMealCount = ForbiddenMealCount(briefing);
        if (forbiddenMealCount > 0)
            requests.Add(new ResourceRequest(ResourceRequestKind.Item,
                $"{forbiddenMealCount} forbidden {ForbiddenMealLabel(briefing, forbiddenMealCount)}",
                "visible meals are forbidden and excluded from the reachable food buffer",
                Quantity: forbiddenMealCount,
                Priority: priority));
        if (briefing.UnknownFoodUnits > 0 && briefing.MealsCount == 0 && briefing.RawFoodCount == 0)
            requests.Add(new ResourceRequest(ResourceRequestKind.StockpileSpace,
                $"reachable stockpile visibility for {briefing.UnknownFoodUnits} unknown food units",
                "unknown_food_units exists, but no meal or raw-food category is visible to Food",
                Quantity: briefing.UnknownFoodUnits,
                Priority: priority,
                Icon: SimpleMealIcon));
        if (briefing.ReadyToHarvest > 0 || briefing.WildHarvestCandidates > 0)
            requests.Add(new ResourceRequest(ResourceRequestKind.Labor,
                "PlantCut work today",
                "food buffer is below 7 days and harvestable food exists",
                Priority: priority,
                RequestedFrom: "Labor",
                WorkType: WorkType.PlantCut,
                Skill: "Plants",
                Icon: briefing.ReadyToHarvest > 0 ? HarvestIcon(briefing) : WildHarvestIcon(briefing)));
        if (CanSuggestHunting(briefing))
        {
            requests.Add(new ResourceRequest(ResourceRequestKind.Labor,
                "Hunt work for selected animals",
                "food buffer is below 7 days and low-risk wild animals are visible",
                Priority: priority,
                RequestedFrom: "Labor",
                WorkType: WorkType.Hunt,
                Skill: "Shooting"));
            if (!briefing.Kitchen.HasButcherTable)
                requests.Add(new ResourceRequest(ResourceRequestKind.Building,
                    "butcher table for hunted animals",
                    "hunted animals must be butchered before they become usable meals",
                    Priority: priority,
                    RequestedFrom: "Construction"));
        }
        FoodCropCandidate? cropCandidate = FoodCropMath.Recommend(briefing).BestCandidate;
        if (cropCandidate is not null)
        {
            requests.Add(new ResourceRequest(ResourceRequestKind.Tile,
                $"{cropCandidate.Tiles} emergency food growing tiles",
                "food buffer is below 7 days and the growing window is still open",
                Quantity: cropCandidate.Tiles,
                Priority: priority,
                RequestedFrom: "Construction",
                Icon: ItemIcon(cropCandidate.CropDef)));
            requests.Add(new ResourceRequest(ResourceRequestKind.Labor,
                "Grow work for emergency food zone",
                "new food growing tiles only help once sown",
                Priority: priority,
                RequestedFrom: "Labor",
                WorkType: WorkType.Grow,
                Skill: "Plants",
                Icon: ItemIcon(cropCandidate.CropDef)));
        }
        if (!briefing.Kitchen.HasCookingBuilding)
            requests.Add(new ResourceRequest(ResourceRequestKind.Building,
                "campfire or stove for simple meals",
                "the food chain cannot turn raw food into meals without a cooking building",
                Priority: priority,
                RequestedFrom: "Construction",
                Icon: CampfireIcon));
        if (briefing.RawFoodCount > 0)
        {
            requests.Add(new ResourceRequest(ResourceRequestKind.Bill,
                $"cook simple meals until {SimpleMealTarget(briefing)}",
                "raw food must become meals during an urgent shortage",
                Priority: priority,
                Icon: SimpleMealIcon));
            requests.Add(new ResourceRequest(ResourceRequestKind.Labor,
                "Cook work today",
                "raw food must become meals during an urgent shortage",
                Priority: priority,
                RequestedFrom: "Labor",
                WorkType: WorkType.Cook,
                Skill: "Cooking",
                Icon: SimpleMealIcon));
        }
        if (requests.Count == 0)
            requests.Add(new ResourceRequest(ResourceRequestKind.TradeCapacity,
                "emergency food acquisition path",
                "no stored, harvestable, cookable, or sowable food path is visible in the briefing",
                Priority: priority,
                RequestedFrom: "Mayor"));
        return requests;
    }

    private static IReadOnlyList<AdviceAction> EmergencyActions(FoodBriefing briefing)
    {
        List<AdviceAction> actions = [];
        int forbiddenMealCount = ForbiddenMealCount(briefing);
        if (forbiddenMealCount > 0)
            actions.Add(new AdviceAction(
                AdviceActionKind.Unforbid,
                ForbiddenMealActionText(briefing, forbiddenMealCount),
                Quantity: forbiddenMealCount,
                Reason: "visible meals are forbidden and excluded from the reachable food buffer",
                Icon: SimpleMealIcon,
                Apply: UnforbidApply(briefing)));
        if (briefing.UnknownFoodUnits > 0 && briefing.MealsCount == 0 && briefing.RawFoodCount == 0)
            actions.Add(new AdviceAction(
                AdviceActionKind.SetStockpileZone,
                $"Make {briefing.UnknownFoodUnits} unknown food units visible in a reachable food stockpile; treat the buffer as zero if they are not edible.",
                Quantity: briefing.UnknownFoodUnits,
                Reason: "unknown_food_units exists, but no meal or raw-food category is visible to Food",
                Icon: SimpleMealIcon));
        if (briefing.ReadyToHarvest > 0)
            actions.Add(HarvestAction(briefing, briefing.EstimatedDaysOfFood ?? 0f));
        if (briefing.WildHarvestCandidates > 0)
            actions.Add(WildHarvestAction(briefing, briefing.EstimatedDaysOfFood ?? 0f));
        if (CanSuggestHunting(briefing))
            actions.Add(HuntingAction(briefing));
        if (!briefing.Kitchen.HasCookingBuilding)
            actions.Add(new AdviceAction(
                AdviceActionKind.PlaceBlueprint,
                "Place a campfire or stove so raw food can become meals.",
                Owner: "Construction",
                Reason: "the food chain cannot turn raw food into meals without a cooking building",
                Icon: CampfireIcon));
        if (briefing.RawFoodCount > 0)
        {
            if (ShouldSuggestCookBill(briefing))
            {
                actions.Add(new AdviceAction(
                    AdviceActionKind.ProductionBill,
                    CookBillActionText(briefing),
                    Quantity: SimpleMealTarget(briefing),
                    Reason: "raw food must become meals during an urgent shortage",
                    Icon: SimpleMealIcon,
                    Apply: CookBillApply(briefing)));
            }
            actions.Add(new AdviceAction(
                AdviceActionKind.SetPriority,
                "Put the best cook on Cook work until simple meals are stocked.",
                Owner: "Labor",
                WorkType: WorkType.Cook,
                Skill: "Cooking",
                Reason: "raw food must become meals during an urgent shortage",
                Icon: SimpleMealIcon));
        }
        FoodCropCandidate? cropCandidate = FoodCropMath.Recommend(briefing).BestCandidate;
        if (cropCandidate is not null)
            actions.Add(GrowingZoneAction(
                cropCandidate,
                "food buffer is below 7 days and the growing window is still open",
                includeCandidateReason: false));
        if (actions.Count == 0)
            actions.Add(new AdviceAction(
                AdviceActionKind.Trade,
                "Open an emergency food acquisition path because no stored, harvestable, cookable, or sowable food path is visible.",
                Owner: "Mayor",
                Reason: "no local food path is visible in the briefing"));
        int limit = forbiddenMealCount > 0 ? 4 : 3;
        return actions.Take(limit).ToList();
    }

    private static IReadOnlyList<AdviceAction> CookBillActions(FoodBriefing briefing, float days)
    {
        List<AdviceAction> actions = [];
        if (ShouldSuggestCookBill(briefing))
            actions.Add(new AdviceAction(AdviceActionKind.ProductionBill,
                CookBillActionText(briefing),
                Quantity: SimpleMealTarget(briefing),
                Reason: "meal count is below two per colonist",
                Icon: SimpleMealIcon,
                Apply: CookBillApply(briefing)));
        if (!briefing.Kitchen.HasCookingBuilding)
            actions.Add(new AdviceAction(
                AdviceActionKind.PlaceBlueprint,
                "Place a campfire or stove before relying on cooked-meal advice.",
                Owner: "Construction",
                Reason: "raw food cannot become meals without a cooking building",
                Icon: CampfireIcon));
        if (ShouldRequestCookingLabor(briefing, days))
            actions.Add(new AdviceAction(
                AdviceActionKind.SetPriority,
                "Put the best cook on Cook work until simple meals are stocked.",
                Owner: "Labor",
                WorkType: WorkType.Cook,
                Skill: "Cooking",
                Reason: "raw food has to become meals and cook coverage is urgent or weak",
                Icon: SimpleMealIcon));
        return actions;
    }

    private static IReadOnlyList<ResourceRequest> HuntingRequests(FoodBriefing briefing, AdvicePriority priority)
    {
        List<ResourceRequest> requests =
        [
            new ResourceRequest(ResourceRequestKind.Labor,
                "Hunt work for selected animals",
                "low-risk wild animals are the best visible local food-acquisition path",
                Priority: priority,
                RequestedFrom: "Labor",
                WorkType: WorkType.Hunt,
                Skill: "Shooting")
        ];
        if (!briefing.Kitchen.HasButcherTable)
            requests.Add(new ResourceRequest(ResourceRequestKind.Building,
                "butcher table for hunted animals",
                "hunting only helps the food chain after animals can be butchered",
                Priority: priority,
                RequestedFrom: "Construction"));
        if (!briefing.Kitchen.HasCookingBuilding)
            requests.Add(new ResourceRequest(ResourceRequestKind.Building,
                "campfire or stove for meat meals",
                "meat must be cooked into safe meals once butchered",
                Priority: priority,
                RequestedFrom: "Construction"));
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
                Owner: "Construction",
                Reason: "hunted animals must be butchered before they become usable meals"));
        if (!briefing.Kitchen.HasCookingBuilding)
            actions.Add(new AdviceAction(
                AdviceActionKind.PlaceBlueprint,
                "Place a campfire or stove so butchered meat can become meals.",
                Owner: "Construction",
                Reason: "meat must be cooked into safe meals once butchered",
                Icon: CampfireIcon));
        return actions.Take(3).ToList();
    }

    private static IReadOnlyList<ResourceRequest> PlantLaborIfNeeded(FoodBriefing briefing, float days, string what, string why) =>
        ShouldRequestPlantLabor(briefing, days)
            ? [new ResourceRequest(ResourceRequestKind.Labor, what, why, RequestedFrom: "Labor", WorkType: WorkType.PlantCut, Skill: "Plants", Icon: HarvestIcon(briefing))]
            : [];

    private static AdviceAction HarvestAction(FoodBriefing briefing, float days) =>
        new(AdviceActionKind.MarkHarvest,
            HarvestActionText(briefing),
            Owner: ShouldRequestPlantLabor(briefing, days) ? "Labor" : null,
            WorkType: WorkType.PlantCut,
            Skill: "Plants",
            Reason: "mature crops only help once harvested",
            Icon: HarvestIcon(briefing),
            Apply: HarvestApply(briefing, "crop"));

    private static AdviceAction WildHarvestAction(FoodBriefing briefing, float days) =>
        new(AdviceActionKind.MarkHarvest,
            WildHarvestActionText(briefing),
            Owner: ShouldRequestPlantLabor(briefing, days) ? "Labor" : null,
            WorkType: WorkType.PlantCut,
            Skill: "Plants",
            Reason: "edible forage requires plant work",
            Icon: WildHarvestIcon(briefing),
            Apply: HarvestApply(briefing, "wild"));

    private static AdviceAction HuntingAction(FoodBriefing briefing) =>
        new(AdviceActionKind.MarkHunt,
            HuntingActionText(briefing),
            Owner: "Labor",
            WorkType: WorkType.Hunt,
            Skill: "Shooting",
            Reason: HuntingReason(briefing),
            Icon: HuntingIcon(briefing),
            Apply: HuntApply(briefing));

    private static AdviceAction GrowingZoneAction(
        FoodCropCandidate candidate,
        string reason,
        bool includeCandidateReason = true) =>
        new(AdviceActionKind.DesignateZone,
            $"Create about {candidate.Tiles} emergency {candidate.Label} growing tiles; use fertile soil near storage when possible.",
            Quantity: candidate.Tiles,
            Owner: "Food",
            WorkType: WorkType.Grow,
            Skill: "Plants",
            Reason: includeCandidateReason ? $"{reason}; {candidate.Reason}" : reason,
            Icon: ItemIcon(candidate.CropDef));

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

        return new AdviceActionApply(
            Kind: AdviceApplyKind.MarkHarvestArea,
            Label: label,
            TargetSummary: summary,
            MapId: briefing.MapId,
            TargetCount: target.Count,
            Rect: target.Rect,
            TargetIds: target.PlantIds);
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

        return new AdviceActionApply(
            Kind: AdviceApplyKind.MarkHuntArea,
            Label: "Mark hunt",
            TargetSummary: summary,
            MapId: briefing.MapId,
            TargetCount: target.Count,
            Rect: target.Rect,
            TargetIds: target.AnimalIds);
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

        return new AdviceActionApply(
            Kind: AdviceApplyKind.UnforbidThings,
            Label: label,
            TargetSummary: summary,
            MapId: briefing.MapId,
            TargetCount: targets.Count,
            ThingIds: targets.Select(target => target.Id).ToList(),
            ThingTargets: targets.Select(target => new AdviceThingApplyTarget(
                Id: target.Id,
                Def: target.Def,
                Kind: target.Kind,
                Source: target.Source,
                Position: target.Position)).ToList());
    }

    private static AdviceActionApply? CookBillApply(FoodBriefing briefing)
    {
        if (briefing.RawFoodCount <= 0 || !briefing.Kitchen.HasCookingBuilding)
            return null;

        string? workbenchId = briefing.Kitchen.SingleCookingBuildingId;
        if (string.IsNullOrWhiteSpace(workbenchId))
            return null;

        int target = Math.Min(SimpleMealTarget(briefing), AssistedApplyLimits.MaxProductionBillTarget);
        return new AdviceActionApply(
            Kind: AdviceApplyKind.UpsertProductionBill,
            Label: "Set simple meal bill",
            TargetSummary: $"simple meal bill on {CookingStationTarget(briefing)} until {target} meals",
            MapId: briefing.MapId,
            TargetCount: target,
            WorkbenchBuildingId: workbenchId,
            RecipeSelectorKey: SimpleMealRecipeSelector,
            RepeatMode: BillRepeatModeTargetCount);
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

    private static IconRef HarvestIcon(FoodBriefing briefing) =>
        ItemIcon(FirstNonBlank(
            briefing.CropZoneSummaries
                .Where(zone => zone.ReadyCount > 0)
                .OrderByDescending(zone => zone.ReadyCount)
                .Select(zone => zone.Def),
            briefing.CropBreakdown
                .OrderByDescending(crop => crop.Count)
                .Select(crop => crop.Def))
            ?? "Plant_Rice");

    private static IconRef WildHarvestIcon(FoodBriefing briefing) =>
        ItemIcon(FirstNonBlank(briefing.WildHarvestClusters.Select(cluster => cluster.Def)) ?? "Plant_Berry");

    private static IconRef HuntingIcon(FoodBriefing briefing) =>
        ItemIcon(FirstNonBlank(briefing.WildHuntTargets.Select(target => target.Def)) ?? "Animal");

    private static string? FirstNonBlank(params IEnumerable<string?>[] groups)
    {
        foreach (IEnumerable<string?> group in groups)
        {
            string? value = group.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    private static IconRef ItemIcon(string defName) => new("item", defName);

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

    private static string HuntingReason(FoodBriefing briefing)
    {
        WildHuntTarget target = briefing.WildHuntTargets[0];
        if (!string.IsNullOrWhiteSpace(target.ScoreReason))
            return $"low-risk wild animals are the best visible local food-acquisition path; {target.ScoreReason}";

        return "low-risk wild animals are the best visible local food-acquisition path";
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

    private static string FormatTick(FoodBriefing b) =>
        $"Y{b.Date.Year ?? 0}{b.Date.Quadrum ?? "?"}D{b.Date.Day ?? 0}";

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
}
