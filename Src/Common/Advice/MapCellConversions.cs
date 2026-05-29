using RimBob.Core.Aggregates;

namespace RimBob.Core.Advice;

public static class MapCellConversions
{
    public static MapCell ToMapCell(this MapPosition position) =>
        new(position.X, position.Z);

    public static MapPosition ToMapPosition(this MapCell cell, int y = 0) =>
        new(cell.X, y, cell.Z);
}
