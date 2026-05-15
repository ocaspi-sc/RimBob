namespace RimAI.Core.Briefings;

public static class FoodStateSummary
{
    public static string Build(FoodBriefing briefing)
    {
        if (!briefing.DataCoverage.HasLiveState)
            return "Food has no live RimWorld state yet, so the zero counts in this briefing should be treated as unhydrated data rather than colony truth. Start RimWorld/RIMAPI and refresh before making food-chain decisions from this snapshot.";

        return string.Join(" ", new[]
        {
            BuildStoredFoodSentence(briefing),
            BuildGrowingSentence(briefing),
            BuildKitchenStorageSentence(briefing),
            BuildDataGapSentence(briefing)
        }.Where(sentence => !string.IsNullOrWhiteSpace(sentence)));
    }

    private static string BuildStoredFoodSentence(FoodBriefing briefing)
    {
        List<string> stores = [];
        stores.Add(Plural(briefing.MealsCount, "meal"));
        stores.Add($"{briefing.RawFoodCount} raw food");
        if (briefing.UnclassifiedFoodUnits > 0)
            stores.Add($"{briefing.UnclassifiedFoodUnits} unclassified food units");

        if (briefing.EstimatedDaysOfFood is null)
        {
            if (briefing.UnclassifiedFoodUnits > 0)
            {
                return $"Food stores show {JoinList(stores)}, but days-of-food cannot be estimated because the reported food units are not classified as meals or raw food.";
            }

            return $"Food stores show {JoinList(stores)}, but days-of-food cannot be estimated because no usable nutrition signal is available.";
        }

        float days = briefing.EstimatedDaysOfFood.Value;
        string posture = days switch
        {
            < 1f => "near zero",
            < 7f => "urgent",
            < 20f => "below the safety band",
            _ => "stable"
        };

        return $"Food stores show {JoinList(stores)}, about {days:F1} days for {Plural(briefing.ColonistCount, "colonist")}; the buffer is {posture}.";
    }

    private static string BuildGrowingSentence(FoodBriefing briefing)
    {
        if (briefing.CropZoneSummaries.Count > 0)
        {
            string zones = string.Join("; ", briefing.CropZoneSummaries.Take(3).Select(FormatCropZone));
            int remaining = Math.Max(0, briefing.CropZoneSummaries.Count - 3);
            string suffix = remaining > 0 ? $"; plus {Plural(remaining, "more growing area")}" : "";
            return $"Growing areas: {zones}{suffix}.";
        }

        if (briefing.CropBreakdown.Count > 0)
        {
            string crops = string.Join("; ", briefing.CropBreakdown.Take(3).Select(FormatCropBreakdown));
            int remaining = Math.Max(0, briefing.CropBreakdown.Count - 3);
            string suffix = remaining > 0 ? $"; plus {Plural(remaining, "more crop")}" : "";
            return $"Growing areas: {crops}{suffix}.";
        }

        if (briefing.ReadyToHarvest > 0)
            return $"Growing areas: {Plural(briefing.ReadyToHarvest, "crop tile")} ready to harvest, but crop-zone detail is not available.";

        return "Growing areas: no crop signal is visible in this briefing.";
    }

    private static string BuildKitchenStorageSentence(FoodBriefing briefing)
    {
        List<string> parts = [];
        parts.Add(Plural(briefing.Kitchen.CookingBuildings, "cooking station"));
        parts.Add(Plural(briefing.Infrastructure.Coolers, "cooler"));
        parts.Add($"{Plural(briefing.Storage.StockpileZones, "stockpile zone")} / {Plural(briefing.Storage.StockpileCells, "stockpile cell")}");

        if (briefing.RawFoodCount > 0 && briefing.MealsCount < briefing.ColonistCount * 2)
            parts.Add("raw food is waiting on cooking throughput");
        if (briefing.WildHarvestCandidates > 0)
            parts.Add($"{briefing.WildHarvestCandidates} wild harvest candidates");
        if (briefing.WildAnimalCount > 0 && (briefing.EstimatedDaysOfFood ?? 0f) < 20f)
            parts.Add($"{briefing.WildAnimalCount} wild animals may be food targets");

        return $"Kitchen/storage: {string.Join("; ", parts)}.";
    }

    private static string BuildDataGapSentence(FoodBriefing briefing)
    {
        IReadOnlyList<string> gaps = briefing.MissingBriefingSignals
            .Concat(briefing.UnimplementedBriefingSignals)
            .Where(signal => signal != "live_state")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return gaps.Count == 0
            ? ""
            : $"Confidence gaps: {string.Join(", ", gaps)}.";
    }

    private static string FormatCropZone(FoodCropZoneSummary crop)
    {
        string zone = string.IsNullOrWhiteSpace(crop.ZoneId) ? "unlabeled area" : crop.ZoneId;
        string ready = crop.ReadyCount > 0 ? $", {Plural(crop.ReadyCount, "tile")} ready" : "";
        string proximity = string.IsNullOrWhiteSpace(crop.Proximity) ? "" : $", {crop.Proximity}";
        return $"{crop.Count} {LabelCrop(crop.Def)} plants in {zone} at {crop.AverageGrowth:P0} growth{ready}{proximity}";
    }

    private static string FormatCropBreakdown(FoodCropSummary crop) =>
        $"{crop.Count} {LabelCrop(crop.Def)} plants at {crop.AverageGrowth:P0} growth";

    private static string LabelCrop(string def)
    {
        string label = def;
        if (label.StartsWith("Plant_", StringComparison.OrdinalIgnoreCase))
            label = label["Plant_".Length..];

        label = label.Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(label) ? "crop" : label.ToLowerInvariant();
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

    private static string JoinList(IReadOnlyList<string> items) =>
        items.Count switch
        {
            0 => "",
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}"
        };
    }
