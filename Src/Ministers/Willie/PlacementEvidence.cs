using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.State.Derivations.Common;

namespace RimBob.Ministers.Willie;

public sealed class PlacementEvidence
{
    public const int MaxFreeSpaceScanCells = 65_536;
    public const int MaxFreeRects = 64;

    private readonly HashSet<MapCell> occupiedCells;

    private PlacementEvidence(
        int mapId,
        MapBounds? bounds,
        HashSet<MapCell> occupiedCells,
        IReadOnlyList<ResolvedAnchor> anchors,
        IReadOnlyList<ExistingRoomFootprint> roomFootprints,
        IReadOnlyList<FreeRect> freeRects,
        FreeRect? buildableRegionBounds,
        bool freeSpaceScanTruncated)
    {
        MapId = mapId;
        Bounds = bounds;
        this.occupiedCells = occupiedCells;
        Anchors = anchors;
        RoomFootprints = roomFootprints;
        FreeRects = freeRects;
        BuildableRegionBounds = buildableRegionBounds;
        FreeSpaceScanTruncated = freeSpaceScanTruncated;
    }

    public int MapId { get; }

    public MapBounds? Bounds { get; }

    public IReadOnlyList<ResolvedAnchor> Anchors { get; }

    public IReadOnlyList<ExistingRoomFootprint> RoomFootprints { get; }

    public IReadOnlyList<FreeRect> FreeRects { get; }

    public FreeRect? BuildableRegionBounds { get; }

    public bool FreeSpaceScanTruncated { get; }

    // TODO: BuildingRecord.Position is a single cell, not a footprint; occupancy is approximate until per-building footprints land.
    public bool OccupancyIsPointApprox { get; } = true;

    public static PlacementEvidence Build(
        MapInfoSnapshot map,
        BuildingRegistry buildings,
        IReadOnlyList<ResolvedAnchor> anchors,
        IReadOnlyList<WillieRoomAnchor>? roomAnchors = null,
        TerrainSnapshot? terrain = null,
        ThingRegistry? things = null,
        MapZoneRegistry? zones = null,
        StockpileLedger? stockpiles = null)
    {
        MapBounds? bounds = MapBounds.Parse(map.Size);
        IReadOnlyList<ExistingRoomFootprint> roomFootprints = BuildRoomFootprints(roomAnchors ?? []);
        FreeRect? buildableRegionBounds = ResolveBuildableRegionBounds(anchors);
        HashSet<MapCell> occupied = BuildBlockedCells(
            buildings,
            terrain,
            things,
            zones,
            stockpiles);
        FreeRectScanResult freeSpace = BuildFreeRects(ScanBounds(bounds, buildableRegionBounds), occupied.Contains);

        return new PlacementEvidence(
            map.Id,
            bounds,
            occupied,
            anchors,
            roomFootprints,
            freeSpace.Rects,
            buildableRegionBounds,
            freeSpace.ScanTruncated);
    }

    public bool InBounds(MapCell cell) =>
        Bounds is not null && Bounds.Contains(cell);

    public bool IsOccupied(MapCell cell) =>
        occupiedCells.Contains(cell);

    public bool InBuildableRegion(MapCell cell) =>
        BuildableRegionBounds is null || BuildableRegionBounds.Contains(cell);

    public int CountFreeExpansionTilesAround(IReadOnlyList<BlueprintAsset> assets)
    {
        if (Bounds is null || assets.Count == 0) return 0;

        HashSet<MapCell> assetCells = assets.Select(asset => asset.Cell).ToHashSet();
        int minX = assetCells.Min(cell => cell.X);
        int maxX = assetCells.Max(cell => cell.X);
        int minZ = assetCells.Min(cell => cell.Z);
        int maxZ = assetCells.Max(cell => cell.Z);
        HashSet<MapCell> expansionCells = [];

        for (int x = minX - 1; x <= maxX + 1; x++)
        {
            for (int z = minZ - 1; z <= maxZ + 1; z++)
            {
                bool insideFootprintBounds =
                    x >= minX &&
                    x <= maxX &&
                    z >= minZ &&
                    z <= maxZ;
                if (insideFootprintBounds) continue;

                MapCell cell = new(x, z);
                if (!InBounds(cell) || IsOccupied(cell) || assetCells.Contains(cell))
                    continue;

                expansionCells.Add(cell);
            }
        }

        return expansionCells.Count;
    }

