using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RimAI.Core.Ministers;
using RimAI.State;

namespace RimAI.Coordination;

/// <summary>
/// Polls ColonyState's in-game day and wakes the cabinet once per day change.
/// In-game day = EconomyLedger.Tick / 60000 (RimWorld has 60k ticks per day).
/// </summary>
public sealed class DayTickOrchestrator : BackgroundService
{
    private const long TicksPerDay = 60_000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ColonyState _colony;
    private readonly Func<CancellationToken, Task> _runCycle;
    private readonly string _cycleName;
    private readonly ILogger<DayTickOrchestrator> _log;
    private readonly Func<CancellationToken, Task> _refresh;

    private long _lastDay = -1;

    public DayTickOrchestrator(
        ColonyState colony,
        CabinetCycle cabinet,
        ILogger<DayTickOrchestrator> log)
        : this(colony, "Cabinet", log, cabinet.RunAsync, _ => Task.CompletedTask) { }

    internal DayTickOrchestrator(
        ColonyState colony,
        IMinister mayor,
        ILogger<DayTickOrchestrator> log,
        Func<CancellationToken, Task>? refresh = null)
        : this(colony, mayor.Name, log, mayor.RunPlayCycle, refresh) { }

    private DayTickOrchestrator(
        ColonyState colony,
        string cycleName,
        ILogger<DayTickOrchestrator> log,
        Func<CancellationToken, Task> runCycle,
        Func<CancellationToken, Task>? refresh)
    {
        _colony = colony;
        _cycleName = cycleName;
        _log = log;
        _runCycle = runCycle;
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
                long day = tick / TicksPerDay;

                if (_lastDay == -1)
                {
                    _lastDay = day;
                    _log.LogInformation(
                        "Startup ingestion complete (tick={Tick}, day={Day}); firing initial {Cycle} cycle",
                        tick, day, _cycleName);
                    await _runCycle(ct);
                }
                else if (day != _lastDay)
                {
                    _log.LogInformation(
                        "Day rollover: {Previous} -> {Current} (tick={Tick}); waking {Cycle}",
                        _lastDay, day, tick, _cycleName);
                    _lastDay = day;
                    await _runCycle(ct);
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
