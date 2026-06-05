using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http;
using System.Net.Sockets;
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
    private static readonly DateTimeOffset FixedNow = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RuleDecision_PublishesWillieAdviceSnapshot()
    {
        Harness harness = new();
        harness.SetStableState();
        harness.Colony.Power.Update(new PowerNetwork(ProductionW: 100, ConsumptionW: 500, StoredWd: 0, CapacityWd: 0));

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        AdviceItem advice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        advice.Minister.Should().Be("Willie");
        advice.Id.Should().Be("willie_power_net_deficit");
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
        advice.Id.Should().Be("willie_building_request_active");
        advice.Actions.Should().HaveCount(2);
        advice.Actions[0].Apply.Should().BeNull();
        AdviceOption option = advice.Options.Should().ContainSingle().Subject;
        option.Id.Should().Be("placement_freezer_10_12");
        option.Readiness.Should().NotBeNull();
        option.Readiness!.MaterialsReady.Should().Be("ready");
        option.Readiness.ApplyReady.Should().Be("ready");
        PlaceBlueprintGroupApply apply = advice.Actions[1].Apply.Should().BeOfType<PlaceBlueprintGroupApply>().Subject;
        apply.Label.Should().Be(option.Label);
        apply.BlueprintGroup.Should().Be(option.BlueprintGroup);
        apply.AssetCount.Should().Be(option.BlueprintGroup.Assets.Count);
        advice.Rationale.Should().Contain("Placement solver: 1 validated option");
        solver.CallCount.Should().Be(1);
        solver.LastSpec.Should().NotBeNull();
        solver.LastSpec!.TargetClass.Should().Be(BuildingClass.Freezer);
        solver.LastState.Should().BeSameAs(harness.Colony);
        WillieSolverSnapshot snapshot = harness.SolverStore.Latest("Willie")!;
        snapshot.Should().NotBeNull();
        snapshot.Status.Should().Be("options");
        snapshot.Request.Should().NotBeNull();
        snapshot.Request!.Request.Should().Be("starter freezer near kitchen");
        snapshot.Request.SourceMinister.Should().Be("Chef");
        snapshot.GameTick.Should().Be(harness.Cache.GetWillieBriefing().GameTick);
        snapshot.Trace.Should().NotBeNull();
        snapshot.Options.Should().ContainSingle().Which.Id.Should().Be("placement_freezer_10_12");
    }

    [Fact]
    public async Task InboundFreezerFlag_AttachesOneApplyActionPerPlacementOption()
    {
        AdviceOption closeOption = PlacementOption("placement_freezer_close", "Close freezer", 10);
        AdviceOption cheapOption = PlacementOption("placement_freezer_cheap", "Cheap freezer", 30);
        FakePlacementSolver solver = FakePlacementSolver.WithOptions(closeOption, cheapOption);
        Harness harness = new(solver);
        harness.SetStableState();
        harness.Flags.Publish(FreezerFlag());

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        AdviceItem advice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        IReadOnlyList<PlaceBlueprintGroupApply> applies = advice.Actions
            .Select(action => action.Apply)
            .OfType<PlaceBlueprintGroupApply>()
            .ToList();
        applies.Should().HaveCount(2);
        applies.Select(apply => apply.Label).Should().Equal("Close freezer", "Cheap freezer");
        applies[0].BlueprintGroup.Should().Be(closeOption.BlueprintGroup);
        applies[1].BlueprintGroup.Should().Be(cheapOption.BlueprintGroup);
    }

    [Fact]
    public async Task InboundFreezerFlag_WhenConnectionFails_PreservesPriorOptionsSnapshot()
    {
        await SolverOfflinePreservesPriorOptionsAsync(
            new HttpRequestException(
                "connection refused",
                new SocketException((int)SocketError.ConnectionRefused)));
    }

    [Fact]
    public async Task InboundFreezerFlag_WhenNoMapIsLoaded_PreservesPriorOptionsSnapshot()
    {
        await SolverOfflinePreservesPriorOptionsAsync(
            new RimApiLiveStateUnavailableException(
                RimApiLiveStateUnavailableReason.NoLoadedMap,
                "RIMAPI returned no maps. Load a colony map before refreshing live state."));
    }

    [Fact]
    public async Task InboundFreezerFlag_WhenSolverFailsForNonConnectionReason_PublishesProseDegrade()
    {
        FakePlacementSolver solver = FakePlacementSolver.Throwing(new InvalidOperationException("validator broke"));
        Harness harness = new(solver);
        harness.SetStableState();
        harness.Flags.Publish(FreezerFlag());

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        AdviceItem advice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        advice.Id.Should().Be("willie_building_request_active");
        advice.Options.Should().BeNull();
        advice.Body.Should().Contain("Placement solver could not suggest layout options because it hit InvalidOperationException before validation completed.");
        advice.Rationale.Should().Contain("Placement solver unavailable: InvalidOperationException. Keeping prose advice.");
        harness.OutputStore.GetAdviceSnapshot("Willie")!.Advice.Should().ContainSingle()
            .Which.Id.Should().Be("willie_building_request_active");
        harness.SolverStore.Latest("Willie")!.Status.Should().Be("error");
    }

    [Fact]
    public async Task InboundFreezerFlag_PopulatesPlacementSpecMaterialsFromStoredResources()
    {
        FakePlacementSolver solver = FakePlacementSolver.WithOptions(PlacementOption());
        Harness harness = new(solver);
        harness.SetStableState();
        harness.Colony.StoredResources.Update(new StoredResourceRegistry(
            Items: [],
            CountByDef: new Dictionary<string, int>
            {
                ["Steel"] = 106,
                ["BlocksGranite"] = 90,
                ["ComponentIndustrial"] = 3
            },
            CountByCategory: new Dictionary<string, int>()));
        harness.Flags.Publish(FreezerFlag());

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        solver.LastSpec.Should().NotBeNull();
        solver.LastSpec!.MaterialsOnHand.Should().Equal(
            new MaterialHint("BlocksGranite", 90),
            new MaterialHint("ComponentIndustrial", 3),
            new MaterialHint("Steel", 106));
    }

    [Fact]
    public async Task InboundNonFreezerFlag_RunsSolverAndAttachesPlacementOptions()
    {
        AdviceOption option = PlacementOption("placement_workshop_20_12", "Starter workshop", 20);
        FakePlacementSolver solver = FakePlacementSolver.WithOptions(option);
        Harness harness = new(solver);
        harness.SetStableState();
        harness.Flags.Publish(WorkshopFlag());

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        AdviceItem advice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        advice.Id.Should().Be("willie_building_request_active");
        advice.Title.Should().Contain("Workshop request");
        advice.Options.Should().ContainSingle().Which.Id.Should().Be("placement_workshop_20_12");
        advice.Actions.Should().HaveCount(2);
        advice.Actions.Should().Contain(action => action.Apply is PlaceBlueprintGroupApply);
        solver.CallCount.Should().Be(1);
        solver.LastSpec.Should().NotBeNull();
        solver.LastSpec!.RoomClass.Should().Be(RoomClass.Workshop);
        solver.LastSpec.TargetClass.Should().Be(BuildingClass.ProductionBench);
    }

    [Fact]
    public async Task InboundZoneFlag_RunsGrowZoneSolverAndRecordsZoneBoardOutcome()
    {
        FakePlacementSolver solver = FakePlacementSolver.WithOptions(PlacementOption());
        FakeGrowZonePlacementSolver growZoneSolver = FakeGrowZonePlacementSolver.WithNoFit(NoFitReason.NoTerrainGrid);
        Harness harness = new(solver, growZoneSolver);
        harness.SetStableState();
        harness.Flags.Publish(ZoneFlag());

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        AdviceItem advice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        advice.Id.Should().Be("willie_zone_request_active");
        advice.Title.Should().Contain("Growing request");
        advice.Body.Should().Contain("Grow-zone solver could not suggest zone options");
        AdviceAction action = advice.Actions.Should().ContainSingle().Subject;
        action.Kind.Should().Be(AdviceActionKind.DesignateZone);
        action.Apply.Should().BeNull();
        solver.CallCount.Should().Be(0);
        growZoneSolver.CallCount.Should().Be(1);
        WillieZoneRequestBoardRow row = harness.SolverStore.ZoneRequestBoard("Willie").Should().ContainSingle().Subject;
        row.Inbound.SourceMinister.Should().Be("Chef");
        row.Inbound.Request.Should().BeEquivalentTo(ZoneFlag().ZoneRequests!.Single());
        row.Outcome.Should().NotBeNull();
        row.Outcome!.Status.Should().Be("no_fit");
        row.Outcome.NoFit.Should().Be(nameof(NoFitReason.NoTerrainGrid));
        harness.SolverStore.RequestBoard("Willie").Should().BeEmpty();
    }

    [Fact]
    public async Task InboundFreezerFlag_WhenMaterialsAreShort_AttachesApplyPayload()
    {
        FakePlacementSolver solver = FakePlacementSolver.WithOptions(
            PlacementReadiness.Blocked,
            PlacementReadiness.Ready,
            PlacementOption());
        Harness harness = new(solver);
        harness.SetStableState();
        harness.Flags.Publish(FreezerFlag());

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        AdviceItem advice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        advice.Actions.Should().HaveCount(2);
        advice.Actions.Should().Contain(action => action.Apply is PlaceBlueprintGroupApply);
        AdviceOption option = advice.Options.Should().ContainSingle().Subject;
        option.Readiness.Should().NotBeNull();
        option.Readiness!.MaterialsReady.Should().Be("blocked");
        option.Readiness.ApplyReady.Should().Be("ready");
        advice.Rationale.Should().Contain("materials_ready=blocked, apply_ready=ready");
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
        harness.SolverStore.Latest("Willie").Should().BeNull();
    }

    [Fact]
    public async Task AllHitsDecision_RecordsAndSolvesInboundRequestBoard()
    {
        FakePlacementSolver solver = FakePlacementSolver.WithOptions(PlacementOption());
        Harness harness = new(solver);
        harness.SetStableState();
        harness.Colony.Power.Update(new PowerNetwork(ProductionW: 100, ConsumptionW: 500, StoredWd: 0, CapacityWd: 0));
        harness.Flags.Publish(FreezerFlag());

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        solver.CallCount.Should().Be(1);
        harness.Bus.ActiveAdvice().Select(advice => advice.Id).Should().Equal(
            "willie_power_net_deficit",
            "willie_building_request_active");
        WillieRequestBoardRow row = harness.SolverStore.RequestBoard("Willie").Should().ContainSingle().Subject;
        row.Inbound.SourceMinister.Should().Be("Chef");
        row.Inbound.Request.Should().BeEquivalentTo(FreezerFlag().BuildingRequests!.Single());
        row.Outcome.Should().NotBeNull();
        row.Outcome!.Status.Should().Be("options");
    }

    [Fact]
    public async Task FreezerFlagNotRequestedFromWillie_DoesNotCallPlacementSolver()
    {
        FakePlacementSolver solver = FakePlacementSolver.WithOptions(PlacementOption());
        Harness harness = new(solver);
        harness.SetStableState();
        harness.Flags.Publish(FreezerFlag(requestedFrom: "Food"));

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        solver.CallCount.Should().Be(0);
        harness.Bus.ActiveAdvice().Should().BeEmpty();
    }

    [Fact]
    public async Task FreezerSolverNoFit_RetainsProseAdviceAndPersistsSolverTrace()
    {
        FakePlacementSolver solver = FakePlacementSolver.WithNoFit(NoFitReason.NoReachablePath);
        CapturingReplayWriter replay = new();
        Harness harness = new(solver, replay: replay);
        harness.SetStableState();
        harness.Flags.Publish(FreezerFlag());

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        AdviceItem advice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        advice.Options.Should().BeNull();
        advice.Body.Should().Contain("Placement solver could not suggest layout options because no walkable route to a kitchen anchor.");
        advice.Rationale.Should().Contain("no walkable route to a kitchen anchor");
        MinisterReplayRecord record = replay.Records.Should().ContainSingle().Subject;
        record.OutputKind.Should().Be("placement_solver");
        record.Output.Should().BeOfType<PlacementSolverReplayOutput>();
        string outputJson = JsonSerializer.Serialize(record.Output);
        outputJson.Should().Contain(nameof(NoFitReason.NoReachablePath));
        outputJson.Should().Contain("test_note");
        WillieSolverSnapshot snapshot = harness.SolverStore.Latest("Willie")!;
        snapshot.Should().NotBeNull();
        snapshot.Status.Should().Be("no_fit");
        snapshot.NoFit.Should().Be(nameof(NoFitReason.NoReachablePath));
    }

    [Fact]
    public async Task InboundNonFreezerNoFit_UsesRequestedRoomInNote()
    {
        FakePlacementSolver solver = FakePlacementSolver.WithNoFit(NoFitReason.NoDrafts);
        Harness harness = new(solver);
        harness.SetStableState();
        harness.Flags.Publish(WorkshopFlag());

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        AdviceItem advice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        advice.Options.Should().BeNull();
        advice.Body.Should().Contain("Placement solver could not suggest layout options because no workshop drafts were generated.");
        advice.Rationale.Should().Contain("no workshop drafts were generated");
    }

    [Fact]
    public async Task FreezerFlagWithMissingKitchen_SolvesBoardAndMissingRoomTrace()
    {
        FakePlacementSolver solver = FakePlacementSolver.WithNoFit(NoFitReason.NoAnchors);
        CapturingReplayWriter replay = new();
        Harness harness = new(solver, replay: replay);
        harness.SetStableStateWithoutKitchen();
        harness.Flags.Publish(FreezerFlag());

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        solver.CallCount.Should().Be(2);
        IReadOnlyList<AdviceItem> advice = harness.Bus.ActiveAdvice();
        advice.Select(item => item.Id).Should().Equal(
            "willie_building_request_active",
            "willie_kitchen_missing");
        AdviceItem requestAdvice = advice.Should()
            .Contain(item => item.Id == "willie_building_request_active")
            .Which;
        requestAdvice.Rationale.Should().Contain("no kitchen anchor is available");
        requestAdvice.Options.Should().BeNull();
        MinisterReplayRecord record = replay.Records.Should().ContainSingle().Subject;
        record.RuleTrace.Should().Be("rules:building_request_active+kitchen_missing");
        record.OutputKind.Should().Be("placement_solver");
        string outputJson = JsonSerializer.Serialize(record.Output);
        outputJson.Should().Contain(nameof(NoFitReason.NoAnchors));
    }

    [Fact]
    public async Task MultipleInboundRequests_RecordFreshOutcomeForEachBoardRow()
    {
        FakePlacementSolver solver = FakePlacementSolver.WithOptions(PlacementOption());
        Harness harness = new(solver);
        harness.SetStableState();
        harness.Flags.Publish(FreezerFlag());
        harness.Flags.Publish(WorkshopFlag());

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        solver.CallCount.Should().Be(2);
        IReadOnlyList<WillieRequestBoardRow> rows = harness.SolverStore.RequestBoard("Willie");
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(row => row.Outcome != null);
        rows.Select(row => row.Outcome!.Status).Should().Equal("options", "options");
        solver.Specs.Select(spec => spec.RoomClass).Should().BeEquivalentTo([RoomClass.Freezer, RoomClass.Workshop]);
    }

    [Fact]
    public async Task FlagFiredCycle_RecordsFullInboundBoard_NotJustTriggeringFlag()
    {
        FakePlacementSolver solver = FakePlacementSolver.WithOptions(PlacementOption());
        Harness harness = new(solver);
        harness.SetStableState();
        AgentFlag freezerFlag = FreezerFlag();
        AgentFlag workshopFlag = WorkshopFlag();
        harness.Flags.Publish(freezerFlag);
        harness.Flags.Publish(workshopFlag);

        await harness.Minister.RunPlayCycle(
            new PlayCycleContext(PlayCycleTrigger.FlagFired, Flag: freezerFlag),
            CancellationToken.None);

        solver.CallCount.Should().Be(1);
        IReadOnlyList<WillieRequestBoardRow> rows = harness.SolverStore.RequestBoard("Willie");
        rows.Should().HaveCount(2);
        rows.Select(row => row.Inbound.Request.Request).Should().Equal(
            "starter freezer near kitchen",
            "starter workshop near storage");
        rows.Single(row => row.Inbound.Request.Request == "starter freezer near kitchen")
            .Outcome.Should().NotBeNull();
        rows.Single(row => row.Inbound.Request.Request == "starter workshop near storage")
            .Outcome.Should().BeNull();
    }

    [Fact]
    public async Task FlagFiredCycles_KeepFullInboundBoardAcrossPerFlagRuns()
    {
        FakePlacementSolver solver = FakePlacementSolver.WithOptions(PlacementOption());
        Harness harness = new(solver);
        harness.SetStableState();
        AgentFlag freezerFlag = FreezerFlag();
        AgentFlag workshopFlag = WorkshopFlag();
        harness.Flags.Publish(freezerFlag);
        harness.Flags.Publish(workshopFlag);

        await harness.Minister.RunPlayCycle(
            new PlayCycleContext(PlayCycleTrigger.FlagFired, Flag: freezerFlag),
            CancellationToken.None);
        await harness.Minister.RunPlayCycle(
            new PlayCycleContext(PlayCycleTrigger.FlagFired, Flag: workshopFlag),
            CancellationToken.None);

        solver.CallCount.Should().Be(2);
        IReadOnlyList<WillieRequestBoardRow> rows = harness.SolverStore.RequestBoard("Willie");
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(row => row.Outcome != null);
        rows.Select(row => row.Outcome!.Status).Should().Equal("options", "options");
    }

    [Fact]
    public async Task DuplicateInboundRequestKeys_AreSolvedOnce()
    {
        FakePlacementSolver solver = FakePlacementSolver.WithOptions(PlacementOption());
        Harness harness = new(solver);
        harness.SetStableState();
        harness.Flags.Publish(DuplicateFreezerFlag());

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        solver.CallCount.Should().Be(1);
        harness.SolverStore.RequestBoard("Willie").Should().ContainSingle()
            .Which.Outcome.Should().NotBeNull();
    }

    [Fact]
    public async Task KitchenDependentInboundRequestWithMissingKitchen_FeaturesKitchenPrerequisite()
    {
        FakePlacementSolver solver = FakePlacementSolver.WithOptions(PlacementOption("placement_any", "Any placement", 10));
        Harness harness = new(solver);
        harness.SetStableStateWithoutKitchen();
        harness.Flags.Publish(FreezerNearKitchenFlag());

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        IReadOnlyList<AdviceItem> advice = harness.Bus.ActiveAdvice();
        advice.Should().ContainSingle()
            .Which.Id.Should().Be("willie_kitchen_missing");
        advice.Should().NotContain(item => item.Id == "willie_building_request_active");
        advice[0].Options.Should().ContainSingle();
        solver.CallCount.Should().Be(2);
        solver.Specs[0].RoomClass.Should().Be(RoomClass.Kitchen);
        solver.Specs[1].RoomClass.Should().Be(RoomClass.Freezer);
        harness.SolverStore.RequestBoard("Willie").Should().ContainSingle()
            .Which.Outcome.Should().NotBeNull();
    }

    [Fact]
    public async Task MissingKitchen_RunsSolverAndReplacesFallbackWithApplyAction()
    {
        AdviceOption option = PlacementOption("placement_kitchen_30_12", "Starter kitchen", 30);
        FakePlacementSolver solver = FakePlacementSolver.WithOptions(option);
        Harness harness = new(solver);
        harness.SetStableStateWithoutKitchen();

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        AdviceItem advice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        advice.Id.Should().Be("willie_kitchen_missing");
        advice.Options.Should().ContainSingle().Which.Id.Should().Be("placement_kitchen_30_12");
        advice.Actions.Should().ContainSingle()
            .Which.Apply.Should().BeOfType<PlaceBlueprintGroupApply>();
        advice.Rationale.Should().Contain("Placement solver: 1 validated option");
        solver.CallCount.Should().Be(1);
        solver.LastSpec.Should().NotBeNull();
        solver.LastSpec!.RoomClass.Should().Be(RoomClass.Kitchen);
        solver.LastSpec.TargetClass.Should().Be(BuildingClass.ProductionBench);
        solver.LastSpec.Adjacency.Should().ContainSingle()
            .Which.Target.Should().Be("storage");
    }

    private static async Task SolverOfflinePreservesPriorOptionsAsync(Exception solverException)
    {
        FakePlacementSolver solver = FakePlacementSolver.Throwing(solverException);
        CapturingReplayWriter replay = new();
        Harness harness = new(solver, replay: replay);
        harness.SetStableState();
        AdviceItem priorAdvice = PriorOptionsAdvice();
        harness.Bus.ReplaceMinisterAdvice("Willie", [priorAdvice], "Prior Willie options.");
        harness.Flags.Publish(FreezerFlag());

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        AdviceItem activeAdvice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        activeAdvice.Id.Should().Be(priorAdvice.Id);
        activeAdvice.Options.Should().ContainSingle()
            .Which.Id.Should().Be(priorAdvice.Options!.Single().Id);
        activeAdvice.Actions.Should().Contain(action => action.Apply is PlaceBlueprintGroupApply);

        AdviceSnapshot persisted = harness.OutputStore.GetAdviceSnapshot("Willie")
            ?? throw new InvalidOperationException("Expected Willie snapshot to remain persisted.");
        persisted.Advice.Should().ContainSingle()
            .Which.Id.Should().Be(priorAdvice.Id);
        persisted.Advice.Single().Options.Should().ContainSingle()
            .Which.Id.Should().Be(priorAdvice.Options!.Single().Id);

        MinisterReplayRecord record = replay.Records.Should().ContainSingle().Subject;
        record.OutputKind.Should().Be("placement_solver");
        PlacementSolverReplayOutput output = record.Output.Should().BeOfType<PlacementSolverReplayOutput>().Subject;
        output.Status.Should().Be("offline");
        record.Advice.Should().ContainSingle()
            .Which.Rationale.Should().Contain("Placement solver offline");

        WillieSolverSnapshot solverSnapshot = harness.SolverStore.Latest("Willie")
            ?? throw new InvalidOperationException("Expected Willie solver snapshot.");
        solverSnapshot.Status.Should().Be("offline");
        harness.Traces.Latest("Willie")!.Note.Should().Contain("solver offline; preserved prior advice");
    }

    private static AgentFlag FreezerFlag(string requestedFrom = "Willie") =>
        new(
            Id: "food:freezer_missing",
            SourceMinister: "Chef",
            Priority: Priority.Medium,
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
                    Priority: Priority.Medium,
                    RequestedFrom: requestedFrom)
            ]);

    private static AgentFlag FreezerNearKitchenFlag()
    {
        BuildingRequest request = FreezerFlag().BuildingRequests!.Single() with
        {
            Adjacency = [new AdjacencyHint(AdjacencyRelation.Near, "kitchen")]
        };
        return new AgentFlag(
            Id: "food:freezer_missing",
            SourceMinister: "Chef",
            Priority: Priority.Medium,
            Domain: "food",
            Summary: "Food storage needs freezer support",
            BuildingRequests: [request]);
    }

    private static AgentFlag DuplicateFreezerFlag()
    {
        BuildingRequest request = FreezerFlag().BuildingRequests!.Single();
        return new AgentFlag(
            Id: "food:duplicate_freezer_missing",
            SourceMinister: "Chef",
            Priority: Priority.Medium,
            Domain: "food",
            Summary: "Food storage needs freezer support",
            BuildingRequests: [request, request]);
    }

    private static AgentFlag WorkshopFlag(string requestedFrom = "Willie") =>
        new(
            Id: "industry:workshop_needed",
            SourceMinister: "Industry",
            Priority: Priority.Medium,
            Domain: "industry",
            Summary: "Production needs a workshop",
            BuildingRequests:
            [
                new BuildingRequest(
                    Request: "starter workshop near storage",
                    Reason: "Industry needs covered production benches",
                    TargetClass: BuildingClass.ProductionBench,
                    TargetDef: "ElectricTailoringBench",
                    RoomClass: RoomClass.Workshop,
                    CapacityNeed: new CapacityNeed(CapacityMeasure.WorkSlots, 1),
                    Adjacency: [new AdjacencyHint(AdjacencyRelation.Near, "storage")],
                    Priority: Priority.Medium,
                    RequestedFrom: requestedFrom)
            ]);

    private static AgentFlag ZoneFlag(string requestedFrom = "Willie") =>
        new(
            Id: "food:expand_growing_capacity",
            SourceMinister: "Chef",
            Priority: Priority.High,
            Domain: "food",
            Summary: "Expand food growing capacity",
            ZoneRequests:
            [
                new ZoneRequest(
                    Request: "36 rice growing tiles near storage",
                    Reason: "rice fits the season and food buffer is low",
                    ZoneClass: ZoneClass.Growing,
                    PlantDef: "Plant_Rice",
                    TileCount: 36,
                    Adjacency: [new AdjacencyHint(AdjacencyRelation.Near, "storage")],
                    Terrain: new TerrainNeed(MustSupportGrowing: true, PreferredFertility: 1.4f),
                    Priority: Priority.High,
                    RequestedFrom: requestedFrom)
            ]);

    private static AdviceOption PlacementOption(
        string id = "placement_freezer_10_12",
        string label = "Compact freezer",
        int x = 10) =>
        new(
            Id: id,
            Label: label,
            Summary: "Validated freezer shell near kitchen.",
            BlueprintGroup: new BlueprintGroup(
                Label: label,
                MapId: 7,
                Assets:
                [
                    new BlueprintAsset("wall", "Wall", "BlocksGranite", new MapCell(x, 12), 0)
                ]),
            EstimatedMaterials: [new MaterialEstimate("BlocksGranite", 5)],
            TradeoffNote: "Closest to kitchen.");

    private static AdviceItem PriorOptionsAdvice()
    {
        AdviceOption option = PlacementOption("placement_freezer_prior", "Prior freezer", 40);
        return new AdviceItem(
            Id: "willie_prior_options",
            Minister: "Willie",
            Priority: Priority.Medium,
            Title: "Prior freezer options",
            Body: "Previously solved freezer placement.",
            Rationale: "Keep this solved placement while live validation is unavailable.",
            Actions:
            [
                new AdviceAction(
                    AdviceActionKind.PlaceBlueprint,
                    "Place the prior freezer blueprint group.",
                    Owner: "Willie",
                    Apply: new PlaceBlueprintGroupApply(
                        Label: option.Label,
                        TargetSummary: option.Summary,
                        MapId: option.BlueprintGroup.MapId,
                        BlueprintGroup: option.BlueprintGroup,
                        AssetCount: option.BlueprintGroup.Assets.Count))
            ],
            GuideCitationIds: [],
            Stamp: new AdviceStamp(FixedNow, FixedNow.AddHours(6)),
            Options: [option]);
    }

    private sealed class Harness
    {
        public ColonyState Colony { get; } = new();
        public AdviceBus Bus { get; }
        public BriefingCache Cache { get; }
        public FlagChannel Flags { get; } = new();
        public MinisterOfWillie Minister { get; }
        public MinisterOutputStore OutputStore { get; } = new();
        public WillieSolverStore SolverStore { get; } = new();
        public MinisterTraceStore Traces { get; } = new();

        public Harness(
            IPlacementSolver? solver = null,
            IGrowZonePlacementSolver? growZoneSolver = null,
            IReplayCorpusWriter? replay = null)
        {
            Bus = new AdviceBus(OutputStore);
            Cache = new BriefingCache(Colony, new TestLogger<BriefingCache>());
            Minister = new(
                Cache,
                new Rules(new FixedTimeProvider(FixedNow)),
                solver ?? FakePlacementSolver.WithNoFit(NoFitReason.NoDrafts),
                growZoneSolver ?? FakeGrowZonePlacementSolver.WithNoFit(NoFitReason.NoTerrainGrid),
                Colony,
                SolverStore,
                OutputStore,
                Bus,
                Flags,
                Traces,
                NullLogger<MinisterOfWillie>.Instance,
                replay is null ? null : new MinisterReplayRecorder(replay, traces: Traces));
        }

        public void SetStableState()
        {
            Colony.LastRefreshSource = ColonyStateOrigin.Live;
            Colony.LastLiveRefreshAt = FixedNow;
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

        public void SetStableStateWithoutKitchen()
        {
            SetStableState();
            Colony.Rooms.Update(new RoomRegistry([
                Room("hospital-room", "Hospital"),
                Room("storage-room", "Storage")
            ]));
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

    private sealed class FakePlacementSolver(PlacementResult? result, Exception? exception = null) : IPlacementSolver
    {
        public int CallCount { get; private set; }
        public PlacementSpec? LastSpec { get; private set; }
        public ColonyState? LastState { get; private set; }
        public List<PlacementSpec> Specs { get; } = [];

        public static FakePlacementSolver WithOptions(params AdviceOption[] options) =>
            WithOptions(PlacementReadiness.Ready, PlacementReadiness.Ready, options);

        public static FakePlacementSolver WithOptions(
            PlacementReadiness materialsReady,
            PlacementReadiness applyReady,
            params AdviceOption[] options) =>
            new(new PlacementResult(
                Options: options,
                Trace: Trace(),
                NoFit: null,
                Draftable: PlacementReadiness.Ready,
                PlacementValid: PlacementReadiness.Ready,
                MaterialsReady: materialsReady,
                ApplyReady: applyReady));

        public static FakePlacementSolver WithNoFit(NoFitReason reason) =>
            new(new PlacementResult(
                Options: [],
                Trace: Trace(),
                NoFit: reason,
                Draftable: PlacementReadiness.Ready,
                PlacementValid: PlacementReadiness.Blocked,
                MaterialsReady: PlacementReadiness.Unknown,
                ApplyReady: PlacementReadiness.Blocked));

        public static FakePlacementSolver Throwing(Exception exception) =>
            new(null, exception);

        public Task<PlacementResult> SolveAsync(
            PlacementSpec spec,
            WillieBriefing briefing,
            ColonyState colonyState,
            CancellationToken ct = default)
        {
            CallCount++;
            LastSpec = spec;
            LastState = colonyState;
            Specs.Add(spec);
            if (exception is not null)
                return Task.FromException<PlacementResult>(exception);

            return Task.FromResult(result ?? throw new InvalidOperationException("Fake solver has no result."));
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

    private sealed class FakeGrowZonePlacementSolver(PlacementResult? result, Exception? exception = null) : IGrowZonePlacementSolver
    {
        public int CallCount { get; private set; }

        public ZoneRequest? LastRequest { get; private set; }

        public static FakeGrowZonePlacementSolver WithOptions(params AdviceOption[] options) =>
            new(new PlacementResult(
                Options: options,
                Trace: Trace(),
                NoFit: null,
                Draftable: PlacementReadiness.Ready,
                PlacementValid: PlacementReadiness.Ready,
                MaterialsReady: PlacementReadiness.Ready,
                ApplyReady: PlacementReadiness.Blocked));

        public static FakeGrowZonePlacementSolver WithNoFit(NoFitReason reason) =>
            new(new PlacementResult(
                Options: [],
                Trace: Trace(),
                NoFit: reason,
                Draftable: PlacementReadiness.Ready,
                PlacementValid: PlacementReadiness.Blocked,
                MaterialsReady: PlacementReadiness.Ready,
                ApplyReady: PlacementReadiness.Blocked));

        public static FakeGrowZonePlacementSolver Throwing(Exception exception) =>
            new(null, exception);

        public Task<PlacementResult> SolveAsync(
            ZoneRequest request,
            WillieBriefing briefing,
            ColonyState colonyState,
            CancellationToken ct = default)
        {
            CallCount++;
            LastRequest = request;
            if (exception is not null)
                return Task.FromException<PlacementResult>(exception);

            return Task.FromResult(result ?? throw new InvalidOperationException("Fake grow-zone solver has no result."));
        }

        private static PlacementTrace Trace() =>
            new(
                "grow_zone_placement_solver",
                [
                    new PlacementDraftTrace(
                        GeneratorId: "grow_zone_rect",
                        AnchorRoomId: "stockpile:1",
                        Status: "selected",
                        Reason: null,
                        Metrics: [])
                ],
                ["zone_test_note"]);
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
