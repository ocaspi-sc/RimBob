using System.Security.Cryptography;
using System.Text.Json;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.State;

namespace RimBob.Ministers.Willie;

internal static class WillieSolveCacheKey
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string ForBuilding(
        BuildingRequest request,
        WillieBriefing briefing,
        ColonyState colonyState,
        IReadOnlyList<MaterialHint> materialsOnHand) =>
        Fingerprint(new BuildingPlacementCacheInput(
            Request: CanonicalRequest(request),
            RefreshSource: colonyState.LastRefreshSource.ToString(),
            Map: colonyState.Map.Value,
            Anchors: OrderedAnchors(briefing),
            Buildings: OrderedBuildings(colonyState),
            Backlog: OrderedBacklog(colonyState),
            MaterialsOnHand: materialsOnHand));

    public static string ForZone(
        ZoneRequest request,
        WillieBriefing briefing,
        ColonyState colonyState) =>
        Fingerprint(new ZonePlacementCacheInput(
            Request: CanonicalRequest(request),
            RefreshSource: colonyState.LastRefreshSource.ToString(),
            Map: colonyState.Map.Value,
            Anchors: OrderedAnchors(briefing),
            Areas: OrderedAreas(colonyState),
            Buildings: OrderedBuildings(colonyState),
            Rooms: OrderedRooms(colonyState),
            Stockpiles: OrderedStockpiles(colonyState),
            Terrain: TerrainInput(colonyState.Terrain.Value),
            Zones: OrderedZones(colonyState),
            Plants: OrderedPlants(colonyState)));

    private static string Fingerprint<TInput>(TInput input)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(input, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(json));
    }

    private static IReadOnlyList<BuildingRecord> OrderedBuildings(ColonyState colonyState) =>
        colonyState.Buildings.Value.Buildings
            .OrderBy(building => building.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(building => building.Def, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static WillieConstructionBacklog OrderedBacklog(ColonyState colonyState) =>
        colonyState.WillieBacklog.Value with
        {
            Groups = colonyState.WillieBacklog.Value.Groups
                .Select(group => group with
                {
                    ThingIds = group.ThingIds
                        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    SampleCells = OrderedPositions(group.SampleCells),
                    Cost = OrderedMaterialCounts(group.Cost),
                    MaterialsAvailable = OrderedMaterialCounts(group.MaterialsAvailable),
                    MaterialsMissing = OrderedMaterialCounts(group.MaterialsMissing)
                })
                .OrderBy(group => group.Kind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.DefName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.StuffDefName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Count)
                .ToList()
        };

    private static IReadOnlyList<MapArea> OrderedAreas(ColonyState colonyState) =>
        colonyState.Areas.Value.Areas
            .Select(area => area with { Cells = OrderedPositions(area.Cells) })
            .OrderBy(area => area.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(area => area.Type, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<StockpileZone> OrderedStockpiles(ColonyState colonyState) =>
        colonyState.Stockpiles.Value.Zones
            .Select(zone => zone with { Cells = OrderedPositions(zone.Cells) })
            .OrderBy(zone => zone.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(zone => zone.Type, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<MapZoneRecord> OrderedZones(ColonyState colonyState) =>
        colonyState.Zones.Value.Zones
            .Select(zone => zone with { Cells = OrderedPositions(zone.Cells) })
            .OrderBy(zone => zone.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(zone => zone.Type, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<RoomRecord> OrderedRooms(ColonyState colonyState) =>
        colonyState.Rooms.Value.Rooms
            .Select(room => room with
            {
                Cells = OrderedPositions(room.Cells),
                EntryCells = OrderedPositions(room.EntryCells),
                ContainedBedIds = room.ContainedBedIds
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                ContainedBuildingIds = room.ContainedBuildingIds
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .OrderBy(room => room.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(room => room.RoleLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<PlantRecord> OrderedPlants(ColonyState colonyState) =>
        colonyState.Plants.Value.Plants
            .OrderBy(plant => plant.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(plant => plant.Def, StringComparer.OrdinalIgnoreCase)
            .ThenBy(plant => plant.Position?.X)
            .ThenBy(plant => plant.Position?.Y)
            .ThenBy(plant => plant.Position?.Z)
            .ToList();

    private static IReadOnlyList<WillieRoomAnchor> OrderedAnchors(WillieBriefing briefing) =>
        briefing.AnchorInventory.Anchors
            .Select(anchor => anchor with
            {
                Cells = OrderedPositions(anchor.Cells),
                EntryCells = OrderedPositions(anchor.EntryCells),
                ContainedBuildingIds = anchor.ContainedBuildingIds
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .OrderBy(anchor => anchor.Class.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(anchor => anchor.RoomId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(anchor => anchor.RoleLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<MapPosition> OrderedPositions(IEnumerable<MapPosition> positions) =>
        positions
            .OrderBy(position => position.X)
            .ThenBy(position => position.Y)
            .ThenBy(position => position.Z)
            .ToList();

    private static IReadOnlyList<MaterialCount> OrderedMaterialCounts(IEnumerable<MaterialCount> counts) =>
        counts
            .OrderBy(count => count.DefName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(count => count.Count)
            .ThenBy(count => count.Required)
            .ThenBy(count => count.Available)
            .ThenBy(count => count.Missing)
            .ToList();

    private static TerrainCacheInput TerrainInput(TerrainSnapshot terrain) =>
        new(
            Width: terrain.Width,
            Height: terrain.Height,
            CellCountsByDef: terrain.CellCountsByDef
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new NamedCount(pair.Key, pair.Value))
                .ToList(),
            DefsByName: terrain.DefsByName
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new NamedTerrainDef(
                    pair.Key,
                    pair.Value with
                    {
                        Affordances = pair.Value.Affordances
                            .OrderBy(affordance => affordance, StringComparer.OrdinalIgnoreCase)
                            .ToList()
                    }))
                .ToList(),
            Cells: terrain.Cells
                .OrderBy(cell => cell.X)
                .ThenBy(cell => cell.Z)
                .ThenBy(cell => cell.TerrainDef, StringComparer.OrdinalIgnoreCase)
                .ToList());

    private static BuildingRequest CanonicalRequest(BuildingRequest request) =>
        request with
        {
            Adjacency = request.Adjacency?
                .OrderBy(adjacency => adjacency.Relation.ToString(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(adjacency => adjacency.Target, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MaterialsOnHand = request.MaterialsOnHand?
                .OrderBy(material => material.Material, StringComparer.OrdinalIgnoreCase)
                .ThenBy(material => material.ApproxQty)
                .ToList()
        };

    private static ZoneRequest CanonicalRequest(ZoneRequest request) =>
        request with
        {
            Adjacency = request.Adjacency?
                .OrderBy(adjacency => adjacency.Relation.ToString(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(adjacency => adjacency.Target, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Terrain = request.Terrain is null
                ? null
                : request.Terrain with
                {
                    PreferredTerrainDefs = request.Terrain.PreferredTerrainDefs?
                        .OrderBy(def => def, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                },
            AllowedItemDefs = request.AllowedItemDefs?
                .OrderBy(def => def, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            AllowedItemCategories = request.AllowedItemCategories?
                .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

    private sealed record BuildingPlacementCacheInput(
        BuildingRequest Request,
        string RefreshSource,
        MapInfoSnapshot Map,
        IReadOnlyList<WillieRoomAnchor> Anchors,
        IReadOnlyList<BuildingRecord> Buildings,
        WillieConstructionBacklog Backlog,
        IReadOnlyList<MaterialHint> MaterialsOnHand);

    private sealed record ZonePlacementCacheInput(
        ZoneRequest Request,
        string RefreshSource,
        MapInfoSnapshot Map,
        IReadOnlyList<WillieRoomAnchor> Anchors,
        IReadOnlyList<MapArea> Areas,
        IReadOnlyList<BuildingRecord> Buildings,
        IReadOnlyList<RoomRecord> Rooms,
        IReadOnlyList<StockpileZone> Stockpiles,
        TerrainCacheInput Terrain,
        IReadOnlyList<MapZoneRecord> Zones,
        IReadOnlyList<PlantRecord> Plants);

    private sealed record TerrainCacheInput(
        int Width,
        int Height,
        IReadOnlyList<NamedCount> CellCountsByDef,
        IReadOnlyList<NamedTerrainDef> DefsByName,
        IReadOnlyList<TerrainCellRecord> Cells);

    private sealed record NamedCount(string Name, int Count);

    private sealed record NamedTerrainDef(string Name, TerrainDefRecord Def);
}
