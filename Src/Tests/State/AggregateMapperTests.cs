using FluentAssertions;
using RimBob.Core.Aggregates;
using RimBob.Ingestion.Dtos;
using RimBob.State;

namespace RimBob.Tests.State;

public sealed class AggregateMapperTests
{
    [Fact]
    public void ThreatMapper_OrdersRecentIncidentsAndLimitsHistory()
    {
        IReadOnlyList<IncidentDto> incidents =
        [
            new("Old", 8f, "old"),
            new("Newest", 0.1f, "newest"),
            new("Mid", 3f, "mid"),
            new("Recent", 1f, "recent"),
            new("Older", 5f, "older"),
            new("Overflow", 6f, "overflow")
        ];

        ThreatBoard board = ThreatAggregateMapper.FromThreats([], incidents);

        board.RecentIncidents.Should().HaveCount(5);
        board.RecentIncidents.Select(incident => incident.Def)
            .Should().Equal("Newest", "Recent", "Mid", "Older", "Overflow");
    }

    [Fact]
    public void ResourceMapper_TreatsNoneResearchProjectAsMissing()
    {
        ResearchProgressDto research = new(
            Name: "none",
            Label: "None",
            Progress: 0f,
            ResearchPoints: 0f,
            IsFinished: false,
            CanStartNow: false,
            ProgressPercent: 0f);

        ResearchInfo info = ResourceAggregateMapper.FromResearch(research);

        info.CurrentProject.Should().BeNull();
        info.Progress.Should().BeNull();
        info.IsFinished.Should().BeFalse();
    }

    [Fact]
    public void MapMapper_FromRooms_MapsQualityStatsAndBedIds()
    {
        RoomDto dto = new(
            Id: "42",
            RoleLabel: "bedroom",
            Temperature: 21.5f,
            CellsCount: 16,
            TouchesMapEdge: false,
            IsPrisonCell: false,
            IsDoorway: false,
            OpenRoofCount: 0,
            ContainedBedsIds: [10, 11],
            Impressiveness: 31f,
            Beauty: -1.5f,
            Cleanliness: -0.4f,
            Space: 16f,
            Wealth: 420f);

        RoomRegistry registry = MapAggregateMapper.FromRooms([dto]);

        RoomRecord room = registry.Rooms.Should().ContainSingle().Subject;
        room.Id.Should().Be("42");
        room.RoleLabel.Should().Be("bedroom");
        room.ContainedBedIds.Should().Equal("10", "11");
        room.Impressiveness.Should().Be(31f);
        room.Beauty.Should().Be(-1.5f);
        room.Cleanliness.Should().Be(-0.4f);
        room.Space.Should().Be(16f);
        room.Wealth.Should().Be(420f);
    }
}
