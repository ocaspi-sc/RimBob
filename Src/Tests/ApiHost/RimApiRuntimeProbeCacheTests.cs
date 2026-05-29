using FluentAssertions;
using RimBob.Host;

namespace RimBob.Tests.ApiHost;

public sealed class RimApiRuntimeProbeCacheTests
{
    [Fact]
    public async Task GetAsync_WhenReachableSnapshotIsFresh_ReusesCachedProbe()
    {
        ManualTimeProvider time = new(new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.Zero));
        int probeCount = 0;
        RimApiRuntimeProbeCache cache = new(
            _ =>
            {
                probeCount += 1;
                return Task.FromResult(ReachableSnapshot(probeCount));
            },
            time);

        RimApiRuntimeSnapshot first = await cache.GetAsync();
        time.Advance(TimeSpan.FromSeconds(1));
        RimApiRuntimeSnapshot second = await cache.GetAsync();

        probeCount.Should().Be(1);
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task GetAsync_WhenOfflineSnapshotIsFresh_ReusesLongerOfflineCache()
    {
        ManualTimeProvider time = new(new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.Zero));
        int probeCount = 0;
        RimApiRuntimeProbeCache cache = new(
            _ =>
            {
                probeCount += 1;
                return Task.FromResult(RimApiRuntimeSnapshot.Offline($"offline-{probeCount}"));
            },
            time);

        RimApiRuntimeSnapshot first = await cache.GetAsync();
        time.Advance(TimeSpan.FromSeconds(4));
        RimApiRuntimeSnapshot second = await cache.GetAsync();

        probeCount.Should().Be(1);
        second.Should().BeSameAs(first);
        second.LastError.Should().Be("offline-1");
    }

    [Fact]
    public async Task GetAsync_WhenTtlExpires_RefreshesProbe()
    {
        ManualTimeProvider time = new(new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.Zero));
        int probeCount = 0;
        RimApiRuntimeProbeCache cache = new(
            _ =>
            {
                probeCount += 1;
                return Task.FromResult(ReachableSnapshot(probeCount));
            },
            time);

        RimApiRuntimeSnapshot first = await cache.GetAsync();
        time.Advance(TimeSpan.FromSeconds(3));
        RimApiRuntimeSnapshot second = await cache.GetAsync();

        probeCount.Should().Be(2);
        second.Should().NotBeSameAs(first);
        second.GameTick.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_WhenCallersOverlap_CoalescesToOneProbe()
    {
        ManualTimeProvider time = new(new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.Zero));
        TaskCompletionSource<RimApiRuntimeSnapshot> probeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int probeCount = 0;
        RimApiRuntimeProbeCache cache = new(
            _ =>
            {
                probeCount += 1;
                return probeCompletion.Task;
            },
            time);

        Task<RimApiRuntimeSnapshot>[] callers =
        [
            cache.GetAsync(),
            cache.GetAsync(),
            cache.GetAsync(),
            cache.GetAsync()
        ];

        probeCount.Should().Be(1);
        probeCompletion.SetResult(ReachableSnapshot(11));
        RimApiRuntimeSnapshot[] snapshots = await Task.WhenAll(callers);

        probeCount.Should().Be(1);
        snapshots.Should().OnlyContain(snapshot => ReferenceEquals(snapshot, snapshots[0]));
    }

    [Fact]
    public async Task GetAsync_WhenCallerTokenIsCanceled_DoesNotCancelSharedProbe()
    {
        ManualTimeProvider time = new(new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.Zero));
        TaskCompletionSource<RimApiRuntimeSnapshot> probeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int probeCount = 0;
        RimApiRuntimeProbeCache cache = new(
            _ =>
            {
                probeCount += 1;
                return probeCompletion.Task;
            },
            time);
        using CancellationTokenSource cancelledCaller = new();
        cancelledCaller.Cancel();

        Task<RimApiRuntimeSnapshot> canceledCallerTask = cache.GetAsync(cancelledCaller.Token);
        Task<RimApiRuntimeSnapshot> otherCallerTask = cache.GetAsync();

        probeCount.Should().Be(1);
        probeCompletion.SetResult(ReachableSnapshot(42));

        RimApiRuntimeSnapshot[] snapshots = await Task.WhenAll(canceledCallerTask, otherCallerTask);
        snapshots[0].Should().BeSameAs(snapshots[1]);
        snapshots[0].GameTick.Should().Be(42);
    }

    private static RimApiRuntimeSnapshot ReachableSnapshot(long gameTick) =>
        new(
            Reachable: true,
            LastError: null,
            GameTick: gameTick,
            ColonyWealth: 16000,
            ColonistCount: 3,
            Storyteller: "Cassandra",
            Paused: false,
            ProgramState: "Playing",
            MapCount: 1);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset current;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            current = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan offset)
        {
            current += offset;
        }
    }
}
