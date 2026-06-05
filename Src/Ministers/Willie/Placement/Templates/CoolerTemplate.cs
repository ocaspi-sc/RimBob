using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed class CoolerTemplate : IRoomTemplate
{
    public RoomClass RoomClass => RoomClass.Barracks;

    public BuildingClass? TargetClass => BuildingClass.Cooler;

    public string Label => "Starter cooler";

    public RectSize? SizeFor(CapacityNeed? need) => new RectSize(3, 3);

    public RoomShell BuildShell(RectSize interior, DoorSide door)
    {
        RectSize exterior = new(interior.Width + 2, interior.Height + 2);
        DoorSide coolerSide = RoomShellBuilder.Opposite(door);
        MapCell coolerCell = RoomShellBuilder.EdgeCenter(exterior, coolerSide);
        List<TemplateAsset> fixtures =
        [
            new("cooler", "Cooler", null, coolerCell, RoomShellBuilder.RotationFor(coolerSide))
        ];

        return RoomShellBuilder.Build(interior, door, "Concrete", null, fixtures);
    }
}
