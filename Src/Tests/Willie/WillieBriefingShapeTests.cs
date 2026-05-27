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
    public void WillieRoomAnchor_OptionalFields_DefaultToEmptyAndNull()
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
        roundTripped.RegionId.Should().BeNull();
    }
}
