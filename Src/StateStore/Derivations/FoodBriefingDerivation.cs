using RimAI.Core.Aggregates;
using RimAI.Core.Briefings;
using RimAI.State.Derivations.Common;
using RimAI.State.Parsing;

namespace RimAI.State.Derivations;

public static class FoodBriefingDerivation
{
    private const int QualifiedSkillLevel = 6;

    public static FoodBriefing Compute(ColonyState s, long briefingVersion = 0)
    {
        DateStamp date = RimDateParser.Parse(s.Economy.Value.DateTimeRaw);
        SeasonContext season = SeasonDeriver.Derive(date);
        IReadOnlyList<ColonistRecord> pawns = PawnDeriver.LivingColonists(s.Colonists.Value.Colonists);
        ResourceSummary resources = s.Resources.Value;
        FoodItemClassification food = FoodItemClassifier.Classify(
            resources,
            s.StoredResources.Value,
            s.Things.Value,
            s.ThingDefs.Value);

        float? days = food.Nutrition is not null && pawns.Count > 0
            ? food.Nutrition / (FoodNutrition.NutritionPerColonistPerDay * pawns.Count)
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
            ReportedNutrition: food.ReportedNutrition,
            FallbackNutrition: food.FallbackNutrition,
            NutritionSource: food.NutritionSource,
            EstimatedDaysOfFood: days,
            FoodUnits: food.FoodUnits,
            MealsCount: food.MealsCount,
            RawFoodCount: food.RawFoodCount,
            ReadyToHarvest: s.Farm.Value.ReadyToHarvest,
            CropBreakdown: s.Farm.Value.CropBreakdown
                .Select(c => new FoodCropSummary(c.Def, c.Count, c.AverageGrowth))
                .ToList(),
            CropZoneSummaries: DeriveCropZoneSummaries(s, reference),
            WildHarvestCandidates: s.Plants.Value.Plants.Count(p => !p.IsCrop && p.Growth >= 0.85f),
            WildHarvestClusters: DeriveWildHarvestClusters(s, reference),
            WildAnimalCount: s.Animals.Value.Animals.Count(IsHealthyWildAnimal),
            WildHuntTargets: DeriveWildHuntTargets(s, reference),
            StockpileCells: s.Stockpiles.Value.Zones.Sum(z => z.CellCount),
            Skills: DeriveSkills(pawns),
            Infrastructure: DeriveInfrastructure(s),
            Storage: storage,
            Kitchen: kitchen,
            DataCoverage: DeriveDataCoverage(s, food),
            ActiveThreat: ThreatDeriver.HasActiveHostileThreat(s.Threats.Value),
            RecentFoodIncidents: incidents
        );
    }

    private static FoodSkillSnapshot DeriveSkills(IReadOnlyList<ColonistRecord> pawns) =>
        new(PawnDeriver.BestSkill(pawns, "Plants"), PawnDeriver.QualifiedSkillCount(pawns, "Plants", QualifiedSkillLevel),
            PawnDeriver.BestSkill(pawns, "Cooking"), PawnDeriver.QualifiedSkillCount(pawns, "Cooking", QualifiedSkillLevel));

    private static FoodInfrastructureSnapshot DeriveInfrastructure(ColonyState s)
    {
        int coolers = s.Buildings.Value.Buildings.Count(BuildingClassifier.IsCooler);
        float netPower = s.Power.Value.ProductionW - s.Power.Value.ConsumptionW;
        int foodStockpileZones = s.Stockpiles.Value.Zones.Count(z =>
            (z.Label ?? z.Type).Contains("food", StringComparison.OrdinalIgnoreCase) ||
            z.Type.Contains("Stockpile", StringComparison.OrdinalIgnoreCase));
        return new FoodInfrastructureSnapshot(coolers, netPower >= 0, netPower, foodStockpileZones);
    }

    private static FoodKitchenSummary DeriveKitchen(ColonyState s)
    {
        int cookingBuildings = s.Buildings.Value.Buildings.Count(BuildingClassifier.IsCookingBuilding);
        int butcherTables = s.Buildings.Value.Buildings.Count(BuildingClassifier.IsButcherTable);
        return new FoodKitchenSummary(
            CookingBuildings: cookingBuildings,
            ButcherTables: butcherTables,
            HasCookingBuilding: cookingBuildings > 0,
            HasButcherTable: butcherTables > 0);
    }

    private static FoodStorageSummary DeriveStorage(ColonyState s, FoodKitchenSummary kitchen)
    {
        IReadOnlyList<MapPosition> cookingPositions = s.Buildings.Value.Buildings
            .Where(BuildingClassifier.IsCookingBuilding)
            .Select(b => b.Position)
            .Where(p => p is not null)
            .Cast<MapPosition>()
            .ToList();
        IReadOnlyList<MapPosition> stockpileCenters = s.Stockpiles.Value.Zones
            .Select(z => z.Center)
            .Where(p => p is not null)
            .Cast<MapPosition>()
            .ToList();
        int? nearestKitchenDistance = MapDistance.Nearest(cookingPositions, stockpileCenters);
        return new FoodStorageSummary(
            StockpileZones: s.Stockpiles.Value.Zones.Count,
            StockpileCells: s.Stockpiles.Value.Zones.Sum(z => z.CellCount),
            NearestKitchenDistanceCells: nearestKitchenDistance,
            NearestKitchenProximity: MapDistance.ProximityLabel(nearestKitchenDistance, cookingPositions.Count > 0 ? "kitchen" : null));
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
                int? distance = MapDistance.Nearest(g.Select(p => p.Position), reference?.Position);
                return new FoodCropZoneSummary(
                    Def: g.Key.Def,
                    ZoneId: g.Key.ZoneId,
                    Count: g.Count(),
                    AverageGrowth: g.Average(p => p.Growth),
                    ReadyCount: g.Count(p => p.Growth >= 0.85f),
                    Proximity: MapDistance.ProximityLabel(distance, reference?.Name));
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
                int? distance = MapDistance.Nearest(g.Select(p => p.Position), reference?.Position);
                return new WildHarvestCluster(
                    Def: g.Key,
                    Count: g.Count(),
                    AverageGrowth: g.Average(p => p.Growth),
                    Proximity: MapDistance.ProximityLabel(distance, reference?.Name),
                    Reference: reference?.Name);
            })
            .OrderBy(c => MapDistance.Nearest(s.Plants.Value.Plants
                .Where(p => !p.IsCrop && p.Growth >= 0.85f && p.Def == c.Def)
                .Select(p => p.Position), reference?.Position) ?? int.MaxValue)
            .ThenByDescending(c => c.Count)
            .Take(3)
            .ToList();
    }

    private static IReadOnlyList<WildHuntTarget> DeriveWildHuntTargets(
        ColonyState s,
        FoodReferencePoint? reference)
    {
        IReadOnlyList<AnimalRecord> candidates = s.Animals.Value.Animals
            .Where(IsHealthyWildAnimal)
            .Where(animal => !IsDangerousHuntDef(animal.Def))
            .ToList();

        return candidates
            .GroupBy(animal => animal.Def, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                int? distance = MapDistance.Nearest(group.Select(animal => animal.Position), reference?.Position);
                return new HuntCandidateSummary(
                    new WildHuntTarget(
                        Def: group.Key,
                        Count: group.Count(),
                        Proximity: MapDistance.ProximityLabel(distance, reference?.Name),
                        Reference: reference?.Name),
                    distance);
            })
            .OrderBy(candidate => HuntRiskRank(candidate.Target.Def))
            .ThenBy(candidate => candidate.Distance ?? int.MaxValue)
            .ThenByDescending(candidate => candidate.Target.Count)
            .Select(candidate => candidate.Target)
            .Take(3)
            .ToList();
    }

    private static FoodDataCoverage DeriveDataCoverage(ColonyState s, FoodItemClassification food) =>
        new(
            HasPlantPositions: s.Plants.Value.Plants.Any(p => p.Position is not null),
            HasAnimalPositions: s.Animals.Value.Animals.Any(a => a.Position is not null),
            HasZoneCells: s.Stockpiles.Value.Zones.Any(z => z.Center is not null),
            HasBuildingPositions: s.Buildings.Value.Buildings.Any(b => b.Position is not null),
            HasWorkPriorities: false,
            HasTradeAvailability: false)
        {
            HasLiveState = s.GetVersionsForFoodBriefing().Any(version => version > 0),
            HasItemFoodClassification = food.HasItemFoodClassification
        };

    private static FoodReferencePoint? FindFoodReferencePoint(ColonyState s, IReadOnlyList<ColonistRecord> pawns)
    {
        BuildingRecord? cookingBuilding = s.Buildings.Value.Buildings.FirstOrDefault(b => BuildingClassifier.IsCookingBuilding(b) && b.Position is not null);
        if (cookingBuilding?.Position is not null)
            return new FoodReferencePoint("kitchen", cookingBuilding.Position);

        StockpileZone? stockpile = s.Stockpiles.Value.Zones.FirstOrDefault(z => z.Center is not null);
        if (stockpile?.Center is not null)
            return new FoodReferencePoint("food stockpile", stockpile.Center);

        ColonistRecord? pawn = pawns.FirstOrDefault(p => p.Position is not null);
        return pawn?.Position is null ? null : new FoodReferencePoint("colonist position", pawn.Position);
    }

    private static bool IsFoodIncident(string text) =>
        text.Contains("Blight", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("ColdSnap", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Food", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Poison", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("ToxicFallout", StringComparison.OrdinalIgnoreCase);

    private static bool IsHealthyWildAnimal(AnimalRecord animal) =>
        !animal.Tame && animal.Health > 0.6f;

    private static bool IsDangerousHuntDef(string def)
    {
        string normalized = def.ToLowerInvariant();
        return ContainsAny(normalized,
        [
            "bear",
            "boom",
            "cobra",
            "cougar",
            "elephant",
            "insect",
            "lynx",
            "mega",
            "panther",
            "rhinoceros",
            "scaria",
            "thrumbo",
            "warg",
            "wolf"
        ]);
    }

    private static int HuntRiskRank(string def)
    {
        string normalized = def.ToLowerInvariant();
        if (ContainsAny(normalized, ["hare", "rabbit", "squirrel", "rat", "turkey", "tortoise"]))
            return 0;

        if (ContainsAny(normalized, ["deer", "doe", "buck", "gazelle", "ibex", "elk", "caribou", "alpaca", "dromedary"]))
            return 1;

        return 2;
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private sealed record HuntCandidateSummary(WildHuntTarget Target, int? Distance);

    private sealed record FoodReferencePoint(string Name, MapPosition Position);
}
