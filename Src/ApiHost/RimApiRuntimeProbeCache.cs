using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace RimBob.Host;

public sealed class RimApiRuntimeProbeCache
{
    private static readonly TimeSpan ReachableCacheDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OfflineCacheDuration = TimeSpan.FromSeconds(5);

    private readonly object gate = new();
    private readonly Func<CancellationToken, Task<RimApiRuntimeSnapshot>> probe;
    private readonly TimeProvider timeProvider;
    private readonly CancellationToken shutdownToken;

    private CachedProbe? latest;
    private Task<RimApiRuntimeSnapshot>? inFlight;

    public RimApiRuntimeProbeCache(
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime)
        : this(BuildScopedProbe(scopeFactory), TimeProvider.System, lifetime.ApplicationStopping)
    {
    }

    public RimApiRuntimeProbeCache(
        Func<CancellationToken, Task<RimApiRuntimeSnapshot>> probe,
        TimeProvider timeProvider,
        CancellationToken shutdownToken = default)
    {
        this.probe = probe;
        this.timeProvider = timeProvider;
        this.shutdownToken = shutdownToken;
    }

    /// <summary>
    /// Ignores the caller token while awaiting the shared probe so one aborted browser request
    /// cannot cancel RIMAPI reachability metadata for other status/health callers.
    /// </summary>
    public async Task<RimApiRuntimeSnapshot> GetAsync(CancellationToken ct = default)
    {
        Task<RimApiRuntimeSnapshot> task;
        DateTimeOffset now = timeProvider.GetUtcNow();

        lock (gate)
        {
            if (latest is not null && IsFresh(latest.Value, now))
            {
                return latest.Value.Snapshot;
            }

            if (inFlight is not null)
            {
                task = inFlight;
            }
            else
            {
                TaskCompletionSource<RimApiRuntimeSnapshot> completion =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);
                task = completion.Task;
                inFlight = task;
                _ = ProbeAndStoreAsync(completion, task);
            }
        }

        return await task.ConfigureAwait(false);
    }

    private static Func<CancellationToken, Task<RimApiRuntimeSnapshot>> BuildScopedProbe(
        IServiceScopeFactory scopeFactory) =>
        async cancellationToken =>
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            RimApiRuntimeProbe runtimeProbe = scope.ServiceProvider.GetRequiredService<RimApiRuntimeProbe>();
            return await runtimeProbe.ProbeAsync(cancellationToken).ConfigureAwait(false);
        };

    private async Task ProbeAndStoreAsync(
        TaskCompletionSource<RimApiRuntimeSnapshot> completion,
        Task<RimApiRuntimeSnapshot> task)
    {
        try
        {
            RimApiRuntimeSnapshot snapshot = await probe(shutdownToken).ConfigureAwait(false);
            DateTimeOffset fetchedAt = timeProvider.GetUtcNow();

            lock (gate)
            {
                latest = new CachedProbe(snapshot, fetchedAt);
            }

            completion.TrySetResult(snapshot);
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(shutdownToken);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(inFlight, task))
                {
                    inFlight = null;
                }
            }
        }
    }

    private static bool IsFresh(CachedProbe cached, DateTimeOffset now) =>
        now < cached.FetchedAt + DurationFor(cached.Snapshot);

    private static TimeSpan DurationFor(RimApiRuntimeSnapshot snapshot) =>
        snapshot.Reachable ? ReachableCacheDuration : OfflineCacheDuration;

    private readonly record struct CachedProbe(
        RimApiRuntimeSnapshot Snapshot,
        DateTimeOffset FetchedAt);
}
