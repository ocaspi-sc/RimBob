using System.Globalization;
using RimAI.Core.Briefings;

namespace RimAI.Ministers.Food;

public sealed record FoodCropRecommendation(
    IReadOnlyList<FoodCropCandidate> Candidates)
{
    public FoodCropCandidate? BestCandidate => Candidates.FirstOrDefault(candidate => candidate.FitsSeason);
}

public sealed record FoodCropCandidate(
    string CropDef,
    string Label,
    string HarvestedThingDef,
    float BaseGrowDays,
    float GrowDays,
    float? TerrainFertility,
    int HarvestYield,
    int Tiles,
    float ProjectedNutrition,
    float ProjectedDaysAdded,
    int? DaysToWinter,
    float? DaysToWinterMargin,
    bool FitsSeason,
    float ClassificationConfidence,
    float StorageMultiplier,
    float Score,
    string Reason);

public static class FoodCropMath
{
    private const float SeasonSafetyMarginDays = 1f;

    private static readonly IReadOnlyList<FoodCropProfile> Profiles =
    [
        new("Plant_Rice", "rice", "RawRice", 3.0f, 1.0f, 6, 0.15f),
        new("Plant_Potato", "potatoes", "RawPotatoes", 5.8f, 0.4f, 11, 0.35f),
        new("Plant_Corn", "corn", "RawCorn", 11.3f, 1.0f, 22, 0.75f)
    ];

    public static FoodCropRecommendation Recommend(FoodBriefing briefing)
    {
        List<FoodCropCandidate> candidates = Profiles
            .Select(profile => BuildCandidate(profile, briefing))
            .OrderByDescending(candidate => candidate.FitsSeason)
            .ThenByDescending(candidate => candidate.Score)
            .ToList();

        return new FoodCropRecommendation(candidates);
    }

    public static int TileRequest(FoodBriefing briefing) =>
        Math.Clamp(briefing.ColonistCount * 12, 12, 72);

    private static FoodCropCandidate BuildCandidate(FoodCropProfile profile, FoodBriefing briefing)
    {
        int tiles = TileRequest(briefing);
        float? terrainFertility = SelectTerrainFertility(briefing.GrowingTerrain, tiles);
        float growDays = AdjustedGrowDays(profile, terrainFertility);
        float projectedNutrition = tiles * profile.HarvestYield * FoodNutrition.NutritionPerRawFood;
        float projectedDaysAdded = briefing.ColonistCount > 0
            ? projectedNutrition / (FoodNutrition.NutritionPerColonistPerDay * briefing.ColonistCount)
            : 0f;

        float? margin = briefing.Season.DaysToWinter is { } daysToWinter
            ? daysToWinter - growDays - SeasonSafetyMarginDays
            : null;
        bool fitsSeason = margin is null or >= 0f;
        float classificationConfidence = ClassificationConfidence(briefing);
        float storageMultiplier = StorageMultiplier(profile, briefing);
        float score = Score(
            briefing,
            projectedDaysAdded,
            fitsSeason,
            growDays,
            classificationConfidence,
            storageMultiplier);
        string reason = Reason(
            profile,
            briefing,
            fitsSeason,
            margin,
            growDays,
            terrainFertility,
            classificationConfidence,
            storageMultiplier);

        return new FoodCropCandidate(
            CropDef: profile.CropDef,
            Label: profile.Label,
            HarvestedThingDef: profile.HarvestedThingDef,
            BaseGrowDays: profile.GrowDays,
            GrowDays: growDays,
            TerrainFertility: terrainFertility,
            HarvestYield: profile.HarvestYield,
            Tiles: tiles,
            ProjectedNutrition: projectedNutrition,
            ProjectedDaysAdded: projectedDaysAdded,
            DaysToWinter: briefing.Season.DaysToWinter,
            DaysToWinterMargin: margin,
            FitsSeason: fitsSeason,
            ClassificationConfidence: classificationConfidence,
            StorageMultiplier: storageMultiplier,
            Score: score,
            Reason: reason);
    }

    private static float Score(
        FoodBriefing briefing,
        float projectedDaysAdded,
        bool fitsSeason,
        float growDays,
        float classificationConfidence,
        float storageMultiplier)
    {
        if (!fitsSeason) return -1000f;

        float urgencyWeight = briefing.EstimatedDaysOfFood switch
        {
            null => 2f,
            < 7f => 3f,
            < 12f => 2f,
            < 20f => 1.5f,
            _ => 0.5f
        };

        float baseScore = urgencyWeight / growDays + projectedDaysAdded * 0.05f;
        return baseScore * classificationConfidence * storageMultiplier;
    }

