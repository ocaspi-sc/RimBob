using RimAI.Core.Advice;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;

namespace RimAI.Ministers.Food;

public sealed class Rules : IMinisterRules<FoodBriefing>
{
    private const string MinisterName = "Food";
    private const string Domain = "food";

    public RulesResult Evaluate(FoodBriefing briefing, ColonyContext context)
    {
        if (briefing.EstimatedDaysOfFood is null)
        {
            if (briefing.UnclassifiedFoodUnits > 0)
                return DecisionFor(briefing, "nutrition_signal_gap",
                    FoodAdviceType.ManageFoodStockpile,
                    AdvicePriority.Medium,
                    "Food stockpile categories need verification",
                    $"RIMAPI reports {briefing.UnclassifiedFoodUnits} unclassified food units but no usable nutrition signal. Make those items visible as reachable meals or raw food before treating this as a safe buffer.",
                    "Missing nutrition would make days-of-food unreliable; use the fallback counts until upstream data is fixed.",
                    [new(ResourceRequestKind.StockpileSpace, "reachable food stockpile visibility", "unclassified food units cannot be converted into days-of-food")],
                    [new(SuggestedActionKind.SetStockpileZone, "Confirm the stored food is edible and reachable; move it into a visible food stockpile if needed.")],
                    false);

            return DecisionFor(briefing, "unknown_food_state",
                FoodAdviceType.FoodSecurity,
                AdvicePriority.High,
                "Food state unknown",
                "No reliable food stockpile signal is available. Treat this as a food-security check, not confirmed starvation.",
                "The food chain cannot safely decide without stockpile visibility.",
                [new(ResourceRequestKind.StockpileSpace, "visible reachable food stockpile", "food_units and nutrition are both unavailable")],
                [new(SuggestedActionKind.SetStockpileZone, "Create or expose a reachable food stockpile, then refresh RimAI once food is visible.")],
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
                "Food below 7 days is an urgent survival risk. Food owns the next food-chain steps; cross-minister requests carry only the build, tile, and labor needs.",
                EmergencyRequests(briefing, priority),
                EmergencyActions(briefing),
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
                PlantLaborIfNeeded(briefing, days, "PlantCut work for ready crops", "mature crops only help once harvested"),
                [new(SuggestedActionKind.MarkHarvest, HarvestActionText(briefing))],
                days < 15f);
        }

        if (briefing.MealsCount < briefing.ColonistCount * 2 && days > 7f && briefing.RawFoodCount > 0)
        {
            IReadOnlyList<ResourceRequest> requests = CookBillRequests(briefing, days);
            IReadOnlyList<SuggestedAction> actions = CookBillActions(briefing);
            return DecisionFor(briefing, "meals_understocked",
                FoodAdviceType.ManageCookBills,
                AdvicePriority.Medium,
                "Cooked meals are understocked",
                $"Only {briefing.MealsCount} meals are reported for {briefing.ColonistCount} colonists while raw food exists.",
                "A raw-food buffer still needs cooking throughput to become safe daily nutrition.",
                requests,
                actions,
                false);
        }

        if (days < 20f && briefing.WildHarvestCandidates > 0 && briefing.ReadyToHarvest == 0)
            return DecisionFor(briefing, "wild_harvest_available",
                FoodAdviceType.WildHarvest,
                days < 10f ? AdvicePriority.High : AdvicePriority.Medium,
                "Wild food can extend the buffer",
                WildHarvestBody(briefing, days),
                "Wild harvest is lower-risk than hunting when no mature crops are ready.",
                PlantLaborIfNeeded(briefing, days, "PlantCut work for wild harvest", "wild harvest requires plant work"),
                [new(SuggestedActionKind.MarkHarvest, WildHarvestActionText(briefing))],
                days < 10f);

        if (days < 20f && CanSuggestHunting(briefing) && briefing.ReadyToHarvest == 0)
        {
            AdvicePriority priority = days < 10f ? AdvicePriority.High : AdvicePriority.Medium;
            return DecisionFor(briefing, "hunt_low_risk_animals",
                FoodAdviceType.HuntForFood,
                priority,
                "Mark low-risk animals for hunting",
                HuntingBody(briefing, days),
                "The briefing has healthy wild animals and no lower-risk harvest path; hunting is a concrete local food-acquisition step.",
                HuntingRequests(briefing, priority),
                HuntingActions(briefing),
                days < 10f);
        }

        if (days < 20f && CanSowBeforeWinter(briefing))
            return DecisionFor(briefing, "expand_growing_capacity",
                FoodAdviceType.ExpandGrowingCapacity,
                days < 12f ? AdvicePriority.High : AdvicePriority.Medium,
                "Expand food growing capacity",
                $"Food covers about {days:F1} days and the growing window is still open. Add a compact food crop zone instead of waiting for hunting or trade.",
                "A tile request is more actionable than a vague labor request; Construction/Base Layout owns exact placement later.",
                [
                    new(ResourceRequestKind.Tile,
                        $"{GrowingTileRequest(briefing)} food growing tiles near fertile soil and food storage",
                        "current food buffer is below the 20-day safety band",
                        Quantity: GrowingTileRequest(briefing),
                        RequestedFrom: "Construction")
                ],
                [new(SuggestedActionKind.DesignateZone, $"Create or expand a food growing zone by about {GrowingTileRequest(briefing)} tiles; plant rice unless local soil/season makes potatoes safer.")],
                days < 12f);

        if (days < 20f && briefing.WildAnimalCount > 0 && briefing.ReadyToHarvest == 0)
            return new Escalate(
                "Food below 20 days with possible hunting path; target risk/value needs judgment.",
                new { briefing.WildAnimalCount, briefing.ActiveThreat, briefing.Skills.BestCooking });

        if (briefing.Season.DaysToWinter is < 20 && days < 30f)
            return new Escalate(
                "Winter is close and food buffer is below target; crop/freezer/labor tradeoff needs guide-grounded judgment.",
                new { briefing.Season.DaysToWinter, DaysOfFood = days, briefing.CropBreakdown });

        if (briefing.Infrastructure.Coolers == 0 && days >= 20f && briefing.FoodUnits > 0)
            return DecisionFor(briefing, "freezer_missing",
                FoodAdviceType.ManageFreezer,
                AdvicePriority.Medium,
                "Food storage needs freezer support",
                "Food exists but no cooler is visible. Preserve surplus before warm weather or large harvests.",
                "The Food minister owns freezer need; Construction owns the actual build work.",
                [new(ResourceRequestKind.Building, "cooler-backed freezer or cold room", "stored food can spoil without temperature control", RequestedFrom: "Construction")],
                [new(SuggestedActionKind.PlaceBlueprint, "Plan a freezer/cold-room upgrade near food storage.")],
                false);

        if (days >= 30f)
            return new Decision([], [], "maintain_security_threshold");

        return new Escalate(
            "Food state is below ideal but no deterministic rule cleanly chooses the next action.",
            new { DaysOfFood = days, briefing.ReadyToHarvest, briefing.WildHarvestCandidates, briefing.WildAnimalCount });
    }

