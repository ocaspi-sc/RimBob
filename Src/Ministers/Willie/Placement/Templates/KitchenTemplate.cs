using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed class KitchenTemplate : IRoomTemplate
{
    public RoomClass RoomClass => RoomClass.Kitchen;

    public string Label => "Starter kitchen";

    public RectSize? SizeFor(CapacityNeed? need)
    {
        int workSlots = CapacitySizing.CountOrDefault(need, CapacityMeasure.WorkSlots, defaultCount: 1);
        if (workSlots < 0) return null;

        return new RectSize(Math.Max(5, workSlots * 3 + 2), 5);
    }

    public RoomShell BuildShell(RectSize interior, DoorSide door)
    {
        List<TemplateAsset> fixtures = [];
        int stoves = Math.Max(1, (interior.Width - 1) / 3);
        for (int stove = 0; stove < stoves; stove++)
        {
            int x = 1 + stove * 3;
            fixtures.Add(new TemplateAsset("stove", "FueledStove", null, new MapCell(x, 2), 1));
        }

        return RoomShellBuilder.Build(interior, door, "Concrete", null, fixtures);
    }
}
