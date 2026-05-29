using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed class StorageTemplate : IRoomTemplate
{
    public RoomClass RoomClass => RoomClass.Storage;

    public string Label => "Starter storage";

    public RectSize? SizeFor(CapacityNeed? need)
    {
        int stacks = CapacitySizing.CountOrDefault(need, CapacityMeasure.StorageStacks, defaultCount: 12);
        if (stacks < 0) return null;

        return CapacitySizing.NearSquareFromTiles(stacks, minimumWidth: 4, minimumHeight: 4);
    }

    public RoomShell BuildShell(RectSize interior, DoorSide door)
    {
        List<TemplateAsset> fixtures = [];
        int shelfCount = Math.Min(8, Math.Max(1, interior.Width * interior.Height / 4));
        for (int shelf = 0; shelf < shelfCount; shelf++)
        {
            int x = 1 + shelf % Math.Max(1, interior.Width - 1);
            int z = 1 + shelf / Math.Max(1, interior.Width - 1);
            if (z > interior.Height) break;
            fixtures.Add(new TemplateAsset("shelf", "Shelf", "WoodLog", new MapCell(x, z), 0));
        }

        return RoomShellBuilder.Build(interior, door, "Concrete", null, fixtures);
    }
}
