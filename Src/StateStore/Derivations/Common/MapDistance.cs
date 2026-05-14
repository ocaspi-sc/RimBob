using RimAI.Core.Aggregates;

namespace RimAI.State.Derivations.Common;

public static class MapDistance
{
    public static int Manhattan(MapPosition a, MapPosition b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z);

    public static int? Nearest(IEnumerable<MapPosition?> from, MapPosition? to)
    {
        if (to is null) return null;
        IReadOnlyList<MapPosition> positions = from
            .Where(position => position is not null)
            .Cast<MapPosition>()
            .ToList();
        if (positions.Count == 0) return null;
        return positions.Min(position => Manhattan(position, to));
    }

    public static int? Nearest(IReadOnlyList<MapPosition> from, IReadOnlyList<MapPosition> to)
    {
        if (from.Count == 0 || to.Count == 0) return null;
        return from.Min(a => to.Min(b => Manhattan(a, b)));
    }

    public static string? ProximityLabel(int? distance, string? reference = null)
    {
        if (distance is null) return null;
        string bucket = distance.Value switch
        {
            <= 8 => "adjacent",
            <= 25 => "nearby",
            <= 60 => "moderate",
            _ => "far"
        };
        return reference is null
            ? $"{bucket} ({distance.Value} cells)"
            : $"{bucket} ({distance.Value} cells from {reference})";
    }
}
