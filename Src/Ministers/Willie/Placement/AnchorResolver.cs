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
        WillieBriefing briefing)
    {
        List<ResolvedAnchor> anchors = [];
        foreach (AdjacencyHint hint in spec.Adjacency.Where(hint => hint.Relation == AdjacencyRelation.Near))
        {
            RoomClass? targetClass = ParseRoomClass(hint.Target);
            if (targetClass is null) continue;

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

        return anchors;
    }

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

    private static string Normalize(string value)
    {
        char[] chars = value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }
}
