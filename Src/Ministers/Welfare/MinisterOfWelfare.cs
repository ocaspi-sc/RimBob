using Microsoft.Extensions.Logging;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.State;

namespace RimBob.Ministers.Welfare;

public sealed class MinisterOfWelfare(
    BriefingCache briefings,
    Rules rules,
    MinisterOutputStore outputStore,
    AdviceBus bus,
    FlagChannel flags,
    ILogger<MinisterOfWelfare> log,
    MinisterReplayRecorder? replay = null) : IMinister
{
    public string Name => "Welfare";

    public async Task RunPlayCycle(PlayCycleContext cycle, CancellationToken ct)
    {
        WelfareSourceBriefing briefing = briefings.GetWelfareBriefing();
        MinisterBriefingContext context = BuildContext(outputStore.CurrentMayorAgenda);
        RuleRun result = rules.Evaluate(briefing, ColonyContext.Default);
        DecisionProjectionContext projectionContext = new(
            Minister: Name,
            Domain: "welfare",
            BriefingVersion: briefing.BriefingVersion,
            GameDate: null,
            GameTick: briefing.GameTick,
            Now: DateTimeOffset.UtcNow);
        IReadOnlyList<AdviceItem> advice = DecisionProjection.ProjectAdvice(result.Decisions, projectionContext);
        IReadOnlyList<AgentFlag> emittedFlags = DecisionProjection.ProjectFlags(result.Decisions, projectionContext);
        string stateSummary = WelfareStateSummary.Build(briefing);
        PublishSnapshot(advice, emittedFlags, stateSummary);
        string? trace = result.Decisions.Count > 0
            ? MinisterRuleTableEvaluator.CompositeTrace(result.Decisions)
            : result.Diagnostics.SelectedRule?.Value;
        await PersistReplayAsync(new MinisterReplayEntry(
            Minister: Name,
            Cycle: cycle,
            Path: "rules",
            Briefing: briefing,
            Context: context,
            RuleTrace: trace,
            RuleDiagnostics: result.Diagnostics,
            Advice: advice,
            Flags: emittedFlags,
            StateSummary: stateSummary), ct);
        log.LogInformation(
            "Welfare rules decision trace={Trace} advice={AdviceCount} flags={FlagCount}",
            trace,
            advice.Count,
            emittedFlags.Count);
    }

    public Task RunRefinement(CancellationToken ct) => Task.CompletedTask;

    public static MinisterBriefingContext BuildContext(MayorAgenda? agenda)
    {
        if (agenda is null) return MinisterBriefingContext.Empty;

        string? direction = agenda.CabinetDirection.TryGetValue("welfare", out string? exact)
            ? exact
            : agenda.CabinetDirection.TryGetValue("Welfare", out string? titleCase)
                ? titleCase
                : agenda.CabinetDirection.TryGetValue("mood", out string? mood)
                    ? mood
                    : null;

        IReadOnlyList<string> domains = agenda.ShortTerm
            .Where(priority => priority.Status == AgendaPriorityStatus.Active)
            .Select(priority => InferDomain(priority.Text))
            .Where(domain => domain is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MinisterBriefingContext(agenda.Posture, direction, domains);
    }

    private Task PersistReplayAsync(MinisterReplayEntry entry, CancellationToken ct) =>
        replay?.RecordAsync(entry, ct) ?? Task.CompletedTask;

    private void PublishSnapshot(
        IReadOnlyList<AdviceItem> advice,
        IReadOnlyList<AgentFlag> emittedFlags,
        string stateSummary)
    {
        bus.ReplaceMinisterAdvice(Name, advice, stateSummary, flags: emittedFlags);
        foreach (AgentFlag flag in emittedFlags)
            flags.Publish(flag);
    }

    private static string? InferDomain(string text)
    {
        string lower = text.ToLowerInvariant();
        if (lower.Contains("mood") ||
            lower.Contains("break") ||
            lower.Contains("recreation") ||
            lower.Contains("comfort") ||
            lower.Contains("beauty") ||
            lower.Contains("sleep") ||
            lower.Contains("bed"))
        {
            return "welfare";
        }

        if (lower.Contains("food") || lower.Contains("meal") || lower.Contains("harvest"))
            return "food";
        if (lower.Contains("build") || lower.Contains("power") || lower.Contains("room"))
            return "construction";
        if (lower.Contains("defense") || lower.Contains("raid"))
            return "defense";
        if (lower.Contains("research"))
            return "research";
        return null;
    }
}
