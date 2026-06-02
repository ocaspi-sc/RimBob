using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed class BarracksTemplate : IRoomTemplate
{
    private readonly BedroomTemplate bedLayout = new();

    public RoomClass RoomClass => RoomClass.Barracks;

    public string Label => "Starter barracks";

    public RectSize? SizeFor(CapacityNeed? need) => bedLayout.SizeFor(need);

    public RoomShell BuildShell(RectSize interior, DoorSide door) =>
        bedLayout.BuildShell(interior, door);
}
