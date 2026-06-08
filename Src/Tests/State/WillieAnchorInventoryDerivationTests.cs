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

        WillieRoomAnchor bedroom = inventory.Anchors.Single(anchor => anchor.RoomId == "bedroom-room");
        bedroom.Class.Should().Be(RoomClass.Bedroom);
        bedroom.Centroid.Should().Be(new MapPosition(8, 0, 9));
        bedroom.ContainedBuildingIds.Should().Equal("bed-1");
        inventory.Anchors.Should().NotContain(anchor => anchor.RoomId == "unknown-room");
    }

    [Fact]
    public void Derive_BarracksWithStoveEmitsPrimaryAndKitchenFunctionAnchors()
    {
        ColonyState state = new();
        state.Rooms.Update(new RoomRegistry([
            new RoomRecord(
                Id: "multi-room",
                RoleLabel: "Barracks",
                Temperature: 20f,
                CellsCount: 4,
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
                Bounds: new MapRect(10, 10, 12, 12),
                Cells:
                [
                    new MapPosition(10, 0, 10),
                    new MapPosition(12, 0, 10),
                    new MapPosition(10, 0, 12),
                    new MapPosition(12, 0, 12)
                ],
                EntryCells: [new MapPosition(11, 0, 9)],
                RegionId: 42,
                ContainedBuildingIds: ["bed-1", "stove-1"])
        ]));
        state.Buildings.Update(new BuildingRegistry([
            new BuildingRecord("bed-1", "Bed", 1f, null, null, new MapPosition(10, 0, 10)),
            new BuildingRecord("stove-1", "FueledStove", 1f, null, null, new MapPosition(12, 0, 11), "fueled stove")
        ]));

        WillieAnchorInventory inventory = WillieAnchorInventoryDerivation.Derive(state);

        inventory.Anchors.Should().HaveCount(2);
        WillieRoomAnchor barracks = inventory.Anchors.Single(anchor => anchor.Class == RoomClass.Barracks);
        barracks.RoomId.Should().Be("multi-room");
        barracks.Centroid.Should().Be(new MapPosition(11, 0, 11));
        barracks.ContainedBuildingIds.Should().Equal("bed-1", "stove-1");
        barracks.EntryCells.Should().Equal(new MapPosition(11, 0, 9));

        WillieRoomAnchor kitchen = inventory.Anchors.Single(anchor => anchor.Class == RoomClass.Kitchen);
        kitchen.RoomId.Should().Be("multi-room");
        kitchen.RoleLabel.Should().Be("Barracks");
        kitchen.Centroid.Should().Be(new MapPosition(12, 0, 11));
        kitchen.Bounds.Should().Be(new MapRect(10, 10, 12, 12));
        kitchen.Cells.Should().HaveCount(4);
        kitchen.ContainedBuildingIds.Should().Equal("bed-1", "stove-1");
        inventory.Anchors.Should().NotContain(anchor => anchor.Class == RoomClass.Bedroom);
    }

    [Fact]
    public void Derive_HomeAreaWithoutRoomsEmitsBuildableRegionAnchor()
    {
        ColonyState state = new();
        state.Areas.Update(new MapAreaRegistry([
            new MapArea(
                Id: "0",
                Type: "Area_Home",
                Label: "Home",
                CellCount: 25,
                Bounds: new MapRect(10, 20, 14, 24),
                Centroid: new MapPosition(12, 0, 22))
        ]));

        WillieAnchorInventory inventory = WillieAnchorInventoryDerivation.Derive(state);

        WillieRoomAnchor anchor = inventory.Anchors.Should().ContainSingle().Subject;
        anchor.RoomId.Should().Be("area:0");
        anchor.Class.Should().Be(RoomClass.BuildableRegion);
        anchor.RoleLabel.Should().Be("Home");
        anchor.CellsCount.Should().Be(25);
        anchor.Centroid.Should().Be(new MapPosition(12, 0, 22));
        anchor.Bounds.Should().Be(new MapRect(10, 20, 14, 24));
        anchor.Cells.Should().BeEmpty();
        anchor.EntryCells.Should().BeEmpty();
    }

    [Fact]
    public void Derive_HomeAreaWithPositiveCountButNoCellsUsesMapBounds()
    {
        ColonyState state = new();
        state.Map.Update(new MapInfoSnapshot(0, "(250, 1, 250)"));
        state.Areas.Update(new MapAreaRegistry([
            new MapArea(
                Id: "0",
                Type: "Area_Home",
                Label: "Home",
                CellCount: 20)
        ]));

        WillieAnchorInventory inventory = WillieAnchorInventoryDerivation.Derive(state);

        WillieRoomAnchor anchor = inventory.Anchors.Should().ContainSingle().Subject;
        anchor.RoomId.Should().Be("area:0");
        anchor.Class.Should().Be(RoomClass.BuildableRegion);
        anchor.CellsCount.Should().Be(20);
        anchor.Centroid.Should().BeNull();
        anchor.Bounds.Should().Be(new MapRect(0, 0, 249, 249));
    }

    [Fact]
    public void Derive_HomeAreaWithZeroCountAndNoCellsUsesMapBounds()
    {
        ColonyState state = new();
        state.Map.Update(new MapInfoSnapshot(0, "(250, 1, 250)"));
        state.Areas.Update(new MapAreaRegistry([
            new MapArea(
                Id: "0",
                Type: "Area_Home",
                Label: "Home",
                CellCount: 0)
        ]));

        WillieAnchorInventory inventory = WillieAnchorInventoryDerivation.Derive(state);

        WillieRoomAnchor anchor = inventory.Anchors.Should().ContainSingle().Subject;
        anchor.RoomId.Should().Be("area:0");
        anchor.Class.Should().Be(RoomClass.BuildableRegion);
        anchor.CellsCount.Should().Be(0);
        anchor.Centroid.Should().BeNull();
        anchor.Bounds.Should().Be(new MapRect(0, 0, 249, 249));
    }
}
