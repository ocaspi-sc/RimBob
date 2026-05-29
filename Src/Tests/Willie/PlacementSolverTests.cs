using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Core.Placement;
using RimBob.Ministers.Willie;
using RimBob.State;

namespace RimBob.Tests.Willie;

public sealed class PlacementSolverTests
{
    [Fact]
    public async Task SolveAsync_WithValidatedFreezerDraft_ReturnsOneOption()
    {
        FakePathCostProbe pathCost = new(reachable: true, cost: 12);
        FakePlacementValidator validator = new(canPlaceAll: true);
        PlacementSolver solver = new(pathCost, validator);

        PlacementResult result = await solver.SolveAsync(SpecWithMaterials(), Briefing(), State([]));

        result.NoFit.Should().BeNull();
        result.Options.Should().ContainSingle();
        AdviceOption option = result.Options[0];
        option.BlueprintGroup.Assets.Should().Contain(asset => asset.Role == "cooler");
        option.EstimatedMaterials.Should().ContainSingle(material =>
            material.DefName == "BlocksGranite" && material.Count == 5);
        result.Draftable.Should().Be(PlacementReadiness.Ready);
        result.PlacementValid.Should().Be(PlacementReadiness.Ready);
        result.MaterialsReady.Should().Be(PlacementReadiness.Ready);
        MetricValue metric = result.Trace.Drafts.Single(trace => trace.Status == "scored").Metrics
            .Should().ContainSingle().Subject;
        metric.Unit.Should().Be("path_tiles");
        metric.RawValue.Should().Be(12);
        metric.Normalized.Should().BeGreaterThan(0);
        validator.ValidatedGroup.Should().NotBeNull();
    }

    [Fact]
    public async Task SolveAsync_WhenDraftOverlapsOccupiedCell_ReturnsNoFit()
    {
        ResolvedAnchor anchor = ResolvedKitchenAnchor(new MapPosition(8, 0, 8));
        PlacementDraft draft = new(
            GeneratorId: "fixed",
            Group: new BlueprintGroup(
                Label: "blocked",
                MapId: 7,
                Assets: [new BlueprintAsset("wall", "Wall", "BlocksGranite", new MapCell(5, 5), 0)]),
            SourceAnchor: anchor,
            AccessCells: [new MapCell(4, 5)],
            Assumptions: [],
            ReasonSummary: "test draft");
        PlacementSolver solver = new(
            new FakePathCostProbe(true, 1),
            new FakePlacementValidator(true),
            [new FixedDraftGenerator(draft)]);

        PlacementResult result = await solver.SolveAsync(
            SpecWithMaterials(),
            Briefing(),
            State([new BuildingRecord("occupied", "Wall", 1f, null, null, new MapPosition(5, 0, 5))]));

        result.NoFit.Should().Be(NoFitReason.HardGateRejected);
        result.Options.Should().BeEmpty();
        result.Trace.Drafts.Should().ContainSingle(trace =>
            trace.Status == "rejected" &&
            trace.Reason == "occupied_cell");
    }

    [Fact]
    public async Task SolveAsync_WhenValidateRejects_ReturnsNoFit()
    {
        PlacementSolver solver = new(
            new FakePathCostProbe(reachable: true, cost: 12),
            new FakePlacementValidator(canPlaceAll: false));

        PlacementResult result = await solver.SolveAsync(SpecWithMaterials(), Briefing(), State([]));

        result.NoFit.Should().Be(NoFitReason.ValidationRejected);
        result.Options.Should().BeEmpty();
        result.Trace.Drafts.Should().Contain(trace => trace.Status == "validation_rejected");
    }

    public static PlacementSpec SpecWithMaterials() =>
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
            MaterialsOnHand: [new MaterialHint("BlocksGranite", 100)],
            Deadline: null,
            Priority: AdvicePriority.Medium,
            Source: "Chef",
            Constraints: []);

    public static WillieBriefing Briefing() =>
        StableBriefing() with
        {
            AnchorInventory = new WillieAnchorInventory([
                new WillieRoomAnchor(
                    "kitchen",
                    RoomClass.Kitchen,
                    "Kitchen",
                    20,
                    new MapPosition(8, 0, 8),
                    [])
            ])
        };

    public static ColonyState State(IReadOnlyList<BuildingRecord> buildings)
    {
        ColonyState state = new();
        state.Map.Update(new MapInfoSnapshot(7, "30x30"));
        state.Buildings.Update(new BuildingRegistry(buildings));
        return state;
    }

    private static ResolvedAnchor ResolvedKitchenAnchor(MapPosition position) =>
        new(
            new WillieRoomAnchor("kitchen", RoomClass.Kitchen, "Kitchen", 20, position, []),
            position,
            AnchorMatchReason.CentroidFallback);

    private static WillieBriefing StableBriefing() =>
        new(
            BriefingVersion: 1,
            Date: GameTime.Create("5th of Aprimay, 5500, 14h", 300_000, 5500, "Aprimay", 5, 14),
            GameTick: 300_000,
            MapId: 7,
            ColonistCount: 3,
            PowerStability: new WilliePowerStabilitySummary(900, 650, 800, 1000, 250, 1, 2),
            ThermalControl: new WillieThermalControlSummary(1, 0, 0),
            FunctionalRooms: new WillieFunctionalRoomsSummary(new Dictionary<string, int>()),
            StoragePlacement: new WillieStoragePlacementSummary(1, 40),
            MaterialBottleneck: new WillieMaterialBottleneckSummary([], [], 0, 0),
            FireRisk: new WillieFireRiskSummary(0),
            StalledBuilds: new WillieStalledBuildsSummary([], 0, 0, 0, 0),
            BaseLayout: new WillieBaseLayoutSummary(3, 8),
            DataCoverage: new WillieDataCoverage(true, true, true, true, true, true, true, false));

    private sealed class FakePathCostProbe(bool reachable, int cost) : IPathCostProbe
    {
        public Task<IReadOnlyList<PathCostResult>> GetPathCostsAsync(
            int mapId,
            IReadOnlyList<PathCostPair> pairs,
            string tier = "region",
            string mode = "pass_doors",
            string peMode = "on_cell",
            CancellationToken ct = default)
        {
            IReadOnlyList<PathCostResult> results = pairs
                .Select(pair => new PathCostResult(reachable, cost, pair.From, pair.To))
                .ToList();
            return Task.FromResult(results);
        }
    }

    private sealed class FakePlacementValidator(bool canPlaceAll) : IPlacementValidator
    {
        public BlueprintGroup? ValidatedGroup { get; private set; }

        public Task<PlacementValidationResult> ValidateAsync(
            BlueprintGroup group,
            CancellationToken ct = default)
        {
            ValidatedGroup = group;
            PlacementValidationResult result = new(
                CanPlaceAll: canPlaceAll,
                Items: [],
                Cost: [new MaterialEstimate("BlocksGranite", 5)],
                OverlapConflicts: []);
            return Task.FromResult(result);
        }
    }

    private sealed class FixedDraftGenerator(PlacementDraft draft) : IPlacementGenerator
    {
        public string Id => "fixed";

        public IReadOnlyList<PlacementDraft> Generate(
            PlacementSpec spec,
            PlacementEvidence evidence,
            GenerationBudget budget) =>
            [draft];
    }
}
