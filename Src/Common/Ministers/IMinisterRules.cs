namespace RimAI.Core.Ministers;

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
    IReadOnlyList<MinisterGoal> Goals,
    IReadOnlyList<AgentFlag> Flags,
    string Trace) : RulesResult;

/// <summary>Rules couldn't cover this case. Escalate to the LLM.</summary>
public record Escalate(string Reason, object? Context = null) : RulesResult;
