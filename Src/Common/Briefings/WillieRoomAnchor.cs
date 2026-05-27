using RimBob.Core.Advice;
using RimBob.Core.Aggregates;

namespace RimBob.Core.Briefings;

public sealed record WillieAnchorInventory(IReadOnlyList<WillieRoomAnchor> Anchors)
{
    public static WillieAnchorInventory Empty { get; } = new([]);
}

public sealed record WillieRoomAnchor(
    string RoomId,
    RoomClass Class,
    string RoleLabel,
    int CellsCount,
    MapPosition? Centroid,
    IReadOnlyList<string> ContainedBuildingIds)
{
    // TODO: populate after rimapi-room-entry-cells lands.
    public IReadOnlyList<MapPosition> EntryCells { get; init; } = [];

    // TODO: populate after rimapi-map-region-at lands.
    public int? RegionId { get; init; }
}
