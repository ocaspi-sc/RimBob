using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Ministers.Willie;

namespace RimBob.Tests.Willie;

public sealed class RoomTemplateSetTests
{
    [Fact]
    public void ForSpec_ResolvesFreezerByRoomClassAndTargetFallback()
    {
        RoomTemplateSet templates = new([new FreezerTemplate()]);

        templates.ForSpec(Spec(BuildingClass.Wall, RoomClass.Freezer))
            .Should().BeOfType<FreezerTemplate>();
        templates.ForSpec(Spec(BuildingClass.Freezer, roomClass: null))
            .Should().BeOfType<FreezerTemplate>();
    }

    [Fact]
    public void ForSpec_ReturnsNullForUnregisteredRoomClass()
    {
        RoomTemplateSet templates = new([new FreezerTemplate()]);

        templates.ForSpec(Spec(BuildingClass.Bed, RoomClass.Bedroom))
            .Should().BeNull();
    }

    [Fact]
    public void Default_IncludesProductionTemplateBreadth()
    {
        RoomTemplateSet.Default.ForSpec(Spec(BuildingClass.ProductionBench, RoomClass.Kitchen))
            .Should().BeOfType<KitchenTemplate>();
        RoomTemplateSet.Default.ForSpec(Spec(BuildingClass.Bed, RoomClass.Hospital))
            .Should().BeOfType<HospitalTemplate>();
        RoomTemplateSet.Default.ForSpec(Spec(BuildingClass.Bed, RoomClass.Barracks))
            .Should().BeOfType<BarracksTemplate>();
        RoomTemplateSet.Default.ForSpec(Spec(BuildingClass.Wall, RoomClass.Recreation))
            .Should().BeOfType<RecreationTemplate>();
        RoomTemplateSet.Default.ForSpec(Spec(BuildingClass.Wall, RoomClass.Dining))
            .Should().BeOfType<DiningTemplate>();
        RoomTemplateSet.Default.ForSpec(Spec(BuildingClass.Recreation, roomClass: null))
            .Should().BeOfType<RecreationTemplate>();
        RoomTemplateSet.Default.ForSpec(Spec(BuildingClass.Table, roomClass: null))
            .Should().BeOfType<DiningTemplate>();
        RoomTemplateSet.Default.ForSpec(Spec(BuildingClass.ProductionBench, roomClass: null))
            .Should().BeOfType<WorkshopTemplate>();
        RoomTemplateSet.Default.ForSpec(Spec(BuildingClass.Shelf, roomClass: null))
            .Should().BeOfType<StorageTemplate>();
    }

    private static PlacementSpec Spec(BuildingClass targetClass, RoomClass? roomClass) =>
        new(
            Request: "test",
            Reason: "test",
            TargetClass: targetClass,
            TargetDef: null,
            RoomClass: roomClass,
            CapacityNeed: null,
            Adjacency: [new AdjacencyHint(AdjacencyRelation.Near, "kitchen")],
            Power: null,
            Temperature: null,
            MaterialsOnHand: [],
            Deadline: null,
            Priority: Priority.Medium,
            Source: "test",
            Constraints: []);
}