    private static Decision DecisionFor(
        FoodBriefing briefing,
        string trace,
        FoodAdviceType type,
        AdvicePriority priority,
        string title,
        string body,
        string rationale,
        IReadOnlyList<ResourceRequest> requests,
        IReadOnlyList<SuggestedAction> actions,
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
            ResourceRequests: requests,
            SuggestedActions: actions,
            GuideCitationIds: [],
            IssuedAt: now,
            ExpiresAt: now.AddHours(priority >= AdvicePriority.High ? 4 : 24),
            IssuedInGameTick: FormatTick(briefing),
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

        return new Decision([advice], flags, trace);
    }

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
        if (briefing.UnclassifiedFoodUnits > 0 && briefing.MealsCount == 0 && briefing.RawFoodCount == 0)
            requests.Add(new ResourceRequest(ResourceRequestKind.StockpileSpace,
                $"reachable stockpile visibility for {briefing.UnclassifiedFoodUnits} unclassified food units",
                "unclassified_food_units exists, but no meal or raw-food category is visible to Food",
                Quantity: briefing.UnclassifiedFoodUnits,
                Priority: priority));
        if (briefing.ReadyToHarvest > 0 || briefing.WildHarvestCandidates > 0)
            requests.Add(new ResourceRequest(ResourceRequestKind.Labor,
                "PlantCut work today",
                "food buffer is below 7 days and harvestable food exists",
                Priority: priority,
                RequestedFrom: "Labor",
                WorkType: WorkType.PlantCut,
                Skill: "Plants"));
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
        if (CanSowBeforeWinter(briefing))
        {
            int growingTiles = GrowingTileRequest(briefing);
            requests.Add(new ResourceRequest(ResourceRequestKind.Tile,
                $"{growingTiles} emergency food growing tiles",
                "food buffer is below 7 days and the growing window is still open",
                Quantity: growingTiles,
                Priority: priority,
                RequestedFrom: "Construction"));
            requests.Add(new ResourceRequest(ResourceRequestKind.Labor,
                "Grow work for emergency food zone",
                "new food growing tiles only help once sown",
                Priority: priority,
                RequestedFrom: "Labor",
                WorkType: WorkType.Grow,
                Skill: "Plants"));
        }
        if (!briefing.Kitchen.HasCookingBuilding)
            requests.Add(new ResourceRequest(ResourceRequestKind.Building,
                "campfire or stove for simple meals",
                "the food chain cannot turn raw food into meals without a cooking building",
                Priority: priority,
                RequestedFrom: "Construction"));
        if (briefing.RawFoodCount > 0)
        {
            requests.Add(new ResourceRequest(ResourceRequestKind.Bill,
                $"cook simple meals until {SimpleMealTarget(briefing)}",
                "raw food must become meals during an urgent shortage",
                Priority: priority));
            requests.Add(new ResourceRequest(ResourceRequestKind.Labor,
                "Cook work today",
                "raw food must become meals during an urgent shortage",
                Priority: priority,
                RequestedFrom: "Labor",
                WorkType: WorkType.Cook,
                Skill: "Cooking"));
        }
        if (requests.Count == 0)
            requests.Add(new ResourceRequest(ResourceRequestKind.TradeCapacity,
                "emergency food acquisition path",
                "no stored, harvestable, cookable, or sowable food path is visible in the briefing",
                Priority: priority,
                RequestedFrom: "Mayor"));
        return requests;
    }

    private static IReadOnlyList<SuggestedAction> EmergencyActions(FoodBriefing briefing)
    {
        List<SuggestedAction> actions = [];
        int forbiddenMealCount = ForbiddenMealCount(briefing);
        if (forbiddenMealCount > 0)
            actions.Add(new SuggestedAction(SuggestedActionKind.Unforbid, ForbiddenMealActionText(briefing, forbiddenMealCount)));
        if (briefing.UnclassifiedFoodUnits > 0 && briefing.MealsCount == 0 && briefing.RawFoodCount == 0)
            actions.Add(new SuggestedAction(SuggestedActionKind.SetStockpileZone,
                $"Find the {briefing.UnclassifiedFoodUnits} unclassified food units and make them visible in a reachable food stockpile; if they are not edible, treat the buffer as zero."));
        if (briefing.ReadyToHarvest > 0)
            actions.Add(new SuggestedAction(SuggestedActionKind.MarkHarvest, HarvestActionText(briefing)));
        if (briefing.WildHarvestCandidates > 0)
            actions.Add(new SuggestedAction(SuggestedActionKind.MarkHarvest, WildHarvestActionText(briefing)));
        if (CanSuggestHunting(briefing))
            actions.Add(new SuggestedAction(SuggestedActionKind.MarkHunt, HuntingActionText(briefing)));
        if (!briefing.Kitchen.HasCookingBuilding)
            actions.Add(new SuggestedAction(SuggestedActionKind.PlaceBlueprint, "Place a campfire or stove so raw food can become meals."));
        if (briefing.RawFoodCount > 0)
        {
            actions.Add(new SuggestedAction(SuggestedActionKind.ProductionBill, CookBillActionText(briefing)));
            actions.Add(new SuggestedAction(SuggestedActionKind.SetPriority, "Put the best cook on Cook work until simple meals are stocked."));
        }
        if (CanSowBeforeWinter(briefing))
            actions.Add(new SuggestedAction(SuggestedActionKind.DesignateZone, $"Create about {GrowingTileRequest(briefing)} emergency rice growing tiles; use fertile soil near storage when possible."));
        if (actions.Count == 0)
            actions.Add(new SuggestedAction(SuggestedActionKind.Trade, "Open an emergency food acquisition path because no stored, harvestable, cookable, or sowable food path is visible."));
        int limit = forbiddenMealCount > 0 ? 4 : 3;
        return actions.Take(limit).ToList();
    }

    private static IReadOnlyList<ResourceRequest> CookBillRequests(FoodBriefing briefing, float days)
    {
        List<ResourceRequest> requests =
        [
            new ResourceRequest(ResourceRequestKind.Bill,
                $"cook simple meals until {SimpleMealTarget(briefing)}",
                "meal count is below two per colonist")
        ];
        if (!briefing.Kitchen.HasCookingBuilding)
            requests.Add(new ResourceRequest(ResourceRequestKind.Building,
                "campfire or stove for simple meals",
                "raw food cannot become meals without a cooking building",
                RequestedFrom: "Construction"));
        if (ShouldRequestCookingLabor(briefing, days))
            requests.Add(new ResourceRequest(ResourceRequestKind.Labor,
                "Cook work time",
                "raw food has to become meals and cook coverage is urgent or weak",
                RequestedFrom: "Labor",
                WorkType: WorkType.Cook,
                Skill: "Cooking"));
        return requests;
    }

    private static IReadOnlyList<SuggestedAction> CookBillActions(FoodBriefing briefing)
    {
        List<SuggestedAction> actions =
        [
            new(SuggestedActionKind.ProductionBill, CookBillActionText(briefing))
        ];
        if (!briefing.Kitchen.HasCookingBuilding)
            actions.Add(new SuggestedAction(SuggestedActionKind.PlaceBlueprint, "Place a campfire or stove before relying on cooked-meal advice."));
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

    private static IReadOnlyList<SuggestedAction> HuntingActions(FoodBriefing briefing)
    {
        List<SuggestedAction> actions =
        [
            new SuggestedAction(SuggestedActionKind.MarkHunt, HuntingActionText(briefing))
        ];
        if (!briefing.Kitchen.HasButcherTable)
            actions.Add(new SuggestedAction(SuggestedActionKind.PlaceBlueprint, "Place a butcher table so hunted animals can become meat."));
        if (!briefing.Kitchen.HasCookingBuilding)
            actions.Add(new SuggestedAction(SuggestedActionKind.PlaceBlueprint, "Place a campfire or stove so butchered meat can become meals."));
        return actions.Take(3).ToList();
    }

    private static IReadOnlyList<ResourceRequest> PlantLaborIfNeeded(FoodBriefing briefing, float days, string what, string why) =>
        ShouldRequestPlantLabor(briefing, days)
            ? [new ResourceRequest(ResourceRequestKind.Labor, what, why, RequestedFrom: "Labor", WorkType: WorkType.PlantCut, Skill: "Plants")]
            : [];

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
        string location = string.IsNullOrWhiteSpace(zone.Proximity) ? "" : $" ({zone.Proximity})";
        return $"Mark/prioritize harvest for {zone.ReadyCount} ready {zone.Def} crop tiles{location}.";
    }

    private static string WildHarvestBody(FoodBriefing briefing, float days)
    {
        WildHarvestCluster? cluster = briefing.WildHarvestClusters.FirstOrDefault();
        if (cluster is null)
            return $"{briefing.WildHarvestCandidates} harvestable wild plants are visible while food is at {days:F1} days. Position data is unavailable, so do not assume exact location.";
        return $"{cluster.Count} harvestable {cluster.Def} plants are {cluster.Proximity ?? "visible"} while food is at {days:F1} days.";
    }

    private static string WildHarvestActionText(FoodBriefing briefing)
    {
        WildHarvestCluster? cluster = briefing.WildHarvestClusters.FirstOrDefault();
        if (cluster is null)
            return $"Mark safe edible wild plants for harvest; {briefing.WildHarvestCandidates} candidates are visible but no location summary is available.";
        return $"Mark the nearest {cluster.Count} {cluster.Def} wild plants for harvest ({cluster.Proximity ?? "location unknown"}).";
    }

    private static string HuntingBody(FoodBriefing briefing, float days)
    {
        WildHuntTarget target = briefing.WildHuntTargets[0];
        string location = string.IsNullOrWhiteSpace(target.Proximity)
            ? "with no location summary available"
            : target.Proximity;
        return $"Food covers about {days:F1} days and {target.Count} {LabelDef(target.Def)} are visible {location}. Mark a small, low-risk hunting batch and avoid predators, bonded animals, or anything the colony cannot cover safely.";
    }

    private static string HuntingActionText(FoodBriefing briefing)
    {
        WildHuntTarget target = briefing.WildHuntTargets[0];
        int count = Math.Min(target.Count, Math.Max(1, briefing.ColonistCount * 2));
        string location = string.IsNullOrWhiteSpace(target.Proximity) ? "location unknown" : target.Proximity;
        return $"Mark up to {count} {LabelDef(target.Def)} for hunting ({location}); skip predators, tame/bonded animals, and high-revenge targets.";
    }

    private static string CookBillActionText(FoodBriefing briefing) =>
        $"Set/check simple meal bill target around {SimpleMealTarget(briefing)} meals; keep fine meals off until the buffer is stable.";

    private static string EmergencyBody(FoodBriefing briefing, float days)
    {
        string sentence = $"Food covers about {days:F1} days for {briefing.ColonistCount} colonists. Set up immediate intake, cooking, and growing capacity before routine work.";
        int forbiddenMealCount = ForbiddenMealCount(briefing);
        return forbiddenMealCount > 0
            ? $"{sentence} {forbiddenMealCount} forbidden {ForbiddenMealLabel(briefing, forbiddenMealCount)} are visible but not counted as reachable food."
            : sentence;
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

    private static bool CanSowBeforeWinter(FoodBriefing briefing) =>
        briefing.Season.DaysToWinter is null or > 10;

    private static bool CanSuggestHunting(FoodBriefing briefing) =>
        !briefing.ActiveThreat && briefing.WildHuntTargets.Count > 0;

    private static int GrowingTileRequest(FoodBriefing briefing) =>
        Math.Clamp(briefing.ColonistCount * 12, 12, 72);

    private static string LabelDef(string def)
    {
        string label = def;
        if (label.StartsWith("Animal_", StringComparison.OrdinalIgnoreCase))
            label = label["Animal_".Length..];
        return label.Replace('_', ' ').Trim().ToLowerInvariant();
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
