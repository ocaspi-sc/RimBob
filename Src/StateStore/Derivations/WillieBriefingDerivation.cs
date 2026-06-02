using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.State.Derivations.Common;
using RimBob.State.Parsing;

namespace RimBob.State.Derivations;

public static class WillieBriefingDerivation
{
    public static WillieBriefing Compute(ColonyState state, long briefingVersion = 0)
    {
        GameDate date = RimDateParser.Parse(state.Economy.Value.DateTimeRaw, state.Economy.Value.Tick);
        IReadOnlyList<ColonistRecord> pawns = PawnDeriver.LivingColonists(state.Colonists.Value.Colonists);
        WillieAnchorInventory anchorInventory = WillieAnchorInventoryDerivation.Derive(state);
        WillieConstructionBacklog backlog = state.WillieBacklog.Value;
        PowerNetwork power = state.Power.Value;
        IReadOnlyList<BuildingRecord> buildings = state.Buildings.Value.Buildings;

        return new WillieBriefing(
            BriefingVersion: briefingVersion,
            Date: date,
            GameTick: state.Economy.Value.Tick,
            MapId: state.Map.Value.Id,
            ColonistCount: pawns.Count,
            PowerStability: DerivePowerStability(power, buildings),
            ThermalControl: DeriveThermalControl(buildings, anchorInventory),
            FunctionalRooms: DeriveFunctionalRooms(anchorInventory),
            StoragePlacement: DeriveStoragePlacement(state.Stockpiles.Value),
            MaterialBottleneck: DeriveMaterialBottleneck(backlog),
            FireRisk: DeriveFireRisk(buildings),
            StalledBuilds: DeriveStalledBuilds(backlog),
            BaseLayout: new WillieBaseLayoutSummary(state.Rooms.Value.Rooms.Count, buildings.Count),
            DataCoverage: DeriveDataCoverage(state, anchorInventory, backlog))
        {
            AnchorInventory = anchorInventory,
            ConstructionBacklog = backlog
        };
    }

    private static WilliePowerStabilitySummary DerivePowerStability(
        PowerNetwork power,
        IReadOnlyList<BuildingRecord> buildings) =>
        new(
            ProductionW: power.ProductionW,
            ConsumptionW: power.ConsumptionW,
            StoredWd: power.StoredWd,
            CapacityWd: power.CapacityWd,
            NetW: power.ProductionW - power.ConsumptionW,
            GeneratorCount: buildings.Count(BuildingClassifier.IsGenerator),
            BatteryCount: buildings.Count(BuildingClassifier.IsBattery));

    private static WillieThermalControlSummary DeriveThermalControl(
        IReadOnlyList<BuildingRecord> buildings,
        WillieAnchorInventory anchors) =>
        new(
            CoolerCount: buildings.Count(BuildingClassifier.IsCooler),
            HeaterCount: buildings.Count(BuildingClassifier.IsHeater),
            FreezerAnchorCount: anchors.Anchors.Count(anchor => anchor.Class == RoomClass.Freezer));

    private static WillieFunctionalRoomsSummary DeriveFunctionalRooms(WillieAnchorInventory anchors) =>
        new(anchors.Anchors
            .Where(anchor => anchor.Class != RoomClass.BuildableRegion)
            .GroupBy(anchor => anchor.Class)
            .ToDictionary(
                group => group.Key.ToString(),
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase));

    private static WillieStoragePlacementSummary DeriveStoragePlacement(StockpileLedger stockpiles) =>
        new(
            StockpileZones: stockpiles.Zones.Count,
            StockpileCells: stockpiles.Zones.Sum(zone => zone.CellCount));

    private static WillieMaterialBottleneckSummary DeriveMaterialBottleneck(
        WillieConstructionBacklog backlog) =>
        new(
            BacklogGroups: backlog.Groups,
            MissingMaterials: CollapseMaterials(backlog.Groups.SelectMany(group => group.MaterialsMissing)),
            BlockedCount: backlog.Groups.Sum(group => group.BlockedCount),
            DisallowedCount: backlog.Groups.Sum(group => group.DisallowedCount));

    private static WillieStalledBuildsSummary DeriveStalledBuilds(
        WillieConstructionBacklog backlog) =>
        new(
            BacklogGroups: backlog.Groups,
            PendingBuildCount: backlog.Groups.Sum(group => group.Count),
            TotalWorkLeft: backlog.Groups.Sum(group => group.TotalWorkLeft),
            BlockedCount: backlog.Groups.Sum(group => group.BlockedCount),
            DisallowedCount: backlog.Groups.Sum(group => group.DisallowedCount));

    private static WillieFireRiskSummary DeriveFireRisk(IReadOnlyList<BuildingRecord> buildings) =>
        new(buildings.Count(IsWoodStructure));

    private static WillieDataCoverage DeriveDataCoverage(
        ColonyState state,
        WillieAnchorInventory anchorInventory,
        WillieConstructionBacklog backlog) =>
        new(
            HasLiveState: state.LastRefreshSource == ColonyStateOrigin.Live,
            HasRooms: state.Rooms.Value.Rooms.Count > 0,
            HasBuildings: state.Buildings.Value.Buildings.Count > 0,
            HasPower: state.Power.Version > 0,
            HasStockpiles: state.Stockpiles.Value.Zones.Count > 0,
            HasConstructionBacklog: backlog.SourceAvailable,
            HasAnchorInventory: anchorInventory.Anchors.Count > 0,
            HasReachability: state.LastRefreshSource == ColonyStateOrigin.Live &&
                             anchorInventory.Anchors.Any(HasReachabilityTarget));

    private static bool HasReachabilityTarget(WillieRoomAnchor anchor) =>
        anchor.EntryCells.Count > 0 ||
        anchor.Centroid is not null ||
        anchor.Bounds is not null;

    private static IReadOnlyList<MaterialCount> CollapseMaterials(IEnumerable<MaterialCount> materials) =>
        materials
            .Where(material => !string.IsNullOrWhiteSpace(material.DefName) && material.Count > 0)
            .GroupBy(material => material.DefName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MaterialCount(group.Key, group.Sum(material => material.Count)))
            .OrderByDescending(material => material.Count)
            .ThenBy(material => material.DefName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool IsWoodStructure(BuildingRecord building) =>
        Contains(building.Def, "Wood") ||
        Contains(building.Label, "wood");

    private static bool Contains(string? text, string token) =>
        text?.Contains(token, StringComparison.OrdinalIgnoreCase) == true;
}
