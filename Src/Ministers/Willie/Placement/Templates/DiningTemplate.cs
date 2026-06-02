using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed class DiningTemplate : IRoomTemplate
{
    public RoomClass RoomClass => RoomClass.Dining;

    public string Label => "Starter dining room";

    public RectSize? SizeFor(CapacityNeed? need)
    {
        int occupants = CapacitySizing.CountOrDefault(need, CapacityMeasure.Occupants, defaultCount: 3);
        if (occupants < 0) return null;

        return new RectSize(Math.Max(5, occupants + 2), 5);
    }

    public RoomShell BuildShell(RectSize interior, DoorSide door)
    {
        int centerX = Math.Max(1, interior.Width / 2);
        int centerZ = Math.Max(1, interior.Height / 2);
        List<TemplateAsset> fixtures =
        [
            new("table", "Table1x2c", "WoodLog", new MapCell(centerX, centerZ), 0),
            new("seat", "DiningChair", "WoodLog", new MapCell(Math.Max(1, centerX - 1), centerZ), 1),
            new("seat", "DiningChair", "WoodLog", new MapCell(Math.Min(interior.Width, centerX + 2), centerZ), 3)
        ];

        return RoomShellBuilder.Build(interior, door, "WoodPlankFloor", null, fixtures);
    }
}
