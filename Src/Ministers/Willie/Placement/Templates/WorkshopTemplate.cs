using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed class WorkshopTemplate : IRoomTemplate
{
    public RoomClass RoomClass => RoomClass.Workshop;

    public string Label => "Starter workshop";

    public RectSize? SizeFor(CapacityNeed? need)
    {
        int workSlots = CapacitySizing.CountOrDefault(need, CapacityMeasure.WorkSlots, defaultCount: 1);
        if (workSlots < 0) return null;

        // tune: a 2x4-ish bench footprint plus stand cell needs a wider bay than bedrooms.
        return new RectSize(Math.Max(6, workSlots * 4 + 2), 6);
    }

    public RoomShell BuildShell(RectSize interior, DoorSide door)
    {
        List<TemplateAsset> fixtures = [];
        int benches = Math.Max(1, (interior.Width - 2) / 4);
        for (int bench = 0; bench < benches; bench++)
        {
            int x = 1 + bench * 4;
            fixtures.Add(new TemplateAsset("workbench", "ElectricTailoringBench", "Steel", new MapCell(x, 2), 1));
        }

        return RoomShellBuilder.Build(interior, door, "Concrete", null, fixtures);
    }
}
