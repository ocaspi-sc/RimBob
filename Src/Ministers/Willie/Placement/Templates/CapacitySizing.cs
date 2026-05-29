using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

internal static class CapacitySizing
{
    public static int CountOrDefault(CapacityNeed? need, CapacityMeasure measure, int defaultCount)
    {
        if (need is null)
            return defaultCount;

        if (need.Measure != measure)
            return -1;

        return Math.Max(1, (int)Math.Ceiling(need.Amount ?? defaultCount));
    }

    public static RectSize NearSquareFromTiles(int requiredTiles, int minimumWidth, int minimumHeight)
    {
        int clampedTiles = Math.Max(requiredTiles, minimumWidth * minimumHeight);
        int width = Math.Max(minimumWidth, (int)Math.Ceiling(Math.Sqrt(clampedTiles)));
        int height = Math.Max(minimumHeight, (int)Math.Ceiling((double)clampedTiles / width));
        return new RectSize(width, height);
    }
}