    public static FreeRectScanResult BuildFreeRects(
        MapBounds? bounds,
        IReadOnlySet<MapCell> blocked)
    {
        if (bounds is null) return new FreeRectScanResult([], false);

        MapRect scanBounds = new(0, 0, bounds.Width - 1, bounds.Height - 1);
        return BuildFreeRects(scanBounds, blocked.Contains);
    }

    private static MapRect? ScanBounds(MapBounds? bounds, FreeRect? buildableRegionBounds)
    {
        if (buildableRegionBounds is not null)
        {
            return new MapRect(
                buildableRegionBounds.MinX,
                buildableRegionBounds.MinZ,
                buildableRegionBounds.MaxXExclusive - 1,
                buildableRegionBounds.MaxZExclusive - 1);
        }

        return bounds is null
            ? null
            : new MapRect(0, 0, bounds.Width - 1, bounds.Height - 1);
    }

    private static HashSet<MapCell> BuildBlockedCells(
        BuildingRegistry buildings,
        TerrainSnapshot? terrain,
        ThingRegistry? things,
        MapZoneRegistry? zones,
        StockpileLedger? stockpiles)
    {
        HashSet<MapCell> blocked = buildings.Buildings
            .Select(building => building.Position)
            .Where(position => position is not null)
            .Cast<MapPosition>()
            .Select(position => position.ToMapCell())
            .ToHashSet();

        AddPositions(blocked, things?.Things.Select(thing => thing.Position));
        AddPositions(blocked, zones?.Zones
            .Where(zone => zone.IsGrowing || zone.IsStockpile)
            .SelectMany(zone => zone.Cells));
        AddPositions(blocked, stockpiles?.Zones.SelectMany(zone => zone.Cells));

        if (terrain?.HasCoordinateGrid == true)
        {
            foreach (TerrainCellRecord cell in terrain.Cells.Where(cell => !CellSupportsBuilding(terrain, cell)))
                blocked.Add(new MapCell(cell.X, cell.Z));
        }

        return blocked;
    }

    private static bool CellSupportsBuilding(TerrainSnapshot terrain, TerrainCellRecord cell)
    {
        if (terrain.DefsByName.TryGetValue(cell.TerrainDef, out TerrainDefRecord? def))
            return def.SupportsStockpile;

        return cell.SupportsStockpile;
    }

    private static void AddPositions(
        HashSet<MapCell> blocked,
        IEnumerable<MapPosition?>? positions)
    {
        if (positions is null) return;

        foreach (MapPosition? position in positions)
        {
            if (position is null) continue;
            blocked.Add(position.ToMapCell());
        }
    }

