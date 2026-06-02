using System.Text.Json;
using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.State;
using RimBob.State.Derivations;

namespace RimBob.Tests.Willie;

public sealed class WillieBriefingShapeTests
{
    [Fact]
    public void WillieBriefing_DefaultShape_RoundTripsThroughJson()
    {
        WillieBriefing briefing = WillieBriefingDerivation.Compute(new ColonyState());

        string json = JsonSerializer.Serialize(briefing);
        WillieBriefing? roundTripped = JsonSerializer.Deserialize<WillieBriefing>(json);

        roundTripped.Should().NotBeNull();
        roundTripped!.AnchorInventory.Anchors.Should().BeEmpty();
        roundTripped.ConstructionBacklog.Groups.Should().BeEmpty();
        roundTripped.DataCoverage.HasReachability.Should().BeFalse();
    }

    [Fact]
    public void WillieBriefing_LiveAnchorsWithTargetCells_HasReachability()
    {
        ColonyState state = StateWithKitchenAnchor(ColonyStateOrigin.Live);

        WillieBriefing briefing = WillieBriefingDerivation.Compute(state);

        briefing.DataCoverage.HasAnchorInventory.Should().BeTrue();
        briefing.DataCoverage.HasReachability.Should().BeTrue();
    }

    [Fact]
    public void WillieBriefing_SnapshotAnchors_DoNotClaimReachability()
    {
        ColonyState state = StateWithKitchenAnchor(ColonyStateOrigin.Snapshot);

        WillieBriefing briefing = WillieBriefingDerivation.Compute(state);

        briefing.DataCoverage.HasAnchorInventory.Should().BeTrue();
        briefing.DataCoverage.HasReachability.Should().BeFalse();
    }

    [Fact]
    public void WillieBriefing_MultiFunctionRoomCountsEachAnchorClass()
    {
        ColonyState state = new()
        {
            LastRefreshSource = ColonyStateOrigin.Live
        };
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
                Cells:
                [
                    new MapPosition(10, 0, 10),
                    new MapPosition(12, 0, 10),
                    new MapPosition(10, 0, 12),
                    new MapPosition(12, 0, 12)
                ],
                EntryCells: [new MapPosition(11, 0, 9)],
                ContainedBuildingIds: ["bed-1", "stove-1"])
        ]));
        state.Buildings.Update(new BuildingRegistry([
            new BuildingRecord("bed-1", "Bed", 1f, null, null, new MapPosition(10, 0, 10)),
            new BuildingRecord("stove-1", "FueledStove", 1f, null, null, new MapPosition(12, 0, 11))
        ]));

        WillieBriefing briefing = WillieBriefingDerivation.Compute(state);

        briefing.AnchorInventory.Anchors.Select(anchor => anchor.RoomId)
            .Should().Equal("multi-room", "multi-room");
        briefing.FunctionalRooms.RoomCountsByClass.Should().ContainKey("Barracks").WhoseValue.Should().Be(1);
        briefing.FunctionalRooms.RoomCountsByClass.Should().ContainKey("Kitchen").WhoseValue.Should().Be(1);
    }

    [Fact]
    public void WillieBriefing_HomeAreaAnchorSetsAnchorCoverageWithoutFunctionalRoomCount()
    {
        ColonyState state = new()
        {
            LastRefreshSource = ColonyStateOrigin.Live
        };
        state.Areas.Update(new MapAreaRegistry([
            new MapArea(
                Id: "0",
                Type: "Area_Home",
                Label: "Home",
                CellCount: 25,
                Bounds: new MapRect(10, 20, 14, 24),
                Centroid: new MapPosition(12, 0, 22))
        ]));

        WillieBriefing briefing = WillieBriefingDerivation.Compute(state);

        briefing.DataCoverage.HasAnchorInventory.Should().BeTrue();
        briefing.DataCoverage.HasReachability.Should().BeTrue();
        briefing.AnchorInventory.Anchors.Should().ContainSingle(anchor => anchor.Class == RoomClass.BuildableRegion);
        briefing.FunctionalRooms.RoomCountsByClass.Should().NotContainKey(nameof(RoomClass.BuildableRegion));
    }

    [Fact]
    public void WillieRoomAnchor_OptionalFields_DefaultEntryCellsToEmpty()
    {
        WillieRoomAnchor anchor = new(
            RoomId: "room-1",
            Class: RoomClass.Kitchen,
            RoleLabel: "Kitchen",
            CellsCount: 16,
            Centroid: null,
            ContainedBuildingIds: []);

        string json = JsonSerializer.Serialize(anchor);
        WillieRoomAnchor? roundTripped = JsonSerializer.Deserialize<WillieRoomAnchor>(json);

        roundTripped.Should().NotBeNull();
        roundTripped!.EntryCells.Should().BeEmpty();
    }

    private static ColonyState StateWithKitchenAnchor(ColonyStateOrigin origin)
    {
        ColonyState state = new()
        {
            LastRefreshSource = origin
        };
        state.Rooms.Update(new RoomRegistry([
            new RoomRecord(
                Id: "kitchen-room",
                RoleLabel: "Kitchen",
                Temperature: 21f,
                CellsCount: 4,
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
                Cells:
                [
                    new MapPosition(2, 0, 2),
                    new MapPosition(3, 0, 2),
                    new MapPosition(2, 0, 3),
                    new MapPosition(3, 0, 3)
                ],
                EntryCells: [new MapPosition(2, 0, 1)])
        ]));
        return state;
    }
}
