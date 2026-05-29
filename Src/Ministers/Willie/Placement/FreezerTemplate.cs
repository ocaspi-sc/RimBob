using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed class FreezerTemplate : IRoomTemplate
{
    private const int FoodUnitsPerInteriorTile = 8;
    private const int DefaultInteriorTiles = 16;

    public RoomClass RoomClass => RoomClass.Freezer;

    public string Label => "Starter freezer";

    public static RectSize? SizeFor(CapacityNeed? need)
    {
        if (need is not null && need.Measure != CapacityMeasure.FoodUnits)
            return null;

        int requiredTiles = need?.Amount is null
            ? DefaultInteriorTiles
            : Math.Max(DefaultInteriorTiles, (int)Math.Ceiling(need.Amount.Value / FoodUnitsPerInteriorTile));
        int width = (int)Math.Ceiling(Math.Sqrt(requiredTiles));
        int height = (int)Math.Ceiling((double)requiredTiles / width);
        return new RectSize(width, height);
    }

    public static RoomShell BuildShell(RectSize interior, DoorSide door)
    {
        RectSize exterior = new(interior.Width + 2, interior.Height + 2);
        DoorSide coolerSide = RoomShellBuilder.Opposite(door);
        MapCell coolerCell = RoomShellBuilder.EdgeCenter(exterior, coolerSide);
        return RoomShellBuilder.Build(
            interior,
            door,
            floorDefName: "Concrete",
            floorStuffDefName: null,
            fixtures:
            [
                new TemplateAsset("cooler", "Cooler", null, coolerCell, RoomShellBuilder.RotationFor(coolerSide))
            ]);
    }

    RectSize? IRoomTemplate.SizeFor(CapacityNeed? need) => SizeFor(need);

    RoomShell IRoomTemplate.BuildShell(RectSize interior, DoorSide door) => BuildShell(interior, door);
}
