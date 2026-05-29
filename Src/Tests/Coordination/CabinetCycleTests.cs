using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task TriggerCabinetAsync_WhenLiveRefreshFailsWithRestoredSnapshot_ReportsFallback()
    {
        (ColonyState colony, ColonyStateSnapshotStore snapshotStore) =
            await RestoredStateWithSnapshotAsync();
        FakeMinister mayor = new("Mayor");
        FakeMinister chef = new("Chef");
        FakeMinister willie = new("Willie");
        CabinetCycle sut = BuildCycle(
            new ThrowingRefresher(ConnectionRefused()),
            colony,
            snapshotStore,
            new MinisterTraceStore(),
            [mayor, chef, willie]);

        CabinetTriggerResult result = await sut.TriggerCabinetAsync(CancellationToken.None);

        result.UsedRestoredSnapshot.Should().BeTrue();
        result.StateSource.Should().Be(nameof(ColonyStateOrigin.Snapshot));
        mayor.WakeCount.Should().Be(1);
        chef.WakeCount.Should().Be(1);
        willie.WakeCount.Should().Be(1);
    }

    private static CabinetCycle BuildCycle(
        IColonyStateRefresher refresher,
        ColonyState colony,
        ColonyStateSnapshotStore snapshotStore,
        MinisterTraceStore traces,
        IReadOnlyList<IMinister> ministers) =>
        new(
            refresher,
            colony,
            snapshotStore,
            ministers,
            new MinisterRegistry(),
            traces,
            NullLogger<CabinetCycle>.Instance);

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

    private sealed class FakeMinister(string name, ColonyState? colony = null) : IMinister
    {
        public string Name { get; } = name;

        public int WakeCount { get; private set; }

        public int? ObservedMapId { get; private set; }

        public List<PlayCycleTrigger> Triggers { get; } = [];

        public Task RunPlayCycle(PlayCycleContext context, CancellationToken ct)
        {
            WakeCount++;
            ObservedMapId = colony?.Map.Value.Id;
            Triggers.Add(context.Trigger);
            return Task.CompletedTask;
        }

        public Task RunRefinement(CancellationToken ct) => Task.CompletedTask;
    }
}
