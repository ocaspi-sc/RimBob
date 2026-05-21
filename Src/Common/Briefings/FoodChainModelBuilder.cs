using RimBob.Core.Advice;

namespace RimBob.Core.Briefings;

public static class FoodChainModelBuilder
{
    public static AdviceChainModel Build(FoodBriefing briefing, IReadOnlyList<AdviceItem> advice)
    {
        if (!briefing.DataCoverage.HasLiveState)
            return AdviceChainModel.Empty;

        FoodChainActionTargets targets = FoodChainActionTargets.From(advice);

        return new AdviceChainModel(
        [
            new AdviceChainPath("Grow path",
            [
                TriggerStep("grow.trigger", briefing),
                GrowZoneStep(briefing, targets),
                GrowHarvestStep(briefing, targets),
                CookStep("grow.cook", briefing, targets),
                StoreStep("grow.store", briefing, targets),
                MealsStep("grow.meals", briefing, targets)
            ]),
            new AdviceChainPath("Hunt path",
            [
                TriggerStep("hunt.trigger", briefing),
                HuntStep(briefing, targets),
                ButcherStep(briefing, targets),
                CookStep("hunt.cook", briefing, targets),
                StoreStep("hunt.store", briefing, targets),
                MealsStep("hunt.meals", briefing, targets)
            ]),
            new AdviceChainPath("Forage path",
            [
                TriggerStep("forage.trigger", briefing),
                ForageStep(briefing, targets),
                CookStep("forage.cook", briefing, targets),
                StoreStep("forage.store", briefing, targets),
                MealsStep("forage.meals", briefing, targets)
            ])
        ]);
    }

    private static AdviceChainStep TriggerStep(string key, FoodBriefing briefing)
    {
        AdviceChainStepStatus status = briefing.EstimatedDaysOfFood is null || briefing.EstimatedDaysOfFood < 20f
            ? AdviceChainStepStatus.Trigger
            : AdviceChainStepStatus.Have;

        string detail = briefing.EstimatedDaysOfFood is null
            ? $"days unknown for {Plural(briefing.ColonistCount, "colonist")}"
            : $"{briefing.EstimatedDaysOfFood.Value:F1} days for {Plural(briefing.ColonistCount, "colonist")}";

        return new AdviceChainStep(key, "Food buffer", detail, status);
    }

    private static AdviceChainStep GrowZoneStep(FoodBriefing briefing, FoodChainActionTargets targets)
    {
        AdviceChainStepStatus status;
        string detail;

        if (briefing.CropZoneSummaries.Count > 0)
        {
            int cropTiles = briefing.CropZoneSummaries.Sum(crop => crop.Count);
            status = AdviceChainStepStatus.Have;
            detail = $"{Plural(cropTiles, "crop tile")} in {Plural(briefing.CropZoneSummaries.Count, "growing area")}";
        }
        else if (briefing.CropBreakdown.Count > 0)
        {
            int cropTiles = briefing.CropBreakdown.Sum(crop => crop.Count);
            status = AdviceChainStepStatus.Have;
            detail = $"{Plural(cropTiles, "crop plant")} visible";
        }
        else if (briefing.GrowingTerrain.HasTerrain && briefing.GrowingTerrain.GrowableCells > 0)
        {
            status = AdviceChainStepStatus.Available;
            detail = $"{Plural(briefing.GrowingTerrain.GrowableCells, "growable cell")} visible";
        }
        else
        {
            status = AdviceChainStepStatus.Blocked;
            detail = "no food growing area visible";
        }

        return Step("grow.zone", "Grow", detail, status, targets.Has("grow.zone"));
    }

