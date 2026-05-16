using RimBob.Core.Aggregates;
using RimBob.Ingestion.Dtos;

namespace RimBob.State;

public static class ResourceAggregateMapper
{
    public static ResourceSummary FromResources(ResourcesSummaryDto resources)
    {
        FoodSummaryDto? food = resources.CriticalResources?.FoodSummary;
        return new ResourceSummary(
            TotalItems: resources.TotalItems,
            TotalMarketValue: resources.TotalMarketValue,
            FoodTotal: food?.FoodTotal ?? 0,
            TotalNutrition: food?.TotalNutrition ?? 0f,
            MealsCount: food?.MealsCount ?? 0,
            RawFoodCount: food?.RawFoodCount ?? 0,
            MedicineTotal: resources.CriticalResources?.MedicineTotal ?? 0,
            WeaponCount: resources.CriticalResources?.WeaponCount ?? 0,
            WeaponValue: resources.CriticalResources?.WeaponValue ?? 0f);
    }

    public static ResearchInfo FromResearch(ResearchProgressDto research)
    {
        // RIMAPI sentinel for "no project selected": name == "none", label == "None".
        bool hasProject = !string.IsNullOrEmpty(research.Name) &&
                          !research.Name.Equals("none", StringComparison.OrdinalIgnoreCase);

        return new ResearchInfo(
            CurrentProject: hasProject ? (research.Label ?? research.Name) : null,
            Progress: hasProject ? research.ProgressPercent / 100f : null,
            IsFinished: research.IsFinished);
    }
}
