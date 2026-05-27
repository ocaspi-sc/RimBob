using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.State;
using RimBob.State.Derivations;

namespace RimBob.Tests.State;

public sealed class WillieAnchorInventoryDerivationTests
{
    [Fact]
    public void Derive_MapsRoleLabelsAndContainedBedFallbacks()
    {
        ColonyState state = new();
        state.Rooms.Update(new RoomRegistry([
            new RoomRecord(
                Id: "kitchen-room",
                RoleLabel: "Kitchen",
                Temperature: 21f,
                CellsCount: 18,
                TouchesMapEdge: false,
                IsPrisonCell: false,
                IsDoorway: false,
                OpenRoofCount: 0,
                ContainedBedIds: [],
                Impressiveness: null,
                Beauty: null,
                Cleanliness: null,
                Space: null,
                Wealth: null),
            new RoomRecord(
                Id: "unknown-room",
                RoleLabel: "None",
                Temperature: 20f,
                CellsCount: 6,
                TouchesMapEdge: false,
                IsPrisonCell: false,
                IsDoorway: false,
                OpenRoofCount: 0,
                ContainedBedIds: [],
                Impressiveness: null,
                Beauty: null,
                Cleanliness: null,
                Space: null,
                Wealth: null),
            new RoomRecord(
                Id: "bedroom-room",
                RoleLabel: "",
                Temperature: 19f,
                CellsCount: 12,
                TouchesMapEdge: false,
                IsPrisonCell: false,
                IsDoorway: false,
                OpenRoofCount: 0,
                ContainedBedIds: ["bed-1"],
                Impressiveness: null,
                Beauty: null,
                Cleanliness: null,
                Space: null,
                Wealth: null)
        ]));
        state.Buildings.Update(new BuildingRegistry([
            new BuildingRecord("bed-1", "Bed", 1f, null, null, new MapPosition(8, 0, 9))
        ]));

        WillieAnchorInventory inventory = WillieAnchorInventoryDerivation.Derive(state);

        inventory.Anchors.Should().HaveCount(2);
        WillieRoomAnchor kitchen = inventory.Anchors.Single(anchor => anchor.RoomId == "kitchen-room");
        kitchen.Class.Should().Be(RoomClass.Kitchen);
        kitchen.Centroid.Should().BeNull();
        kitchen.EntryCells.Should().BeEmpty();
        kitchen.RegionId.Should().BeNull();

        WillieRoomAnchor bedroom = inventory.Anchors.Single(anchor => anchor.RoomId == "bedroom-room");
        bedroom.Class.Should().Be(RoomClass.Bedroom);
        bedroom.Centroid.Should().Be(new MapPosition(8, 0, 9));
        bedroom.ContainedBuildingIds.Should().Equal("bed-1");
        inventory.Anchors.Should().NotContain(anchor => anchor.RoomId == "unknown-room");
    }
}
