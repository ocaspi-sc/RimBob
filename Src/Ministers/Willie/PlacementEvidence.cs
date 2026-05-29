using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.State.Derivations.Common;

namespace RimBob.Ministers.Willie;

public sealed class PlacementEvidence
{
    private readonly HashSet<MapCell> occupiedCells;

    private PlacementEvidence(
        int mapId,
        MapBounds? bounds,
        HashSet<MapCell> occupiedCells,
        IReadOnlyList<ResolvedAnchor> anchors)
    {
        MapId = mapId;
        Bounds = bounds;
        this.occupiedCells = occupiedCells;
        Anchors = anchors;
    }

    public int MapId { get; }

    public MapBounds? Bounds { get; }

    public IReadOnlyList<ResolvedAnchor> Anchors { get; }

    // TODO: BuildingRecord.Position is a single cell, not a footprint; occupancy is approximate until per-building footprints land.
    public bool OccupancyIsPointApprox { get; } = true;

    public static PlacementEvidence Build(
        MapInfoSnapshot map,
        BuildingRegistry buildings,
        IReadOnlyList<ResolvedAnchor> anchors)
    {
        HashSet<MapCell> occupied = buildings.Buildings
            .Select(building => building.Position)
            .Where(position => position is not null)
            .Cast<MapPosition>()
            .Select(position => position.ToMapCell())
            .ToHashSet();

        return new PlacementEvidence(
            map.Id,
            MapBounds.Parse(map.Size),
            occupied,
            anchors);
    }

    public bool InBounds(MapCell cell) =>
        Bounds is not null && Bounds.Contains(cell);

    public bool IsOccupied(MapCell cell) =>
        occupiedCells.Contains(cell);
}
