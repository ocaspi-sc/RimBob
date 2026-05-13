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
    public Task RunAsync(CancellationToken ct) => RunAsync(PlayCycleContext.CabinetRefresh, ct);

    public async Task RunAsync(PlayCycleContext cycle, CancellationToken ct)
    {
        await ingestion.RefreshAllAsync(ct);

        IMinister? food = ministers.FirstOrDefault(m => m.Name.Equals("Food", StringComparison.OrdinalIgnoreCase));
        IMinister? mayor = ministers.FirstOrDefault(m => m.Name.Equals("Mayor", StringComparison.OrdinalIgnoreCase));

        if (food is not null)
        {
            log.LogInformation("Cabinet cycle: running Food before Mayor");
            await RunMinisterAsync(food, cycle, ct);
        }

        if (mayor is null)
            throw new InvalidOperationException("Cabinet cycle could not resolve Mayor minister.");

        await RunMinisterAsync(mayor, cycle, ct);
    }

    private async Task RunMinisterAsync(IMinister minister, PlayCycleContext cycle, CancellationToken ct)
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
}
