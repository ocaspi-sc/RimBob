using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public static class DraftDedupe
{
    public static IReadOnlyList<PlacementDraft> ByCellsShapeAnchor(IReadOnlyList<PlacementDraft> drafts)
    {
        HashSet<string> seenCellKeys = new(StringComparer.Ordinal);
        HashSet<string> seenShapeAnchorKeys = new(StringComparer.Ordinal);
        List<PlacementDraft> unique = [];

        foreach (PlacementDraft draft in drafts)
        {
            string cellKey = CellKey(draft);
            string shapeAnchorKey = ShapeAnchorKey(draft);
            if (seenCellKeys.Contains(cellKey) || seenShapeAnchorKeys.Contains(shapeAnchorKey))
                continue;

            seenCellKeys.Add(cellKey);
            seenShapeAnchorKeys.Add(shapeAnchorKey);
            unique.Add(draft);
        }

        return unique;
    }

    private static string CellKey(PlacementDraft draft) =>
        string.Join(
            ";",
            draft.Group.Assets
                .Select(asset => asset.Cell)
                .Distinct()
                .OrderBy(cell => cell.X)
                .ThenBy(cell => cell.Z)
                .Select(cell => $"{cell.X},{cell.Z}"));

    private static string ShapeAnchorKey(PlacementDraft draft)
    {
        Footprint footprint = Footprint.From(draft.Group.Assets);
        return string.Join(
            "|",
            draft.SourceAnchor.Anchor.RoomId,
            footprint.MinX,
            footprint.MinZ,
            footprint.Width,
            footprint.Height);
    }
}

public sealed record Footprint(
    int MinX,
    int MinZ,
    int MaxX,
    int MaxZ,
    int Width,
    int Height)
{
    public static Footprint From(IReadOnlyList<BlueprintAsset> assets)
    {
        int minX = assets.Count == 0 ? 0 : assets.Min(asset => asset.Cell.X);
        int minZ = assets.Count == 0 ? 0 : assets.Min(asset => asset.Cell.Z);
        int maxX = assets.Count == 0 ? 0 : assets.Max(asset => asset.Cell.X);
        int maxZ = assets.Count == 0 ? 0 : assets.Max(asset => asset.Cell.Z);
        return new Footprint(
            MinX: minX,
            MinZ: minZ,
            MaxX: maxX,
            MaxZ: maxZ,
            Width: maxX - minX + 1,
            Height: maxZ - minZ + 1);
    }

    public double OriginDistanceTo(Footprint other)
    {
        int deltaX = MinX - other.MinX;
        int deltaZ = MinZ - other.MinZ;
        return Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
    }
}
