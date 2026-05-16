using RimBob.Core.Briefings;

namespace RimBob.Knowledge;

public sealed class FoodRagQueryBuilder : IRagQueryBuilder<FoodBriefing>
{
    public string BuildQuery(FoodBriefing briefing, IReadOnlyList<string> directives) =>
        Build(briefing);

    public static string Build(FoodBriefing briefing)
    {
        List<string> parts = new()
        {
            "RimWorld food farming crops cooking freezer nutrition spoilage hunting wild harvest.",
            $"Food days: {(briefing.EstimatedDaysOfFood is null ? "unknown" : briefing.EstimatedDaysOfFood.Value.ToString("F1"))}.",
            $"Meals {briefing.MealsCount}; raw food {briefing.RawFoodCount}; ready harvest {briefing.ReadyToHarvest}; wild harvest {briefing.WildHarvestCandidates}; animals {briefing.WildAnimalCount}."
        };
        if (briefing.UnclassifiedFoodUnits > 0)
        {
            if (briefing.ExcludedFoodUnits > 0)
                parts.Add($"Food units excluded from reachable buffer: {briefing.ExcludedFoodUnits}.");
            if (briefing.UnknownFoodUnits > 0)
                parts.Add($"Unknown food units: {briefing.UnknownFoodUnits}.");
        }
        if (briefing.Season.DaysToWinter is { } winter)
            parts.Add($"Days to winter: {winter}.");
        if (briefing.GrowingTerrain.HasTerrain && briefing.GrowingTerrain.BestFertility is { } fertility)
            parts.Add($"Best growable terrain fertility: {fertility:F1}; growable cells: {briefing.GrowingTerrain.GrowableCells}.");
        if (briefing.Infrastructure.Coolers == 0)
            parts.Add("No cooler/freezer signal.");
        if (briefing.RecentFoodIncidents.Count > 0)
            parts.Add(string.Join(' ', briefing.RecentFoodIncidents));
        return string.Join(' ', parts);
    }
}