    private static AdviceChainStep GrowHarvestStep(FoodBriefing briefing, FoodChainActionTargets targets)
    {
        if (briefing.ReadyToHarvest > 0)
        {
            string detail = $"{Plural(briefing.ReadyToHarvest, "tile")} ready";
            return Step("grow.harvest", "Harvest crops", detail, AdviceChainStepStatus.Available, targets.Has("grow.harvest"));
        }

        FoodCropZoneSummary? closestZone = briefing.CropZoneSummaries.FirstOrDefault();
        if (closestZone is not null)
        {
            string detail = $"{Plural(closestZone.Count, $"{LabelCrop(closestZone.Def)} plant")} at {closestZone.AverageGrowth:P0}";
            return Step("grow.harvest", "Harvest crops", detail, AdviceChainStepStatus.Have, targets.Has("grow.harvest"));
        }

        if (briefing.CropBreakdown.Count > 0)
        {
            FoodCropSummary crop = briefing.CropBreakdown[0];
            string detail = $"{Plural(crop.Count, $"{LabelCrop(crop.Def)} plant")} at {crop.AverageGrowth:P0}";
            return Step("grow.harvest", "Harvest crops", detail, AdviceChainStepStatus.Have, targets.Has("grow.harvest"));
        }

        return Step("grow.harvest", "Harvest crops", "no crop harvest visible", AdviceChainStepStatus.Blocked, targets.Has("grow.harvest"));
    }

    private static AdviceChainStep HuntStep(FoodBriefing briefing, FoodChainActionTargets targets)
    {
        if (briefing.WildHuntTargets.Count > 0)
        {
            WildHuntTarget target = briefing.WildHuntTargets[0];
            string proximity = string.IsNullOrWhiteSpace(target.Proximity) ? "" : $" ({target.Proximity})";
            string detail = $"{Plural(target.Count, LabelAnimal(target.Def))}{proximity}";
            return Step("hunt.hunt", "Hunt", detail, AdviceChainStepStatus.Available, targets.Has("hunt.hunt"));
        }

        if (briefing.WildAnimalCount > 0)
        {
            string detail = $"{Plural(briefing.WildAnimalCount, "wild animal")} visible; risk unresolved";
            return Step("hunt.hunt", "Hunt", detail, AdviceChainStepStatus.Available, targets.Has("hunt.hunt"));
        }

        return Step("hunt.hunt", "Hunt", "no hunt target visible", AdviceChainStepStatus.Blocked, targets.Has("hunt.hunt"));
    }

    private static AdviceChainStep ButcherStep(FoodBriefing briefing, FoodChainActionTargets targets)
    {
        AdviceChainStepStatus status = briefing.Kitchen.ButcherTables > 0
            ? AdviceChainStepStatus.Have
            : AdviceChainStepStatus.Blocked;
        string detail = briefing.Kitchen.ButcherTables > 0
            ? Plural(briefing.Kitchen.ButcherTables, "butcher table")
            : "no butcher table visible";
        return Step("hunt.butcher", "Butcher", detail, status, targets.Has("hunt.butcher"));
    }

    private static AdviceChainStep ForageStep(FoodBriefing briefing, FoodChainActionTargets targets)
    {
        if (briefing.WildHarvestClusters.Count > 0)
        {
            WildHarvestCluster cluster = briefing.WildHarvestClusters[0];
            string proximity = string.IsNullOrWhiteSpace(cluster.Proximity) ? "" : $" ({cluster.Proximity})";
            string detail = $"{Plural(cluster.Count, $"{LabelCrop(cluster.Def)} plant")}{proximity}";
            return Step("forage.harvest", "Forage", detail, AdviceChainStepStatus.Available, targets.Has("forage.harvest"));
        }

        if (briefing.WildHarvestCandidates > 0)
        {
            string detail = Plural(briefing.WildHarvestCandidates, "forage candidate");
            return Step("forage.harvest", "Forage", detail, AdviceChainStepStatus.Available, targets.Has("forage.harvest"));
        }

        return Step("forage.harvest", "Forage", "no edible wild plants visible", AdviceChainStepStatus.Blocked, targets.Has("forage.harvest"));
    }

