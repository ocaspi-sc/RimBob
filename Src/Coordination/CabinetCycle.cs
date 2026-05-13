using Microsoft.Extensions.Logging;
using RimAI.Core.Ministers;
using RimAI.State;

namespace RimAI.Coordination;

public sealed class CabinetCycle(
    IngestionDispatcher ingestion,
    IEnumerable<IMinister> ministers,
    MinisterTraceStore traces,
    ILogger<CabinetCycle> log)
{
    public Task RunAsync(CancellationToken ct) => RunAsync(PlayCycleContext.ManualTrigger, ct);

    public async Task RunAsync(PlayCycleContext cycle, CancellationToken ct)
    {
        await ingestion.RefreshAllAsync(ct);

        IMinister? food = ministers.FirstOrDefault(m => m.Name.Equals("Food", StringComparison.OrdinalIgnoreCase));
        IMinister? mayor = ministers.FirstOrDefault(m => m.Name.Equals("Mayor", StringComparison.OrdinalIgnoreCase));

        if (food is not null)
        {
            log.LogInformation("Cabinet cycle: running Food before Mayor");
            await RunResolvedMinisterAsync(food, cycle, ct);
        }

        if (mayor is null)
            throw new InvalidOperationException("Cabinet cycle could not resolve Mayor minister.");

        await RunResolvedMinisterAsync(mayor, cycle, ct);
    }

    public async Task<MinisterTriggerResult?> TriggerMinisterAsync(string ministerKey, CancellationToken ct)
    {
        await ingestion.RefreshAllAsync(ct);

        IMinister? minister = ResolveMinister(ministerKey);
        if (minister is null) return null;

        await RunResolvedMinisterAsync(minister, PlayCycleContext.ManualTrigger, ct);
        return new MinisterTriggerResult(MinisterKey(minister.Name), minister.Name, PlayCycleContext.ManualTrigger.Trigger.ToString());
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

    private IMinister? ResolveMinister(string ministerKey)
    {
        string normalized = MinisterKey(ministerKey);
        return ministers.FirstOrDefault(m => MinisterKey(m.Name).Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string MinisterKey(string name) =>
        name.Trim().Replace(" ", "_", StringComparison.Ordinal).ToLowerInvariant();
}

public sealed record MinisterTriggerResult(string Scope, string Minister, string Trigger);
