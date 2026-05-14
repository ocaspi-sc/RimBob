using FluentAssertions;
using RimAI.Core.Aggregates;
using RimAI.Ingestion.Dtos;
using RimAI.State;

namespace RimAI.Tests.State;

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
}
