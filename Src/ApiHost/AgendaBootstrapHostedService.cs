using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Ministers.Mayor;
using RimBob.State;

namespace RimBob.Host;

public sealed class AgendaBootstrapHostedService(
    AgendaStore agendaStore,
    BriefingCache briefings,
    MayorAgendaRules rules,
    FlagChannel flags,
    AdviceBus bus,
    ILogger<AgendaBootstrapHostedService> log) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (agendaStore.Current is not null)
        {
            log.LogInformation(
                "Mayor agenda bootstrap skipped; current agenda v{Version} already exists.",
                agendaStore.Current.Version);
            return;
        }

        MayorBriefing briefing = briefings.GetMayorBriefing();
        IReadOnlyList<AgentFlag> activeFlags = flags.Active(FlagSeverity.Medium);
        MayorDirectiveSet directiveSet = rules.Evaluate(briefing, ColonyContext.Default, activeFlags);
        MayorAgendaInput input = MayorAgendaBootstrap.Build(
            briefing,
            directiveSet.Directives,
            activeFlags,
            "No persisted Mayor agenda existed at Host startup.");

        MayorAgenda agenda = await agendaStore.UpdateAsync(input, FormatTick(briefing), cancellationToken);
        bus.Publish(new AgendaUpdated(agenda));

        log.LogWarning(
            "Initialized bootstrap Mayor agenda v{Version} because no persisted agenda existed.",
            agenda.Version);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string FormatTick(MayorBriefing briefing) =>
        $"Y{briefing.Date.Year ?? 0}{briefing.Date.Quadrum ?? "?"}D{briefing.Date.Day ?? 0}";
}
