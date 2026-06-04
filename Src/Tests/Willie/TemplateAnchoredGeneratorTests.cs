using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Ministers.Willie;

namespace RimBob.Tests.Willie;

public sealed class TemplateAnchoredGeneratorTests
{
    [Fact]
    public void Generate_FirstFitPicksNearestInBoundsPosition()
    {
        ResolvedAnchor anchor = ResolvedKitchenAnchor(new MapPosition(8, 0, 8));
        PlacementEvidence evidence = Evidence([anchor], []);

        PlacementDraft draft = new TemplateAnchoredGenerator()
            .Generate(Spec(), evidence, new GenerationBudget(MaxDrafts: 1, MaxSearchRadius: 3))
            .Should().ContainSingle().Subject;

        draft.GeneratorId.Should().Be("template_anchored");
        draft.Group.MapId.Should().Be(7);
        draft.Group.Assets.Min(asset => asset.Cell.X).Should().Be(8);
        draft.Group.Assets.Min(asset => asset.Cell.Z).Should().Be(8);
        draft.AccessCells.Should().Contain(new MapCell(8, 11));
        draft.AccessCells.Should().Contain(new MapCell(7, 11));
        draft.Assumptions.Should().Contain("occupancy=building_position_point_approximation");
    }

    [Fact]
    public void Generate_DoorFacesTheAnchor()
    {
        ResolvedAnchor anchor = ResolvedKitchenAnchor(new MapPosition(12, 0, 8));
        PlacementDraft draft = new TemplateAnchoredGenerator()
            .Generate(Spec(), Evidence([anchor], []), new GenerationBudget(1, 3))
            .Should().ContainSingle().Subject;

        BlueprintAsset door = draft.Group.Assets.Single(asset => asset.Role == "door");
        BlueprintAsset cooler = draft.Group.Assets.Single(asset => asset.Role == "cooler");

        door.Cell.X.Should().Be(draft.Group.Assets.Min(asset => asset.Cell.X));
        cooler.Cell.X.Should().Be(draft.Group.Assets.Max(asset => asset.Cell.X));
    }

    [Fact]
    public void Generate_SkipsOccupiedScanPosition()
    {
        ResolvedAnchor anchor = ResolvedKitchenAnchor(new MapPosition(8, 0, 8));
        PlacementEvidence evidence = Evidence(
            [anchor],
            [new BuildingRecord("occupied", "ExistingWall", 1f, null, null, new MapPosition(9, 0, 9))]);

        PlacementDraft draft = new TemplateAnchoredGenerator()
            .Generate(Spec(), evidence, new GenerationBudget(MaxDrafts: 1, MaxSearchRadius: 4))
            .Should().ContainSingle().Subject;

        draft.Group.Assets.Should().NotContain(asset => asset.Cell == new MapCell(9, 9));
    }

    [Fact]
    public void Generate_SkipsOccupiedAccessCell()
    {
        ResolvedAnchor anchor = ResolvedKitchenAnchor(new MapPosition(8, 0, 8));
        MapCell blockedAccessCell = new(8, 11);
        PlacementEvidence evidence = Evidence(
            [anchor],
            [new BuildingRecord("occupied-access", "Wall", 1f, null, null, new MapPosition(blockedAccessCell.X, 0, blockedAccessCell.Z))]);

        PlacementDraft draft = new TemplateAnchoredGenerator()
            .Generate(Spec(), evidence, new GenerationBudget(MaxDrafts: 1, MaxSearchRadius: 4))
            .Should().ContainSingle().Subject;

        draft.AccessCells.Should().NotContain(blockedAccessCell);
    }

    [Fact]
    public void Generate_WithHigherBudgetEmitsDeterministicVariants()
    {
        ResolvedAnchor anchor = ResolvedKitchenAnchor(new MapPosition(8, 0, 8));
        PlacementEvidence evidence = Evidence([anchor], []);

        IReadOnlyList<PlacementDraft> drafts = new TemplateAnchoredGenerator()
            .Generate(Spec(), evidence, new GenerationBudget(MaxDrafts: 4, MaxSearchRadius: 3));

        drafts.Should().HaveCount(4);
        drafts
            .Select(draft => (X: draft.Group.Assets.Min(asset => asset.Cell.X), Z: draft.Group.Assets.Min(asset => asset.Cell.Z)))
            .Distinct()
            .Should().HaveCount(4);
    }

    [Fact]
    public void Generate_RespectsSearchRadius()
    {
        ResolvedAnchor anchor = ResolvedKitchenAnchor(new MapPosition(0, 0, 0));
        PlacementEvidence evidence = Evidence([anchor], []);

        new TemplateAnchoredGenerator()
            .Generate(Spec(), evidence, new GenerationBudget(MaxDrafts: 1, MaxSearchRadius: 0))
            .Should().BeEmpty();
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
        IReadOnlyList<ResolvedAnchor> anchors,
        IReadOnlyList<BuildingRecord> buildings) =>
        PlacementEvidence.Build(
            new MapInfoSnapshot(7, "30x30"),
            new BuildingRegistry(buildings),
            anchors);

    private static ResolvedAnchor ResolvedKitchenAnchor(MapPosition position) =>
        new(
            new WillieRoomAnchor("kitchen", RoomClass.Kitchen, "Kitchen", 20, position, []),
            position,
            AnchorMatchReason.CentroidFallback);
}