    private static AdviceChainStep CookStep(string key, FoodBriefing briefing, FoodChainActionTargets targets)
    {
        AdviceChainStepStatus status;
        string detail;
        FoodCookingBillSummary? simpleMealBill = SimpleMealBill(briefing);

        if (!briefing.Kitchen.HasCookingBuilding)
        {
            status = AdviceChainStepStatus.Blocked;
            detail = "no cooking station visible";
        }
        else if (simpleMealBill is not null && !simpleMealBill.Suspended)
        {
            status = AdviceChainStepStatus.Have;
            detail = $"simple meal bill to {simpleMealBill.TargetCount}";
        }
        else
        {
            status = AdviceChainStepStatus.Available;
            detail = $"{Plural(briefing.Kitchen.CookingBuildings, "cooking station")}; bill not confirmed";
        }

        return Step(key, "Cook", detail, status, targets.Has("cook"));
    }

    private static AdviceChainStep StoreStep(string key, FoodBriefing briefing, FoodChainActionTargets targets)
    {
        AdviceChainStepStatus status;
        string detail;

        if (briefing.Storage.StockpileZones <= 0 && briefing.Storage.StockpileCells <= 0)
        {
            status = AdviceChainStepStatus.Blocked;
            detail = "no food stockpile visible";
        }
        else if (briefing.Infrastructure.Coolers <= 0)
        {
            status = AdviceChainStepStatus.Blocked;
            detail = $"{Plural(briefing.Storage.StockpileZones, "stockpile zone")}; no cooler";
        }
        else if (briefing.Storage.CoolerAdjacentFoodUnits > 0)
        {
            status = AdviceChainStepStatus.Have;
            detail = $"{Plural(briefing.Storage.CoolerAdjacentFoodUnits, "food unit")} near coolers";
        }
        else
        {
            status = AdviceChainStepStatus.Available;
            detail = $"{Plural(briefing.Storage.StockpileZones, "stockpile zone")}; {Plural(briefing.Infrastructure.Coolers, "cooler")}";
        }

        return Step(key, "Store", detail, status, targets.Has("store"));
    }

    private static AdviceChainStep MealsStep(string key, FoodBriefing briefing, FoodChainActionTargets targets)
    {
        int targetMeals = briefing.ColonistCount * 2;
        AdviceChainStepStatus status = briefing.MealsCount >= targetMeals
            ? AdviceChainStepStatus.Have
            : briefing.MealsCount > 0 || briefing.RawFoodCount > 0
                ? AdviceChainStepStatus.Available
                : AdviceChainStepStatus.Blocked;

        string detail = $"{Plural(briefing.MealsCount, "meal")} / target {targetMeals}";
        return Step(key, "Meals", detail, status, targets.Has("meals"));
    }

    private static AdviceChainStep Step(
        string key,
        string label,
        string detail,
        AdviceChainStepStatus status,
        bool isAction) =>
        new(key, label, detail, isAction ? AdviceChainStepStatus.Action : status);

    private static FoodCookingBillSummary? SimpleMealBill(FoodBriefing briefing) =>
        briefing.Kitchen.CookingBills.FirstOrDefault(candidate =>
            string.Equals(candidate.RecipeDefName, "CookMealSimple", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.RecipeDefName, "CookMealSimpleBulk", StringComparison.OrdinalIgnoreCase));

    private static string LabelCrop(string def)
    {
        string label = def;
        if (label.StartsWith("Plant_", StringComparison.OrdinalIgnoreCase))
            label = label["Plant_".Length..];

        label = label.Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(label) ? "crop" : label.ToLowerInvariant();
    }

    private static string LabelAnimal(string def)
    {
        string label = def;
        if (label.StartsWith("Animal_", StringComparison.OrdinalIgnoreCase))
            label = label["Animal_".Length..];

        label = label.Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(label) ? "animal" : label.ToLowerInvariant();
    }

    private static string Plural(int count, string singular)
    {
        string label = count == 1 ? singular : Pluralize(singular);
        return $"{count} {label}";
    }

