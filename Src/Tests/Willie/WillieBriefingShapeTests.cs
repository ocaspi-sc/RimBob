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
