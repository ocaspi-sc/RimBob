using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;

namespace RimBob.Tests;

internal sealed record ProjectedRuleRun(
    IReadOnlyList<AdviceItem> Advice,
    IReadOnlyList<AgentFlag> Flags,
    string Trace,
    RuleTraceDetails Diagnostics,
    IReadOnlyList<Decision> Effects)
{
    public Escalate? Escalation => Effects.OfType<Escalate>().SingleOrDefault();
}

internal static class RuleRunTestProjection
{
    public static ProjectedRuleRun ProjectFor(
        this RuleRun run,
        string minister,
        string domain,
        long briefingVersion,
        GameDate? gameDate,
        long? gameTick,
        DateTimeOffset now)
    {
        DecisionProjectionContext context = new(minister, domain, briefingVersion, gameDate, gameTick, now);
        return new ProjectedRuleRun(
            DecisionProjection.ProjectAdvice(run.Decisions, context),
            DecisionProjection.ProjectFlags(run.Decisions, context),
            TraceFor(run),
            run.Diagnostics,
            run.Decisions);
    }

    private static string TraceFor(RuleRun run) =>
        run.Decisions.Count > 0
            ? MinisterRuleTableEvaluator.CompositeTrace(run.Decisions)
            : run.Diagnostics.SelectedRule?.Value ?? string.Empty;
}
