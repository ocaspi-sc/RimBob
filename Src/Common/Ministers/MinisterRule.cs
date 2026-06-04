namespace RimBob.Core.Ministers;

public sealed record MinisterRule<TBriefing>(
    RuleId Id,
    Func<TBriefing, bool> Matches,
    Func<TBriefing, string> Reason,
    Func<TBriefing, IReadOnlyList<Decision>> Build);

public sealed record MinisterRuleTraceDescriptor<TBriefing>(
    RuleId Id,
    Func<TBriefing, string> Reason,
    Func<TBriefing, RuleId?, RuleOutcome> Outcome);
