using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public interface IRoomTemplate
{
    RoomClass RoomClass { get; }

    string Label { get; }

    RectSize? SizeFor(CapacityNeed? need);

    RoomShell BuildShell(RectSize interior, DoorSide door);
}
