using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RimBob.Coordination;
using RimBob.Core.Aggregates;
using RimBob.Core.Ministers;
using RimBob.State;

namespace RimBob.Tests.Coordination;

public sealed class DayTickOrchestratorTests
{
    private const long TicksPerDay = 60_000;

    [Fact]
    public async Task FirstObservation_TriggersMayor()
    {
        ColonyState colony = new();
        colony.Economy.Update(new EconomyLedger(TicksPerDay * 3, 0, "", "", false, ""));
        FakeMinister mayor = new();

        DayTickOrchestrator sut = new(colony, mayor, NullLogger<DayTickOrchestrator>.Instance);
        await RunOneCycleAsync(sut);

        mayor.WakeCount.Should().Be(1);
        mayor.Triggers.Should().ContainSingle().Which.Should().Be(PlayCycleTrigger.StartupBootstrap);
    }

    [Fact]
    public async Task DayChange_TriggersOnRollover()
    {
        ColonyState colony = new();
        FakeMinister mayor = new();
        DayTickOrchestrator sut = new(colony, mayor, NullLogger<DayTickOrchestrator>.Instance);

        colony.Economy.Update(new EconomyLedger(TicksPerDay * 3, 0, "", "", false, ""));
        await RunOneCycleAsync(sut);  // first observation → fires
        mayor.WakeCount.Should().Be(1);

        colony.Economy.Update(new EconomyLedger(TicksPerDay * 4, 0, "", "", false, ""));
        await RunOneCycleAsync(sut);  // day rollover → fires again
        mayor.WakeCount.Should().Be(2);
        mayor.Triggers.Should().Equal(PlayCycleTrigger.StartupBootstrap, PlayCycleTrigger.CabinetRefresh);
    }

    [Fact]
    public async Task SameDayPolls_DoNotRetrigger()
    {
        ColonyState colony = new();
        FakeMinister mayor = new();
        DayTickOrchestrator sut = new(colony, mayor, NullLogger<DayTickOrchestrator>.Instance);

        colony.Economy.Update(new EconomyLedger(TicksPerDay * 3, 0, "", "", false, ""));
        await RunOneCycleAsync(sut);  // first observation → fires (count = 1)

        // Mid-day tick advances — same day, should not re-trigger.
        colony.Economy.Update(new EconomyLedger(TicksPerDay * 3 + 30_000, 0, "", "", false, ""));
        await RunOneCycleAsync(sut);
        colony.Economy.Update(new EconomyLedger(TicksPerDay * 3 + 59_999, 0, "", "", false, ""));
        await RunOneCycleAsync(sut);

        mayor.WakeCount.Should().Be(1);
    }

    [Fact]
    public async Task MayorThrows_ServiceContinues()
    {
        ColonyState colony = new();
        FakeMinister mayor = new() { ThrowOnWake = true };
        DayTickOrchestrator sut = new(colony, mayor, NullLogger<DayTickOrchestrator>.Instance);

        colony.Economy.Update(new EconomyLedger(TicksPerDay * 3, 0, "", "", false, ""));

        // First observation fires the mayor; mayor throws; service must swallow and continue.
        Func<Task> act = () => RunOneCycleAsync(sut);
        await act.Should().NotThrowAsync();

        mayor.WakeCount.Should().Be(1);
    }

    /// <summary>
    /// Runs the BackgroundService just long enough to perform one observation cycle.
    /// The orchestrator polls every 2s; we cancel after 200ms which is enough for the
    /// initial body to execute exactly once (Task.Delay throws on cancel, exiting the loop).
    /// </summary>
    private static async Task RunOneCycleAsync(DayTickOrchestrator sut)
    {
        using CancellationTokenSource cts = new();
        Task run = sut.StartAsync(cts.Token);
        await Task.Delay(200);
        await sut.StopAsync(CancellationToken.None);
        cts.Cancel();
        try { await run; } catch (OperationCanceledException) { }
    }

    private sealed class FakeMinister : IMinister
    {
        public string Name => "FakeMayor";
        public int    WakeCount   { get; private set; }
        public bool   ThrowOnWake { get; init; }
        public List<PlayCycleTrigger> Triggers { get; } = [];

        public Task RunPlayCycle(PlayCycleContext context, CancellationToken ct)
        {
            WakeCount++;
            Triggers.Add(context.Trigger);
            if (ThrowOnWake) throw new InvalidOperationException("boom");
            return Task.CompletedTask;
        }

        public Task RunRefinement(CancellationToken ct) => Task.CompletedTask;
    }
}
