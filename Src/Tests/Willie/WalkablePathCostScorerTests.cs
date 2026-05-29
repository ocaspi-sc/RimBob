using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Core.Placement;
using RimBob.Ministers.Willie;

namespace RimBob.Tests.Willie;

public sealed class WalkablePathCostScorerTests
{
    [Fact]
    public void Score_WithProbeResults_UsesMinimumReachableCost()
    {
        PlacementDraft draft = Draft(
            "template",
            "kitchen",
            [new MapCell(1, 1), new MapCell(1, 2)],
            new MapPosition(5, 0, 5));
        IReadOnlyList<PlacementDraft> drafts = [draft];
        IReadOnlyList<PathCostPair> pairs = PathCostLookup.RequestPairsFor(drafts);
        PathCostLookup lookup = PathCostLookup.FromProbeResults(
            drafts,
            [
                new PathCostResult(false, 0, pairs[0].From, pairs[0].To),
                new PathCostResult(true, 11, pairs[1].From, pairs[1].To)
            ]);

        ScoredDraft scored = new WalkablePathCostScorer()
            .Score(drafts, lookup)
            .Should().ContainSingle().Subject;

        scored.RawCost.Should().Be(11);
        MetricValue metric = scored.Metrics.Single(metric => metric.Id == "freezer_to_kitchen_distance");
        metric.Id.Should().Be("freezer_to_kitchen_distance");
        metric.Unit.Should().Be("path_tiles");
        metric.Normalized.Should().BeApproximately(1d / 12d, 0.0001);
        metric.Weight.Should().Be(16);
        metric.Contribution.Should().BeApproximately(16d / 12d, 0.0001);
        metric.Better.Should().Be("lower");
        scored.Metrics.Should().Contain(component => component.Id == "generator_confidence");
    }

    [Fact]
    public void Score_WithProbeAvailableButNoReachablePairs_SkipsDraft()
    {
        PlacementDraft draft = Draft(
            "template",
            "kitchen",
            [new MapCell(1, 1)],
            new MapPosition(5, 0, 5));
        IReadOnlyList<PlacementDraft> drafts = [draft];
        IReadOnlyList<PathCostPair> pairs = PathCostLookup.RequestPairsFor(drafts);
        PathCostLookup lookup = PathCostLookup.FromProbeResults(
            drafts,
            [new PathCostResult(false, 0, pairs[0].From, pairs[0].To)]);

        IReadOnlyList<ScoredDraft> scored = new WalkablePathCostScorer().Score(drafts, lookup);

        scored.Should().BeEmpty();
    }

    [Fact]
    public void Score_WhenProbeUnavailable_UsesManhattanFallback()
    {
        PlacementDraft draft = Draft(
            "template",
            "kitchen",
            [new MapCell(1, 1)],
            new MapPosition(4, 0, 5));
        IReadOnlyList<PlacementDraft> drafts = [draft];
        PathCostLookup lookup = PathCostLookup.ProbeUnavailable(drafts);

        ScoredDraft scored = new WalkablePathCostScorer()
            .Score(drafts, lookup)
            .Should().ContainSingle().Subject;

        scored.RawCost.Should().Be(7);
        MetricValue metric = scored.Metrics.Single(metric => metric.Id == "freezer_to_kitchen_distance");
        metric.Unit.Should().Be("tiles");
        metric.Normalized.Should().BeApproximately(1d / 8d, 0.0001);
    }

    [Fact]
    public void Score_WithSharedAccessCell_BindsCostsByPairIndex()
    {
        MapCell sharedAccessCell = new(3, 3);
        MapPosition sharedTarget = new(8, 0, 8);
        PlacementDraft first = Draft("first", "kitchen-a", [sharedAccessCell], sharedTarget, originX: 1);
        PlacementDraft second = Draft("second", "kitchen-b", [sharedAccessCell], sharedTarget, originX: 2);
        IReadOnlyList<PlacementDraft> drafts = [first, second];
        IReadOnlyList<PathCostPair> pairs = PathCostLookup.RequestPairsFor(drafts);
        PathCostLookup lookup = PathCostLookup.FromProbeResults(
            drafts,
            [
                new PathCostResult(true, 30, pairs[0].From, pairs[0].To),
                new PathCostResult(true, 5, pairs[1].From, pairs[1].To)
            ]);

        IReadOnlyList<ScoredDraft> scored = new WalkablePathCostScorer().Score(drafts, lookup);

        scored.Should().HaveCount(2);
        scored[0].Draft.Should().BeSameAs(first);
        scored[0].RawCost.Should().Be(30);
        scored[1].Draft.Should().BeSameAs(second);
        scored[1].RawCost.Should().Be(5);
    }

    private static PlacementDraft Draft(
        string generatorId,
        string roomId,
        IReadOnlyList<MapCell> accessCells,
        MapPosition target,
        int originX = 0) =>
        new(
            GeneratorId: generatorId,
            Group: new BlueprintGroup(
                Label: generatorId,
                MapId: 7,
                Assets:
                [
                    new BlueprintAsset(
                        Role: "floor",
                        DefName: "Concrete",
                        StuffDefName: null,
                        Cell: new MapCell(originX, 1),
                        Rotation: 0)
                ]),
            SourceAnchor: new ResolvedAnchor(
                new WillieRoomAnchor(roomId, RoomClass.Kitchen, "Kitchen", 20, target, []),
                target,
                AnchorMatchReason.CentroidFallback),
            AccessCells: accessCells,
            Assumptions: [],
            ReasonSummary: "test draft");
}
