using System.Text.Json.Serialization;

namespace RimAI.Core.Briefings;

public sealed record FoodBriefing(
    long BriefingVersion,
    DateStamp Date,
    long GameTick,
    SeasonContext Season,
    int ColonistCount,
    float? ReportedNutrition,
    float? FallbackNutrition,
    string NutritionSource,
    float? EstimatedDaysOfFood,
    int FoodUnits,
    int MealsCount,
    int RawFoodCount,
    int ReadyToHarvest,
    IReadOnlyList<FoodCropSummary> CropBreakdown,
    IReadOnlyList<FoodCropZoneSummary> CropZoneSummaries,
    int WildHarvestCandidates,
    IReadOnlyList<WildHarvestCluster> WildHarvestClusters,
    int WildAnimalCount,
    int StockpileCells,
    FoodSkillSnapshot Skills,
    FoodInfrastructureSnapshot Infrastructure,
    FoodStorageSummary Storage,
    FoodKitchenSummary Kitchen,
    FoodDataCoverage DataCoverage,
    bool ActiveThreat,
    IReadOnlyList<string> RecentFoodIncidents
) : IBriefing
{
    public int UnclassifiedFoodUnits => Math.Max(0, FoodUnits - MealsCount - RawFoodCount);

    public IReadOnlyList<string> MissingBriefingSignals
    {
        get
        {
            List<string> signals = [];
            if (!DataCoverage.HasLiveState)
                signals.Add("live_state");
            if (UnclassifiedFoodUnits > 0)
                signals.Add("food_unit_classification");
            if ((ReadyToHarvest > 0 || WildHarvestCandidates > 0 || CropBreakdown.Count > 0) &&
                !DataCoverage.HasPlantPositions)
                signals.Add("plant_positions");
            if (WildAnimalCount > 0 && !DataCoverage.HasAnimalPositions)
                signals.Add("animal_positions");
            if (StockpileCells > 0 && !DataCoverage.HasZoneCells)
                signals.Add("zone_cells");
            if ((Kitchen.CookingBuildings > 0 || Kitchen.ButcherTables > 0 || Infrastructure.Coolers > 0) &&
                !DataCoverage.HasBuildingPositions)
                signals.Add("building_positions");
            return signals;
        }
    }

    public IReadOnlyList<string> UnimplementedBriefingSignals
    {
        get
        {
            List<string> signals = [];
            if (!DataCoverage.HasWorkPriorities)
                signals.Add("work_priorities");
            if (!DataCoverage.HasTradeAvailability)
                signals.Add("trade_availability");
            return signals;
        }
    }
}

public sealed record FoodCropSummary(string Def, int Count, float AverageGrowth);

public sealed record FoodCropZoneSummary(
    string Def,
    string? ZoneId,
    int Count,
    float AverageGrowth,
    int ReadyCount,
    string? Proximity
);

public sealed record WildHarvestCluster(
    string Def,
    int Count,
    float AverageGrowth,
    string? Proximity,
    string? Reference
);

public sealed record FoodSkillSnapshot(
    int BestPlants,
    int QualifiedGrowers,
    int BestCooking,
    int QualifiedCooks
);

public sealed record FoodInfrastructureSnapshot(
    int Coolers,
    bool PowerNetPositive,
    float NetPowerW,
    int FoodStockpileZones
);

public sealed record FoodStorageSummary(
    int StockpileZones,
    int StockpileCells,
    int? NearestKitchenDistanceCells,
    string? NearestKitchenProximity
);

public sealed record FoodKitchenSummary(
    int CookingBuildings,
    int ButcherTables,
    bool HasCookingBuilding,
    bool HasButcherTable
);

public sealed record FoodDataCoverage(
    bool HasPlantPositions,
    bool HasAnimalPositions,
    bool HasZoneCells,
    bool HasBuildingPositions,
    bool HasWorkPriorities,
    bool HasTradeAvailability
)
{
    public bool HasLiveState { get; init; }
}

public static class FoodNutrition
{
    public const float NutritionPerColonistPerDay = 1.6f;
    public const float NutritionPerMeal = 0.9f;
    public const float NutritionPerRawFood = 0.05f;

    public static float EstimateFallback(int mealsCount, int rawFoodCount) =>
        mealsCount * NutritionPerMeal + rawFoodCount * NutritionPerRawFood;
}
