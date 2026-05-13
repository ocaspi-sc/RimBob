using RimAI.Core.Aggregates;
using RimAI.Core.Briefings;
using RimAI.State.Parsing;

namespace RimAI.State.Derivations;

public static class FoodBriefingDerivation
{
    private const int QualifiedSkillLevel = 6;

    public static FoodBriefing Compute(ColonyState s, long briefingVersion = 0)
    {
        DateStamp date = RimDateParser.Parse(s.Economy.Value.DateTimeRaw);
        SeasonContext season = DeriveSeason(date);
        IReadOnlyList<ColonistRecord> pawns = s.Colonists.Value.Colonists.Where(p => !p.IsDead).ToList();
        ResourceSummary resources = s.Resources.Value;

        float? reported = resources.TotalNutrition > 0f ? resources.TotalNutrition : null;
        float fallback = FoodNutrition.EstimateFallback(resources.MealsCount, resources.RawFoodCount);
        float? fallbackNutrition = fallback > 0f ? fallback : null;
        float? nutrition = reported ?? fallbackNutrition;
        string nutritionSource = reported is not null
            ? "reported"
            : fallbackNutrition is not null
                ? "fallback_meal_raw_counts"
                : "unknown";
        float? days = nutrition is not null && pawns.Count > 0
            ? nutrition / (FoodNutrition.NutritionPerColonistPerDay * pawns.Count)
            : null;

        IReadOnlyList<string> incidents = s.Threats.Value.RecentIncidents
            .Where(i => IsFoodIncident(i.Def) || (i.Label is not null && IsFoodIncident(i.Label)))
            .Select(i => i.Label is null ? $"{i.Def} ({i.DaysSince:0.#}d ago)" : $"{i.Label} ({i.DaysSince:0.#}d ago)")
            .ToList();
        FoodReferencePoint? reference = FindFoodReferencePoint(s, pawns);
        FoodKitchenSummary kitchen = DeriveKitchen(s);
        FoodStorageSummary storage = DeriveStorage(s, kitchen);

        return new FoodBriefing(
            BriefingVersion: briefingVersion,
            Date: date,
            GameTick: s.Economy.Value.Tick,
            Season: season,
            ColonistCount: pawns.Count,
            ReportedNutrition: reported,
            FallbackNutrition: fallbackNutrition,
            NutritionSource: nutritionSource,
            EstimatedDaysOfFood: days,
            FoodUnits: resources.FoodTotal,
            MealsCount: resources.MealsCount,
            RawFoodCount: resources.RawFoodCount,
            ReadyToHarvest: s.Farm.Value.ReadyToHarvest,
            CropBreakdown: s.Farm.Value.CropBreakdown
                .Select(c => new FoodCropSummary(c.Def, c.Count, c.AverageGrowth))
                .ToList(),
            CropZoneSummaries: DeriveCropZoneSummaries(s, reference),
            WildHarvestCandidates: s.Plants.Value.Plants.Count(p => !p.IsCrop && p.Growth >= 0.85f),
            WildHarvestClusters: DeriveWildHarvestClusters(s, reference),
            WildAnimalCount: s.Animals.Value.Animals.Count(a => !a.Tame && a.Health > 0.6f),
            StockpileCells: s.Stockpiles.Value.Zones.Sum(z => z.CellCount),
            Skills: DeriveSkills(pawns),
            Infrastructure: DeriveInfrastructure(s),
            Storage: storage,
            Kitchen: kitchen,
            DataCoverage: DeriveDataCoverage(s),
            ActiveThreat: DeriveActiveThreat(s.Threats.Value),
            RecentFoodIncidents: incidents
        );
    }

    private static FoodSkillSnapshot DeriveSkills(IReadOnlyList<ColonistRecord> pawns) =>
        new(BestSkill(pawns, "Plants"), QualifiedSkillCount(pawns, "Plants"),
            BestSkill(pawns, "Cooking"), QualifiedSkillCount(pawns, "Cooking"));

