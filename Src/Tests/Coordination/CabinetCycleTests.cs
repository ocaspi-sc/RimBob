using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RimBob.Core.Advice;
using RimBob.Coordination;
using RimBob.Core.Aggregates;
using RimBob.Core.Ministers;
using RimBob.State;

namespace RimBob.Tests.Coordination;

public sealed class CabinetCycleTests
{
    [Fact]
    public async Task TriggerMinisterAsync_WhenLiveRefreshFailsWithRestoredSnapshot_RunsMinister()
    {
        (ColonyState colony, ColonyStateSnapshotStore snapshotStore) =
            await RestoredStateWithSnapshotAsync();
        FakeMinister willie = new("Willie");
        MinisterTraceStore traces = new();
        CabinetCycle sut = BuildCycle(
            new ThrowingRefresher(ConnectionRefused()),
            colony,
            snapshotStore,
            traces,
            [willie]);

        MinisterTriggerResult result = await sut.TriggerMinisterAsync("willie", CancellationToken.None)
            ?? throw new InvalidOperationException("Expected Willie trigger result.");

        result.UsedRestoredSnapshot.Should().BeTrue();
        result.StateSource.Should().Be(nameof(ColonyStateOrigin.Snapshot));
        willie.WakeCount.Should().Be(1);
        willie.Triggers.Should().Equal(PlayCycleTrigger.ManualTrigger);
        traces.Latest("Willie")!.Status.Should().Be("completed");
        traces.Latest("Willie")!.Note.Should().Contain("restored colony snapshot");
    }

    [Fact]
    public async Task TriggerCabinetAsync_RecordsOrderedSuccessfulRunSteps()
    {
        MinisterTraceStore traces = new();
        CabinetRunLogStore runLogs = new();
        FakeMinister mayor = new("Mayor", onRun: context =>
            traces.RecordPath("Mayor", context, "llm", null, null, "daily agenda refresh", null, 1, 0));
        FakeMinister chef = new("Chef", onRun: context =>
            traces.RecordPath("Chef", context, "rules", "food_buffer_low", null, null, null, 2, 1));
        FakeMinister welfare = new("Welfare", onRun: context =>
            traces.RecordPath("Welfare", context, "rules", "shelter_floor", null, null, null, 1, 1));
        FakeMinister willie = new("Willie", onRun: context =>
            traces.RecordPath("Willie", context, "rules", "freezer_request_active", null, null, null, 1, 0));
        CabinetCycle sut = BuildCycle(
            new NoopRefresher(),
            new ColonyState(),
            new ColonyStateSnapshotStore(),
            traces,
            [mayor, chef, welfare, willie],
            runLogs);

        CabinetTriggerResult result = await sut.TriggerCabinetAsync(CancellationToken.None, "run-success");

        result.RunLog.RunId.Should().Be("run-success");
        result.RunLog.Status.Should().Be("completed");
        result.RunLog.Steps.Select(step => step.Key).Should().Equal(
            "request_accepted",
            "live_state_refresh",
            "minister_food",
            "minister_welfare",
            "minister_willie",
            "minister_mayor",
            "cabinet_complete");
        result.RunLog.Steps.Single(step => step.Key == "minister_food").RuleFired.Should().Be("food_buffer_low");
        result.RunLog.Steps.Single(step => step.Key == "minister_welfare").RuleFired.Should().Be("shelter_floor");
        result.RunLog.Steps.Single(step => step.Key == "minister_food").AdviceCount.Should().Be(2);
        result.RunLog.Steps.Single(step => step.Key == "minister_food").FlagCount.Should().Be(1);
    }

