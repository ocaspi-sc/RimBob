using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Ministers.Willie;
using RimBob.State;
using RimBob.Tests.Infrastructure;

namespace RimBob.Tests.Willie;

public sealed class MinisterOfWillieTests
{
    [Fact]
    public async Task RuleDecision_PublishesWillieAdviceSnapshot()
    {
        Harness harness = new();
        harness.SetStableState();
        harness.Colony.Power.Update(new PowerNetwork(ProductionW: 100, ConsumptionW: 500, StoredWd: 0, CapacityWd: 0));

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        AdviceItem advice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        advice.Minister.Should().Be("Willie");
        advice.Concern.Should().Be("power_stability");
        harness.Bus.ActiveSnapshot().StateSummaries.Should().ContainKey("Willie")
            .WhoseValue.Should().Contain("Power:");
    }

    [Fact]
    public async Task InboundFreezerFlag_PublishesThermalControlAdvice()
    {
        FakePlacementSolver solver = FakePlacementSolver.WithOptions(PlacementOption());
        Harness harness = new(solver);
        harness.SetStableState();
        harness.Flags.Publish(FreezerFlag());

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        AdviceItem advice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        advice.Minister.Should().Be("Willie");
        advice.Concern.Should().Be("thermal_control");
        advice.Actions.Should().ContainSingle().Which.Apply.Should().BeNull();
        AdviceOption option = advice.Options.Should().ContainSingle().Subject;
        option.Id.Should().Be("placement_freezer_10_12");
        option.Readiness.Should().NotBeNull();
        option.Readiness!.MaterialsReady.Should().Be("ready");
        advice.Rationale.Should().Contain("Placement solver: 1 validated option");
        solver.CallCount.Should().Be(1);
        solver.LastSpec.Should().NotBeNull();
        solver.LastSpec!.TargetClass.Should().Be(BuildingClass.Freezer);
        solver.LastState.Should().BeSameAs(harness.Colony);
    }

    [Fact]
    public async Task NonFreezerDecision_DoesNotCallPlacementSolver()
    {
        FakePlacementSolver solver = FakePlacementSolver.WithOptions(PlacementOption());
        Harness harness = new(solver);
        harness.SetStableState();
        harness.Colony.Power.Update(new PowerNetwork(ProductionW: 100, ConsumptionW: 500, StoredWd: 0, CapacityWd: 0));

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        solver.CallCount.Should().Be(0);
        AdviceItem advice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        advice.Options.Should().BeNull();
    }

    [Fact]
    public async Task FreezerSolverNoFit_RetainsProseAdviceAndPersistsSolverTrace()
    {
        FakePlacementSolver solver = FakePlacementSolver.WithNoFit(NoFitReason.NoReachablePath);
        CapturingReplayWriter replay = new();
        Harness harness = new(solver, replay);
        harness.SetStableState();
        harness.Flags.Publish(FreezerFlag());

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        AdviceItem advice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        advice.Options.Should().BeNull();
        advice.Rationale.Should().Contain("no walkable route to a kitchen anchor");
        MinisterReplayRecord record = replay.Records.Should().ContainSingle().Subject;
        record.OutputKind.Should().Be("placement_solver");
        record.Output.Should().BeOfType<PlacementSolverReplayOutput>();
        string outputJson = JsonSerializer.Serialize(record.Output);
        outputJson.Should().Contain(nameof(NoFitReason.NoReachablePath));
        outputJson.Should().Contain("test_note");
    }

    private static AgentFlag FreezerFlag() =>
        new(
            Id: "food:freezer_missing",
            SourceMinister: "Chef",
            Severity: FlagSeverity.Medium,
            Domain: "food",
            Summary: "Food storage needs freezer support",
            BuildingRequests:
            [
                new BuildingRequest(
                    Request: "starter freezer near kitchen",
                    Reason: "Chef needs cold storage",
                    TargetClass: BuildingClass.Freezer,
                    TargetDef: "Cooler",
                    RoomClass: RoomClass.Freezer,
                    Temperature: new TempNeed(TemperatureBand.Freezing, MustHold: true),
                    Priority: AdvicePriority.Medium,
                    RequestedFrom: "Willie")
            ]);

    private static AdviceOption PlacementOption() =>
        new(
            Id: "placement_freezer_10_12",
            Label: "Compact freezer",
            Summary: "Validated freezer shell near kitchen.",
            BlueprintGroup: new BlueprintGroup(
                Label: "Compact freezer",
                MapId: 7,
                Assets:
                [
                    new BlueprintAsset("wall", "Wall", "BlocksGranite", new MapCell(10, 12), 0)
                ]),
            EstimatedMaterials: [new MaterialEstimate("BlocksGranite", 5)],
            TradeoffNote: "Closest to kitchen.");

