using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Ministers.Willie;

namespace RimBob.Tests.Willie;

public sealed class DiverseSelectorTests
{
    [Fact]
    public void SelectTopK_RespectsMaxValidate()
    {
        IReadOnlyList<ScoredDraft> selected = DiverseSelector.SelectTopK(
            [
                Score("a", "kitchen", new MapCell(1, 1), rawCost: 1),
                Score("b", "kitchen", new MapCell(5, 1), rawCost: 2),
                Score("c", "kitchen", new MapCell(9, 1), rawCost: 3)
            ],
            maxValidate: 2);

        selected.Should().HaveCount(2);
    }

    [Fact]
    public void SelectTopK_PrefersDifferentAnchorOverNearDuplicate()
    {
        ScoredDraft best = Score("best", "kitchen-a", new MapCell(1, 1), rawCost: 1);
        ScoredDraft nearDuplicate = Score("near", "kitchen-a", new MapCell(2, 1), rawCost: 2);
        ScoredDraft differentAnchor = Score("anchor", "kitchen-b", new MapCell(10, 1), rawCost: 3);

        IReadOnlyList<ScoredDraft> selected = DiverseSelector.SelectTopK(
            [best, nearDuplicate, differentAnchor],
            maxValidate: 2);

        selected[0].Draft.GeneratorId.Should().Be("best");
        selected[1].Draft.GeneratorId.Should().Be("anchor");
        selected[1].DiversityReason.Should().Be("different_anchor");
        selected[1].Metrics.Should().Contain(metric => metric.Id == "diversity_bonus");
    }

    [Fact]
    public void SelectTopK_PrefersDifferentOrientationWhenAnchorMatches()
    {
        ScoredDraft northDoor = Score("north", "kitchen", new MapCell(1, 1), rawCost: 1, doorRotation: 0);
        ScoredDraft nearSameDoor = Score("near", "kitchen", new MapCell(2, 1), rawCost: 2, doorRotation: 0);
        ScoredDraft eastDoor = Score("east", "kitchen", new MapCell(4, 1), rawCost: 3, doorRotation: 1);

        IReadOnlyList<ScoredDraft> selected = DiverseSelector.SelectTopK(
            [northDoor, nearSameDoor, eastDoor],
            maxValidate: 2);

        selected[1].Draft.GeneratorId.Should().Be("east");
        selected[1].DiversityReason.Should().Be("different_orientation");
    }

    [Fact]
    public void SelectTopK_SingletonGetsOnlyCandidateReason()
    {
        ScoredDraft only = Score("only", "kitchen", new MapCell(1, 1), rawCost: 1);

        ScoredDraft selected = DiverseSelector.SelectTopK([only], maxValidate: 3)
            .Should().ContainSingle().Subject;

        selected.DiversityReason.Should().Be("only_candidate");
    }

    private static ScoredDraft Score(
        string id,
        string roomId,
        MapCell origin,
        int rawCost,
        int doorRotation = 0)
    {
        PlacementDraft draft = new(
            GeneratorId: id,
            Group: new BlueprintGroup(
                Label: id,
                MapId: 7,
                Assets:
                [
                    new BlueprintAsset("floor", "Concrete", null, origin, 0),
                    new BlueprintAsset("door", "Door", "WoodLog", new MapCell(origin.X + 1, origin.Z), doorRotation)
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
        double normalized = 1d / (1d + rawCost);
        return new ScoredDraft(
            draft,
            rawCost,
            [
                new MetricValue(
                    Id: "freezer_to_kitchen_distance",
                    RawValue: rawCost,
                    Unit: "tiles",
                    Normalized: normalized,
                    Weight: 16,
                    Contribution: normalized * 16,
                    Better: "lower")
            ]);
    }
}
