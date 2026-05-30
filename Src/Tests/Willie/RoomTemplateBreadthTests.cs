using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Ministers.Willie;

namespace RimBob.Tests.Willie;

public sealed class RoomTemplateBreadthTests
{
    public static TheoryData<IRoomTemplate, CapacityNeed, string> Templates =>
        new()
        {
            { new KitchenTemplate(), new CapacityNeed(CapacityMeasure.WorkSlots, 1), "stove" },
            { new HospitalTemplate(), new CapacityNeed(CapacityMeasure.Beds, 3), "medical_bed" },
            { new BedroomTemplate(), new CapacityNeed(CapacityMeasure.Occupants, 1), "bed" },
            { new WorkshopTemplate(), new CapacityNeed(CapacityMeasure.WorkSlots, 2), "workbench" },
            { new StorageTemplate(), new CapacityNeed(CapacityMeasure.StorageStacks, 16), "shelf" }
        };

    [Theory]
    [MemberData(nameof(Templates))]
    public void Templates_BuildWalledDooredShellWithExpectedFixtures(
        IRoomTemplate template,
        CapacityNeed need,
        string expectedFixtureRole)
    {
        RectSize? maybeInterior = template.SizeFor(need);
        maybeInterior.Should().NotBeNull();
        RectSize interior = maybeInterior!;
        RoomShell shell = template.BuildShell(interior, DoorSide.North);

        shell.ExteriorSize.Should().Be(new RectSize(interior.Width + 2, interior.Height + 2));
        shell.Assets.Should().Contain(asset => asset.Role == "door");
        shell.Assets.Should().Contain(asset => asset.Role == "wall");
        shell.Assets.Should().Contain(asset => asset.Role == expectedFixtureRole);
        shell.Assets
            .Where(asset => asset.Role != "floor")
            .Select(asset => asset.RelativeCell)
            .Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Templates_HaveSaneDefaultSizing(IRoomTemplate template, CapacityNeed need, string expectedFixtureRole)
    {
        need.Measure.Should().BeDefined();
        RectSize? maybeInterior = template.SizeFor(null);
        maybeInterior.Should().NotBeNull();
        RectSize interior = maybeInterior!;

        interior.Width.Should().BeGreaterThan(0);
        interior.Height.Should().BeGreaterThan(0);
        template.BuildShell(interior, DoorSide.East)
            .Assets.Should().Contain(asset => asset.Role == expectedFixtureRole);
    }

    [Theory]
    [InlineData(RoomClass.Kitchen)]
    [InlineData(RoomClass.Hospital)]
    [InlineData(RoomClass.Workshop)]
    [InlineData(RoomClass.Storage)]
    public void Templates_RejectWrongCapacityMeasure(RoomClass roomClass)
    {
        IRoomTemplate template = roomClass switch
        {
            RoomClass.Kitchen => new KitchenTemplate(),
            RoomClass.Hospital => new HospitalTemplate(),
            RoomClass.Workshop => new WorkshopTemplate(),
            _ => new StorageTemplate()
        };

        template.SizeFor(new CapacityNeed(CapacityMeasure.FoodUnits, 64))
            .Should().BeNull();
    }
}
