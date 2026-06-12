using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public static class RoomShellBuilder
{
    public static RoomShell Build(
        RectSize interior,
        DoorSide door,
        string floorDefName,
        string? floorStuffDefName,
        IReadOnlyList<TemplateAsset> fixtures)
    {
        RectSize exterior = new(interior.Width + 2, interior.Height + 2);
        MapCell doorCell = EdgeCenter(exterior, door);
        HashSet<MapCell> fixtureCells = fixtures
            .SelectMany(FixtureFootprintCells)
            .ToHashSet();
        List<TemplateAsset> assets = [];

        for (int z = 1; z <= interior.Height; z++)
        {
            for (int x = 1; x <= interior.Width; x++)
            {
                if (fixtureCells.Contains(new MapCell(x, z))) continue;

                assets.Add(new TemplateAsset("floor", floorDefName, floorStuffDefName, new MapCell(x, z), 0));
            }
        }

        for (int z = 0; z < exterior.Height; z++)
        {
            for (int x = 0; x < exterior.Width; x++)
            {
                MapCell cell = new(x, z);
                bool border = x == 0 || z == 0 || x == exterior.Width - 1 || z == exterior.Height - 1;
                if (!border || cell == doorCell || fixtureCells.Contains(cell)) continue;
                assets.Add(new TemplateAsset("wall", "Wall", "BlocksGranite", cell, 0));
            }
        }

        assets.Add(new TemplateAsset("door", "Door", "WoodLog", doorCell, RotationFor(door)));
        assets.AddRange(fixtures);

        return new RoomShell(
            InteriorSize: interior,
            ExteriorSize: exterior,
            Door: door,
            Assets: assets,
            DoorCell: doorCell,
            AccessCell: AccessCell(exterior, door),
            CoolerCell: fixtures.FirstOrDefault(fixture => fixture.Role == "cooler")?.RelativeCell);
    }

    public static MapCell EdgeCenter(RectSize size, DoorSide side) =>
        side switch
        {
            DoorSide.North => new MapCell(size.Width / 2, 0),
            DoorSide.East => new MapCell(size.Width - 1, size.Height / 2),
            DoorSide.South => new MapCell(size.Width / 2, size.Height - 1),
            _ => new MapCell(0, size.Height / 2)
        };

    public static MapCell AccessCell(RectSize size, DoorSide side) =>
        side switch
        {
            DoorSide.North => new MapCell(size.Width / 2, -1),
            DoorSide.East => new MapCell(size.Width, size.Height / 2),
            DoorSide.South => new MapCell(size.Width / 2, size.Height),
            _ => new MapCell(-1, size.Height / 2)
        };

    public static DoorSide Opposite(DoorSide side) =>
        side switch
        {
            DoorSide.North => DoorSide.South,
            DoorSide.East => DoorSide.West,
            DoorSide.South => DoorSide.North,
            _ => DoorSide.East
        };

    public static int RotationFor(DoorSide side) =>
        side switch
        {
            DoorSide.North => 0,
            DoorSide.East => 1,
            DoorSide.South => 2,
            _ => 3
        };

    private static IReadOnlyList<MapCell> FixtureFootprintCells(TemplateAsset fixture) =>
        fixture.DefName switch
        {
            "Bed" => LineFromOrigin(fixture.RelativeCell, fixture.Rotation, 2),
            "Shelf" => LineFromOrigin(fixture.RelativeCell, fixture.Rotation, 2),
            "FueledStove" => CenteredLine(fixture.RelativeCell, fixture.Rotation, 3),
            _ => [fixture.RelativeCell]
        };

    private static IReadOnlyList<MapCell> LineFromOrigin(MapCell origin, int rotation, int length)
    {
        (int dx, int dz) = Direction(rotation);
        return Enumerable.Range(0, length)
            .Select(offset => new MapCell(origin.X + dx * offset, origin.Z + dz * offset))
            .ToList();
    }

    private static IReadOnlyList<MapCell> CenteredLine(MapCell center, int rotation, int length)
    {
        (int dx, int dz) = CenteredDirection(rotation);
        int before = length / 2;
        return Enumerable.Range(0, length)
            .Select(offset => offset - before)
            .Select(offset => new MapCell(center.X + dx * offset, center.Z + dz * offset))
            .ToList();
    }

    private static (int Dx, int Dz) Direction(int rotation) =>
        rotation switch
        {
            1 => (1, 0),
            2 => (0, -1),
            3 => (-1, 0),
            _ => (0, 1)
        };

    private static (int Dx, int Dz) CenteredDirection(int rotation) =>
        rotation % 2 == 0
            ? (1, 0)
            : (0, 1);
}
