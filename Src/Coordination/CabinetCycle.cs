using Microsoft.Extensions.Logging;
using RimAI.Core.Ministers;
using RimAI.State;

namespace RimAI.Coordination;

public sealed class CabinetCycle(
    IngestionDispatcher ingestion,
    IEnumerable<IMinister> ministers,
    ILogger<CabinetCycle> log)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await ingestion.RefreshAllAsync(ct);

        IMinister? food = ministers.FirstOrDefault(m => m.Name.Equals("Food", StringComparison.OrdinalIgnoreCase));
        IMinister? mayor = ministers.FirstOrDefault(m => m.Name.Equals("Mayor", StringComparison.OrdinalIgnoreCase));

        if (food is not null)
        {
            log.LogInformation("Cabinet cycle: running Food before Mayor");
            await food.RunPlayCycle(ct);
        }

        if (mayor is null)
            throw new InvalidOperationException("Cabinet cycle could not resolve Mayor minister.");

        await mayor.RunPlayCycle(ct);
    }
}
