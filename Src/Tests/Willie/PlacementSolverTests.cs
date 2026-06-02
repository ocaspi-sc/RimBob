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
    public async Task SolveAsync_WithValidatedFreezerDraft_ReturnsMultipleOptions()
    {
        FakePathCostProbe pathCost = new(reachable: true, cost: 12);
        FakePlacementValidator validator = new(canPlaceAll: true);
        PlacementSolver solver = new(pathCost, validator);

        PlacementResult result = await solver.SolveAsync(SpecWithMaterials(), Briefing(), State([]));

        result.NoFit.Should().BeNull();
        result.Options.Should().HaveCount(3);
        AdviceOption option = result.Options[0];
        option.BlueprintGroup.Assets.Should().Contain(asset => asset.Role == "cooler");
        option.EstimatedMaterials.Should().ContainSingle(material =>
            material.DefName == "BlocksGranite" && material.Count == 5);
        result.Draftable.Should().Be(PlacementReadiness.Ready);
        result.PlacementValid.Should().Be(PlacementReadiness.Ready);
        result.MaterialsReady.Should().Be(PlacementReadiness.Ready);
        result.ApplyReady.Should().Be(PlacementReadiness.Ready);
        result.Trace.Notes.Should().NotContain("group_place_apply_out_of_scope");
        MetricValue metric = result.Trace.Drafts
            .Where(trace => trace.Status == "scored")
            .SelectMany(trace => trace.Metrics)
            .First(metric => metric.Id == "freezer_to_kitchen_distance");
        metric.Unit.Should().Be("path_tiles");
        metric.RawValue.Should().Be(12);
        metric.Normalized.Should().BeGreaterThan(0);
        result.Trace.Drafts.Where(trace => trace.Status == "selected").Should().HaveCount(3);
        result.Trace.Drafts.Where(trace => trace.Status == "validated").Should().HaveCount(3);
        validator.ValidateCount.Should().Be(3);
        validator.ValidatedGroup.Should().NotBeNull();
    }

    [Fact]
    public async Task SolveAsync_WithValidatedDraftAndUnknownMaterials_AllowsApply()
    {
        PlacementSolver solver = new(
            new FakePathCostProbe(reachable: true, cost: 12),
            new FakePlacementValidator(canPlaceAll: true));

        PlacementResult result = await solver.SolveAsync(
            SpecWithMaterials() with { MaterialsOnHand = [] },
            Briefing(),
            State([]));

        result.NoFit.Should().BeNull();
        result.Options.Should().NotBeEmpty();
        result.MaterialsReady.Should().Be(PlacementReadiness.Unknown);
        result.ApplyReady.Should().Be(PlacementReadiness.Ready);
    }

    [Fact]
    public async Task SolveAsync_WithValidatedDraftAndShortMaterials_AllowsApply()
    {
        PlacementSolver solver = new(
            new FakePathCostProbe(reachable: true, cost: 12),
            new FakePlacementValidator(canPlaceAll: true));

        PlacementResult result = await solver.SolveAsync(
            SpecWithMaterials() with { MaterialsOnHand = [new MaterialHint("BlocksGranite", 4)] },
            Briefing(),
            State([]));

        result.NoFit.Should().BeNull();
        result.Options.Should().NotBeEmpty();
        result.MaterialsReady.Should().Be(PlacementReadiness.Blocked);
        result.ApplyReady.Should().Be(PlacementReadiness.Ready);
    }

    [Fact]
    public async Task SolveAsync_WhenNoAnchors_ReturnsNoAnchorsNoFit()
    {
        PlacementSolver solver = new(
            new FakePathCostProbe(reachable: true, cost: 12),
            new FakePlacementValidator(canPlaceAll: true));

        PlacementResult result = await solver.SolveAsync(
            SpecWithMaterials(),
            StableBriefing() with { AnchorInventory = new WillieAnchorInventory([]) },
            State([]));

        result.NoFit.Should().Be(NoFitReason.NoAnchors);
        result.Options.Should().BeEmpty();
        result.Draftable.Should().Be(PlacementReadiness.Blocked);
        result.ApplyReady.Should().Be(PlacementReadiness.Blocked);
        result.Trace.Notes.Should().Contain("no room anchor and no Home-area buildable region with a target cell");
    }

    [Fact]
    public async Task SolveAsync_WhenNoRoomAnchorsAndBuildableRegionPresent_UsesFallbackAnchor()
    {
        PlacementSolver solver = new(
            new FakePathCostProbe(reachable: true, cost: 12),
            new FakePlacementValidator(canPlaceAll: true));
        WillieRoomAnchor homeArea = new(
            RoomId: "area:0",
            Class: RoomClass.BuildableRegion,
            RoleLabel: "Home",
            CellsCount: 400,
            Centroid: new MapPosition(15, 0, 15),
            ContainedBuildingIds: [])
        {
            Bounds = new MapRect(5, 5, 24, 24)
        };
        WillieBriefing briefing = StableBriefing() with
        {
            AnchorInventory = new WillieAnchorInventory([homeArea])
        };

        PlacementResult result = await solver.SolveAsync(SpecWithMaterials(), briefing, State([]));

        result.NoFit.Should().BeNull();
        result.Options.Should().NotBeEmpty();
        result.Trace.Notes.Should().Contain("no room anchor matched; using Home-area buildable region as fallback locus");
        result.Trace.Drafts.Should().Contain(trace => trace.AnchorRoomId == "area:0");
        result.Options.SelectMany(option => option.BlueprintGroup.Assets)
            .Should().OnlyContain(asset =>
                asset.Cell.X >= 5 &&
                asset.Cell.X <= 24 &&
                asset.Cell.Z >= 5 &&
                asset.Cell.Z <= 24);
    }

    [Fact]
    public async Task SolveAsync_WhenRoomAnchorExists_DoesNotUseBuildableRegionFallback()
    {
        PlacementSolver solver = new(
            new FakePathCostProbe(reachable: true, cost: 12),
            new FakePlacementValidator(canPlaceAll: true));
        WillieRoomAnchor homeArea = new(
            RoomId: "area:0",
            Class: RoomClass.BuildableRegion,
            RoleLabel: "Home",
            CellsCount: 400,
            Centroid: new MapPosition(15, 0, 15),
            ContainedBuildingIds: [])
        {
            Bounds = new MapRect(5, 5, 24, 24)
        };
        WillieBriefing roomBriefing = Briefing();
        WillieBriefing briefing = roomBriefing with
        {
            AnchorInventory = new WillieAnchorInventory([.. roomBriefing.AnchorInventory.Anchors, homeArea])
        };

        PlacementResult result = await solver.SolveAsync(SpecWithMaterials(), briefing, State([]));

        result.NoFit.Should().BeNull();
        result.Trace.Notes.Should().NotContain("no room anchor matched; using Home-area buildable region as fallback locus");
        result.Trace.Drafts.Should().NotContain(trace => trace.AnchorRoomId == "area:0");
    }

    [Fact]
    public async Task SolveAsync_WithKitchenFunctionAnchorInBarracks_ReturnsOptions()
    {
        PlacementSolver solver = new(
            new FakePathCostProbe(reachable: true, cost: 12),
            new FakePlacementValidator(canPlaceAll: true));
        WillieBriefing briefing = StableBriefing() with
        {
            AnchorInventory = new WillieAnchorInventory([
                new WillieRoomAnchor(
                    "multi-room",
                    RoomClass.Barracks,
                    "Barracks",
                    64,
                    new MapPosition(20, 0, 20),
                    ["bed-1", "stove-1"]),
                new WillieRoomAnchor(
                    "multi-room",
                    RoomClass.Kitchen,
                    "Barracks",
                    64,
                    new MapPosition(8, 0, 8),
                    ["bed-1", "stove-1"])
            ])
        };

        PlacementResult result = await solver.SolveAsync(SpecWithMaterials(), briefing, State([]));

        result.NoFit.Should().BeNull();
        result.Options.Should().NotBeEmpty();
        result.Trace.Notes.Should().NotContain("no resolved near-anchor with a target cell");
    }

    [Fact]
    public async Task SolveAsync_WhenGeneratorsEmitNoDrafts_ReturnsNoDraftsNoFit()
    {
        PlacementSolver solver = new(
            new FakePathCostProbe(reachable: true, cost: 12),
            new FakePlacementValidator(canPlaceAll: true),
            [new FixedDraftGenerator(Array.Empty<PlacementDraft>())]);

        PlacementResult result = await solver.SolveAsync(SpecWithMaterials(), Briefing(), State([]));

        result.NoFit.Should().Be(NoFitReason.NoDrafts);
        result.Options.Should().BeEmpty();
        result.Draftable.Should().Be(PlacementReadiness.Blocked);
        result.Trace.Notes.Should().Contain("generators emitted no drafts");
    }

    [Fact]
    public async Task SolveAsync_WhenPathCostHasNoReachablePairs_ReturnsNoReachablePathNoFit()
    {
        PlacementSolver solver = new(
            new FakePathCostProbe(reachable: false, cost: 12),
            new FakePlacementValidator(canPlaceAll: true));

        PlacementResult result = await solver.SolveAsync(SpecWithMaterials(), Briefing(), State([]));

        result.NoFit.Should().Be(NoFitReason.NoReachablePath);
        result.Options.Should().BeEmpty();
        result.Draftable.Should().Be(PlacementReadiness.Ready);
        result.Trace.Notes.Should().Contain("no reachable path from draft access cell to anchor target");
    }

    [Fact]
    public async Task SolveAsync_WithZeroPathCosts_EmitsFiniteDeterministicScores()
    {
        PlacementSolver solver = new(
            new FakePathCostProbe(reachable: true, cost: 0),
            new FakePlacementValidator(canPlaceAll: true));

        PlacementResult first = await solver.SolveAsync(SpecWithMaterials(), Briefing(), State([]));
        PlacementResult second = await solver.SolveAsync(SpecWithMaterials(), Briefing(), State([]));

        IReadOnlyList<MetricValue> distanceMetrics = first.Trace.Drafts
            .Where(trace => trace.Status == "scored")
            .SelectMany(trace => trace.Metrics)
            .Where(metric => metric.Id == "freezer_to_kitchen_distance")
            .ToList();
        distanceMetrics.Should().NotBeEmpty();
        distanceMetrics.Should().OnlyContain(metric =>
            metric.RawValue == 0 &&
            metric.Normalized == 1d &&
            !double.IsNaN(metric.Contribution) &&
            !double.IsInfinity(metric.Contribution));
        second.Trace.Should().BeEquivalentTo(first.Trace, options => options.WithStrictOrdering());
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

    [Fact]
    public async Task SolveAsync_WithOneValidSelectedDraft_ReturnsOneOption()
    {
        ResolvedAnchor anchor = ResolvedKitchenAnchor(new MapPosition(8, 0, 8));
        PlacementDraft valid = Draft("valid", anchor, new MapCell(5, 5), new MapCell(4, 5));
        PlacementSolver solver = new(
            new FakePathCostProbe(reachable: true, cost: 4),
            new LabelPlacementValidator(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "valid" }),
            [new FixedDraftGenerator(valid)]);

        PlacementResult result = await solver.SolveAsync(SpecWithMaterials(), Briefing(), State([]));

        result.NoFit.Should().BeNull();
        result.Options.Should().ContainSingle().Which.Label.Should().Be("valid");
        result.Trace.Drafts.Should().ContainSingle(trace => trace.Status == "selected");
        result.Trace.Drafts.Should().ContainSingle(trace => trace.Status == "validated");
    }

    [Fact]
    public async Task SolveAsync_WithSeveralDrafts_ValidatesAtMostThree()
    {
        ResolvedAnchor anchor = ResolvedKitchenAnchor(new MapPosition(8, 0, 8));
        IReadOnlyList<PlacementDraft> drafts =
        [
            Draft("draft-a", anchor, new MapCell(3, 3), new MapCell(2, 3)),
            Draft("draft-b", anchor, new MapCell(6, 3), new MapCell(5, 3)),
            Draft("draft-c", anchor, new MapCell(9, 3), new MapCell(8, 3)),
            Draft("draft-d", anchor, new MapCell(12, 3), new MapCell(11, 3))
        ];
        CountingPlacementValidator validator = new(canPlaceAll: true);
        PlacementSolver solver = new(
            new SequencedPathCostProbe([1, 2, 3, 4]),
            validator,
            [new FixedDraftGenerator(drafts)]);

        PlacementResult result = await solver.SolveAsync(SpecWithMaterials(), Briefing(), State([]));

        result.NoFit.Should().BeNull();
        result.Options.Should().HaveCount(3);
        validator.ValidateCount.Should().Be(3);
        result.Trace.Drafts.Where(trace => trace.Status == "selected").Should().HaveCount(3);
    }

    [Fact]
    public async Task SolveAsync_WithDefaultGenerators_CompetesTemplateAndLargestEmptyRectangle()
    {
        PlacementSolver solver = new(
            new FakePathCostProbe(reachable: true, cost: 12),
            new FakePlacementValidator(canPlaceAll: true));

        PlacementResult result = await solver.SolveAsync(SpecWithMaterials(), Briefing(), State([]));

        result.NoFit.Should().BeNull();
        result.Trace.Drafts
            .Where(trace => trace.Status == "scored")
            .Select(trace => trace.GeneratorId)
            .Should().Contain(["template_anchored", "largest_empty_rect"]);
        result.Trace.Drafts
            .Where(trace => trace.Status == "selected")
            .Select(trace => trace.GeneratorId)
            .Should().Contain("largest_empty_rect");
    }

    [Fact]
    public async Task SolveAsync_WithHospitalSpec_ReturnsHospitalOptions()
    {
        RoomTemplateSet templates = BreadthTemplates();
        PlacementSolver solver = new(
            new FakePathCostProbe(reachable: true, cost: 8),
            new FakePlacementValidator(canPlaceAll: true),
            [new TemplateAnchoredGenerator(templates), new LargestEmptyRectangleGenerator(templates)]);

        PlacementResult result = await solver.SolveAsync(HospitalSpec(), HospitalBriefing(), State([]));

        result.NoFit.Should().BeNull();
        result.Options.Should().NotBeEmpty();
        AdviceOption option = result.Options[0];
        option.Label.Should().Be("Starter hospital");
        option.BlueprintGroup.Assets.Should().Contain(asset => asset.Role == "medical_bed");
        option.Summary.Should().Contain("starter hospital");
    }

    [Fact]
    public async Task SolveAsync_WithDefaultGenerators_UsesProductionTemplateBreadth()
    {
        PlacementSolver solver = new(
            new FakePathCostProbe(reachable: true, cost: 8),
            new FakePlacementValidator(canPlaceAll: true));

        PlacementResult result = await solver.SolveAsync(HospitalSpec(), HospitalBriefing(), State([]));

        result.NoFit.Should().BeNull();
        result.Options.Should().NotBeEmpty();
        result.Options[0].Label.Should().Be("Starter hospital");
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

    private static RoomTemplateSet BreadthTemplates() =>
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
            Adjacency: [new AdjacencyHint(AdjacencyRelation.Near, "medbay")],
            Power: null,
            Temperature: null,
            MaterialsOnHand: [new MaterialHint("BlocksGranite", 100)],
            Deadline: null,
            Priority: AdvicePriority.Medium,
            Source: "Willie",
            Constraints: []);

    private static WillieBriefing HospitalBriefing() =>
        StableBriefing() with
        {
            AnchorInventory = new WillieAnchorInventory([
                new WillieRoomAnchor(
                    "hospital-anchor",
                    RoomClass.Hospital,
                    "Hospital",
                    24,
                    new MapPosition(8, 0, 8),
                    [])
            ])
        };

    private static ResolvedAnchor ResolvedKitchenAnchor(MapPosition position) =>
        new(
            new WillieRoomAnchor("kitchen", RoomClass.Kitchen, "Kitchen", 20, position, []),
            position,
            AnchorMatchReason.CentroidFallback);

    private static PlacementDraft Draft(
        string label,
        ResolvedAnchor anchor,
        MapCell origin,
        MapCell accessCell) =>
        new(
            GeneratorId: label,
            Group: new BlueprintGroup(
                Label: label,
                MapId: 7,
                Assets:
                [
                    new BlueprintAsset("floor", "Concrete", null, origin, 0),
                    new BlueprintAsset("door", "Door", "WoodLog", new MapCell(origin.X + 1, origin.Z), 0)
                ]),
            SourceAnchor: anchor,
            AccessCells: [accessCell],
            Assumptions: [],
            ReasonSummary: "test draft");

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
        public int ValidateCount { get; private set; }

        public Task<PlacementValidationResult> ValidateAsync(
            BlueprintGroup group,
            CancellationToken ct = default)
        {
            ValidateCount++;
            ValidatedGroup = group;
            PlacementValidationResult result = new(
                CanPlaceAll: canPlaceAll,
                Items: [],
                Cost: [new MaterialEstimate("BlocksGranite", 5)],
                OverlapConflicts: []);
            return Task.FromResult(result);
        }
    }

    private sealed class CountingPlacementValidator(bool canPlaceAll) : IPlacementValidator
    {
        public int ValidateCount { get; private set; }

        public Task<PlacementValidationResult> ValidateAsync(
            BlueprintGroup group,
            CancellationToken ct = default)
        {
            ValidateCount++;
            PlacementValidationResult result = new(
                CanPlaceAll: canPlaceAll,
                Items: [],
                Cost: [new MaterialEstimate("BlocksGranite", 5)],
                OverlapConflicts: []);
            return Task.FromResult(result);
        }
    }

    private sealed class LabelPlacementValidator(IReadOnlySet<string> validLabels) : IPlacementValidator
    {
        public Task<PlacementValidationResult> ValidateAsync(
            BlueprintGroup group,
            CancellationToken ct = default)
        {
            bool valid = validLabels.Contains(group.Label);
            PlacementValidationResult result = new(
                CanPlaceAll: valid,
                Items: [],
                Cost: [new MaterialEstimate("BlocksGranite", 5)],
                OverlapConflicts: []);
            return Task.FromResult(result);
        }
    }

    private sealed class FixedDraftGenerator : IPlacementGenerator
    {
        private readonly IReadOnlyList<PlacementDraft> drafts;

        public FixedDraftGenerator(PlacementDraft draft)
        {
            drafts = [draft];
        }

        public FixedDraftGenerator(IReadOnlyList<PlacementDraft> drafts)
        {
            this.drafts = drafts;
        }

        public string Id => "fixed";

        public IReadOnlyList<PlacementDraft> Generate(
            PlacementSpec spec,
            PlacementEvidence evidence,
            GenerationBudget budget) =>
            drafts;
    }

    private sealed class SequencedPathCostProbe(IReadOnlyList<int> costs) : IPathCostProbe
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
                .Select((pair, index) => new PathCostResult(true, costs[index % costs.Count], pair.From, pair.To))
                .ToList();
            return Task.FromResult(results);
        }
    }
}
