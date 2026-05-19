using RimBob.Core.Advice;

namespace RimBob.Core.Ministers;

/// <summary>
/// The rules layer for a minister. Returns either a Decision (handled by rules,
/// no LLM needed) or an Escalate (rules couldn't cover this — call the LLM).
/// </summary>
public interface IMinisterRules<TBriefing>
{
    RulesResult Evaluate(TBriefing briefing, ColonyContext context);
}

/// <summary>Discriminated union: Decision | Escalate.</summary>
public abstract record RulesResult;

/// <summary>Rules produced a decision. No LLM call needed.</summary>
public record Decision(
    IReadOnlyList<RimBob.Core.Advice.AdviceItem> Advice,
    IReadOnlyList<AgentFlag> Flags,
    string Trace,
    RuleTraceDetails? Diagnostics = null) : RulesResult;

/// <summary>Rules couldn't cover this case. Escalate to the LLM.</summary>
public record Escalate(
    string Reason,
    object? Context = null,
    RuleTraceDetails? Diagnostics = null) : RulesResult;

/// <summary>
/// Diagnostic trace of the rules layer: selected/matched rules plus the advice,
/// actions, and flags emitted from that path.
/// </summary>
public sealed record RuleTraceDetails
{
    public RuleTraceDetails(
        string? SelectedRule,
        IReadOnlyList<RuleTraceEntry> MatchedSignals,
        IReadOnlyList<RuleTraceEntry> SuppressedCandidates)
        : this(SelectedRule, MatchedSignals, SuppressedCandidates, [], [], [])
    {
    }

    public RuleTraceDetails(
        string? SelectedRule,
        IReadOnlyList<RuleTraceEntry> MatchedSignals,
        IReadOnlyList<RuleTraceEntry> SuppressedCandidates,
        IReadOnlyList<RuleEmittedAdviceTrace> EmittedAdvice,
        IReadOnlyList<RuleEmittedActionTrace> EmittedActions,
        IReadOnlyList<RuleEmittedFlagTrace> EmittedFlags)
    {
        this.SelectedRule = SelectedRule;
        this.MatchedSignals = MatchedSignals;
        this.SuppressedCandidates = SuppressedCandidates;
        this.EmittedAdvice = EmittedAdvice;
        this.EmittedActions = EmittedActions;
        this.EmittedFlags = EmittedFlags;
    }

    public string? SelectedRule { get; init; }
    public IReadOnlyList<RuleTraceEntry> MatchedSignals { get; init; }
    public IReadOnlyList<RuleTraceEntry> SuppressedCandidates { get; init; }
    public IReadOnlyList<RuleEmittedAdviceTrace> EmittedAdvice { get; init; }
    public IReadOnlyList<RuleEmittedActionTrace> EmittedActions { get; init; }
    public IReadOnlyList<RuleEmittedFlagTrace> EmittedFlags { get; init; }

    public static RuleTraceDetails Escalated(string selectedRule, string reason) =>
        new(
            selectedRule,
            [new RuleTraceEntry(selectedRule, "escalated", reason)],
            []);

    public RuleTraceDetails WithEmissions(
        string source,
        string rule,
        IReadOnlyList<AdviceItem> advice,
        IReadOnlyList<AgentFlag> flags)
    {
        List<RuleEmittedAdviceTrace> emittedAdvice = [];
        List<RuleEmittedActionTrace> emittedActions = [];

        foreach (AdviceItem item in advice)
        {
            emittedAdvice.Add(new RuleEmittedAdviceTrace(
                Source: source,
                Rule: rule,
                AdviceId: item.Id,
                AdviceType: item.AdviceType,
                Priority: item.Priority,
                Title: item.Title,
                ActionCount: item.Actions.Count));

            for (int i = 0; i < item.Actions.Count; i++)
            {
                AdviceAction action = item.Actions[i];
                emittedActions.Add(new RuleEmittedActionTrace(
                    Source: source,
                    Rule: rule,
                    AdviceId: item.Id,
                    ActionIndex: i,
                    Kind: action.Kind,
                    Instruction: action.Instruction,
                    Reason: action.Reason,
                    ApplyKind: action.Apply?.Kind,
                    ApplyLabel: action.Apply?.Label,
                    ApplyTargetSummary: action.Apply?.TargetSummary));
            }
        }

        IReadOnlyList<RuleEmittedFlagTrace> emittedFlags = flags
            .Select(flag => new RuleEmittedFlagTrace(
                Source: source,
                Rule: rule,
                FlagId: flag.Id,
                Severity: flag.Severity,
                Summary: flag.Summary,
                RequestCount: flag.Requests?.Count ?? 0))
            .ToList();

        return this with
        {
            EmittedAdvice = emittedAdvice,
            EmittedActions = emittedActions,
            EmittedFlags = emittedFlags
        };
    }
}

public sealed record RuleTraceEntry(
    string Rule,
    string Outcome,
    string Reason);

public sealed record RuleEmittedAdviceTrace(
    string Source,
    string Rule,
    string AdviceId,
    string AdviceType,
    AdvicePriority Priority,
    string Title,
    int ActionCount);

public sealed record RuleEmittedActionTrace(
    string Source,
    string Rule,
    string AdviceId,
    int ActionIndex,
    AdviceActionKind Kind,
    string Instruction,
    string? Reason,
    AdviceApplyKind? ApplyKind,
    string? ApplyLabel,
    string? ApplyTargetSummary);

public sealed record RuleEmittedFlagTrace(
    string Source,
    string Rule,
    string FlagId,
    FlagSeverity Severity,
    string Summary,
    int RequestCount);
