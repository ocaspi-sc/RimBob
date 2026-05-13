using RimAI.Core.Advice;

namespace RimAI.Core.Ministers;

public enum PlayCycleTrigger
{
    StartupBootstrap,
    CabinetRefresh,
    FlagFired,
    Heartbeat,
    ScheduledWakeupFired
}

public sealed record PlayCycleContext(
    PlayCycleTrigger Trigger,
    AgentFlag? Flag = null,
    string? WakeupPayload = null)
{
    public bool IsBootstrap => Trigger == PlayCycleTrigger.StartupBootstrap;

    public static PlayCycleContext StartupBootstrap { get; } = new(PlayCycleTrigger.StartupBootstrap);
    public static PlayCycleContext CabinetRefresh { get; } = new(PlayCycleTrigger.CabinetRefresh);
}