    private static int BestSkill(IReadOnlyList<ColonistRecord> pawns, string def) =>
        pawns.SelectMany(p => p.Skills)
            .Where(s => s.Def.Equals(def, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Level)
            .DefaultIfEmpty(0)
            .Max();

    private static int QualifiedSkillCount(IReadOnlyList<ColonistRecord> pawns, string def) =>
        pawns.Count(p => p.Skills.Any(s =>
            s.Def.Equals(def, StringComparison.OrdinalIgnoreCase) && s.Level >= QualifiedSkillLevel));

    private static FoodInfrastructureSnapshot DeriveInfrastructure(ColonyState s)
    {
        int coolers = s.Buildings.Value.Buildings.Count(b =>
            b.Def.Contains("Cooler", StringComparison.OrdinalIgnoreCase));
        float netPower = s.Power.Value.ProductionW - s.Power.Value.ConsumptionW;
        int foodStockpileZones = s.Stockpiles.Value.Zones.Count(z =>
            (z.Label ?? z.Type).Contains("food", StringComparison.OrdinalIgnoreCase) ||
            z.Type.Contains("Stockpile", StringComparison.OrdinalIgnoreCase));
        return new FoodInfrastructureSnapshot(coolers, netPower >= 0, netPower, foodStockpileZones);
    }

    private static FoodKitchenSummary DeriveKitchen(ColonyState s)
    {
        int cookingBuildings = s.Buildings.Value.Buildings.Count(IsCookingBuilding);
        int butcherTables = s.Buildings.Value.Buildings.Count(IsButcherTable);
        return new FoodKitchenSummary(
            CookingBuildings: cookingBuildings,
            ButcherTables: butcherTables,
            HasCookingBuilding: cookingBuildings > 0,
            HasButcherTable: butcherTables > 0);
    }

    private static FoodStorageSummary DeriveStorage(ColonyState s, FoodKitchenSummary kitchen)
    {
        IReadOnlyList<MapPosition> cookingPositions = s.Buildings.Value.Buildings
            .Where(IsCookingBuilding)
            .Select(b => b.Position)
            .Where(p => p is not null)
            .Cast<MapPosition>()
            .ToList();
        IReadOnlyList<MapPosition> stockpileCenters = s.Stockpiles.Value.Zones
            .Select(z => z.Center)
            .Where(p => p is not null)
            .Cast<MapPosition>()
            .ToList();
        int? nearestKitchenDistance = NearestDistance(cookingPositions, stockpileCenters);
        return new FoodStorageSummary(
            StockpileZones: s.Stockpiles.Value.Zones.Count,
            StockpileCells: s.Stockpiles.Value.Zones.Sum(z => z.CellCount),
            NearestKitchenDistanceCells: nearestKitchenDistance,
            NearestKitchenProximity: Proximity(nearestKitchenDistance, cookingPositions.Count > 0 ? "kitchen" : null));
    }

    private static IReadOnlyList<FoodCropZoneSummary> DeriveCropZoneSummaries(
        ColonyState s,
        FoodReferencePoint? reference)
    {
        return s.Plants.Value.Plants
            .Where(p => p.IsCrop)
            .GroupBy(p => new { p.Def, p.ZoneId })
            .Select(g =>
            {
                int? distance = NearestDistance(g.Select(p => p.Position), reference?.Position);
                return new FoodCropZoneSummary(
                    Def: g.Key.Def,
                    ZoneId: g.Key.ZoneId,
                    Count: g.Count(),
                    AverageGrowth: g.Average(p => p.Growth),
                    ReadyCount: g.Count(p => p.Growth >= 0.85f),
                    Proximity: Proximity(distance, reference?.Name));
            })
            .OrderByDescending(c => c.ReadyCount)
            .ThenByDescending(c => c.Count)
            .Take(5)
            .ToList();
    }

    private static IReadOnlyList<WildHarvestCluster> DeriveWildHarvestClusters(
        ColonyState s,
        FoodReferencePoint? reference)
    {
        return s.Plants.Value.Plants
            .Where(p => !p.IsCrop && p.Growth >= 0.85f)
            .GroupBy(p => p.Def)
            .Select(g =>
            {
                int? distance = NearestDistance(g.Select(p => p.Position), reference?.Position);
                return new WildHarvestCluster(
                    Def: g.Key,
                    Count: g.Count(),
                    AverageGrowth: g.Average(p => p.Growth),
                    Proximity: Proximity(distance, reference?.Name),
                    Reference: reference?.Name);
            })
            .OrderBy(c => NearestDistance(s.Plants.Value.Plants
                .Where(p => !p.IsCrop && p.Growth >= 0.85f && p.Def == c.Def)
                .Select(p => p.Position), reference?.Position) ?? int.MaxValue)
            .ThenByDescending(c => c.Count)
            .Take(3)
            .ToList();
    }

    private static FoodDataCoverage DeriveDataCoverage(ColonyState s) =>
        new(
            HasPlantPositions: s.Plants.Value.Plants.Any(p => p.Position is not null),
            HasAnimalPositions: s.Animals.Value.Animals.Any(a => a.Position is not null),
            HasZoneCells: s.Stockpiles.Value.Zones.Any(z => z.Center is not null),
            HasBuildingPositions: s.Buildings.Value.Buildings.Any(b => b.Position is not null),
            HasWorkPriorities: false,
            HasTradeAvailability: false);

    private static FoodReferencePoint? FindFoodReferencePoint(ColonyState s, IReadOnlyList<ColonistRecord> pawns)
    {
        BuildingRecord? cookingBuilding = s.Buildings.Value.Buildings.FirstOrDefault(b => IsCookingBuilding(b) && b.Position is not null);
        if (cookingBuilding?.Position is not null)
            return new FoodReferencePoint("kitchen", cookingBuilding.Position);

        StockpileZone? stockpile = s.Stockpiles.Value.Zones.FirstOrDefault(z => z.Center is not null);
        if (stockpile?.Center is not null)
            return new FoodReferencePoint("food stockpile", stockpile.Center);

        ColonistRecord? pawn = pawns.FirstOrDefault(p => p.Position is not null);
        return pawn?.Position is null ? null : new FoodReferencePoint("colonist position", pawn.Position);
    }

    private static bool IsCookingBuilding(BuildingRecord building) =>
        building.Def.Contains("Stove", StringComparison.OrdinalIgnoreCase) ||
        building.Def.Contains("Campfire", StringComparison.OrdinalIgnoreCase);

    private static bool IsButcherTable(BuildingRecord building) =>
        building.Def.Contains("Butcher", StringComparison.OrdinalIgnoreCase);

    private static int? NearestDistance(IEnumerable<MapPosition?> from, MapPosition? to)
    {
        if (to is null) return null;
        IReadOnlyList<MapPosition> positions = from
            .Where(p => p is not null)
            .Cast<MapPosition>()
            .ToList();
        if (positions.Count == 0) return null;
        return positions.Min(p => ManhattanDistance(p, to));
    }

    private static int? NearestDistance(IReadOnlyList<MapPosition> from, IReadOnlyList<MapPosition> to)
    {
        if (from.Count == 0 || to.Count == 0) return null;
        return from.Min(a => to.Min(b => ManhattanDistance(a, b)));
    }

    private static int ManhattanDistance(MapPosition a, MapPosition b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z);

    private static string? Proximity(int? distance, string? reference = null)
    {
        if (distance is null) return null;
        string bucket = distance.Value switch
        {
            <= 8 => "adjacent",
            <= 25 => "nearby",
            <= 60 => "moderate",
            _ => "far"
        };
        return reference is null
            ? $"{bucket} ({distance.Value} cells)"
            : $"{bucket} ({distance.Value} cells from {reference})";
    }

    private static bool DeriveActiveThreat(ThreatBoard board) =>
        board.Lords.Any(l => l.JobType is not null && (
            l.JobType.Contains("Raid", StringComparison.OrdinalIgnoreCase) ||
            l.JobType.Contains("Siege", StringComparison.OrdinalIgnoreCase) ||
            l.JobType.Contains("Assault", StringComparison.OrdinalIgnoreCase) ||
            l.JobType.Contains("Sapper", StringComparison.OrdinalIgnoreCase)));

    private static bool IsFoodIncident(string text) =>
        text.Contains("Blight", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("ColdSnap", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Food", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Poison", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("ToxicFallout", StringComparison.OrdinalIgnoreCase);

    private static SeasonContext DeriveSeason(DateStamp date)
    {
        string[] quadrums = ["Aprimay", "Jugust", "Septober", "Decembary"];
        const int daysPerQuadrum = 15;
        if (date.Quadrum is null || date.Day is null)
            return new SeasonContext(null, null, null);

        int idx = Array.FindIndex(quadrums, q => q.Equals(date.Quadrum, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            return new SeasonContext(date.Quadrum, null, null);

        int daysToNext = daysPerQuadrum - date.Day.Value + 1;
        int winterIdx = Array.IndexOf(quadrums, "Decembary");
        int quadrumsAway = (winterIdx - idx + 4) % 4;
        int daysToWinter = quadrumsAway == 0
            ? 0
            : (quadrumsAway - 1) * daysPerQuadrum + daysToNext;

        return new SeasonContext(quadrums[idx], daysToNext, daysToWinter);
    }

    private sealed record FoodReferencePoint(string Name, MapPosition Position);
}
