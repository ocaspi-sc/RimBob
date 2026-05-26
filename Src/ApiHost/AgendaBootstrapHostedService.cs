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
    MinisterOutputStore outputStore,
    BriefingCache briefings,
    MayorAgendaRules rules,
    FlagChannel flags,
    AdviceBus bus,
    ILogger<AgendaBootstrapHostedService> log) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (outputStore.HasAnyOutput)
        {
            log.LogInformation("Minister output bootstrap skipped; persisted output already exists.");
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

        MayorAgenda agenda = await outputStore.UpdateMayorAsync(input, briefing.Date, cancellationToken);
        bus.Publish(new AgendaUpdated(agenda));

        log.LogWarning(
            "Initialized bootstrap Mayor agenda v{Version} because no persisted agenda existed.",
            agenda.Version);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

}
