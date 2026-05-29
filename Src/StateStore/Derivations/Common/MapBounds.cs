using System.Text.RegularExpressions;
using RimBob.Core.Advice;

namespace RimBob.State.Derivations.Common;

public sealed partial record MapBounds(int Width, int Height)
{
    public bool Contains(MapCell cell) =>
        cell.X >= 0 &&
        cell.Z >= 0 &&
        cell.X < Width &&
        cell.Z < Height;

    public static MapBounds? Parse(string? size)
    {
        if (string.IsNullOrWhiteSpace(size)) return null;

        Match compact = CompactSizePattern().Match(size);
        if (compact.Success)
        {
            return ParsePositive(
                compact.Groups["width"].Value,
                compact.Groups["height"].Value);
        }

        Match vector = VectorSizePattern().Match(size);
        if (vector.Success)
        {
            return ParsePositive(
                vector.Groups["width"].Value,
                vector.Groups["height"].Value);
        }

        return null;
    }

    private static MapBounds? ParsePositive(string widthText, string heightText)
    {
        if (!int.TryParse(widthText, out int width) ||
            !int.TryParse(heightText, out int height) ||
            width <= 0 ||
            height <= 0)
        {
            return null;
        }

        return new MapBounds(width, height);
    }

    [GeneratedRegex(@"^\s*(?<width>\d+)\s*x\s*(?<height>\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CompactSizePattern();

    [GeneratedRegex(@"^\s*\(\s*(?<width>\d+)\s*,\s*\d+\s*,\s*(?<height>\d+)\s*\)\s*$")]
    private static partial Regex VectorSizePattern();
}
