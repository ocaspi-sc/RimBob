using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RimAI.Core.Ministers;
using RimAI.State;

namespace RimAI.Coordination;

/// <summary>
/// Polls ColonyState's in-game day and wakes the Mayor once per day change.
/// In-game day = EconomyLedger.Tick / 60000 (RimWorld has 60k ticks per day).
///
/// On first successful poll: refreshes state fully and fires Mayor immediately
/// so the dashboard has an agenda on load without waiting for a day to roll over.
/// On each subsequent poll: refreshes state (keeps ColonyState current), fires
/// Mayor only when the day number changes.
/// </summary>
public sealed class DayTickOrchestrator : BackgroundService
{
    private const long TicksPerDay = 60_000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ColonyState _colony;
    private readonly IMinister _mayor;
    private readonly ILogger<DayTickOrchestrator> _log;
    private readonly Func<CancellationToken, Task> _refresh;

    private long _lastDay = -1;

    /// <summary>Production constructor — wired by DI.</summary>
    public DayTickOrchestrator(
        ColonyState                       colony,
        IngestionDispatcher               ingestion,
        IMinister                         mayor,
        ILogger<DayTickOrchestrator>      log)
        : this(colony, mayor, log, ingestion.RefreshAllAsync) { }

    /// <summary>
    /// Test constructor. Pass a custom <paramref name="refresh"/> delegate to control
    /// when/how ColonyState is updated; pass <c>null</c> to make refresh a no-op so
    /// tests can set <see cref="ColonyState"/> aggregates directly.
    /// </summary>
    internal DayTickOrchestrator(
        ColonyState                       colony,
        IMinister                         mayor,
        ILogger<DayTickOrchestrator>      log,
        Func<CancellationToken, Task>?    refresh = null)
    {
        _colony  = colony;
        _mayor   = mayor;
        _log     = log;
        _refresh = refresh ?? (_ => Task.CompletedTask);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("DayTickOrchestrator started; polling every {Interval}", PollInterval);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _refresh(ct);

                long tick = _colony.Economy.Value.Tick;
                long day  = tick / TicksPerDay;

                if (_lastDay == -1)
                {
                    // First successful poll: publish initial briefing so dashboard loads with content.
                    _lastDay = day;
                    _log.LogInformation(
                        "Startup ingestion complete (tick={Tick}, day={Day}); firing initial briefing",
                        tick, day);
                    await _mayor.RunPlayCycle(ct);
                }
                else if (day != _lastDay)
                {
                    _log.LogInformation(
                        "Day rollover: {Previous} → {Current} (tick={Tick}); waking {Minister}",
                        _lastDay, day, tick, _mayor.Name);
                    _lastDay = day;
                    await _mayor.RunPlayCycle(ct);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "DayTickOrchestrator cycle failed; continuing");
            }

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}
