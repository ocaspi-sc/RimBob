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

            RoomClass? roomClass = RoomClassMapper.FromRoleLabel(room.RoleLabel) ??
                                   RoomClassMapper.FromContainedBuildings(containedBuildings);
            if (roomClass is null)
                continue;

            anchors.Add(new WillieRoomAnchor(
                RoomId: room.Id,
                Class: roomClass.Value,
                RoleLabel: room.RoleLabel,
                CellsCount: room.CellsCount,
                Centroid: Centroid(room.Cells, containedBuildings),
                ContainedBuildingIds: containedBuildings.Select(building => building.Id).ToList())
            {
                Bounds = room.Bounds,
                Cells = room.Cells,
                EntryCells = room.EntryCells,
                RegionId = room.RegionId
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
}
