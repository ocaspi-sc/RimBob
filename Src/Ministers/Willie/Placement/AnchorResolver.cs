using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;

namespace RimBob.Ministers.Willie;

public static class AnchorResolver
{
    private static readonly IReadOnlyDictionary<string, RoomClass> Aliases = new Dictionary<string, RoomClass>(StringComparer.Ordinal)
    {
        ["medbay"] = RoomClass.Hospital,
        ["infirmary"] = RoomClass.Hospital,
        ["clinic"] = RoomClass.Hospital,
        ["fridge"] = RoomClass.Freezer,
        ["cooler"] = RoomClass.Freezer,
        ["craftroom"] = RoomClass.Workshop,
        ["crafting"] = RoomClass.Workshop,
        ["shop"] = RoomClass.Workshop,
        ["lab"] = RoomClass.Research,
        ["bunks"] = RoomClass.Barracks,
        ["dorm"] = RoomClass.Barracks,
        ["pantry"] = RoomClass.Storage,
        ["warehouse"] = RoomClass.Storage
    };

    public static IReadOnlyList<ResolvedAnchor> ResolveNear(
        PlacementSpec spec,
        WillieBriefing briefing) =>
        ResolveNear(spec.Adjacency, briefing);

    public static IReadOnlyList<ResolvedAnchor> ResolveNear(
        IReadOnlyList<AdjacencyHint>? adjacency,
        WillieBriefing briefing,
        IReadOnlyList<StockpileZone>? stockpiles = null)
    {
        IReadOnlyList<AdjacencyHint> hints = adjacency ?? [];
        List<ResolvedAnchor> anchors = [];
        foreach (AdjacencyHint hint in hints.Where(hint => hint.Relation == AdjacencyRelation.Near))
        {
            RoomClass? targetClass = ParseRoomClass(hint.Target);
            if (targetClass is not null)
            {
                IEnumerable<WillieRoomAnchor> matching = briefing.AnchorInventory.Anchors
                    .Where(anchor => anchor.Class == targetClass)
                    .OrderBy(anchor => anchor.RoomId, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(anchor => anchor.CellsCount);

                foreach (WillieRoomAnchor anchor in matching)
                {
                    ResolvedAnchor? resolved = ResolveAnchor(anchor);
                    if (resolved is not null) anchors.Add(resolved);
                }
            }

            if (stockpiles is not null && IsStorageTarget(hint.Target))
                anchors.AddRange(ResolveStockpileAnchors(stockpiles));
        }

        return anchors;
    }

    public static IReadOnlyList<ResolvedAnchor> ResolveBuildableRegion(WillieBriefing briefing) =>
        briefing.AnchorInventory.Anchors
            .Where(anchor => anchor.Class == RoomClass.BuildableRegion)
            .OrderBy(anchor => anchor.RoomId, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(anchor => anchor.CellsCount)
            .Select(ResolveBuildableRegionAnchor)
            .OfType<ResolvedAnchor>()
            .ToList();

    private static ResolvedAnchor? ResolveAnchor(WillieRoomAnchor anchor)
    {
        MapPosition? entryCell = anchor.EntryCells
            .OrderBy(cell => cell.X)
            .ThenBy(cell => cell.Z)
            .FirstOrDefault();
        if (entryCell is not null)
            return new ResolvedAnchor(anchor, entryCell, AnchorMatchReason.EntryCells);

        if (anchor.Centroid is not null)
            return new ResolvedAnchor(anchor, anchor.Centroid, AnchorMatchReason.CentroidFallback);

        return null;
    }

    private static ResolvedAnchor? ResolveBuildableRegionAnchor(WillieRoomAnchor anchor)
    {
        MapPosition? entryCell = anchor.EntryCells
            .OrderBy(cell => cell.X)
            .ThenBy(cell => cell.Z)
            .FirstOrDefault();
        if (entryCell is not null)
            return new ResolvedAnchor(anchor, entryCell, AnchorMatchReason.EntryCells);

        MapPosition? targetCell = BuildableRegionTargetCell(anchor);
        if (targetCell is not null)
            return new ResolvedAnchor(anchor, targetCell, AnchorMatchReason.BuildableRegionFallback);

        return null;
    }

    private static MapPosition? BuildableRegionTargetCell(WillieRoomAnchor anchor)
    {
        MapPosition? preferred = anchor.Bounds is not null
            ? InteriorPoint(anchor.Bounds)
            : anchor.Centroid;
        if (anchor.Cells.Count > 0)
            return ClosestCell(anchor.Cells, preferred);

        if (anchor.Bounds is not null)
            return InteriorPoint(anchor.Bounds);

        return anchor.Centroid;
    }

    private static MapPosition ClosestCell(
        IReadOnlyList<MapPosition> cells,
        MapPosition? preferred)
    {
        return cells
            .OrderBy(cell => preferred is null ? 0 : Manhattan(cell, preferred))
            .ThenBy(cell => cell.X)
            .ThenBy(cell => cell.Z)
            .First();
    }

    private static MapPosition InteriorPoint(MapRect bounds)
    {
        int width = bounds.X2 - bounds.X1 + 1;
        int height = bounds.Z2 - bounds.Z1 + 1;
        int xOffset = width <= 1 ? 0 : Math.Max(1, width / 3);
        int zOffset = height <= 1 ? 0 : Math.Max(1, height / 3);
        int x = Math.Min(bounds.X2, bounds.X1 + xOffset);
        int z = Math.Min(bounds.Z2, bounds.Z1 + zOffset);
        return new MapPosition(x, 0, z);
    }

    private static int Manhattan(MapPosition a, MapPosition b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z);

    private static IReadOnlyList<ResolvedAnchor> ResolveStockpileAnchors(IReadOnlyList<StockpileZone> stockpiles)
    {
        List<ResolvedAnchor> anchors = [];
        foreach (StockpileZone stockpile in stockpiles
                     .Where(stockpile => stockpile.Center is not null)
                     .OrderBy(stockpile => stockpile.Id, StringComparer.OrdinalIgnoreCase))
        {
            MapPosition center = stockpile.Center!;
            WillieRoomAnchor anchor = new(
                $"stockpile:{stockpile.Id}",
                RoomClass.Storage,
                stockpile.Label ?? "stockpile",
                stockpile.CellCount,
                center,
                [])
            {
                Bounds = BoundsFor(stockpile.Cells),
                Cells = stockpile.Cells
            };
            anchors.Add(new ResolvedAnchor(anchor, center, AnchorMatchReason.StockpileCenter));
        }

        return anchors;
    }

    private static MapPosition? CenterOf(MapRect? bounds)
    {
        if (bounds is null) return null;
        int x = (int)Math.Round((bounds.X1 + bounds.X2) / 2d);
        int z = (int)Math.Round((bounds.Z1 + bounds.Z2) / 2d);
        return new MapPosition(x, 0, z);
    }

    private static RoomClass? ParseRoomClass(string target)
    {
        string normalizedTarget = Normalize(target);
        foreach (RoomClass roomClass in Enum.GetValues<RoomClass>())
        {
            if (Normalize(roomClass.ToString()) == normalizedTarget)
                return roomClass;
        }

        return Aliases.TryGetValue(normalizedTarget, out RoomClass alias)
            ? alias
            : null;
    }

    private static bool IsStorageTarget(string target)
    {
        string normalizedTarget = Normalize(target);
        return normalizedTarget.Contains("storage", StringComparison.OrdinalIgnoreCase) ||
            normalizedTarget.Contains("stockpile", StringComparison.OrdinalIgnoreCase);
    }

    private static MapRect? BoundsFor(IReadOnlyList<MapPosition> cells)
    {
        if (cells.Count == 0) return null;

        int minX = cells.Min(cell => cell.X);
        int maxX = cells.Max(cell => cell.X);
        int minZ = cells.Min(cell => cell.Z);
        int maxZ = cells.Max(cell => cell.Z);
        return new MapRect(minX, minZ, maxX, maxZ);
    }

    private static string Normalize(string value)
    {
        char[] chars = value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }
}
