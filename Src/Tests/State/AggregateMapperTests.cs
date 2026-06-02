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
            new(
                Id: 10,
                Def: "Wall",
                Label: "granite wall",
                Type: "Building",
                Position: new PositionDto(1, 0, 2),
                Rotation: 0,
                Size: new PositionDto(1, 0, 1),
                Hp: 180f,
                MaxHp: 200f,
                Stuff: "BlocksGranite",
                RoomId: 12,
                IsWorking: true,
                Power: new BuildingPowerDto(Required: true, On: false, ConsumptionW: 100f),
                Fuel: new BuildingFuelDto(Current: 18.5f, Capacity: 25f, FuelDef: "WoodLog"),
                FlickableOn: false,
                Flammability: 0.1f)
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
        original.Hp.Should().Be(180f);
        original.MaxHp.Should().Be(200f);
        original.Stuff.Should().Be("BlocksGranite");
        original.RoomId.Should().Be(12);
        original.IsWorking.Should().BeTrue();
        original.PowerOn.Should().BeFalse();
        original.Power.Should().Be(new BuildingPower(Required: true, On: false, ConsumptionW: 100f));
        original.Fuel.Should().Be(new BuildingFuel(Current: 18.5f, Capacity: 25f, FuelDef: "WoodLog"));
        original.FlickableOn.Should().BeFalse();
        original.Flammability.Should().Be(0.1f);

        BuildingRecord stove = registry.Buildings.Single(building => building.Id == "44710");
        stove.Def.Should().Be("FueledStove");
        stove.Label.Should().Be("fueled stove");
        stove.Position.Should().Be(new MapPosition(93, 0, 186));
        stove.Hp.Should().BeNull();
        stove.Power.Should().BeNull();
        stove.Fuel.Should().BeNull();
    }

    [Fact]
    public void MapMapper_FromAreas_KeepsOnlyHomeAreaAndDerivesBoundsAndCentroid()
    {
        IReadOnlyList<ZoneDto> zones =
        [
            new(
                Id: "stockpile-1",
                Type: "Zone_Stockpile",
                Label: "main",
                Cells: [new PositionDto(1, 0, 1)],
                PlantDef: null),
            new(
                Id: "grow-1",
                Type: "GrowingZone",
                Label: "rice",
                Cells: [new PositionDto(4, 0, 4)],
                PlantDef: "Plant_Rice"),
            new(
                Id: "0",
                Type: "Area_Home",
                Label: "Home",
                Cells:
                [
                    new PositionDto(10, 0, 20),
                    new PositionDto(14, 0, 22)
                ],
                PlantDef: null),
            new(
                Id: "4",
                Type: "Area_Allowed",
                Label: "Area 1",
                Cells: [new PositionDto(30, 0, 40)],
                PlantDef: null)
        ];

        MapAreaRegistry areas = MapAggregateMapper.FromAreas(zones);
        StockpileLedger stockpiles = MapAggregateMapper.FromStockpiles(zones);

        MapArea home = areas.Areas.Should().ContainSingle().Subject;
        home.Id.Should().Be("0");
        home.Type.Should().Be("Area_Home");
        home.Label.Should().Be("Home");
        home.CellCount.Should().Be(2);
        home.Bounds.Should().Be(new MapRect(10, 20, 14, 22));
        home.Centroid.Should().Be(new MapPosition(12, 0, 21));
        stockpiles.Zones.Should().ContainSingle().Which.Id.Should().Be("stockpile-1");
    }
}
