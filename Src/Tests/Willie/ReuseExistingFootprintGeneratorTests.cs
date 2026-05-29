using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Ministers.Willie;
using RimBob.State.Derivations.Common;

namespace RimBob.Tests.Willie;

public sealed class ReuseExistingFootprintGeneratorTests
{
    [Fact]
    public void Generate_UsesLiveSameClassRoomCellsForInteriorAssets()
    {
        ResolvedAnchor kitchen = ResolvedAnchor("kitchen", RoomClass.Kitchen, new MapPosition(4, 0, 4));
        WillieRoomAnchor hospital = new(
            RoomId: "hospital-1",
            Class: RoomClass.Hospital,
            RoleLabel: "Hospital",
            CellsCount: 25,
            Centroid: new MapPosition(12, 0, 12),
            ContainedBuildingIds: [])
        {
            Bounds = new MapRect(10, 10, 14, 14),
            Cells = CellsForRect(10, 10, 14, 14),
            EntryCells = [new MapPosition(12, 0, 9)],
            RegionId = 77
        };
        PlacementEvidence evidence = PlacementEvidence.Build(
            new MapInfoSnapshot(7, "30x30"),
            new BuildingRegistry([]),
            [kitchen],
            [hospital]);

        IReadOnlyList<PlacementDraft> drafts = new ReuseExistingFootprintGenerator(Templates())
            .Generate(HospitalSpec(), evidence, new GenerationBudget(MaxDrafts: 2, MaxSearchRadius: 0));

        PlacementDraft draft = drafts.Should().ContainSingle().Subject;
        draft.GeneratorId.Should().Be("reuse_existing_footprint");
        draft.Group.Label.Should().Be("Starter hospital in existing room");
        draft.Group.Assets.Should().Contain(asset => asset.Role == "medical_bed");
        draft.Group.Assets.Should().OnlyContain(asset =>
            (asset.Role == "floor" || asset.Role == "medical_bed") &&
            hospital.Cells.Select(cell => cell.ToMapCell()).Contains(asset.Cell));
        draft.AccessCells.Should().Equal(new MapCell(12, 9));
        draft.Assumptions.Should().Contain("room_footprint=live_rimapi_cells");
        draft.Assumptions.Should().Contain("reuse_room=hospital-1");
    }

    [Fact]
    public void Generate_ReturnsEmptyWhenRoomCellsAreMissing()
    {
        ResolvedAnchor kitchen = ResolvedAnchor("kitchen", RoomClass.Kitchen, new MapPosition(4, 0, 4));
        WillieRoomAnchor hospital = new(
            RoomId: "hospital-1",
            Class: RoomClass.Hospital,
            RoleLabel: "Hospital",
            CellsCount: 25,
            Centroid: new MapPosition(12, 0, 12),
            ContainedBuildingIds: []);
        PlacementEvidence evidence = PlacementEvidence.Build(
            new MapInfoSnapshot(7, "30x30"),
            new BuildingRegistry([]),
            [kitchen],
            [hospital]);

        new ReuseExistingFootprintGenerator(Templates())
            .Generate(HospitalSpec(), evidence, new GenerationBudget(MaxDrafts: 2, MaxSearchRadius: 0))
            .Should().BeEmpty();
    }

    private static RoomTemplateSet Templates() =>
        new(
        [
            new FreezerTemplate(),
            new HospitalTemplate(),
            new BedroomTemplate(),
            new WorkshopTemplate(),
            new StorageTemplate()
        ]);

    private static PlacementSpec HospitalSpec() =>
        new(
            Request: "starter hospital",
            Reason: "medical beds",
            TargetClass: BuildingClass.Bed,
            TargetDef: null,
            RoomClass: RoomClass.Hospital,
            CapacityNeed: new CapacityNeed(CapacityMeasure.Beds, 2),
            Adjacency: [new AdjacencyHint(AdjacencyRelation.Near, "kitchen")],
            Power: null,
            Temperature: null,
            MaterialsOnHand: [],
            Deadline: null,
            Priority: AdvicePriority.Medium,
            Source: "Willie",
            Constraints: []);

    private static ResolvedAnchor ResolvedAnchor(
        string roomId,
        RoomClass roomClass,
        MapPosition position) =>
        new(
            new WillieRoomAnchor(roomId, roomClass, roomClass.ToString(), 20, position, []),
            position,
            AnchorMatchReason.CentroidFallback);

    private static IReadOnlyList<MapPosition> CellsForRect(int x1, int z1, int x2, int z2)
    {
        List<MapPosition> cells = [];
        for (int z = z1; z <= z2; z++)
        {
            for (int x = x1; x <= x2; x++)
            {
                cells.Add(new MapPosition(x, 0, z));
            }
        }

        return cells;
    }
}
