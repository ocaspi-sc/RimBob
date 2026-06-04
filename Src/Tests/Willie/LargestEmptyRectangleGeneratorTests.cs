using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Ministers.Willie;

namespace RimBob.Tests.Willie;

public sealed class LargestEmptyRectangleGeneratorTests
{
    [Fact]
    public void Generate_UsesLargestFreeRectAndAnchorPreferredOrigin()
    {
        ResolvedAnchor anchor = ResolvedKitchenAnchor(new MapPosition(10, 0, 10));
        PlacementDraft draft = new LargestEmptyRectangleGenerator()
            .Generate(Spec(), Evidence("20x20", [anchor], []), new GenerationBudget(1, 32))
            .Should().ContainSingle().Subject;

        draft.GeneratorId.Should().Be("largest_empty_rect");
        draft.Group.Assets.Min(asset => asset.Cell.X).Should().Be(7);
        draft.Group.Assets.Min(asset => asset.Cell.Z).Should().Be(7);
        draft.Assumptions.Should().Contain("free_space=largest_empty_rectangle");
        draft.Assumptions.Should().Contain("free_rect=20x20");
    }

    [Fact]
    public void Generate_RespectsOccupiedCells()
    {
        MapCell occupied = new(7, 7);
        ResolvedAnchor anchor = ResolvedKitchenAnchor(new MapPosition(10, 0, 10));
        IReadOnlyList<PlacementDraft> drafts = new LargestEmptyRectangleGenerator()
            .Generate(
                Spec(),
                Evidence(
                    "20x20",
                    [anchor],
                    [new BuildingRecord("blocked", "Wall", 1f, null, null, new MapPosition(occupied.X, 0, occupied.Z))]),
                new GenerationBudget(3, 32));

        drafts.Should().NotBeEmpty();
        drafts.SelectMany(draft => draft.Group.Assets).Should().NotContain(asset => asset.Cell == occupied);
    }

    [Fact]
    public void Generate_IsDeterministic()
    {
        ResolvedAnchor anchor = ResolvedKitchenAnchor(new MapPosition(10, 0, 10));
        PlacementEvidence evidence = Evidence("20x20", [anchor], []);
        LargestEmptyRectangleGenerator generator = new();

        IReadOnlyList<PlacementDraft> first = generator.Generate(Spec(), evidence, new GenerationBudget(4, 32));
        IReadOnlyList<PlacementDraft> second = generator.Generate(Spec(), evidence, new GenerationBudget(4, 32));

        second.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
    }

    private static PlacementSpec Spec() =>
        new(
            Request: "starter freezer",
            Reason: "food storage",
            TargetClass: BuildingClass.Freezer,
            TargetDef: null,
            RoomClass: RoomClass.Freezer,
            CapacityNeed: new CapacityNeed(CapacityMeasure.FoodUnits, 64),
            Adjacency: [new AdjacencyHint(AdjacencyRelation.Near, "kitchen")],
            Power: null,
            Temperature: new TempNeed(TemperatureBand.Freezing, true),
            MaterialsOnHand: [],
            Deadline: null,
            Priority: Priority.Medium,
            Source: "Chef",
            Constraints: []);

    private static PlacementEvidence Evidence(
        string mapSize,
        IReadOnlyList<ResolvedAnchor> anchors,
        IReadOnlyList<BuildingRecord> buildings) =>
        PlacementEvidence.Build(
            new MapInfoSnapshot(7, mapSize),
            new BuildingRegistry(buildings),
            anchors);

    private static ResolvedAnchor ResolvedKitchenAnchor(MapPosition position) =>
        new(
            new WillieRoomAnchor("kitchen", RoomClass.Kitchen, "Kitchen", 20, position, []),
            position,
            AnchorMatchReason.CentroidFallback);
}
