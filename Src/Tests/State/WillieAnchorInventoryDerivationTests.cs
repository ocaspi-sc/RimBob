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
                Wealth: null,
                Bounds: new MapRect(2, 2, 5, 5),
                Cells:
                [
                    new MapPosition(2, 0, 2),
                    new MapPosition(3, 0, 2),
                    new MapPosition(4, 0, 2),
                    new MapPosition(5, 0, 2)
                ],
                EntryCells: [new MapPosition(3, 0, 1)],
                RegionId: 99),
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
                Wealth: null,
                ContainedBuildingIds: ["bed-1"])
        ]));
        state.Buildings.Update(new BuildingRegistry([
            new BuildingRecord("bed-1", "Bed", 1f, null, null, new MapPosition(8, 0, 9))
        ]));

        WillieAnchorInventory inventory = WillieAnchorInventoryDerivation.Derive(state);

        inventory.Anchors.Should().HaveCount(2);
        WillieRoomAnchor kitchen = inventory.Anchors.Single(anchor => anchor.RoomId == "kitchen-room");
        kitchen.Class.Should().Be(RoomClass.Kitchen);
        kitchen.Centroid.Should().Be(new MapPosition(4, 0, 2));
        kitchen.Bounds.Should().Be(new MapRect(2, 2, 5, 5));
        kitchen.Cells.Should().HaveCount(4);
        kitchen.EntryCells.Should().Equal(new MapPosition(3, 0, 1));
        kitchen.RegionId.Should().Be(99);

        WillieRoomAnchor bedroom = inventory.Anchors.Single(anchor => anchor.RoomId == "bedroom-room");
        bedroom.Class.Should().Be(RoomClass.Bedroom);
        bedroom.Centroid.Should().Be(new MapPosition(8, 0, 9));
        bedroom.ContainedBuildingIds.Should().Equal("bed-1");
        inventory.Anchors.Should().NotContain(anchor => anchor.RoomId == "unknown-room");
    }
}