    [Fact]
    public async Task TriggerMinisterAsync_WhenLiveRefreshFailsWithoutRestoredSnapshot_DoesNotRunMinister()
    {
        ColonyState colony = new();
        FakeMinister willie = new("Willie");
        CabinetCycle sut = BuildCycle(
            new ThrowingRefresher(ConnectionRefused()),
            colony,
            new ColonyStateSnapshotStore(),
            new MinisterTraceStore(),
            [willie]);

        Func<Task> act = () => sut.TriggerMinisterAsync("willie", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        willie.WakeCount.Should().Be(0);
    }

    [Fact]
    public async Task TriggerMinisterAsync_WhenRefreshFailsForNonConnectionError_DoesNotUseSnapshotFallback()
    {
        (ColonyState colony, ColonyStateSnapshotStore snapshotStore) =
            await RestoredStateWithSnapshotAsync();
        FakeMinister willie = new("Willie");
        CabinetCycle sut = BuildCycle(
            new ThrowingRefresher(new InvalidOperationException("schema drift")),
            colony,
            snapshotStore,
            new MinisterTraceStore(),
            [willie]);

        Func<Task> act = () => sut.TriggerMinisterAsync("willie", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("schema drift");
        willie.WakeCount.Should().Be(0);
    }

    [Fact]
    public async Task TriggerMinisterAsync_WhenRefreshFailsForHttpStatus_DoesNotUseSnapshotFallback()
    {
        (ColonyState colony, ColonyStateSnapshotStore snapshotStore) =
            await RestoredStateWithSnapshotAsync();
        FakeMinister willie = new("Willie");
        CabinetCycle sut = BuildCycle(
            new ThrowingRefresher(new HttpRequestException("server error", null, HttpStatusCode.InternalServerError)),
            colony,
            snapshotStore,
            new MinisterTraceStore(),
            [willie]);

        Func<Task> act = () => sut.TriggerMinisterAsync("willie", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>()
            .Where(ex => ex.StatusCode == HttpStatusCode.InternalServerError);
        willie.WakeCount.Should().Be(0);
    }

    [Fact]
    public async Task TriggerMinisterAsync_WhenConnectionFailsAfterPartialRefresh_RestoresSnapshotBeforeMinisterRuns()
    {
        (ColonyState colony, ColonyStateSnapshotStore snapshotStore) =
            await RestoredStateWithSnapshotAsync(7);
        colony.LastRefreshSource = ColonyStateOrigin.Live;
        colony.LastLiveRefreshAt = DateTimeOffset.UtcNow;
        FakeMinister willie = new("Willie", colony);
        CabinetCycle sut = BuildCycle(
            new MutatingThrowingRefresher(colony, ConnectionRefused(), 99),
            colony,
            snapshotStore,
            new MinisterTraceStore(),
            [willie]);

        MinisterTriggerResult result = await sut.TriggerMinisterAsync("willie", CancellationToken.None)
            ?? throw new InvalidOperationException("Expected Willie trigger result.");

        result.UsedRestoredSnapshot.Should().BeTrue();
        result.StateSource.Should().Be(nameof(ColonyStateOrigin.Snapshot));
        willie.ObservedMapId.Should().Be(7);
        colony.Map.Value.Id.Should().Be(7);
    }

    [Fact]
    public async Task TriggerMinisterAsync_WhenRimApiHasNoLoadedMap_RestoresSnapshotBeforeMinisterRuns()
    {
        (ColonyState colony, ColonyStateSnapshotStore snapshotStore) =
            await RestoredStateWithSnapshotAsync(7);
        colony.LastRefreshSource = ColonyStateOrigin.Live;
        colony.LastLiveRefreshAt = DateTimeOffset.UtcNow;
        FakeMinister willie = new("Willie", colony);
        CabinetCycle sut = BuildCycle(
            new MutatingThrowingRefresher(
                colony,
                new RimApiLiveStateUnavailableException(
                    RimApiLiveStateUnavailableReason.NoLoadedMap,
                    "RIMAPI returned no maps. Load a colony map before refreshing live state."),
                99),
            colony,
            snapshotStore,
            new MinisterTraceStore(),
            [willie]);

        MinisterTriggerResult result = await sut.TriggerMinisterAsync("willie", CancellationToken.None)
            ?? throw new InvalidOperationException("Expected Willie trigger result.");

        result.UsedRestoredSnapshot.Should().BeTrue();
        result.StateSource.Should().Be(nameof(ColonyStateOrigin.Snapshot));
        willie.ObservedMapId.Should().Be(7);
        colony.Map.Value.Id.Should().Be(7);
    }

    [Fact]
    public async Task TriggerCabinetAsync_WhenLiveRefreshFailsWithRestoredSnapshot_ReportsFallback()
    {
        (ColonyState colony, ColonyStateSnapshotStore snapshotStore) =
            await RestoredStateWithSnapshotAsync();
        FakeMinister mayor = new("Mayor");
        FakeMinister chef = new("Chef");
        FakeMinister welfare = new("Welfare");
        FakeMinister willie = new("Willie");
        CabinetRunLogStore runLogs = new();
        CabinetCycle sut = BuildCycle(
            new ThrowingRefresher(ConnectionRefused()),
            colony,
            snapshotStore,
            new MinisterTraceStore(),
            [mayor, chef, welfare, willie],
            runLogs);

        CabinetTriggerResult result = await sut.TriggerCabinetAsync(CancellationToken.None, "run-fallback");

        result.UsedRestoredSnapshot.Should().BeTrue();
        result.StateSource.Should().Be(nameof(ColonyStateOrigin.Snapshot));
        result.RunLog.Steps.Single(step => step.Key == "live_state_refresh").Status.Should().Be("failed");
        result.RunLog.Steps.Single(step => step.Key == "restored_snapshot_fallback").Status.Should().Be("completed");
        mayor.WakeCount.Should().Be(1);
        chef.WakeCount.Should().Be(1);
        welfare.WakeCount.Should().Be(1);
        willie.WakeCount.Should().Be(1);
    }

    [Fact]
    public async Task TriggerMinisterAsync_WhenSourcePublishesWillieBuildRequest_RunsWillieFollowUp()
    {
        FlagChannel flags = new();
        AgentFlag freezerRequest = WillieBuildingRequestFlag();
        flags.Publish(freezerRequest);
        FakeMinister chef = new("Chef", flags: flags, emittedFlags: [freezerRequest]);
        FakeMinister willie = new("Willie");
        CabinetCycle sut = BuildCycle(
            new NoopRefresher(),
            new ColonyState(),
            new ColonyStateSnapshotStore(),
            new MinisterTraceStore(),
            [chef, willie],
            flags: flags);

        MinisterTriggerResult result = await sut.TriggerMinisterAsync("food", MinisterRunMode.RulesOnly, CancellationToken.None)
            ?? throw new InvalidOperationException("Expected Chef trigger result.");

        result.Scope.Should().Be("food");
        chef.WakeCount.Should().Be(1);
        willie.WakeCount.Should().Be(1);
        willie.Triggers.Should().Equal(PlayCycleTrigger.FlagFired);
        willie.RunModes.Should().Equal(MinisterRunMode.RulesOnly);
        willie.WakeupPayloads.Should().Equal("building_request:food:freezer");
        willie.FlagIds.Should().Equal("food:freezer");
    }

    [Fact]
    public async Task TriggerMinisterAsync_WhenWelfarePublishesWillieBuildRequest_RunsWillieFollowUp()
    {
        FlagChannel flags = new();
        AgentFlag shelterRequest = WelfareBuildingRequestFlag();
        flags.Publish(shelterRequest);
        FakeMinister welfare = new("Welfare", flags: flags, emittedFlags: [shelterRequest]);
        FakeMinister willie = new("Willie");
        CabinetCycle sut = BuildCycle(
            new NoopRefresher(),
            new ColonyState(),
            new ColonyStateSnapshotStore(),
            new MinisterTraceStore(),
            [welfare, willie],
            flags: flags);

        MinisterTriggerResult result = await sut.TriggerMinisterAsync("welfare", MinisterRunMode.RulesOnly, CancellationToken.None)
            ?? throw new InvalidOperationException("Expected Welfare trigger result.");

        result.Scope.Should().Be("welfare");
        welfare.WakeCount.Should().Be(1);
        willie.WakeCount.Should().Be(1);
        willie.Triggers.Should().Equal(PlayCycleTrigger.FlagFired);
        willie.RunModes.Should().Equal(MinisterRunMode.RulesOnly);
        willie.WakeupPayloads.Should().Equal("building_request:welfare:shelter_floor");
        willie.FlagIds.Should().Equal("welfare:shelter_floor");
    }

    [Fact]
    public async Task TriggerCabinetAsync_WhenFoodPublishesWillieBuildRequest_DoesNotRunWillieTwice()
    {
        FlagChannel flags = new();
        AgentFlag freezerRequest = WillieBuildingRequestFlag();
        FakeMinister chef = new("Chef", flags: flags, emittedFlags: [freezerRequest]);
        FakeMinister welfare = new("Welfare");
        FakeMinister willie = new("Willie");
        FakeMinister mayor = new("Mayor");
        CabinetCycle sut = BuildCycle(
            new NoopRefresher(),
            new ColonyState(),
            new ColonyStateSnapshotStore(),
            new MinisterTraceStore(),
            [chef, welfare, willie, mayor],
            flags: flags);

        CabinetTriggerResult result = await sut.TriggerCabinetAsync(CancellationToken.None);

        result.StateSource.Should().Be(nameof(ColonyStateOrigin.None));
        chef.WakeCount.Should().Be(1);
        welfare.WakeCount.Should().Be(1);
        willie.WakeCount.Should().Be(1);
        mayor.WakeCount.Should().Be(1);
        willie.Triggers.Should().Equal(PlayCycleTrigger.FlagFired);
        willie.FlagIds.Should().Equal("food:freezer");
    }

    [Fact]
    public async Task TriggerCabinetAsync_WhenMinisterThrows_MarksMinisterAndRunFailed()
    {
        CabinetRunLogStore runLogs = new();
        FakeMinister chef = new("Chef", exception: new InvalidOperationException("kitchen exploded"));
        CabinetCycle sut = BuildCycle(
            new NoopRefresher(),
            new ColonyState(),
            new ColonyStateSnapshotStore(),
            new MinisterTraceStore(),
            [chef],
            runLogs);

        Func<Task> act = () => sut.TriggerCabinetAsync(CancellationToken.None, "run-failed");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("kitchen exploded");
        CabinetRunLogSnapshot failedRun = runLogs.Latest("run-failed")
            ?? throw new InvalidOperationException("Expected failed run log.");
        failedRun.Status.Should().Be("failed");
        failedRun.ErrorType.Should().Be(nameof(InvalidOperationException));
        failedRun.Steps.Single(step => step.Key == "minister_food").Status.Should().Be("failed");
        failedRun.Steps.Single(step => step.Key == "cabinet_failed").ErrorMessage.Should().Be("kitchen exploded");
    }

    [Fact]
    public async Task TriggerMinisterAsync_WithRulesOnly_UsesRulesOnlyContext()
    {
        FakeMinister willie = new("Willie");
        CabinetCycle sut = BuildCycle(
            new NoopRefresher(),
            new ColonyState(),
            new ColonyStateSnapshotStore(),
            new MinisterTraceStore(),
            [willie]);

        MinisterTriggerResult result = await sut.TriggerMinisterAsync("willie", MinisterRunMode.RulesOnly, CancellationToken.None)
            ?? throw new InvalidOperationException("Expected Willie trigger result.");

        result.RunMode.Should().Be(nameof(MinisterRunMode.RulesOnly));
        willie.RunModes.Should().Equal(MinisterRunMode.RulesOnly);
        willie.WakeupPayloads.Should().Equal("dashboard:rules");
    }

    [Fact]
    public async Task TriggerMinisterAsync_WithForceLlm_UsesForceLlmContext()
    {
        FakeMinister chef = new("Chef");
        CabinetCycle sut = BuildCycle(
            new NoopRefresher(),
            new ColonyState(),
            new ColonyStateSnapshotStore(),
            new MinisterTraceStore(),
            [chef]);

        MinisterTriggerResult result = await sut.TriggerMinisterAsync("food", MinisterRunMode.ForceLlm, CancellationToken.None)
            ?? throw new InvalidOperationException("Expected Chef trigger result.");

        result.RunMode.Should().Be(nameof(MinisterRunMode.ForceLlm));
        chef.RunModes.Should().Equal(MinisterRunMode.ForceLlm);
        chef.WakeupPayloads.Should().Equal("dashboard:llm");
    }

    [Fact]
    public async Task TriggerMinisterAsync_WhenModeNotSupported_ReturnsNull()
    {
        FakeMinister willie = new("Willie");
        CabinetCycle sut = BuildCycle(
            new NoopRefresher(),
            new ColonyState(),
            new ColonyStateSnapshotStore(),
            new MinisterTraceStore(),
            [willie]);

        MinisterTriggerResult? result = await sut.TriggerMinisterAsync("willie", MinisterRunMode.ForceLlm, CancellationToken.None);

        result.Should().BeNull();
        willie.WakeCount.Should().Be(0);
    }

    private static CabinetCycle BuildCycle(
        IColonyStateRefresher refresher,
        ColonyState colony,
        ColonyStateSnapshotStore snapshotStore,
        MinisterTraceStore traces,
        IReadOnlyList<IMinister> ministers,
        CabinetRunLogStore? runLogs = null,
        FlagChannel? flags = null) =>
        new(
            refresher,
            colony,
            snapshotStore,
            ministers,
            new MinisterRegistry(),
            traces,
            flags ?? new FlagChannel(),
            runLogs ?? new CabinetRunLogStore(),
            NullLogger<CabinetCycle>.Instance);

    private static AgentFlag WillieBuildingRequestFlag() =>
        new(
            Id: "food:freezer",
            SourceMinister: "Chef",
            Severity: FlagSeverity.High,
            Domain: "food",
            Summary: "Freezer needed",
            BuildingRequests:
            [
                new BuildingRequest(
                    Request: "starter freezer near kitchen",
                    Reason: "Food will spoil without cold storage.",
                    TargetClass: BuildingClass.Freezer,
                    RoomClass: RoomClass.Freezer,
                    Priority: AdvicePriority.High,
                    RequestedFrom: "Willie")
            ]);

    private static AgentFlag WelfareBuildingRequestFlag() =>
        new(
            Id: "welfare:shelter_floor",
            SourceMinister: "Welfare",
            Severity: FlagSeverity.High,
            Domain: "welfare",
            Summary: "Starter barracks needed",
            BuildingRequests:
            [
                new BuildingRequest(
                    Request: "basic barracks with 3 beds",
                    Reason: "colony has no bedroom/beds; colonists will sleep unsheltered",
                    TargetClass: BuildingClass.Bed,
                    TargetDef: "Bed",
                    RoomClass: RoomClass.Barracks,
                    CapacityNeed: new CapacityNeed(CapacityMeasure.Beds, 3),
                    Priority: AdvicePriority.High,
                    RequestedFrom: "Willie")
            ]);

    private static async Task<(ColonyState Colony, ColonyStateSnapshotStore SnapshotStore)>
        RestoredStateWithSnapshotAsync(int mapId = 0)
    {
        ColonyState colony = new()
        {
            LastRefreshSource = ColonyStateOrigin.Live,
            LastLiveRefreshAt = DateTimeOffset.UtcNow
        };
        colony.Map.Update(new MapInfoSnapshot(mapId, "snapshot"));

        ColonyStateSnapshotStore snapshotStore = new();
        await snapshotStore.SaveAsync(colony);
        snapshotStore.RestoreInto(colony);

        return (colony, snapshotStore);
    }

    private static HttpRequestException ConnectionRefused() =>
        new("connection refused", new SocketException((int)SocketError.ConnectionRefused));

    private sealed class ThrowingRefresher(Exception exception) : IColonyStateRefresher
    {
        public Task RefreshAllAsync(CancellationToken ct = default) =>
            Task.FromException(exception);
    }

    private sealed class NoopRefresher : IColonyStateRefresher
    {
        public Task RefreshAllAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class MutatingThrowingRefresher(
        ColonyState colony,
        Exception exception,
        int partialMapId) : IColonyStateRefresher
    {
        public Task RefreshAllAsync(CancellationToken ct = default)
        {
            colony.Map.Update(new MapInfoSnapshot(partialMapId, "partial-refresh"));
            return Task.FromException(exception);
        }
    }

    private sealed class FakeMinister(
        string name,
        ColonyState? colony = null,
        Action<PlayCycleContext>? onRun = null,
        Exception? exception = null,
        FlagChannel? flags = null,
        IReadOnlyList<AgentFlag>? emittedFlags = null) : IMinister
    {
        public string Name { get; } = name;

        public int WakeCount { get; private set; }

        public int? ObservedMapId { get; private set; }

        public List<PlayCycleTrigger> Triggers { get; } = [];

        public List<MinisterRunMode> RunModes { get; } = [];

        public List<string?> WakeupPayloads { get; } = [];

        public List<string?> FlagIds { get; } = [];

        public Task RunPlayCycle(PlayCycleContext context, CancellationToken ct)
        {
            WakeCount++;
            ObservedMapId = colony?.Map.Value.Id;
            Triggers.Add(context.Trigger);
            RunModes.Add(context.RunMode);
            WakeupPayloads.Add(context.WakeupPayload);
            FlagIds.Add(context.Flag?.Id);
            onRun?.Invoke(context);
            if (flags is not null && emittedFlags is not null)
            {
                foreach (AgentFlag flag in emittedFlags)
                    flags.Publish(flag);
            }

            if (exception is not null)
                return Task.FromException(exception);
            return Task.CompletedTask;
        }

        public Task RunRefinement(CancellationToken ct) => Task.CompletedTask;
    }
}