    private static string Pluralize(string singular)
    {
        if (singular.EndsWith("y", StringComparison.OrdinalIgnoreCase))
            return $"{singular[..^1]}ies";

        return $"{singular}s";
    }

    private sealed class FoodChainActionTargets
    {
        private readonly HashSet<string> _steps = new(StringComparer.OrdinalIgnoreCase);

        public static FoodChainActionTargets From(IReadOnlyList<AdviceItem> advice)
        {
            FoodChainActionTargets targets = new();
            foreach (AdviceItem item in advice)
            {
                targets.AddAdviceType(item.AdviceType);
                foreach (AdviceAction action in item.Actions)
                    targets.AddAction(item.AdviceType, action);
            }

            return targets;
        }

        public bool Has(string step) => _steps.Contains(step);

        private void AddAdviceType(string adviceType)
        {
            string canonical = Canonical(adviceType);
            if (canonical == "harvestnow")
                _steps.Add("grow.harvest");
            else if (canonical == "wildharvest")
                _steps.Add("forage.harvest");
            else if (canonical == "huntforfood")
                _steps.Add("hunt.hunt");
            else if (canonical == "expandgrowingcapacity")
                _steps.Add("grow.zone");
            else if (canonical is "managecookbills" or "managebutcherbills")
                _steps.Add("cook");
            else if (canonical is "managefreezer" or "managefoodstockpile")
                _steps.Add("store");
        }

        private void AddAction(string adviceType, AdviceAction action)
        {
            string canonicalAdviceType = Canonical(adviceType);
            switch (action.Kind)
            {
                case AdviceActionKind.MarkHarvest:
                    _steps.Add(canonicalAdviceType == "wildharvest" ? "forage.harvest" : "grow.harvest");
                    break;
                case AdviceActionKind.MarkHunt:
                    _steps.Add("hunt.hunt");
                    break;
                case AdviceActionKind.DesignateZone:
                    _steps.Add("grow.zone");
                    break;
                case AdviceActionKind.ProductionBill:
                    _steps.Add(canonicalAdviceType == "managebutcherbills" ? "hunt.butcher" : "cook");
                    break;
                case AdviceActionKind.PlaceBlueprint:
                    AddBlueprintTarget(canonicalAdviceType, action);
                    break;
                case AdviceActionKind.SetPriority:
                    AddWorkTarget(action.WorkType);
                    break;
                case AdviceActionKind.SetStockpileZone:
                    _steps.Add("store");
                    break;
                case AdviceActionKind.Unforbid:
                    _steps.Add("meals");
                    break;
            }
        }

        private void AddBlueprintTarget(string canonicalAdviceType, AdviceAction action)
        {
            if (canonicalAdviceType == "managefreezer" || IsColdStorageBlueprint(action))
                _steps.Add("store");
            else if (canonicalAdviceType == "managebutcherbills" || canonicalAdviceType == "huntforfood")
                _steps.Add("hunt.butcher");
            else
                _steps.Add("cook");
        }

        private static bool IsColdStorageBlueprint(AdviceAction action)
        {
            string text = $"{action.Instruction} {action.Reason}".ToLowerInvariant();
            return text.Contains("freezer", StringComparison.Ordinal) ||
                   text.Contains("cold storage", StringComparison.Ordinal) ||
                   text.Contains("cold-room", StringComparison.Ordinal) ||
                   text.Contains("cooler", StringComparison.Ordinal);
        }

        private void AddWorkTarget(WorkType? workType)
        {
            if (workType is WorkType.Cook)
                _steps.Add("cook");
            else if (workType is WorkType.Hunt)
                _steps.Add("hunt.hunt");
            else if (workType is WorkType.Grow)
                _steps.Add("grow.zone");
            else if (workType is WorkType.PlantCut)
            {
                _steps.Add("grow.harvest");
                _steps.Add("forage.harvest");
            }
        }

        private static string Canonical(string value) =>
            value.Replace("_", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal)
                .Trim()
                .ToLowerInvariant();
    }
}
