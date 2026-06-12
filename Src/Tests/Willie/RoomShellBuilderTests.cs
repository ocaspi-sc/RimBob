using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Ministers.Willie;

namespace RimBob.Tests.Willie;

public sealed class RoomShellBuilderTests
{
    [Fact]
    public void Build_OmitsFloorsUnderFullFixtureFootprint()
    {
        RoomShell shell = RoomShellBuilder.Build(
            new RectSize(5, 5),
            DoorSide.East,
            "Concrete",
            null,
            [new TemplateAsset("stove", "FueledStove", null, new MapCell(1, 2), 1)]);

        IReadOnlySet<MapCell> floorCells = shell.Assets
            .Where(asset => asset.Role == "floor")
            .Select(asset => asset.RelativeCell)
            .ToHashSet();

        floorCells.Should().NotContain(new MapCell(1, 1));
        floorCells.Should().NotContain(new MapCell(1, 2));
        floorCells.Should().NotContain(new MapCell(1, 3));
    }

    [Fact]
    public void BedroomTemplate_PlacesBedsAwayFromWalls()
    {
        RoomShell shell = new BedroomTemplate().BuildShell(new RectSize(8, 4), DoorSide.East);

        IReadOnlySet<MapCell> wallCells = shell.Assets
            .Where(asset => asset.Role == "wall")
            .Select(asset => asset.RelativeCell)
            .ToHashSet();

        foreach (TemplateAsset bed in shell.Assets.Where(asset => asset.Role == "bed"))
        {
            IReadOnlyList<MapCell> occupied =
            [
                bed.RelativeCell,
                new MapCell(bed.RelativeCell.X, bed.RelativeCell.Z - 1)
            ];
            occupied.Should().NotIntersectWith(wallCells);
        }
    }
}
