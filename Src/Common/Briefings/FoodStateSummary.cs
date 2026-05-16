namespace RimAI.Core.Briefings;

public static class FoodStateSummary
{
    public static string Build(FoodBriefing briefing)
    {
        if (!briefing.DataCoverage.HasLiveState)
        {
            return FormatBullets([
                "Live state: no live RimWorld state yet; zero counts are unhydrated data, not colony truth.",
                "Refresh: start RimWorld/RIMAPI and refresh before making food-chain decisions."
            ]);
        }

        return FormatBullets(new[]
        {
            BuildStoredFoodLine(briefing),
            BuildGrowingLine(briefing),
            BuildAcquisitionLine(briefing),
            BuildKitchenStorageLine(briefing),
            BuildDataGapLine(briefing)
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string BuildStoredFoodLine(FoodBriefing briefing)
    {
        List<string> stores = [];
        stores.Add(Plural(briefing.MealsCount, "meal"));
        stores.Add($"{briefing.RawFoodCount} raw food");
        if (briefing.UnclassifiedFoodUnits > 0)
            stores.Add(FormatUncountedFoodUnits(briefing));
        string unclassifiedDetails = FormatUnclassifiedDetails(briefing);

        if (briefing.EstimatedDaysOfFood is null)
        {
            if (briefing.UnclassifiedFoodUnits > 0)
            {
                return $"Stores: {JoinList(stores)}{unclassifiedDetails}; days-of-food cannot be estimated because the reported food units are not classified as meals or raw food.";
            }

            return $"Stores: {JoinList(stores)}; days-of-food cannot be estimated because no usable nutrition signal is available.";
        }

        float days = briefing.EstimatedDaysOfFood.Value;
        string posture = days switch
        {
            < 1f => "near zero",
            < 7f => "urgent",
            < 20f => "below the safety band",
            _ => "stable"
        };

        return $"Stores: {JoinList(stores)}{unclassifiedDetails}; about {days:F1} days for {Plural(briefing.ColonistCount, "colonist")}; buffer {posture}.";
    }

    private static string BuildGrowingLine(FoodBriefing briefing)
    {
        if (briefing.CropZoneSummaries.Count > 0)
        {
            string zones = string.Join("; ", briefing.CropZoneSummaries.Take(3).Select(FormatCropZone));
            int remaining = Math.Max(0, briefing.CropZoneSummaries.Count - 3);
            string suffix = remaining > 0 ? $"; plus {Plural(remaining, "more growing area")}" : "";
            return $"Crops: {zones}{suffix}.";
        }

        if (briefing.CropBreakdown.Count > 0)
        {
            string crops = string.Join("; ", briefing.CropBreakdown.Take(3).Select(FormatCropBreakdown));
            int remaining = Math.Max(0, briefing.CropBreakdown.Count - 3);
            string suffix = remaining > 0 ? $"; plus {Plural(remaining, "more crop")}" : "";
            return $"Crops: {crops}{suffix}.";
        }

        if (briefing.ReadyToHarvest > 0)
            return $"Crops: {Plural(briefing.ReadyToHarvest, "crop tile")} ready to harvest; crop-zone detail is not available.";

        return "Crops: no crop signal is visible in this briefing.";
    }

    private static string BuildAcquisitionLine(FoodBriefing briefing)
    {
        List<string> parts =
        [
            Plural(briefing.WildHarvestCandidates, "wild harvest candidate"),
            $"{Plural(briefing.WildAnimalCount, "wild animal")} visible"
        ];
        if (briefing.WildHuntTargets.Count > 0 && (briefing.EstimatedDaysOfFood ?? 0f) < 20f)
        {
            WildHuntTarget target = briefing.WildHuntTargets[0];
            parts.Add($"{Plural(target.Count, LabelAnimal(target.Def))} hunt targets");
        }

        return $"Acquisition: {string.Join("; ", parts)}.";
    }

    private static string BuildKitchenStorageLine(FoodBriefing briefing)
    {
        List<string> parts = [];
        parts.Add(Plural(briefing.Kitchen.CookingBuildings, "cooking station"));
        parts.Add(Plural(briefing.Infrastructure.Coolers, "cooler"));
        parts.Add($"{Plural(briefing.Storage.StockpileZones, "stockpile zone")} / {Plural(briefing.Storage.StockpileCells, "stockpile cell")}");

        if (briefing.RawFoodCount > 0 && briefing.MealsCount < briefing.ColonistCount * 2)
            parts.Add("raw food is waiting on cooking throughput");
        return $"Kitchen/storage: {string.Join("; ", parts)}.";
    }

    private static string BuildDataGapLine(FoodBriefing briefing)
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

    private static string FormatUncountedFoodUnits(FoodBriefing briefing)
    {
        List<string> parts = [];
        if (briefing.ExcludedFoodUnits > 0)
            parts.Add($"{briefing.ExcludedFoodUnits} food units excluded from reachable buffer");
        if (briefing.UnknownFoodUnits > 0)
            parts.Add($"{briefing.UnknownFoodUnits} unknown food units");

        return JoinList(parts);
    }

    private static string FormatBullets(IEnumerable<string> lines) =>
        string.Join("\n", lines.Select(line => $"- {line}"));

    private static string FormatUnclassifiedDetails(FoodBriefing briefing)
    {
        if (briefing.UnclassifiedFoodItems.Count == 0)
            return "";

        string details = string.Join("; ", briefing.UnclassifiedFoodItems
            .Take(3)
            .Select(FormatUnclassifiedItem));
        int remaining = Math.Max(0, briefing.UnclassifiedFoodItems.Count - 3);
        string suffix = remaining > 0 ? $"; plus {Plural(remaining, "more item group")}" : "";
        return $" ({details}{suffix})";
    }

    private static string FormatUnclassifiedItem(FoodUnclassifiedItem item)
    {
        string forbidden = item.IsForbidden ? "forbidden " : "";
        string label = CountedLabel(item.Count, item.Label ?? item.Def);
        string position = string.IsNullOrWhiteSpace(item.Position) ? "" : $" at {item.Position}";
        return $"{item.Count} {forbidden}{label}{position}";
    }

    private static string CountedLabel(int count, string label)
    {
        if (count == 1 || label.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            return label;

        return Pluralize(label);
    }

    private static string FormatCropZone(FoodCropZoneSummary crop)
    {
        string zone = FormatZoneLabel(crop.ZoneId);
        string ready = crop.ReadyCount > 0 ? $", {Plural(crop.ReadyCount, "tile")} ready" : "";
        string proximity = string.IsNullOrWhiteSpace(crop.Proximity) ? "" : $", {crop.Proximity}";
        return $"{crop.Count} {LabelCrop(crop.Def)} plants in {zone} at {crop.AverageGrowth:P0} growth{ready}{proximity}";
    }

    private static string FormatCropBreakdown(FoodCropSummary crop) =>
        $"{crop.Count} {LabelCrop(crop.Def)} plants at {crop.AverageGrowth:P0} growth";

    private static string FormatZoneLabel(string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
            return "unlabeled area";

        return zoneId.All(char.IsDigit) ? $"zone {zoneId}" : zoneId;
    }

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

    private static string JoinList(IReadOnlyList<string> items) =>
        items.Count switch
        {
            0 => "",
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}"
        };
    }
