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
        bool freeSpaceScanTruncated)
    {
        MapId = mapId;
        Bounds = bounds;
        this.occupiedCells = occupiedCells;
        Anchors = anchors;
        RoomFootprints = roomFootprints;
        FreeRects = freeRects;
        FreeSpaceScanTruncated = freeSpaceScanTruncated;
    }

    public int MapId { get; }

    public MapBounds? Bounds { get; }

    public IReadOnlyList<ResolvedAnchor> Anchors { get; }

    public IReadOnlyList<ExistingRoomFootprint> RoomFootprints { get; }

    // TODO: terrain affordance still absent (rimapi-buildability-layers); free-space is occupancy-only.
    public IReadOnlyList<FreeRect> FreeRects { get; }

    public bool FreeSpaceScanTruncated { get; }

    // TODO: BuildingRecord.Position is a single cell, not a footprint; occupancy is approximate until per-building footprints land.
    public bool OccupancyIsPointApprox { get; } = true;

    public static PlacementEvidence Build(
        MapInfoSnapshot map,
        BuildingRegistry buildings,
        IReadOnlyList<ResolvedAnchor> anchors,
        IReadOnlyList<WillieRoomAnchor>? roomAnchors = null)
    {
        HashSet<MapCell> occupied = buildings.Buildings
            .Select(building => building.Position)
            .Where(position => position is not null)
            .Cast<MapPosition>()
            .Select(position => position.ToMapCell())
            .ToHashSet();

        MapBounds? bounds = MapBounds.Parse(map.Size);
        FreeSpaceScanResult freeSpace = BuildFreeRects(bounds, occupied);
        IReadOnlyList<ExistingRoomFootprint> roomFootprints = BuildRoomFootprints(roomAnchors ?? []);

        return new PlacementEvidence(
            map.Id,
            bounds,
            occupied,
            anchors,
            roomFootprints,
            freeSpace.Rects,
            freeSpace.ScanTruncated);
    }

    public bool InBounds(MapCell cell) =>
        Bounds is not null && Bounds.Contains(cell);

    public bool IsOccupied(MapCell cell) =>
        occupiedCells.Contains(cell);

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

    private static FreeSpaceScanResult BuildFreeRects(
        MapBounds? bounds,
        HashSet<MapCell> occupied)
    {
        if (bounds is null) return new FreeSpaceScanResult([], false);

        int scanCells = bounds.Width * bounds.Height;
        if (scanCells > MaxFreeSpaceScanCells)
            return new FreeSpaceScanResult([], true);

        int[,] clearRunRight = BuildClearRunRight(bounds, occupied);
        List<FreeRect> candidates = [];
        for (int z = 0; z < bounds.Height; z++)
        {
            for (int x = 0; x < bounds.Width; x++)
            {
                FreeRect? bestFromOrigin = LargestRectFromOrigin(clearRunRight, x, z, bounds.Height);
                if (bestFromOrigin is not null)
                    candidates.Add(bestFromOrigin);
            }
        }

        return new FreeSpaceScanResult(ReduceFreeRects(candidates), false);
    }

    private static int[,] BuildClearRunRight(
        MapBounds bounds,
        HashSet<MapCell> occupied)
    {
        int[,] clearRunRight = new int[bounds.Width, bounds.Height];
        for (int z = 0; z < bounds.Height; z++)
        {
            int run = 0;
            for (int x = bounds.Width - 1; x >= 0; x--)
            {
                if (occupied.Contains(new MapCell(x, z)))
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
            FreeRect candidate = new(new MapCell(originX, originZ), minWidth, height);
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

    private sealed record FreeSpaceScanResult(
        IReadOnlyList<FreeRect> Rects,
        bool ScanTruncated);

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
