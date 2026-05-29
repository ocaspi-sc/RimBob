using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Ministers.Willie;

namespace RimBob.Tests.Willie;

public sealed class DraftDedupeTests
{
    [Fact]
    public void ByCellsShapeAnchor_CollapsesIdenticalFootprints()
    {
        PlacementDraft first = Draft("first", "kitchen", new MapCell(2, 2));
        PlacementDraft duplicate = Draft("duplicate", "kitchen", new MapCell(2, 2));

        IReadOnlyList<PlacementDraft> unique = DraftDedupe.ByCellsShapeAnchor([first, duplicate]);

        unique.Should().ContainSingle().Which.Should().BeSameAs(first);
    }

    [Fact]
    public void ByCellsShapeAnchor_KeepsDistinctOrigins()
    {
        PlacementDraft near = Draft("near", "kitchen", new MapCell(2, 2));
        PlacementDraft offset = Draft("offset", "kitchen", new MapCell(5, 2));

        IReadOnlyList<PlacementDraft> unique = DraftDedupe.ByCellsShapeAnchor([near, offset]);

        unique.Should().Equal(near, offset);
    }

    [Fact]
    public void ByCellsShapeAnchor_IsDeterministicAndKeepsFirstDraft()
    {
        PlacementDraft first = Draft("first", "kitchen", new MapCell(2, 2));
        PlacementDraft second = Draft("second", "kitchen", new MapCell(2, 2));
        PlacementDraft third = Draft("third", "kitchen", new MapCell(4, 4));

        IReadOnlyList<PlacementDraft> unique = DraftDedupe.ByCellsShapeAnchor([first, second, third]);

        unique.Should().Equal(first, third);
    }

    private static PlacementDraft Draft(string generatorId, string roomId, MapCell origin) =>
        new(
            GeneratorId: generatorId,
            Group: new BlueprintGroup(
                Label: generatorId,
                MapId: 7,
                Assets:
                [
                    new BlueprintAsset("floor", "Concrete", null, origin, 0),
                    new BlueprintAsset("door", "Door", "WoodLog", new MapCell(origin.X + 1, origin.Z), 0)
                ]),
            SourceAnchor: new ResolvedAnchor(
                new WillieRoomAnchor(
                    roomId,
                    RoomClass.Kitchen,
                    "Kitchen",
                    20,
                    new MapPosition(8, 0, 8),
                    []),
                new MapPosition(8, 0, 8),
                AnchorMatchReason.CentroidFallback),
            AccessCells: [new MapCell(origin.X, origin.Z - 1)],
            Assumptions: [],
            ReasonSummary: "test");
}
