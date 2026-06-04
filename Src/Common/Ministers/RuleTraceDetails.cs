using System.Text.Json.Serialization;
using RimBob.Core.Advice;

namespace RimBob.Core.Ministers;

public sealed record RuleTraceDetails(
    RuleId? SelectedRule,
    IReadOnlyList<RuleEvaluationTrace> AllRules)
{
    [JsonIgnore]
    public IReadOnlyList<RuleTraceEntry> MatchedSignals =>
        AllRules
            .Where(rule => rule.Outcome is RuleOutcome.Selected or RuleOutcome.Escalated)
            .Select(rule => new RuleTraceEntry(rule.Rule, rule.Outcome, rule.Reason ?? rule.Conditions))
            .ToList();

    public static RuleTraceDetails Escalated(RuleId selectedRule, string reason) =>
        new(
            selectedRule,
            [
                new RuleEvaluationTrace(
                    selectedRule,
                    RuleOutcome.Escalated,
                    reason,
                    "escalate",
                    reason,
                    [new RuleEmission("escalate", null, reason, null, null, null)])
            ]);
}

public sealed record RuleEmission(
    string Kind,
    Priority? Priority,
    string Label,
    string? To,
    string? TargetDef,
    string? WorkType);

public sealed record RuleTraceEntry(
    RuleId Rule,
    RuleOutcome Outcome,
    string Reason);

public sealed record RuleEvaluationTrace(
    RuleId Rule,
    RuleOutcome Outcome,
    string Conditions,
    string OutputAction,
    string? Reason,
    IReadOnlyList<RuleEmission>? Emissions = null)
{
    public IReadOnlyList<RuleEmission> Emissions { get; init; } = Emissions ?? [];
}
