using System.Text.Json.Serialization;
using RimBob.Core.Aggregates;

namespace RimBob.Core.Briefings;

public sealed record FoodBriefing(
    long BriefingVersion,
    GameDate Date,
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
    IReadOnlyList<WildHuntTarget> WildHuntTargets,
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
    public int MapId { get; init; }

    public FoodGrowingTerrainSummary GrowingTerrain { get; init; } = FoodGrowingTerrainSummary.Unknown;

    public IReadOnlyDictionary<string, float> CropHarvestNutritionByDef { get; init; } =
        new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

    public int UnclassifiedFoodUnits => Math.Max(0, FoodUnits - MealsCount - RawFoodCount);

    public IReadOnlyList<FoodUnclassifiedItem> UnclassifiedFoodItems { get; init; } = [];

    public IReadOnlyList<FoodUnforbidTarget> UnforbidTargets { get; init; } = [];

    public IReadOnlyList<FoodHarvestTarget> HarvestTargets { get; init; } = [];

    public IReadOnlyList<FoodHuntTarget> HuntTargets { get; init; } = [];

    public IReadOnlyList<FoodHuntRiskSummary> HuntRiskSummaries { get; init; } = [];

    public int ExcludedFoodUnits =>
        Math.Min(UnclassifiedFoodUnits, UnclassifiedFoodItems.Sum(item => Math.Max(0, item.Count)));

    public int UnknownFoodUnits => Math.Max(0, UnclassifiedFoodUnits - ExcludedFoodUnits);

    [JsonIgnore]
    public bool UsesFallbackNutrition =>
        ReportedNutrition is null &&
        FallbackNutrition is > 0f &&
        (MealsCount > 0 || RawFoodCount > 0);

    public IReadOnlyList<string> MissingBriefingSignals
    {
        get
        {
            List<string> signals = [];
            if (!DataCoverage.HasLiveState)
                signals.Add("live_state");
            if (UsesFallbackNutrition)
                signals.Add(FoodNutrition.MissingSignalRimApiTotalNutritionMissing);
            if (UnknownFoodUnits > 0)
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
            if (Infrastructure.Coolers > 0 && Storage.UnpositionedFoodUnits > 0)
                signals.Add("stored_food_positions");
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

public sealed record FoodUnclassifiedItem(
    string Def,
    string? Label,
    int Count,
    string Kind,
    bool IsForbidden,
    string Source,
    string? Position
);

public sealed record FoodUnforbidTarget(
    string Id,
    string Def,
    string? Label,
    int Count,
    string Kind,
    string Source,
    MapPosition Position
);

public sealed record FoodHarvestTarget(
    string Source,
    string Def,
    int Count,
    MapRect Rect,
    IReadOnlyList<string> PlantIds,
    string? ZoneId,
    string? Proximity,
    string? Reference
);

public sealed record FoodHuntTarget(
    string Def,
    int Count,
    MapRect Rect,
    IReadOnlyList<string> AnimalIds,
    string? Proximity,
    string? Reference
)
{
    public string Risk { get; init; } = "low";

    public float? EstimatedNutrition { get; init; }

    public string? ScoreReason { get; init; }
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

public sealed record FoodGrowingTerrainSummary(
    bool HasTerrain,
    int GrowableCells,
    float? BestFertility,
    float? AverageFertility,
    IReadOnlyList<FoodTerrainFertilityBand> FertilityBands
)
{
    public static FoodGrowingTerrainSummary Unknown { get; } = new(false, 0, null, null, []);
}

public sealed record FoodTerrainFertilityBand(
    string Def,
    string? Label,
    float Fertility,
    int Cells
);

public sealed record WildHarvestCluster(
    string Def,
    int Count,
    float AverageGrowth,
    string? Proximity,
    string? Reference
);

public sealed record WildHuntTarget(
    string Def,
    int Count,
    string? Proximity,
    string? Reference
)
{
    public string Risk { get; init; } = "low";

    public float? EstimatedNutrition { get; init; }

    public string? ScoreReason { get; init; }
}

public sealed record FoodHuntRiskSummary(
    string Def,
    int Count,
    string Risk,
    float? EstimatedNutrition,
    string? Reason
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
)
{
    public int PositionedFoodUnits { get; init; }

    public int CoolerAdjacentFoodUnits { get; init; }

    public int UnpositionedFoodUnits { get; init; }

    public int OtherPositionedFoodUnits => Math.Max(0, PositionedFoodUnits - CoolerAdjacentFoodUnits);

    public bool HasFoodPlacementSignal => PositionedFoodUnits > 0 || UnpositionedFoodUnits > 0;
}

public sealed record FoodKitchenSummary(
    int CookingBuildings,
    int ButcherTables,
    bool HasCookingBuilding,
    bool HasButcherTable
)
{
    public IReadOnlyList<string> CookingBuildingIds { get; init; } = [];
    public IReadOnlyList<FoodCookingBuildingSummary> CookingBuildingDetails { get; init; } = [];
    public IReadOnlyList<FoodCookingBillSummary> CookingBills { get; init; } = [];

    public string? SingleCookingBuildingId
    {
        get
        {
            if (CookingBuildingIds.Count == 1)
                return CookingBuildingIds[0];
            return CookingBuildingIds.Count == 0 && CookingBuildingDetails.Count == 1
                ? CookingBuildingDetails[0].Id
                : null;
        }
    }

    public FoodCookingBuildingSummary? SingleCookingBuilding =>
        CookingBuildingDetails.Count == 1 ? CookingBuildingDetails[0] : null;
}

public sealed record FoodCookingBuildingSummary(
    string Id,
    string Def,
    string? Label,
    MapPosition? Position
);

public sealed record FoodCookingBillSummary(
    string WorkbenchBuildingId,
    int LoadId,
    string? RecipeDefName,
    string? RecipeLabel,
    bool Suspended,
    bool Paused,
    string? RepeatMode,
    int RepeatCount,
    int TargetCount
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

    public bool HasItemFoodClassification { get; init; }

    public bool HasTerrainFertility { get; init; }
}

public static class FoodNutrition
{
    public const float NutritionPerColonistPerDay = 1.6f;
    public const float NutritionPerMeal = 0.9f;
    public const float NutritionPerRawFood = 0.05f;
    public const string NutritionSourceReported = "reported";
    public const string NutritionSourceFallbackMealRawCounts = "fallback_meal_raw_counts";
    public const string NutritionSourceUnknown = "unknown";
    // Stable confidence-gap key when RIMAPI total_nutrition is zero/missing and
    // fallback nutrition supports food days.
    public const string MissingSignalRimApiTotalNutritionMissing = "rimapi_total_nutrition_missing";

    public static float EstimateFallback(int mealsCount, int rawFoodCount) =>
        mealsCount * NutritionPerMeal + rawFoodCount * NutritionPerRawFood;
}
