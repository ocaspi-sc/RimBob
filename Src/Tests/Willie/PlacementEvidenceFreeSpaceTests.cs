using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Ministers.Willie;

namespace RimBob.Tests.Willie;

public sealed class PlacementEvidenceFreeSpaceTests
{
    [Fact]
    public void Build_WithEmptyMap_ExposesWholeMapFreeRect()
    {
        PlacementEvidence evidence = Evidence("8x6", []);

        FreeRect rect = evidence.FreeRects.Should().ContainSingle().Subject;
        rect.Origin.Should().Be(new MapCell(0, 0));
        rect.Width.Should().Be(8);
        rect.Height.Should().Be(6);
        evidence.FreeSpaceScanTruncated.Should().BeFalse();
    }

    [Fact]
    public void Build_WithOccupiedPoint_RemovesRectsContainingOccupiedCell()
    {
        MapCell occupied = new(2, 1);
        PlacementEvidence evidence = Evidence(
            "5x4",
            [new BuildingRecord("wall", "Wall", 1f, null, null, new MapPosition(occupied.X, 0, occupied.Z))]);

        evidence.FreeRects.Should().NotBeEmpty();
        evidence.FreeRects.Should().NotContain(rect => rect.Contains(occupied));
        evidence.FreeRects[0].Area.Should().Be(10);
    }

    [Fact]
    public void Build_WithThingsAndBuildZones_RemovesBlockedCells()
    {
        MapCell thingCell = new(2, 1);
        MapCell zoneCell = new(3, 1);
        MapCell homeAreaCell = new(4, 1);
        PlacementEvidence evidence = PlacementEvidence.Build(
            new MapInfoSnapshot(7, "5x4"),
            new BuildingRegistry([]),
            [],
            things: new ThingRegistry(
            [
                new ThingRecord(
                    Id: "meal",
                    Def: "MealSimple",
                    Label: "simple meal",
                    StackCount: 1,
                    Categories: [],
                    IsForbidden: false,
                    Position: new MapPosition(thingCell.X, 0, thingCell.Z))
            ]),
            zones: new MapZoneRegistry(
            [
                new MapZoneRecord(
                    Id: "zone",
                    Type: "GrowingZone",
                    Label: "rice",
                    CellCount: 1,
                    Cells: [new MapPosition(zoneCell.X, 0, zoneCell.Z)]),
                new MapZoneRecord(
                    Id: "area",
                    Type: "Area_Home",
                    Label: "Home",
                    CellCount: 1,
                    Cells: [new MapPosition(homeAreaCell.X, 0, homeAreaCell.Z)])
            ]));

        evidence.FreeRects.Should().NotContain(rect => rect.Contains(thingCell));
        evidence.FreeRects.Should().NotContain(rect => rect.Contains(zoneCell));
        evidence.FreeRects.Should().Contain(rect => rect.Contains(homeAreaCell));
    }

    [Fact]
    public void Build_WithTerrainGrid_RemovesNonBuildableCells()
    {
        MapCell waterCell = new(2, 1);
        TerrainSnapshot terrain = Terrain("5x4", waterCell);

        PlacementEvidence evidence = PlacementEvidence.Build(
            new MapInfoSnapshot(7, "5x4"),
            new BuildingRegistry([]),
            [],
            terrain: terrain);

        evidence.FreeRects.Should().NotContain(rect => rect.Contains(waterCell));
    }

    [Fact]
    public void BuildFreeRects_WithBlockedPredicate_ExcludesMaskedCellsInsideOffsetBounds()
    {
        MapCell blocked = new(12, 21);

        PlacementEvidence.FreeRectScanResult result = PlacementEvidence.BuildFreeRects(
            new MapRect(10, 20, 14, 23),
            cell => cell == blocked);

        result.ScanTruncated.Should().BeFalse();
        result.Rects.Should().NotBeEmpty();
        result.Rects.Should().OnlyContain(rect =>
            rect.MinX >= 10 &&
            rect.MinZ >= 20 &&
            rect.MaxXExclusive <= 15 &&
            rect.MaxZExclusive <= 24);
        result.Rects.Should().NotContain(rect => rect.Contains(blocked));
    }

    [Fact]
    public void Build_WhenMapExceedsScanBound_SkipsFreeSpaceScan()
    {
        PlacementEvidence evidence = Evidence("300x300", []);

        evidence.FreeSpaceScanTruncated.Should().BeTrue();
        evidence.FreeRects.Should().BeEmpty();
    }

    private static PlacementEvidence Evidence(
        string mapSize,
        IReadOnlyList<BuildingRecord> buildings) =>
        PlacementEvidence.Build(
            new MapInfoSnapshot(7, mapSize),
            new BuildingRegistry(buildings),
            []);

    private static TerrainSnapshot Terrain(string mapSize, MapCell blockedCell)
    {
        string[] parts = mapSize.Split('x');
        int width = int.Parse(parts[0]);
        int height = int.Parse(parts[1]);
        List<TerrainCellRecord> cells = [];
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                bool blocked = x == blockedCell.X && z == blockedCell.Z;
                cells.Add(new TerrainCellRecord(
                    x,
                    z,
                    blocked ? "WaterDeep" : "Soil",
                    blocked ? 0f : 1f,
                    SupportsGrowing: !blocked,
                    SupportsStockpile: !blocked));
            }
        }

        return new TerrainSnapshot(
            width,
            height,
            new Dictionary<string, int>(),
            new Dictionary<string, TerrainDefRecord>(),
            cells);
    }
}
