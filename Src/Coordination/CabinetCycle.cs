using Microsoft.Extensions.Logging;
using RimAI.Core.Ministers;
using RimAI.State;

namespace RimAI.Coordination;

public sealed class CabinetCycle(
    IngestionDispatcher ingestion,
    IEnumerable<IMinister> ministers,
    MinisterRegistry registry,
    MinisterTraceStore traces,
    ILogger<CabinetCycle> log)
{
    public Task RunAsync(CancellationToken ct) => RunAsync(PlayCycleContext.ManualTrigger, ct);

    public async Task RunAsync(PlayCycleContext cycle, CancellationToken ct)
    {
        await ingestion.RefreshAllAsync(ct);

        foreach (MinisterDescriptor descriptor in registry.CabinetMinisters)
        {
            IMinister? minister = ResolveMinister(descriptor);
            if (minister is null)
                throw new InvalidOperationException($"Cabinet cycle could not resolve {descriptor.Label} minister.");

            log.LogInformation("Cabinet cycle: running {Minister}", descriptor.Label);
            await RunResolvedMinisterAsync(minister, cycle, ct);
        }
    }

    public async Task<MinisterTriggerResult?> TriggerMinisterAsync(string ministerKey, CancellationToken ct)
    {
        MinisterDescriptor? descriptor = registry.FindMinister(ministerKey);
        if (descriptor is not { Ready: true, CanManualTrigger: true }) return null;

        await ingestion.RefreshAllAsync(ct);

        IMinister? minister = ResolveMinister(descriptor);
        if (minister is null)
            throw new InvalidOperationException($"Manual trigger could not resolve {descriptor.Label} minister.");

        await RunResolvedMinisterAsync(minister, PlayCycleContext.ManualTrigger, ct);
        return new MinisterTriggerResult(descriptor.Key, descriptor.Label, PlayCycleContext.ManualTrigger.Trigger.ToString());
    }

    private async Task RunResolvedMinisterAsync(IMinister minister, PlayCycleContext cycle, CancellationToken ct)
    {
        traces.Begin(minister.Name, cycle);
        try
        {
            await minister.RunPlayCycle(cycle, ct);
            traces.Complete(minister.Name);
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

public sealed record MinisterTriggerResult(string Scope, string Minister, string Trigger);
