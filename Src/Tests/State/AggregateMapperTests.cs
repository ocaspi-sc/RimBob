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
            Wealth: 420f,
            Bounds: new MapRectDto(3, 4, 6, 7),
            Cells:
            [
                new PositionDto(3, 0, 4),
                new PositionDto(4, 0, 4)
            ],
            EntryCells: [new PositionDto(3, 0, 3)],
            RegionId: 123,
            ContainedBuildingIds: [10, 11, 12]);

        RoomRegistry registry = MapAggregateMapper.FromRooms([dto]);

        RoomRecord room = registry.Rooms.Should().ContainSingle().Subject;
        room.Id.Should().Be("42");
        room.RoleLabel.Should().Be("bedroom");
        room.ContainedBedIds.Should().Equal("10", "11");
        room.ContainedBuildingIds.Should().Equal("10", "11", "12");
        room.Bounds.Should().Be(new MapRect(3, 4, 6, 7));
        room.Cells.Should().Equal(new MapPosition(3, 0, 4), new MapPosition(4, 0, 4));
        room.EntryCells.Should().Equal(new MapPosition(3, 0, 3));
        room.RegionId.Should().Be(123);
        room.Impressiveness.Should().Be(31f);
        room.Beauty.Should().Be(-1.5f);
        room.Cleanliness.Should().Be(-0.4f);
        room.Space.Should().Be(16f);
        room.Wealth.Should().Be(420f);
    }

    [Fact]
    public void MapMapper_FromBuildings_MergesWorkTablesAndKeepsBuildingRecordOnIdCollision()
    {
        IReadOnlyList<BuildingDto> buildings =
        [
            new(10, "Wall", "granite wall", "Building", new PositionDto(1, 0, 2))
        ];
        IReadOnlyList<WorkTableDto> workTables =
        [
            new(10, "FueledStove", "duplicate stove", new PositionDto(3, 0, 4), 1),
            new(44710, "FueledStove", "fueled stove", new PositionDto(93, 0, 186), 1)
        ];

        BuildingRegistry registry = MapAggregateMapper.FromBuildings(buildings, workTables);

        registry.Buildings.Should().HaveCount(2);
        BuildingRecord original = registry.Buildings.Single(building => building.Id == "10");
        original.Def.Should().Be("Wall");
        original.Label.Should().Be("granite wall");

        BuildingRecord stove = registry.Buildings.Single(building => building.Id == "44710");
        stove.Def.Should().Be("FueledStove");
        stove.Label.Should().Be("fueled stove");
        stove.Position.Should().Be(new MapPosition(93, 0, 186));
    }
}
