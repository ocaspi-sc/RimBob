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

    [Fact]
    public void Generate_ReturnsEmptyForFreezerRooms()
    {
        ResolvedAnchor kitchen = ResolvedAnchor("kitchen", RoomClass.Kitchen, new MapPosition(4, 0, 4));
        WillieRoomAnchor freezer = RoomFootprint("freezer-1", RoomClass.Freezer, 10, 10, 14, 14);
        PlacementEvidence evidence = PlacementEvidence.Build(
            new MapInfoSnapshot(7, "30x30"),
            new BuildingRegistry([]),
            [kitchen],
            [freezer]);

        new ReuseExistingFootprintGenerator(Templates())
            .Generate(FreezerSpec(), evidence, new GenerationBudget(MaxDrafts: 2, MaxSearchRadius: 0))
            .Should().BeEmpty();
    }

    [Fact]
    public void Generate_RespectsMaxDrafts()
    {
        ResolvedAnchor kitchen = ResolvedAnchor("kitchen", RoomClass.Kitchen, new MapPosition(4, 0, 4));
        WillieRoomAnchor hospital = RoomFootprint("hospital-1", RoomClass.Hospital, 10, 10, 15, 14);
        PlacementEvidence evidence = PlacementEvidence.Build(
            new MapInfoSnapshot(7, "30x30"),
            new BuildingRegistry([]),
            [kitchen],
            [hospital]);

        IReadOnlyList<PlacementDraft> drafts = new ReuseExistingFootprintGenerator(Templates())
            .Generate(HospitalSpec(), evidence, new GenerationBudget(MaxDrafts: 1, MaxSearchRadius: 0));

        drafts.Should().ContainSingle();
    }

    [Fact]
    public void Generate_SkipsOccupiedInteriorCells()
    {
        ResolvedAnchor kitchen = ResolvedAnchor("kitchen", RoomClass.Kitchen, new MapPosition(4, 0, 4));
        WillieRoomAnchor hospital = RoomFootprint("hospital-1", RoomClass.Hospital, 10, 10, 14, 14);
        BuildingRecord occupyingBuilding = new(
            Id: "building-1",
            Def: "Bed",
            Hp: 100,
            PowerOn: null,
            IsWorking: null,
            Position: new MapPosition(10, 0, 10),
            Label: "existing bed");
        PlacementEvidence evidence = PlacementEvidence.Build(
            new MapInfoSnapshot(7, "30x30"),
            new BuildingRegistry([occupyingBuilding]),
            [kitchen],
            [hospital]);

        PlacementDraft draft = new ReuseExistingFootprintGenerator(Templates())
            .Generate(HospitalSpec(), evidence, new GenerationBudget(MaxDrafts: 2, MaxSearchRadius: 0))
            .Should().ContainSingle().Subject;

        draft.Group.Assets.Should().NotContain(asset => asset.Cell == new MapCell(10, 10));
        draft.Group.Assets.Should().Contain(asset => asset.Role == "medical_bed");
    }

    [Fact]
    public void Generate_DedupesSameCellSetAcrossAnchors()
    {
        ResolvedAnchor firstAnchor = ResolvedAnchor("kitchen-a", RoomClass.Kitchen, new MapPosition(4, 0, 4));
        ResolvedAnchor secondAnchor = ResolvedAnchor("kitchen-b", RoomClass.Kitchen, new MapPosition(6, 0, 4));
        WillieRoomAnchor hospital = RoomFootprint("hospital-1", RoomClass.Hospital, 10, 10, 14, 14);
        PlacementEvidence evidence = PlacementEvidence.Build(
            new MapInfoSnapshot(7, "30x30"),
            new BuildingRegistry([]),
            [firstAnchor, secondAnchor],
            [hospital]);

        PlacementDraft draft = new ReuseExistingFootprintGenerator(Templates())
            .Generate(HospitalSpec(), evidence, new GenerationBudget(MaxDrafts: 5, MaxSearchRadius: 0))
            .Should().ContainSingle().Subject;

        draft.SourceAnchor.Anchor.RoomId.Should().Be("kitchen-a");
    }

    [Fact]
    public void Generate_DuplicateAnchorsDoNotStarveLaterFootprints()
    {
        ResolvedAnchor firstAnchor = ResolvedAnchor("kitchen-a", RoomClass.Kitchen, new MapPosition(4, 0, 4));
        ResolvedAnchor secondAnchor = ResolvedAnchor("kitchen-b", RoomClass.Kitchen, new MapPosition(6, 0, 4));
        WillieRoomAnchor firstHospital = RoomFootprint("hospital-1", RoomClass.Hospital, 10, 10, 14, 14);
        WillieRoomAnchor secondHospital = RoomFootprint("hospital-2", RoomClass.Hospital, 20, 10, 24, 14);
        PlacementEvidence evidence = PlacementEvidence.Build(
            new MapInfoSnapshot(7, "30x30"),
            new BuildingRegistry([]),
            [firstAnchor, secondAnchor],
            [firstHospital, secondHospital]);

        IReadOnlyList<PlacementDraft> drafts = new ReuseExistingFootprintGenerator(Templates())
            .Generate(HospitalSpec(), evidence, new GenerationBudget(MaxDrafts: 2, MaxSearchRadius: 0));

        drafts.Should().HaveCount(2);
        drafts.Select(draft => draft.ReasonSummary).Should().Equal(
            "Starter hospital reuses existing Hospital room hospital-1",
            "Starter hospital reuses existing Hospital room hospital-2");
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

    private static PlacementSpec FreezerSpec() =>
        new(
            Request: "starter freezer",
            Reason: "food storage",
            TargetClass: BuildingClass.Freezer,
            TargetDef: null,
            RoomClass: RoomClass.Freezer,
            CapacityNeed: new CapacityNeed(CapacityMeasure.FoodUnits, 80),
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

    private static WillieRoomAnchor RoomFootprint(
        string roomId,
        RoomClass roomClass,
        int x1,
        int z1,
        int x2,
        int z2) =>
        new(
            RoomId: roomId,
            Class: roomClass,
            RoleLabel: roomClass.ToString(),
            CellsCount: (x2 - x1 + 1) * (z2 - z1 + 1),
            Centroid: new MapPosition((x1 + x2) / 2, 0, (z1 + z2) / 2),
            ContainedBuildingIds: [])
        {
            Bounds = new MapRect(x1, z1, x2, z2),
            Cells = CellsForRect(x1, z1, x2, z2),
            EntryCells = [new MapPosition((x1 + x2) / 2, 0, z1 - 1)],
            RegionId = 77
        };

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