    private static string Reason(
        FoodCropProfile profile,
        FoodBriefing briefing,
        bool fitsSeason,
        float? margin,
        float growDays,
        float? terrainFertility,
        float classificationConfidence,
        float storageMultiplier)
    {
        string context = ContextPhrase(profile, terrainFertility, classificationConfidence, storageMultiplier);
        if (!fitsSeason)
        {
            return briefing.Season.DaysToWinter is { } daysToWinter
                ? $"{profile.Label} needs {FormatGrowingDays(growDays)} plus margin{ContextSuffix(context)}; only {FormatDayCount(daysToWinter)} to winter."
                : $"{profile.Label} does not fit the current growing window.";
        }

        if (profile.CropDef == "Plant_Rice" && briefing.EstimatedDaysOfFood is null or < 20f)
            return margin is null
                ? $"fastest food crop{ContextSuffix(context)}."
                : $"fastest food crop with {FormatDayCount(margin.Value)} of winter margin{ContextSuffix(context)}.";

        if (profile.CropDef == "Plant_Corn")
            return margin is null
                ? $"long-season crop adds the most raw nutrition{ContextSuffix(context)}."
                : $"long-season crop fits with {FormatDayCount(margin.Value)} of winter margin{ContextSuffix(context)}.";

        return margin is null
            ? $"middle-speed food crop{ContextSuffix(context)}."
            : $"middle-speed food crop fits with {FormatDayCount(margin.Value)} of winter margin{ContextSuffix(context)}.";
    }

    private static float ClassificationConfidence(FoodBriefing briefing)
    {
        float confidence = briefing.NutritionSource switch
        {
            "item_def_catalog" => 1f,
            "reported" => 0.9f,
            "item_category_counts" => 0.8f,
            "fallback_meal_raw_counts" => 0.75f,
            _ => 0.55f
        };

        if (!briefing.DataCoverage.HasItemFoodClassification)
            confidence = Math.Min(confidence, 0.8f);

        int foodUnits = Math.Max(briefing.FoodUnits, 1);
        float unknownShare = briefing.UnknownFoodUnits / (float)foodUnits;
        float excludedShare = briefing.ExcludedFoodUnits / (float)foodUnits;
        confidence -= Math.Min(0.25f, unknownShare * 0.4f);
        confidence -= Math.Min(0.2f, excludedShare * 0.3f);

        return Math.Clamp(confidence, 0.35f, 1f);
    }

    private static float StorageMultiplier(FoodCropProfile profile, FoodBriefing briefing)
    {
        float storageRisk = StorageRisk(briefing);
        float multiplier = 1f - (storageRisk * profile.SurplusStorageSensitivity);
        return Math.Clamp(multiplier, 0.5f, 1f);
    }

    private static float StorageRisk(FoodBriefing briefing)
    {
        if (briefing.Infrastructure.Coolers <= 0)
            return 0.8f;

        if (!briefing.Storage.HasFoodPlacementSignal)
            return 0.65f;

        if (briefing.Storage.PositionedFoodUnits <= 0)
            return 0.55f;

        float coolerShare = briefing.Storage.CoolerAdjacentFoodUnits / (float)briefing.Storage.PositionedFoodUnits;
        if (coolerShare <= 0f)
            return 0.55f;

        if (coolerShare < 0.5f)
            return 0.25f;

        return 0f;
    }

    private static float? SelectTerrainFertility(FoodGrowingTerrainSummary terrain, int tiles)
    {
        if (!terrain.HasTerrain || terrain.FertilityBands.Count == 0)
            return null;

        int remaining = Math.Max(1, tiles);
        int used = 0;
        float weighted = 0f;
        foreach (FoodTerrainFertilityBand band in terrain.FertilityBands.OrderByDescending(band => band.Fertility))
        {
            int cells = Math.Min(remaining, band.Cells);
            if (cells <= 0)
                continue;

            weighted += cells * band.Fertility;
            used += cells;
            remaining -= cells;
            if (remaining == 0)
                break;
        }

        return used == 0 ? null : weighted / used;
    }

    private static float AdjustedGrowDays(FoodCropProfile profile, float? terrainFertility)
    {
        if (terrainFertility is null)
            return profile.GrowDays;

        float fertilityFactor = 1f + ((terrainFertility.Value - 1f) * profile.FertilitySensitivity);
        return profile.GrowDays / Math.Max(0.1f, fertilityFactor);
    }

    private static string ContextPhrase(
        FoodCropProfile profile,
        float? terrainFertility,
        float classificationConfidence,
        float storageMultiplier)
    {
        List<string> parts =
        [
            terrainFertility is null
                ? "soil fertility is not known"
                : $"fertility {FormatDays(terrainFertility.Value)}, zone placement not computed"
        ];

        if (classificationConfidence < 0.8f)
            parts.Add("buffer classification uncertain");

        if (storageMultiplier < 0.85f)
        {
            string storageNote = profile.CropDef == "Plant_Corn"
                ? "storage/freezer posture weak for surplus crops"
                : "storage/freezer posture weak";
            parts.Add(storageNote);
        }

        return string.Join("; ", parts);
    }

    private static string ContextSuffix(string context)
    {
        return string.IsNullOrWhiteSpace(context)
            ? ""
            : $"; {context}";
    }

    private static string FormatDays(float days) =>
        days.ToString("0.#", CultureInfo.InvariantCulture);

    private static string FormatGrowingDays(float days) =>
        $"{FormatDays(days)} growing {(Math.Abs(days - 1f) < 0.05f ? "day" : "days")}";

    private static string FormatDayCount(float days) =>
        $"{FormatDays(days)} {(Math.Abs(days - 1f) < 0.05f ? "day" : "days")}";

    private sealed record FoodCropProfile(
        string CropDef,
        string Label,
        string HarvestedThingDef,
        float GrowDays,
        float FertilitySensitivity,
        int HarvestYield,
        float SurplusStorageSensitivity);
}
