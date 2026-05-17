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
/// Diagnostic trace of the rules layer: the selected rule plus lower-priority
/// matches that were true but did not own the final path.
/// </summary>
public sealed record RuleTraceDetails(
    string? SelectedRule,
    IReadOnlyList<RuleTraceEntry> MatchedSignals,
    IReadOnlyList<RuleTraceEntry> SuppressedCandidates);

public sealed record RuleTraceEntry(
    string Rule,
    string Outcome,
    string Reason);
