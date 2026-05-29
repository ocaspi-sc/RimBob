using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed class HospitalTemplate : IRoomTemplate
{
    public RoomClass RoomClass => RoomClass.Hospital;

    public string Label => "Starter hospital";

    public RectSize? SizeFor(CapacityNeed? need)
    {
        int beds = CapacitySizing.CountOrDefault(need, CapacityMeasure.Beds, defaultCount: 2);
        if (beds < 0) return null;

        // tune: one medical bed per two interior columns leaves a center walkway.
        return new RectSize(Math.Max(5, beds * 2 + 1), 5);
    }

    public RoomShell BuildShell(RectSize interior, DoorSide door)
    {
        List<TemplateAsset> fixtures = [];
        int bedSlots = Math.Max(1, (interior.Width - 1) / 2);
        for (int slot = 0; slot < bedSlots; slot++)
        {
            int x = 1 + slot * 2;
            fixtures.Add(new TemplateAsset("medical_bed", "Bed", "Steel", new MapCell(x, 1), 2));
        }

        return RoomShellBuilder.Build(interior, door, "SterileTile", null, fixtures);
    }
}
