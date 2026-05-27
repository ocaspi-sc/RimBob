using RimBob.Core.Aggregates;

namespace RimBob.Core.Briefings;

public sealed record WilliePowerStabilitySummary(
    float ProductionW,
    float ConsumptionW,
    float StoredWd,
    float CapacityWd,
    float NetW,
    int GeneratorCount,
    int BatteryCount);

public sealed record WillieThermalControlSummary(
    int CoolerCount,
    int HeaterCount,
    int FreezerAnchorCount);

public sealed record WillieFunctionalRoomsSummary(
    IReadOnlyDictionary<string, int> RoomCountsByClass);

public sealed record WillieStoragePlacementSummary(
    int StockpileZones,
    int StockpileCells);

public sealed record WillieMaterialBottleneckSummary(
    IReadOnlyList<WillieBacklogGroup> BacklogGroups,
    IReadOnlyList<MaterialCount> MissingMaterials,
    int BlockedCount,
    int DisallowedCount);

public sealed record WillieFireRiskSummary(
    int WoodStructureCount);

public sealed record WillieStalledBuildsSummary(
    IReadOnlyList<WillieBacklogGroup> BacklogGroups,
    int PendingBuildCount,
    float TotalWorkLeft,
    int BlockedCount,
    int DisallowedCount);

public sealed record WillieBaseLayoutSummary(
    int RoomCount,
    int BuildingCount);
