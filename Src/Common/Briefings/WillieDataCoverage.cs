namespace RimBob.Core.Briefings;

public sealed record WillieDataCoverage(
    bool HasLiveState,
    bool HasRooms,
    bool HasBuildings,
    bool HasPower,
    bool HasStockpiles,
    bool HasConstructionBacklog,
    bool HasAnchorInventory,
    bool HasReachability);
