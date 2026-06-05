using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed class HeaterTemplate : IRoomTemplate
{
    public RoomClass RoomClass => RoomClass.Barracks;

    public BuildingClass? TargetClass => BuildingClass.Heater;

    public string Label => "Starter heater";

    public RectSize? SizeFor(CapacityNeed? need) => new RectSize(3, 3);

    public RoomShell BuildShell(RectSize interior, DoorSide door)
    {
        List<TemplateAsset> fixtures =
        [
            new("heater", "Heater", null, new MapCell(2, 2), 0)
        ];

        return RoomShellBuilder.Build(interior, door, "Concrete", null, fixtures);
    }
}
