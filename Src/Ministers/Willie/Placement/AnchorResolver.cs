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

        if (anchor.Centroid is not null)
            return new ResolvedAnchor(anchor, anchor.Centroid, AnchorMatchReason.BuildableRegionFallback);

        MapPosition? boundsCenter = CenterOf(anchor.Bounds);
        return boundsCenter is null
            ? null
            : new ResolvedAnchor(anchor, boundsCenter, AnchorMatchReason.BuildableRegionFallback);
    }

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