    public static FreeRectScanResult BuildFreeRects(
        MapRect? bounds,
        Func<MapCell, bool> isBlocked)
    {
        if (bounds is null) return new FreeRectScanResult([], false);

        int width = bounds.X2 - bounds.X1 + 1;
        int height = bounds.Z2 - bounds.Z1 + 1;
        if (width <= 0 || height <= 0)
            return new FreeRectScanResult([], false);

        int scanCells = width * height;
        if (scanCells > MaxFreeSpaceScanCells)
            return new FreeRectScanResult([], true);

        int[,] clearRunRight = BuildClearRunRight(bounds, width, height, isBlocked);
        List<FreeRect> candidates = [];
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                FreeRect? bestFromOrigin = LargestRectFromOrigin(clearRunRight, bounds, x, z, height);
                if (bestFromOrigin is not null)
                    candidates.Add(bestFromOrigin);
            }
        }

        return new FreeRectScanResult(ReduceFreeRects(candidates), false);
    }

    private static int[,] BuildClearRunRight(
        MapRect bounds,
        int width,
        int height,
        Func<MapCell, bool> isBlocked)
    {
        int[,] clearRunRight = new int[width, height];
        for (int z = 0; z < height; z++)
        {
            int run = 0;
            for (int x = width - 1; x >= 0; x--)
            {
                MapCell cell = new(bounds.X1 + x, bounds.Z1 + z);
                if (isBlocked(cell))
                {
                    run = 0;
                    clearRunRight[x, z] = 0;
                    continue;
                }

                run++;
                clearRunRight[x, z] = run;
            }
        }

        return clearRunRight;
    }

    private static FreeRect? LargestRectFromOrigin(
        int[,] clearRunRight,
        MapRect bounds,
        int originX,
        int originZ,
        int mapHeight)
    {
        if (clearRunRight[originX, originZ] == 0) return null;

        int minWidth = int.MaxValue;
        FreeRect? best = null;
        for (int z = originZ; z < mapHeight; z++)
        {
            int rowRun = clearRunRight[originX, z];
            if (rowRun == 0) break;

            minWidth = Math.Min(minWidth, rowRun);
            int height = z - originZ + 1;
            FreeRect candidate = new(new MapCell(bounds.X1 + originX, bounds.Z1 + originZ), minWidth, height);
            if (best is null ||
                candidate.Area > best.Area ||
                (candidate.Area == best.Area && candidate.Width > best.Width))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static IReadOnlyList<FreeRect> ReduceFreeRects(IReadOnlyList<FreeRect> candidates)
    {
        List<FreeRect> ordered = candidates
            .OrderByDescending(rect => rect.Area)
            .ThenBy(rect => rect.MinX)
            .ThenBy(rect => rect.MinZ)
            .ThenByDescending(rect => rect.Width)
            .ThenByDescending(rect => rect.Height)
            .ToList();
        List<FreeRect> reduced = [];
        foreach (FreeRect candidate in ordered)
        {
            if (reduced.Any(rect => rect.Contains(candidate)))
                continue;

            reduced.Add(candidate);
            if (reduced.Count >= MaxFreeRects)
                break;
        }

        return reduced;
    }

    public sealed record FreeRectScanResult(
        IReadOnlyList<FreeRect> Rects,
        bool ScanTruncated);

    private static FreeRect? ResolveBuildableRegionBounds(IReadOnlyList<ResolvedAnchor> anchors)
    {
        if (anchors.Count == 0 ||
            anchors.Any(anchor => anchor.Anchor.Class != RoomClass.BuildableRegion))
        {
            return null;
        }

        MapRect? bounds = anchors
            .Select(anchor => anchor.Anchor.Bounds)
            .FirstOrDefault(bounds => bounds is not null);
        if (bounds is null) return null;

        int width = bounds.X2 - bounds.X1 + 1;
        int height = bounds.Z2 - bounds.Z1 + 1;
        if (width <= 0 || height <= 0) return null;

        return new FreeRect(new MapCell(bounds.X1, bounds.Z1), width, height);
    }

    private static IReadOnlyList<ExistingRoomFootprint> BuildRoomFootprints(
        IReadOnlyList<WillieRoomAnchor> roomAnchors)
    {
        return roomAnchors
            .Select(TryBuildRoomFootprint)
            .OfType<ExistingRoomFootprint>()
            .OrderBy(footprint => footprint.Anchor.RoomId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ExistingRoomFootprint? TryBuildRoomFootprint(WillieRoomAnchor anchor)
    {
        HashSet<MapCell> cells = anchor.Cells
            .Select(cell => cell.ToMapCell())
            .ToHashSet();
        if (cells.Count == 0)
            return null;

        FreeRect bounds = BoundsFor(cells);
        IReadOnlyList<MapCell> entryCells = anchor.EntryCells
            .Select(cell => cell.ToMapCell())
            .OrderBy(cell => cell.X)
            .ThenBy(cell => cell.Z)
            .ToList();
        return new ExistingRoomFootprint(anchor, cells, bounds, entryCells);
    }

    private static FreeRect BoundsFor(IReadOnlySet<MapCell> cells)
    {
        int minX = cells.Min(cell => cell.X);
        int maxX = cells.Max(cell => cell.X);
        int minZ = cells.Min(cell => cell.Z);
        int maxZ = cells.Max(cell => cell.Z);
        return new FreeRect(new MapCell(minX, minZ), maxX - minX + 1, maxZ - minZ + 1);
    }
}
