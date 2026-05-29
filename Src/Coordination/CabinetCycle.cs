using Microsoft.Extensions.Logging;
using RimBob.Core.Ministers;
using RimBob.State;

namespace RimBob.Coordination;

public sealed class CabinetCycle(
    IColonyStateRefresher ingestion,
    ColonyState colony,
    ColonyStateSnapshotStore snapshotStore,
    IEnumerable<IMinister> ministers,
    MinisterRegistry registry,
    MinisterTraceStore traces,
    ILogger<CabinetCycle> log)
{
    public async Task RunAsync(CancellationToken ct) =>
        await RunCycleAsync(PlayCycleContext.ManualTrigger, ct);

    public async Task RunAsync(PlayCycleContext cycle, CancellationToken ct) =>
        await RunCycleAsync(cycle, ct);

    public async Task<CabinetTriggerResult> TriggerCabinetAsync(CancellationToken ct)
    {
        bool usedRestoredSnapshot = await RunCycleAsync(PlayCycleContext.ManualTrigger, ct);
        return new CabinetTriggerResult(
            "cabinet",
            PlayCycleContext.ManualTrigger.Trigger.ToString(),
            colony.LastRefreshSource.ToString(),
            usedRestoredSnapshot);
    }

    private async Task<bool> RunCycleAsync(PlayCycleContext cycle, CancellationToken ct)
    {
        bool usedRestoredSnapshot = await RefreshStateForReadOnlyEvaluationAsync(cycle, "cabinet", ct);

        foreach (MinisterDescriptor descriptor in registry.CabinetMinisters)
        {
            IMinister? minister = ResolveMinister(descriptor);
            if (minister is null)
                throw new InvalidOperationException($"Cabinet cycle could not resolve {descriptor.Label} minister.");

            log.LogInformation("Cabinet cycle: running {Minister}", descriptor.Label);
            await RunResolvedMinisterAsync(minister, cycle, usedRestoredSnapshot, ct);
        }

        return usedRestoredSnapshot;
    }

    public async Task<MinisterTriggerResult?> TriggerMinisterAsync(string ministerKey, CancellationToken ct)
    {
        MinisterDescriptor? descriptor = registry.FindMinister(ministerKey);
        if (descriptor is not { Ready: true, CanManualTrigger: true }) return null;

        bool usedRestoredSnapshot = await RefreshStateForReadOnlyEvaluationAsync(
            PlayCycleContext.ManualTrigger,
            descriptor.Label,
            ct);

        IMinister? minister = ResolveMinister(descriptor);
        if (minister is null)
            throw new InvalidOperationException($"Manual trigger could not resolve {descriptor.Label} minister.");

        await RunResolvedMinisterAsync(minister, PlayCycleContext.ManualTrigger, usedRestoredSnapshot, ct);
        return new MinisterTriggerResult(
            descriptor.Key,
            descriptor.Label,
            PlayCycleContext.ManualTrigger.Trigger.ToString(),
            colony.LastRefreshSource.ToString(),
            usedRestoredSnapshot);
    }

    private async Task<bool> RefreshStateForReadOnlyEvaluationAsync(
        PlayCycleContext cycle,
        string scope,
        CancellationToken ct)
    {
        try
        {
            await ingestion.RefreshAllAsync(ct);
            return false;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            if (!TryRestoreSnapshotForManualFallback(cycle, ex))
                throw;

            log.LogWarning(
                ex,
                "Live state refresh failed for manual {Scope} trigger; restored colony snapshot state for read-only evaluation.",
                scope);
            return true;
        }
    }

    private bool TryRestoreSnapshotForManualFallback(PlayCycleContext cycle, Exception ex)
    {
        if (cycle.Trigger != PlayCycleTrigger.ManualTrigger) return false;
        if (snapshotStore.Latest is null) return false;
        if (!CanUseSnapshotFallback(ex)) return false;

        snapshotStore.RestoreInto(colony);
        return true;
    }

    private static bool CanUseSnapshotFallback(Exception ex) =>
        RimApiConnectionFailure.IsConnectionFailure(ex) ||
        ex is RimApiLiveStateUnavailableException;

    private async Task RunResolvedMinisterAsync(
        IMinister minister,
        PlayCycleContext cycle,
        bool usedRestoredSnapshot,
        CancellationToken ct)
    {
        traces.Begin(minister.Name, cycle);
        try
        {
            await minister.RunPlayCycle(cycle, ct);
            traces.Complete(
                minister.Name,
                usedRestoredSnapshot
                    ? "Live refresh failed; evaluated against restored colony snapshot state."
                    : null);
        }
        catch (Exception ex)
        {
            traces.Fail(minister.Name, ex);
            throw;
        }
    }

    private IMinister? ResolveMinister(MinisterDescriptor descriptor) =>
        ministers.FirstOrDefault(m =>
            MinisterRegistry.NormalizeKey(m.Name).Equals(descriptor.Key, StringComparison.OrdinalIgnoreCase) ||
            m.Name.Equals(descriptor.Label, StringComparison.OrdinalIgnoreCase));
}

public sealed record MinisterTriggerResult(
    string Scope,
    string Minister,
    string Trigger,
    string StateSource,
    bool UsedRestoredSnapshot);

public sealed record CabinetTriggerResult(
    string Scope,
    string Trigger,
    string StateSource,
    bool UsedRestoredSnapshot);
