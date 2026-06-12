using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed class BedroomTemplate : IRoomTemplate
{
    public RoomClass RoomClass => RoomClass.Bedroom;

    public string Label => "Starter bedroom";

    public RectSize? SizeFor(CapacityNeed? need)
    {
        int occupants = need?.Measure switch
        {
            null => 1,
            CapacityMeasure.Occupants or CapacityMeasure.Beds => Math.Max(1, (int)Math.Ceiling(need.Amount ?? 1)),
            _ => -1
        };
        if (occupants < 0) return null;

        return new RectSize(Math.Max(4, occupants * 2 + 2), 4);
    }

    public RoomShell BuildShell(RectSize interior, DoorSide door)
    {
        List<TemplateAsset> fixtures = [];
        int beds = Math.Max(1, (interior.Width - 2) / 2);
        for (int bed = 0; bed < beds; bed++)
        {
            int x = 1 + bed * 2;
            fixtures.Add(new TemplateAsset("bed", "Bed", "WoodLog", new MapCell(x, 2), 2));
        }

        return RoomShellBuilder.Build(interior, door, "WoodPlankFloor", null, fixtures);
    }
}
