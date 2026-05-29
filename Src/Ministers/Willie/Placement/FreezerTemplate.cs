using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public static class FreezerTemplate
{
    private const int FoodUnitsPerInteriorTile = 8;
    private const int DefaultInteriorTiles = 16;

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
        MapCell doorCell = EdgeCenter(exterior, door);
        DoorSide coolerSide = Opposite(door);
        MapCell coolerCell = EdgeCenter(exterior, coolerSide);
        List<TemplateAsset> assets = [];

        for (int z = 1; z <= interior.Height; z++)
        {
            for (int x = 1; x <= interior.Width; x++)
            {
                assets.Add(new TemplateAsset("floor", "Concrete", null, new MapCell(x, z), 0));
            }
        }

        for (int z = 0; z < exterior.Height; z++)
        {
            for (int x = 0; x < exterior.Width; x++)
            {
                MapCell cell = new(x, z);
                bool border = x == 0 || z == 0 || x == exterior.Width - 1 || z == exterior.Height - 1;
                if (!border || cell == doorCell || cell == coolerCell) continue;
                assets.Add(new TemplateAsset("wall", "Wall", "BlocksGranite", cell, 0));
            }
        }

        assets.Add(new TemplateAsset("door", "Door", "WoodLog", doorCell, RotationFor(door)));
        assets.Add(new TemplateAsset("cooler", "Cooler", null, coolerCell, RotationFor(coolerSide)));

        return new RoomShell(
            InteriorSize: interior,
            ExteriorSize: exterior,
            Door: door,
            Assets: assets,
            DoorCell: doorCell,
            AccessCell: AccessCell(exterior, door),
            CoolerCell: coolerCell);
    }

    private static MapCell EdgeCenter(RectSize size, DoorSide side) =>
        side switch
        {
            DoorSide.North => new MapCell(size.Width / 2, 0),
            DoorSide.East => new MapCell(size.Width - 1, size.Height / 2),
            DoorSide.South => new MapCell(size.Width / 2, size.Height - 1),
            _ => new MapCell(0, size.Height / 2)
        };

    private static MapCell AccessCell(RectSize size, DoorSide side) =>
        side switch
        {
            DoorSide.North => new MapCell(size.Width / 2, -1),
            DoorSide.East => new MapCell(size.Width, size.Height / 2),
            DoorSide.South => new MapCell(size.Width / 2, size.Height),
            _ => new MapCell(-1, size.Height / 2)
        };

    private static DoorSide Opposite(DoorSide side) =>
        side switch
        {
            DoorSide.North => DoorSide.South,
            DoorSide.East => DoorSide.West,
            DoorSide.South => DoorSide.North,
            _ => DoorSide.East
        };

    private static int RotationFor(DoorSide side) =>
        side switch
        {
            DoorSide.North => 0,
            DoorSide.East => 1,
            DoorSide.South => 2,
            _ => 3
        };
}