    private sealed class Harness
    {
        public ColonyState Colony { get; } = new();
        public AdviceBus Bus { get; } = new();
        public FlagChannel Flags { get; } = new();
        public MinisterOfWillie Minister { get; }

        public Harness(IPlacementSolver? solver = null, IReplayCorpusWriter? replay = null)
        {
            BriefingCache cache = new(Colony, new TestLogger<BriefingCache>());
            Minister = new(
                cache,
                new Rules(),
                solver ?? FakePlacementSolver.WithNoFit(NoFitReason.NoDrafts),
                Colony,
                new MinisterOutputStore(),
                Bus,
                Flags,
                NullLogger<MinisterOfWillie>.Instance,
                replay is null ? null : new MinisterReplayRecorder(replay));
        }

        public void SetStableState()
        {
            Colony.LastRefreshSource = ColonyStateOrigin.Live;
            Colony.LastLiveRefreshAt = DateTimeOffset.UtcNow;
            Colony.Map.Update(new MapInfoSnapshot(7, "(250,1,250)"));
            Colony.Economy.Update(new EconomyLedger(300_000, 0f, "", "", false, "5th of Aprimay, 5500, 14h"));
            Colony.Colonists.Update(new ColonistRegistry([
                new ColonistRecord("p1", "Alice", 28, "Female", 1f, 0.7f, 1f, false, false, null, null, [], []),
                new ColonistRecord("p2", "Bob", 30, "Male", 1f, 0.7f, 1f, false, false, null, null, [], []),
                new ColonistRecord("p3", "Cora", 32, "Female", 1f, 0.7f, 1f, false, false, null, null, [], [])
            ]));
            Colony.Rooms.Update(new RoomRegistry([
                Room("kitchen-room", "Kitchen"),
                Room("hospital-room", "Hospital"),
                Room("storage-room", "Storage")
            ]));
            Colony.Stockpiles.Update(new StockpileLedger(
                [new StockpileZone("stockpile-1", "stockpile", "Main storage", 40)],
                new Dictionary<string, int>()));
            Colony.Buildings.Update(new BuildingRegistry([
                new BuildingRecord("cooler-1", "Cooler", 1f, true, true),
                new BuildingRecord("generator-1", "WoodFiredGenerator", 1f, true, true),
                new BuildingRecord("battery-1", "Battery", 1f, true, true)
            ]));
            Colony.Power.Update(new PowerNetwork(ProductionW: 900, ConsumptionW: 650, StoredWd: 800, CapacityWd: 1000));
        }

        private static RoomRecord Room(string id, string role) =>
            new(
                Id: id,
                RoleLabel: role,
                Temperature: 21,
                CellsCount: 25,
                TouchesMapEdge: false,
                IsPrisonCell: false,
                IsDoorway: false,
                OpenRoofCount: 0,
                ContainedBedIds: [],
                Impressiveness: null,
                Beauty: null,
                Cleanliness: null,
                Space: null,
                Wealth: null);
    }

    private sealed class FakePlacementSolver(PlacementResult result) : IPlacementSolver
    {
        public int CallCount { get; private set; }
        public PlacementSpec? LastSpec { get; private set; }
        public ColonyState? LastState { get; private set; }

        public static FakePlacementSolver WithOptions(AdviceOption option) =>
            new(new PlacementResult(
                Options: [option],
                Trace: Trace(),
                NoFit: null,
                Draftable: PlacementReadiness.Ready,
                PlacementValid: PlacementReadiness.Ready,
                MaterialsReady: PlacementReadiness.Ready,
                ApplyReady: PlacementReadiness.Blocked));

        public static FakePlacementSolver WithNoFit(NoFitReason reason) =>
            new(new PlacementResult(
                Options: [],
                Trace: Trace(),
                NoFit: reason,
                Draftable: PlacementReadiness.Ready,
                PlacementValid: PlacementReadiness.Blocked,
                MaterialsReady: PlacementReadiness.Unknown,
                ApplyReady: PlacementReadiness.Blocked));

        public Task<PlacementResult> SolveAsync(
            PlacementSpec spec,
            WillieBriefing briefing,
            ColonyState colonyState,
            CancellationToken ct = default)
        {
            CallCount++;
            LastSpec = spec;
            LastState = colonyState;
            return Task.FromResult(result);
        }

        private static PlacementTrace Trace() =>
            new(
                "placement_solver",
                [
                    new PlacementDraftTrace(
                        GeneratorId: "test_generator",
                        AnchorRoomId: "kitchen-room",
                        Status: "selected",
                        Reason: null,
                        Metrics: [])
                ],
                ["test_note"]);
    }

    private sealed class CapturingReplayWriter : IReplayCorpusWriter
    {
        public List<MinisterReplayRecord> Records { get; } = [];

        public Task WriteAsync(MinisterReplayRecord record, CancellationToken ct)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }
}
