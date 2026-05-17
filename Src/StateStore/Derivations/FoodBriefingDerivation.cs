using RimBob.Core.Aggregates;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.State.Derivations.Common;
using RimBob.State.Parsing;

namespace RimBob.State.Derivations;

public static class FoodBriefingDerivation
{
    private const int QualifiedSkillLevel = 6;
    private const int CoolerAdjacentDistanceCells = 12;
    private const int HarvestTargetSearchLimit = 240;
    private static readonly IReadOnlyList<string> CropHarvestThingDefs = ["RawRice", "RawPotatoes", "RawCorn"];
    private static readonly HashSet<string> EdibleForagePlantDefs = new(StringComparer.OrdinalIgnoreCase)
    {
        "BerryBush",
        "Plant_Agave",
        "Plant_Berry"
    };

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
        IReadOnlyList<PlantRecord> foragePlants = ReadyFoodForagePlants(s.Plants.Value.Plants);

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
            WildHarvestCandidates: foragePlants.Count,
            WildHarvestClusters: DeriveWildHarvestClusters(foragePlants, reference),
            WildAnimalCount: s.Animals.Value.Animals.Count(FoodHuntSafety.IsHealthyWildAnimal),
            WildHuntTargets: DeriveWildHuntTargets(s, reference),
            StockpileCells: s.Stockpiles.Value.Zones.Sum(z => z.CellCount),
            Skills: DeriveSkills(pawns),
            Infrastructure: DeriveInfrastructure(s),
            Storage: storage,
            Kitchen: kitchen,
            DataCoverage: DeriveDataCoverage(s, food),
            ActiveThreat: ThreatDeriver.HasActiveHostileThreat(s.Threats.Value),
            RecentFoodIncidents: incidents
        )
        {
            MapId = s.Map.Value.Id,
            GrowingTerrain = DeriveGrowingTerrain(s.Terrain.Value),
            CropHarvestNutritionByDef = DeriveCropHarvestNutrition(s.ThingDefs.Value),
            UnclassifiedFoodItems = food.UnclassifiedFoodItems,
            UnforbidTargets = DeriveUnforbidTargets(s),
            HarvestTargets = DeriveHarvestTargets(s, reference, foragePlants),
            HuntTargets = DeriveHuntTargets(s, reference, Math.Max(1, Math.Min(pawns.Count * 2, AssistedApplyLimits.MaxHuntTargets)))
        };
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
        IReadOnlyList<BuildingRecord> cookingBuildingRecords = s.Buildings.Value.Buildings
            .Where(BuildingClassifier.IsCookingBuilding)
            .ToList();
        HashSet<string> cookingBuildingIds = cookingBuildingRecords
            .Select(building => building.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<FoodCookingBillSummary> cookingBills = s.WorkTables.Value.WorkTables
            .Where(workTable => cookingBuildingIds.Contains(workTable.BuildingId))
            .SelectMany(workTable => workTable.Bills.Select(bill => new FoodCookingBillSummary(
                workTable.BuildingId,
                bill.LoadId,
                bill.RecipeDefName,
                bill.RecipeLabel,
                bill.Suspended,
                bill.Paused,
                bill.RepeatMode,
                bill.RepeatCount,
                bill.TargetCount)))
            .ToList();
        int cookingBuildings = cookingBuildingRecords.Count;
        int butcherTables = s.Buildings.Value.Buildings.Count(BuildingClassifier.IsButcherTable);
        return new FoodKitchenSummary(
            CookingBuildings: cookingBuildings,
            ButcherTables: butcherTables,
            HasCookingBuilding: cookingBuildings > 0,
            HasButcherTable: butcherTables > 0)
        {
            CookingBuildingIds = cookingBuildingRecords.Select(building => building.Id).ToList(),
            CookingBuildingDetails = cookingBuildingRecords
                .Select(building => new FoodCookingBuildingSummary(
                    building.Id,
                    building.Def,
                    building.Label,
                    building.Position))
                .ToList(),
            CookingBills = cookingBills
        };
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
        IReadOnlyList<MapPosition> coolerPositions = s.Buildings.Value.Buildings
            .Where(BuildingClassifier.IsCooler)
            .Select(b => b.Position)
            .Where(p => p is not null)
            .Cast<MapPosition>()
            .ToList();
        IReadOnlyList<StoredResourceRecord> storedFoodItems = s.StoredResources.Value.Items
            .Where(item => !item.IsForbidden && item.StackCount > 0)
            .Where(item => FoodItemClassifier.IsStoredFoodItem(item, s.ThingDefs.Value))
            .ToList();
        int positionedFoodUnits = storedFoodItems
            .Where(item => item.Position is not null)
            .Sum(item => item.StackCount);
        int coolerAdjacentFoodUnits = storedFoodItems
            .Where(item => item.Position is not null && IsCoolerAdjacent(item.Position, coolerPositions))
            .Sum(item => item.StackCount);
        int unpositionedFoodUnits = storedFoodItems
            .Where(item => item.Position is null)
            .Sum(item => item.StackCount);
        int? nearestKitchenDistance = MapDistance.Nearest(cookingPositions, stockpileCenters);
        return new FoodStorageSummary(
            StockpileZones: s.Stockpiles.Value.Zones.Count,
            StockpileCells: s.Stockpiles.Value.Zones.Sum(z => z.CellCount),
            NearestKitchenDistanceCells: nearestKitchenDistance,
            NearestKitchenProximity: MapDistance.ProximityLabel(nearestKitchenDistance, cookingPositions.Count > 0 ? "kitchen" : null))
        {
            PositionedFoodUnits = positionedFoodUnits,
            CoolerAdjacentFoodUnits = coolerAdjacentFoodUnits,
            UnpositionedFoodUnits = unpositionedFoodUnits
        };
    }

    private static bool IsCoolerAdjacent(MapPosition? foodPosition, IReadOnlyList<MapPosition> coolerPositions)
    {
        int? distance = MapDistance.Nearest(coolerPositions, foodPosition);
        return distance is not null && distance.Value <= CoolerAdjacentDistanceCells;
    }

    private static IReadOnlyList<FoodUnforbidTarget> DeriveUnforbidTargets(ColonyState s)
    {
        Dictionary<string, FoodUnforbidTarget> targets = new(StringComparer.OrdinalIgnoreCase);

        foreach (StoredResourceRecord item in s.StoredResources.Value.Items
                     .Where(item => item.IsForbidden && item.StackCount > 0 && item.Position is not null))
        {
            string? kind = FoodItemClassifier.FoodKindLabel(item, s.ThingDefs.Value);
            if (kind is null) continue;

            targets.TryAdd(item.Id, new FoodUnforbidTarget(
                Id: item.Id,
                Def: item.Def,
                Label: item.Label,
                Count: item.StackCount,
                Kind: kind,
                Source: "resources_stored",
                Position: item.Position!));
        }

        foreach (ThingRecord thing in s.Things.Value.Things
                     .Where(thing => thing.IsForbidden && thing.StackCount > 0 && thing.Position is not null))
        {
            string? kind = FoodItemClassifier.FoodKindLabel(thing, s.ThingDefs.Value);
            if (kind is null) continue;

            targets.TryAdd(thing.Id, new FoodUnforbidTarget(
                Id: thing.Id,
                Def: thing.Def,
                Label: thing.Label,
                Count: thing.StackCount,
                Kind: kind,
                Source: "map_things",
                Position: thing.Position!));
        }

        return targets.Values
            .OrderBy(target => target.Kind == "meal" ? 0 : 1)
            .ThenByDescending(target => target.Count)
            .Take(AssistedApplyLimits.MaxUnforbidTargets)
            .ToList();
    }

    private static IReadOnlyList<FoodHarvestTarget> DeriveHarvestTargets(
        ColonyState s,
        FoodReferencePoint? reference,
        IReadOnlyList<PlantRecord> foragePlants)
    {
        List<FoodHarvestTarget> targets = [];

        targets.AddRange(s.Plants.Value.Plants
            .Where(plant => plant.IsCrop && IsHarvestReady(plant) && plant.Position is not null)
            .GroupBy(plant => new { plant.Def, plant.ZoneId })
            .Select(group => BuildHarvestTarget("crop", group.Key.Def, group.Key.ZoneId, group, reference))
            .Where(target => target is not null)
            .Cast<FoodHarvestTarget>());

        targets.AddRange(foragePlants
            .Where(plant => plant.Position is not null)
            .GroupBy(plant => plant.Def)
            .Select(group => BuildHarvestTarget("wild", group.Key, null, group, reference))
            .Where(target => target is not null)
            .Cast<FoodHarvestTarget>());

        return targets
            .OrderBy(target => target.Source == "crop" ? 0 : 1)
            .ThenBy(target => MapDistance.Nearest(TargetPositions(s.Plants.Value.Plants, target.PlantIds), reference?.Position) ?? int.MaxValue)
            .ThenByDescending(target => target.Count)
            .Take(5)
            .ToList();
    }

    private static FoodHarvestTarget? BuildHarvestTarget(
        string source,
        string def,
        string? zoneId,
        IEnumerable<PlantRecord> plants,
        FoodReferencePoint? reference)
    {
        List<PlantRecord> candidatePlants = OrderPlantsForHarvestTarget(
                plants.Where(plant => plant.Position is not null),
                reference?.Position)
            .Take(HarvestTargetSearchLimit)
            .ToList();
        IReadOnlyList<PlantRecord> targetPlants = SelectBoundedHarvestPlants(candidatePlants, reference);

        if (targetPlants.Count == 0)
            return null;

        MapRect rect = RectFor(targetPlants.Select(plant => plant.Position!));
        int? distance = MapDistance.Nearest(targetPlants.Select(plant => plant.Position), reference?.Position);
        return new FoodHarvestTarget(
            Source: source,
            Def: def,
            Count: targetPlants.Count,
            Rect: rect,
            PlantIds: targetPlants.Select(plant => plant.Id).ToList(),
            ZoneId: zoneId,
            Proximity: MapDistance.ProximityLabel(distance, reference?.Name),
            Reference: reference?.Name);
    }

    private static IReadOnlyList<PlantRecord> SelectBoundedHarvestPlants(
        IReadOnlyList<PlantRecord> plants,
        FoodReferencePoint? reference)
    {
        if (FitsHarvestApplyLimits(plants))
            return plants.ToList();

        HarvestTargetCandidate? best = null;
        IReadOnlyList<PlantRecord> orderedPlants = OrderPlantsForHarvestTarget(plants, reference?.Position).ToList();
        for (int i = 0; i < orderedPlants.Count; i++)
        {
            for (int j = i; j < orderedPlants.Count; j++)
            {
                MapRect candidateRect = RectFor(orderedPlants[i].Position!, orderedPlants[j].Position!);
                if (candidateRect.Area <= 0 || candidateRect.Area > AssistedApplyLimits.MaxHarvestRectArea)
                    continue;

                List<PlantRecord> candidatePlants = plants
                    .Where(candidate => IsInside(candidateRect, candidate.Position))
                    .ToList();
                if (!FitsHarvestApplyLimits(candidatePlants))
                    continue;

                int distance = MapDistance.Nearest(candidatePlants.Select(plant => plant.Position), reference?.Position)
                    ?? int.MaxValue;
                HarvestTargetCandidate candidateTarget = new(
                    OrderPlantsForHarvestTarget(candidatePlants, reference?.Position).ToList(),
                    candidateRect.Area,
                    distance);
                if (best is null || IsBetterHarvestTarget(candidateTarget, best))
                    best = candidateTarget;
            }
        }

        return best?.Plants ?? [];
    }

    private static bool FitsHarvestApplyLimits(IReadOnlyList<PlantRecord> plants)
    {
        if (plants.Count == 0 || plants.Count > AssistedApplyLimits.MaxHarvestTargets)
            return false;

        MapRect rect = RectFor(plants.Select(plant => plant.Position!));
        return rect.Area > 0 && rect.Area <= AssistedApplyLimits.MaxHarvestRectArea;
    }

    private static IOrderedEnumerable<PlantRecord> OrderPlantsForHarvestTarget(
        IEnumerable<PlantRecord> plants,
        MapPosition? reference)
    {
        return plants
            .Where(plant => plant.Position is not null)
            .OrderBy(plant => reference is null ? 0 : MapDistance.Manhattan(plant.Position!, reference))
            .ThenBy(plant => plant.Position!.X)
            .ThenBy(plant => plant.Position!.Z)
            .ThenBy(plant => plant.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsInside(MapRect rect, MapPosition? position) =>
        position is not null &&
        position.X >= rect.X1 &&
        position.X <= rect.X2 &&
        position.Z >= rect.Z1 &&
        position.Z <= rect.Z2;

    private static MapRect RectFor(MapPosition first, MapPosition second) =>
        new(
            X1: Math.Min(first.X, second.X),
            Z1: Math.Min(first.Z, second.Z),
            X2: Math.Max(first.X, second.X),
            Z2: Math.Max(first.Z, second.Z));

    private static bool IsBetterHarvestTarget(HarvestTargetCandidate candidate, HarvestTargetCandidate current)
    {
        if (candidate.Plants.Count != current.Plants.Count)
            return candidate.Plants.Count > current.Plants.Count;
        if (candidate.Distance != current.Distance)
            return candidate.Distance < current.Distance;
        return candidate.Area < current.Area;
    }

    private static IReadOnlyList<MapPosition> TargetPositions(
        IReadOnlyList<PlantRecord> plants,
        IReadOnlyList<string> ids)
    {
        HashSet<string> idSet = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return plants
            .Where(plant => idSet.Contains(plant.Id) && plant.Position is not null)
            .Select(plant => plant.Position!)
            .ToList();
    }

    private static MapRect RectFor(IEnumerable<MapPosition> positions)
    {
        List<MapPosition> list = positions.ToList();
        return new MapRect(
            X1: list.Min(position => position.X),
            Z1: list.Min(position => position.Z),
            X2: list.Max(position => position.X),
            Z2: list.Max(position => position.Z));
    }

    private static IReadOnlyList<FoodCropZoneSummary> DeriveCropZoneSummaries(
        ColonyState s,
        FoodReferencePoint? reference)
    {
        IReadOnlyList<FoodCropZoneSummary> farmZoneSummaries = DeriveFarmCropZoneSummaries(s, reference);
        if (farmZoneSummaries.Count > 0)
            return farmZoneSummaries;

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

    private static IReadOnlyList<FoodCropZoneSummary> DeriveFarmCropZoneSummaries(
        ColonyState s,
        FoodReferencePoint? reference)
    {
        IReadOnlyList<CropTypeCount> cropTypes = s.Farm.Value.CropBreakdown
            .Where(crop => !string.IsNullOrWhiteSpace(crop.ZoneId) || crop.ReadyCount > 0)
            .ToList();
        if (cropTypes.Count == 0)
            return [];

        return cropTypes
            .Select(crop =>
            {
                int? distance = MapDistance.Nearest(
                    s.Plants.Value.Plants
                        .Where(plant => string.Equals(plant.Def, crop.Def, StringComparison.OrdinalIgnoreCase))
                        .Select(plant => plant.Position),
                    reference?.Position);
                return new FoodCropZoneSummary(
                    Def: crop.Def,
                    ZoneId: crop.ZoneId,
                    Count: crop.Count,
                    AverageGrowth: crop.AverageGrowth,
                    ReadyCount: crop.ReadyCount,
                    Proximity: MapDistance.ProximityLabel(distance, reference?.Name));
            })
            .OrderByDescending(crop => crop.ReadyCount)
            .ThenByDescending(crop => crop.Count)
            .Take(5)
            .ToList();
    }

    private static IReadOnlyList<WildHarvestCluster> DeriveWildHarvestClusters(
        IReadOnlyList<PlantRecord> foragePlants,
        FoodReferencePoint? reference)
    {
        return foragePlants
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
            .OrderBy(c => MapDistance.Nearest(foragePlants
                .Where(p => p.Def == c.Def)
                .Select(p => p.Position), reference?.Position) ?? int.MaxValue)
            .ThenByDescending(c => c.Count)
            .Take(3)
            .ToList();
    }

    private static IReadOnlyList<PlantRecord> ReadyFoodForagePlants(IReadOnlyList<PlantRecord> plants) =>
        plants
            .Where(plant => !plant.IsCrop)
            .Where(IsHarvestReady)
            .Where(plant => EdibleForagePlantDefs.Contains(plant.Def))
            .ToList();

    private static bool IsHarvestReady(PlantRecord plant) =>
        plant.IsHarvestable ?? plant.Growth >= 0.85f;

    private static IReadOnlyList<WildHuntTarget> DeriveWildHuntTargets(
        ColonyState s,
        FoodReferencePoint? reference)
    {
        IReadOnlyList<AnimalRecord> candidates = LowRiskHuntCandidates(s);

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
            .OrderBy(candidate => FoodHuntSafety.RiskRank(candidate.Target.Def))
            .ThenBy(candidate => candidate.Distance ?? int.MaxValue)
            .ThenByDescending(candidate => candidate.Target.Count)
            .Select(candidate => candidate.Target)
            .Take(3)
            .ToList();
    }

    private static IReadOnlyList<FoodHuntTarget> DeriveHuntTargets(
        ColonyState s,
        FoodReferencePoint? reference,
        int targetLimit)
    {
        IReadOnlyList<AnimalRecord> candidates = LowRiskHuntCandidates(s)
            .Where(animal => animal.Position is not null)
            .ToList();

        return candidates
            .GroupBy(animal => animal.Def, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildHuntTarget(group.Key, group, reference, targetLimit))
            .Where(candidate => candidate is not null)
            .Cast<HuntTargetCandidate>()
            .OrderBy(candidate => FoodHuntSafety.RiskRank(candidate.Target.Def))
            .ThenBy(candidate => candidate.Distance ?? int.MaxValue)
            .ThenByDescending(candidate => candidate.Target.Count)
            .Select(candidate => candidate.Target)
            .Take(3)
            .ToList();
    }

    private static HuntTargetCandidate? BuildHuntTarget(
        string def,
        IEnumerable<AnimalRecord> animals,
        FoodReferencePoint? reference,
        int targetLimit)
    {
        IReadOnlyList<AnimalRecord> selected = SelectBoundedHuntAnimals(
            animals.Where(animal => animal.Position is not null).ToList(),
            reference,
            targetLimit);
        if (selected.Count == 0)
            return null;

        MapRect rect = RectFor(selected.Select(animal => animal.Position!));
        int? distance = MapDistance.Nearest(selected.Select(animal => animal.Position), reference?.Position);
        return new HuntTargetCandidate(
            new FoodHuntTarget(
                Def: def,
                Count: selected.Count,
                Rect: rect,
                AnimalIds: selected.Select(animal => animal.Id).ToList(),
                Proximity: MapDistance.ProximityLabel(distance, reference?.Name),
                Reference: reference?.Name),
            distance);
    }

    private static IReadOnlyList<AnimalRecord> SelectBoundedHuntAnimals(
        IReadOnlyList<AnimalRecord> animals,
        FoodReferencePoint? reference,
        int targetLimit)
    {
        if (animals.Count == 0)
            return [];

        int limit = Math.Clamp(targetLimit, 1, AssistedApplyLimits.MaxHuntTargets);
        List<AnimalRecord> orderedAnimals = OrderAnimalsForHuntTarget(animals, reference?.Position).ToList();
        if (orderedAnimals.Count <= limit && FitsHuntApplyLimits(orderedAnimals))
            return orderedAnimals;

        List<AnimalRecord> selected = [];
        foreach (AnimalRecord animal in orderedAnimals)
        {
            if (selected.Count >= limit)
                break;

            List<AnimalRecord> candidate = [..selected, animal];
            if (FitsHuntApplyLimits(candidate))
                selected = candidate;
        }

        return selected;
    }

    private static bool FitsHuntApplyLimits(IReadOnlyList<AnimalRecord> animals)
    {
        if (animals.Count == 0 || animals.Count > AssistedApplyLimits.MaxHuntTargets)
            return false;

        MapRect rect = RectFor(animals.Select(animal => animal.Position!));
        return rect.Area > 0 && rect.Area <= AssistedApplyLimits.MaxHuntRectArea;
    }

    private static IOrderedEnumerable<AnimalRecord> OrderAnimalsForHuntTarget(
        IEnumerable<AnimalRecord> animals,
        MapPosition? reference)
    {
        return animals
            .Where(animal => animal.Position is not null)
            .OrderBy(animal => reference is null ? 0 : MapDistance.Manhattan(animal.Position!, reference))
            .ThenBy(animal => animal.Position!.X)
            .ThenBy(animal => animal.Position!.Z)
            .ThenBy(animal => animal.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<AnimalRecord> LowRiskHuntCandidates(ColonyState s) =>
        s.Animals.Value.Animals
            .Where(FoodHuntSafety.IsLowRiskTarget)
            .ToList();

    private static FoodGrowingTerrainSummary DeriveGrowingTerrain(TerrainSnapshot terrain)
    {
        if (terrain.CellCountsByDef.Count == 0 || terrain.DefsByName.Count == 0)
            return FoodGrowingTerrainSummary.Unknown;

        List<FoodTerrainFertilityBand> allBands = terrain.CellCountsByDef
            .Select(entry =>
            {
                terrain.DefsByName.TryGetValue(entry.Key, out TerrainDefRecord? def);
                return new
                {
                    Def = entry.Key,
                    Cells = entry.Value,
                    Record = def
                };
            })
            .Where(item => item.Record is not null && item.Record.SupportsGrowing && item.Cells > 0)
            .Select(item => new FoodTerrainFertilityBand(
                Def: item.Def,
                Label: item.Record!.Label,
                Fertility: item.Record.Fertility,
                Cells: item.Cells))
            .OrderByDescending(band => band.Fertility)
            .ThenByDescending(band => band.Cells)
            .ToList();

        if (allBands.Count == 0)
            return new FoodGrowingTerrainSummary(true, 0, null, null, []);

        int growableCells = allBands.Sum(band => band.Cells);
        float weightedFertility = allBands.Sum(band => band.Fertility * band.Cells) / growableCells;
        IReadOnlyList<FoodTerrainFertilityBand> summaryBands = allBands.Take(5).ToList();

        return new FoodGrowingTerrainSummary(
            HasTerrain: true,
            GrowableCells: growableCells,
            BestFertility: allBands[0].Fertility,
            AverageFertility: weightedFertility,
            FertilityBands: summaryBands);
    }

    private static IReadOnlyDictionary<string, float> DeriveCropHarvestNutrition(ThingDefRegistry thingDefs)
    {
        Dictionary<string, float> nutritionByDef = new(StringComparer.OrdinalIgnoreCase);
        foreach (string harvestedDef in CropHarvestThingDefs)
        {
            if (thingDefs.DefsByName.TryGetValue(harvestedDef, out ThingDefRecord? def) &&
                def.Nutrition > 0f)
            {
                nutritionByDef[harvestedDef] = def.Nutrition;
            }
        }

        return nutritionByDef;
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
            HasItemFoodClassification = food.HasItemFoodClassification,
            HasTerrainFertility = s.Terrain.Value.CellCountsByDef.Count > 0 &&
                                  s.Terrain.Value.DefsByName.Values.Any(def => def.SupportsGrowing)
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

    private sealed record HuntCandidateSummary(WildHuntTarget Target, int? Distance);

    private sealed record HuntTargetCandidate(FoodHuntTarget Target, int? Distance);

    private sealed record HarvestTargetCandidate(IReadOnlyList<PlantRecord> Plants, int Area, int Distance);

    private sealed record FoodReferencePoint(string Name, MapPosition Position);
}
