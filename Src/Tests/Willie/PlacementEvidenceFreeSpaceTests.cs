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
}
