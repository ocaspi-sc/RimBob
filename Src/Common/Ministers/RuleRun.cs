namespace RimBob.Core.Ministers;

public sealed record RuleRun(
    IReadOnlyList<Decision> Decisions,
    RuleTraceDetails Diagnostics);
