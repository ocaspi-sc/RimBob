using RimAI.Core.Aggregates;
using RimAI.Core.Briefings;

namespace RimAI.State.Derivations;

internal static class FoodItemClassifier
{
    public static FoodItemClassification Classify(
        ResourceSummary resources,
        StoredResourceRegistry storedResources,
        ThingRegistry things,
        ThingDefRegistry thingDefs)
    {
        float? reportedNutrition = resources.TotalNutrition > 0f ? resources.TotalNutrition : null;

        FoodItemClassification? stored = ClassifyItems(
            storedResources.Items.Select(item => new FoodSourceItem(
                Def: item.Def,
                Label: item.Label,
                StackCount: item.StackCount,
                Category: item.Category,
                Categories: [item.Category],
                IsForbidden: item.IsForbidden)),
            thingDefs,
            resources.FoodTotal,
            reportedNutrition);

        if (stored is not null)
            return stored;

        FoodItemClassification? broadMap = ClassifyItems(
            things.Things.Select(item => new FoodSourceItem(
                Def: item.Def,
                Label: item.Label,
                StackCount: item.StackCount,
                Category: "",
                Categories: item.Categories,
                IsForbidden: item.IsForbidden)),
            thingDefs,
            resources.FoodTotal,
            reportedNutrition);

        if (broadMap is not null)
            return broadMap;

        float fallback = FoodNutrition.EstimateFallback(resources.MealsCount, resources.RawFoodCount);
        float? fallbackNutrition = fallback > 0f ? fallback : null;
        float? nutrition = reportedNutrition ?? fallbackNutrition;
        string nutritionSource = reportedNutrition is not null
            ? "reported"
            : fallbackNutrition is not null
                ? "fallback_meal_raw_counts"
                : "unknown";

        return new FoodItemClassification(
            ReportedNutrition: reportedNutrition,
            FallbackNutrition: fallbackNutrition,
            Nutrition: nutrition,
            NutritionSource: nutritionSource,
            FoodUnits: resources.FoodTotal,
            MealsCount: resources.MealsCount,
            RawFoodCount: resources.RawFoodCount,
            HasItemFoodClassification: false);
    }

    private static FoodItemClassification? ClassifyItems(
        IEnumerable<FoodSourceItem> sourceItems,
        ThingDefRegistry thingDefs,
        int reportedFoodUnits,
        float? reportedNutrition)
    {
        int meals = 0;
        int rawFood = 0;
        float nutrition = 0f;

        foreach (FoodSourceItem item in sourceItems.Where(item => !item.IsForbidden && item.StackCount > 0))
        {
            FoodItemKind kind = ClassifyKind(item, thingDefs);
            if (kind == FoodItemKind.NotFood)
                continue;

            if (kind == FoodItemKind.Meal)
                meals += item.StackCount;
            else
                rawFood += item.StackCount;

            nutrition += item.StackCount * NutritionFor(item, kind, thingDefs);
        }

        if (meals == 0 && rawFood == 0)
            return null;

        float? fallbackNutrition = nutrition > 0f ? nutrition : null;
        return new FoodItemClassification(
            ReportedNutrition: reportedNutrition,
            FallbackNutrition: fallbackNutrition,
            Nutrition: fallbackNutrition ?? reportedNutrition,
            NutritionSource: fallbackNutrition is not null ? "item_def_catalog" : "item_category_counts",
            FoodUnits: Math.Max(reportedFoodUnits, meals + rawFood),
            MealsCount: meals,
            RawFoodCount: rawFood,
            HasItemFoodClassification: true);
    }

    private static FoodItemKind ClassifyKind(FoodSourceItem item, ThingDefRegistry thingDefs)
    {
        if (IsMeal(item))
            return FoodItemKind.Meal;

        if (IsRawFoodCategory(item))
            return FoodItemKind.RawFood;

        if (TryGetDef(item.Def, thingDefs, out ThingDefRecord? def) &&
            def is not null &&
            def.Nutrition > 0f &&
            def.IsItem &&
            !def.IsDrug &&
            !def.IsMedicine)
            return FoodItemKind.RawFood;

        return FoodItemKind.NotFood;
    }

    private static float NutritionFor(FoodSourceItem item, FoodItemKind kind, ThingDefRegistry thingDefs)
    {
        if (TryGetDef(item.Def, thingDefs, out ThingDefRecord? def) &&
            def is not null &&
            def.Nutrition > 0f)
            return def.Nutrition;

        return kind == FoodItemKind.Meal
            ? FoodNutrition.NutritionPerMeal
            : FoodNutrition.NutritionPerRawFood;
    }

    private static bool IsMeal(FoodSourceItem item) =>
        AnyCategoryContains(item, "food_meals") ||
        AnyCategoryContains(item, "meals") ||
        item.Def.StartsWith("Meal", StringComparison.OrdinalIgnoreCase) ||
        (item.Label?.Contains("meal", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool IsRawFoodCategory(FoodSourceItem item) =>
        AnyCategoryContains(item, "plant_food_raw") ||
        AnyCategoryContains(item, "meat_raw") ||
        AnyCategoryContains(item, "animal_product_raw") ||
        AnyCategoryContains(item, "food_raw");

    private static bool AnyCategoryContains(FoodSourceItem item, string token) =>
        item.Category.Contains(token, StringComparison.OrdinalIgnoreCase) ||
        item.Categories.Any(category => category.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetDef(
        string defName,
        ThingDefRegistry thingDefs,
        out ThingDefRecord? def) =>
        thingDefs.DefsByName.TryGetValue(defName, out def);

    private enum FoodItemKind
    {
        NotFood,
        Meal,
        RawFood
    }

    private sealed record FoodSourceItem(
        string Def,
        string? Label,
        int StackCount,
        string Category,
        IReadOnlyList<string> Categories,
        bool IsForbidden);
}

internal sealed record FoodItemClassification(
    float? ReportedNutrition,
    float? FallbackNutrition,
    float? Nutrition,
    string NutritionSource,
    int FoodUnits,
    int MealsCount,
    int RawFoodCount,
    bool HasItemFoodClassification);
