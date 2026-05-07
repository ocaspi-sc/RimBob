using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RimAI.Core.Ministers;
using RimAI.State;

namespace RimAI.Coordination;

/// <summary>
/// Polls ColonyState's in-game day and wakes the Mayor once when it changes.
/// In-game day = EconomyLedger.Tick / 60000 (RimWorld has 60k ticks per day).
/// Skips the first observation so we don't fire on Host startup.
/// </summary>
public sealed class DayTickOrchestrator(
    ColonyState                       colony,
    IMinister                         mayor,
    ILogger<DayTickOrchestrator>      log
) : BackgroundService
{
    private const long TicksPerDay = 60000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private long _lastDay = -1;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation("DayTickOrchestrator started; polling every {Interval}", PollInterval);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tick = colony.Economy.Value.Tick;
                var day  = tick / TicksPerDay;

                if (_lastDay >= 0 && day != _lastDay)
                {
                    log.LogInformation(
                        "Day rollover detected: {Previous} → {Current} (tick={Tick}); waking {Minister}",
                        _lastDay, day, tick, mayor.Name);
                    await mayor.RunPlayCycle(ct);
                }
                _lastDay = day;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "DayTickOrchestrator cycle failed; continuing");
            }

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}
