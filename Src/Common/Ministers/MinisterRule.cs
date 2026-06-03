using RimBob.Core.Advice;

namespace RimBob.Core.Ministers;

public sealed record RuleEmission(
    string Rule,
    AdviceItem Advice,
    IReadOnlyList<AgentFlag> Flags);

public sealed record MinisterRule<TBriefing>(
    string Id,
    Func<TBriefing, bool> Matches,
    Func<TBriefing, string> Reason,
    Func<TBriefing, RuleEmission> Build);

public sealed record MinisterRuleTraceDescriptor<TBriefing>(
    string Id,
    Func<TBriefing, string> Reason,
    Func<TBriefing, string?, string> Outcome);

public sealed record MinisterRuleTableResult(
    bool AnyRuleMatched,
    Decision? Decision,
    RuleTraceDetails Diagnostics);

public sealed record MinisterRuleAggregate(
    IReadOnlyList<RuleEmission> OrderedEmissions,
    IReadOnlyList<RuleEmission> DedupedEmissions,
    IReadOnlyList<AdviceItem> Advice,
    IReadOnlyList<AgentFlag> Flags,
    string Trace);
