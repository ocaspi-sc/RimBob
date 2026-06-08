using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.State.Derivations.Common;

namespace RimBob.State.Derivations;

public static class WillieAnchorInventoryDerivation
{
    public static WillieAnchorInventory Derive(ColonyState state)
    {
        Dictionary<string, BuildingRecord> buildingsById = state.Buildings.Value.Buildings
            .GroupBy(building => building.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        List<WillieRoomAnchor> anchors = [];
        foreach (RoomRecord room in state.Rooms.Value.Rooms)
        {
            IReadOnlyList<BuildingRecord> containedBuildings = room.ContainedBuildingIds
                .Where(id => buildingsById.ContainsKey(id))
                .Select(id => buildingsById[id])
                .ToList();

            RoomClass? primaryClass = RoomClassMapper.FromRoleLabel(room.RoleLabel) ??
                                      RoomClassMapper.FromContainedBuildings(containedBuildings);
            if (primaryClass is null)
                continue;

            IReadOnlyList<string> containedBuildingIds = containedBuildings
                .Select(building => building.Id)
                .ToList();
            MapPosition? primaryCentroid = Centroid(room.Cells, containedBuildings);
            anchors.Add(new WillieRoomAnchor(
                RoomId: room.Id,
                Class: primaryClass.Value,
                RoleLabel: room.RoleLabel,
                CellsCount: room.CellsCount,
                Centroid: primaryCentroid,
                ContainedBuildingIds: containedBuildingIds)
            {
                Bounds = room.Bounds,
                Cells = room.Cells,
                EntryCells = room.EntryCells
            });

            HashSet<RoomClass> emittedClasses = [primaryClass.Value];
            foreach (RoomWorkFunction function in RoomClassMapper.WorkFunctions(containedBuildings))
            {
                if (!emittedClasses.Add(function.Class))
                    continue;

                anchors.Add(new WillieRoomAnchor(
                    RoomId: room.Id,
                    Class: function.Class,
                    RoleLabel: room.RoleLabel,
                    CellsCount: room.CellsCount,
                    Centroid: function.SourceBuilding.Position ?? primaryCentroid,
                    ContainedBuildingIds: containedBuildingIds)
                {
                    Bounds = room.Bounds,
                    Cells = room.Cells,
                    EntryCells = room.EntryCells
                });
            }
        }

        MapRect? mapBounds = BoundsFromMapSize(state.Map.Value.Size);
        foreach (MapArea area in state.Areas.Value.Areas.OrderBy(area => area.Id, StringComparer.OrdinalIgnoreCase))
        {
            // RIMAPI Home rows may omit geometry or report zero cells; the row itself is still
            // the player's early build locus, so fall back to map bounds when exact bounds are absent.
            MapRect? bounds = area.Bounds ?? mapBounds;
            if (area.Centroid is null && bounds is null)
                continue;

            anchors.Add(new WillieRoomAnchor(
                RoomId: $"area:{area.Id}",
                Class: RoomClass.BuildableRegion,
                RoleLabel: area.Label ?? "Home area",
                CellsCount: area.CellCount,
                Centroid: area.Centroid,
                ContainedBuildingIds: [])
            {
                Bounds = bounds
            });
        }

        return new WillieAnchorInventory(anchors);
    }

    private static MapPosition? Centroid(
        IReadOnlyList<MapPosition> roomCells,
        IReadOnlyList<BuildingRecord> buildings)
    {
        if (roomCells.Count > 0)
        {
            return CenterOf(roomCells);
        }

        IReadOnlyList<MapPosition> positions = buildings
            .Select(building => building.Position)
            .Where(position => position is not null)
            .Cast<MapPosition>()
            .ToList();

        if (positions.Count == 0)
        {
            return null;
        }

        return CenterOf(positions);
    }

    private static MapPosition CenterOf(IReadOnlyList<MapPosition> positions)
    {
        int x = (int)Math.Round(positions.Average(position => position.X));
        int y = (int)Math.Round(positions.Average(position => position.Y));
        int z = (int)Math.Round(positions.Average(position => position.Z));
        return new MapPosition(x, y, z);
    }

    private static MapRect? BoundsFromMapSize(string? mapSize)
    {
        MapBounds? bounds = MapBounds.Parse(mapSize);
        return bounds is null
            ? null
            : new MapRect(0, 0, bounds.Width - 1, bounds.Height - 1);
    }
}
