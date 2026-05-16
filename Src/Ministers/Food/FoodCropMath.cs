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
    float Score,
    string Reason);

public static class FoodCropMath
{
    private const float SeasonSafetyMarginDays = 1f;

    private static readonly IReadOnlyList<FoodCropProfile> Profiles =
    [
        new("Plant_Rice", "rice", "RawRice", 3.0f, 1.0f, 6),
        new("Plant_Potato", "potatoes", "RawPotatoes", 5.8f, 0.4f, 11),
        new("Plant_Corn", "corn", "RawCorn", 11.3f, 1.0f, 22)
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
        float score = Score(profile, briefing, projectedDaysAdded, fitsSeason, growDays);
        string reason = Reason(profile, briefing, fitsSeason, margin, growDays, terrainFertility);

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
            Score: score,
            Reason: reason);
    }

    private static float Score(
        FoodCropProfile profile,
        FoodBriefing briefing,
        float projectedDaysAdded,
        bool fitsSeason,
        float growDays)
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

        return urgencyWeight / growDays + projectedDaysAdded * 0.05f;
    }

    private static string Reason(
        FoodCropProfile profile,
        FoodBriefing briefing,
        bool fitsSeason,
        float? margin,
        float growDays,
        float? terrainFertility)
    {
        if (!fitsSeason)
        {
            return briefing.Season.DaysToWinter is { } daysToWinter
                ? $"{profile.Label} needs {FormatGrowingDays(growDays)} plus margin{TerrainPhrase(terrainFertility)}; only {FormatDayCount(daysToWinter)} to winter."
                : $"{profile.Label} does not fit the current growing window.";
        }

        if (profile.CropDef == "Plant_Rice" && briefing.EstimatedDaysOfFood is null or < 20f)
            return margin is null
                ? $"fastest food crop{TerrainPhrase(terrainFertility)}."
                : $"fastest food crop with {FormatDayCount(margin.Value)} of winter margin{TerrainPhrase(terrainFertility)}.";

        if (profile.CropDef == "Plant_Corn")
            return margin is null
                ? $"long-season crop adds the most raw nutrition{TerrainPhrase(terrainFertility)}."
                : $"long-season crop fits with {FormatDayCount(margin.Value)} of winter margin{TerrainPhrase(terrainFertility)}.";

        return margin is null
            ? $"middle-speed food crop{TerrainPhrase(terrainFertility)}."
            : $"middle-speed food crop fits with {FormatDayCount(margin.Value)} of winter margin{TerrainPhrase(terrainFertility)}.";
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

    private static string TerrainPhrase(float? terrainFertility)
    {
        return terrainFertility is null
            ? "; soil fertility is not known"
            : $" at fertility {FormatDays(terrainFertility.Value)}; exact zone placement is not computed";
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
        int HarvestYield);
}
