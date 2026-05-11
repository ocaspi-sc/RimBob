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
    int WildHarvestCandidates,
    int WildAnimalCount,
    int StockpileCells,
    FoodSkillSnapshot Skills,
    FoodInfrastructureSnapshot Infrastructure,
    bool ActiveThreat,
    IReadOnlyList<string> RecentFoodIncidents
) : IBriefing;

public sealed record FoodCropSummary(string Def, int Count, float AverageGrowth);

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

public static class FoodNutrition
{
    public const float NutritionPerColonistPerDay = 1.6f;
    public const float NutritionPerMeal = 0.9f;
    public const float NutritionPerRawFood = 0.05f;

    public static float EstimateFallback(int mealsCount, int rawFoodCount) =>
        mealsCount * NutritionPerMeal + rawFoodCount * NutritionPerRawFood;
}
