using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed class RecreationTemplate : IRoomTemplate
{
    public RoomClass RoomClass => RoomClass.Recreation;

    public string Label => "Starter recreation room";

    public RectSize? SizeFor(CapacityNeed? need)
    {
        int occupants = CapacitySizing.CountOrDefault(need, CapacityMeasure.Occupants, defaultCount: 3);
        if (occupants < 0) return null;

        return CapacitySizing.NearSquareFromTiles(occupants * 4, minimumWidth: 5, minimumHeight: 5);
    }

    public RoomShell BuildShell(RectSize interior, DoorSide door)
    {
        List<TemplateAsset> fixtures =
        [
            new("joy_source", "HorseshoesPin", "WoodLog", new MapCell(Math.Max(1, interior.Width / 2), Math.Max(1, interior.Height / 2)), 0)
        ];

        return RoomShellBuilder.Build(interior, door, "WoodPlankFloor", null, fixtures);
    }
}
