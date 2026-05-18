using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RimBob.State;

namespace RimBob.Host;

public sealed class ColonySnapshotRestoreHostedService(
    ColonyStateSnapshotStore store,
    ColonyState colony,
    ILogger<ColonySnapshotRestoreHostedService> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ColonyStateSnapshot? snapshot = store.Latest;
        if (snapshot is null)
        {
            log.LogInformation("Colony state snapshot restore skipped; no snapshot is available.");
            return Task.CompletedTask;
        }

        if (colony.LastRefreshSource != ColonyStateOrigin.None)
        {
            log.LogInformation(
                "Colony state snapshot restore skipped; colony state already has source {Source}.",
                colony.LastRefreshSource);
            return Task.CompletedTask;
        }

        store.RestoreInto(colony);
        log.LogWarning(
            "Restored colony state snapshot {SnapshotId} captured_at={CapturedAt}; marked stale until first live refresh.",
            snapshot.SnapshotId,
            snapshot.CapturedAt);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
