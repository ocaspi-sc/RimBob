using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Ministers.Willie;
using RimBob.State.Derivations.Common;

namespace RimBob.Tests.Willie;

public sealed class FreezerTemplateTests
{
    [Fact]
    public void SizeFor_FoodUnitsUsesMonotonicNearSquareSizing()
    {
        FreezerTemplate.SizeFor(null).Should().Be(new RectSize(4, 4));
        FreezerTemplate.SizeFor(new CapacityNeed(CapacityMeasure.FoodUnits, 200))
            .Should().Be(new RectSize(5, 5));
        FreezerTemplate.SizeFor(new CapacityNeed(CapacityMeasure.FoodUnits, 201))!
            .Width.Should().BeGreaterThanOrEqualTo(5);
        FreezerTemplate.SizeFor(new CapacityNeed(CapacityMeasure.Beds, 4))
            .Should().BeNull();
    }

    [Fact]
    public void BuildShell_ReturnsCanonicalAssetsAndAccessCells()
    {
        RoomShell shell = FreezerTemplate.BuildShell(new RectSize(2, 2), DoorSide.North);

        shell.ExteriorSize.Should().Be(new RectSize(4, 4));
        shell.DoorCell.Should().Be(new MapCell(2, 0));
        shell.AccessCell.Should().Be(new MapCell(2, -1));
        shell.CoolerCell.Should().Be(new MapCell(2, 3));
        shell.Assets.Take(4).Select(asset => asset.RelativeCell).Should().Equal(
            new MapCell(1, 1),
            new MapCell(2, 1),
            new MapCell(1, 2),
            new MapCell(2, 2));
        shell.Assets.Count(asset => asset.Role == "floor").Should().Be(4);
        shell.Assets.Count(asset => asset.Role == "wall").Should().Be(10);
        shell.Assets.Should().ContainSingle(asset => asset.Role == "door");
        shell.Assets.Should().ContainSingle(asset => asset.Role == "cooler");
    }

    [Fact]
    public void BuildShell_IsDeterministic()
    {
        RoomShell first = FreezerTemplate.BuildShell(new RectSize(5, 5), DoorSide.West);
        RoomShell second = FreezerTemplate.BuildShell(new RectSize(5, 5), DoorSide.West);

        second.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
    }

    [Fact]
    public void MapBounds_ParseSupportsRimApiShapes()
    {
        MapBounds.Parse("250x300").Should().Be(new MapBounds(250, 300));
        MapBounds.Parse("(250,1,300)").Should().Be(new MapBounds(250, 300));
        MapBounds.Parse("small").Should().BeNull();
    }
}
