using RimBob.Core.Aggregates;

namespace RimBob.Core.Briefings;

public sealed record WillieBriefing(
    long BriefingVersion,
    GameDate Date,
    long GameTick,
    int MapId,
    int ColonistCount,
    WilliePowerStabilitySummary PowerStability,
    WillieThermalControlSummary ThermalControl,
    WillieFunctionalRoomsSummary FunctionalRooms,
    WillieStoragePlacementSummary StoragePlacement,
    WillieMaterialBottleneckSummary MaterialBottleneck,
    WillieFireRiskSummary FireRisk,
    WillieStalledBuildsSummary StalledBuilds,
    WillieBaseLayoutSummary BaseLayout,
    WillieDataCoverage DataCoverage
) : IBriefing
{
    public WillieAnchorInventory AnchorInventory { get; init; } = WillieAnchorInventory.Empty;

    public WillieConstructionBacklog ConstructionBacklog { get; init; } = AggregateDefaults.WillieBacklog;
}
