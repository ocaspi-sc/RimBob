namespace RimAI.Core.Ministers;

/// <summary>
/// Every minister implements this. The Orchestrator calls RunPlayCycle when the
/// minister's briefing version changes. RunRefinement is the same minister in its
/// rule-refinement mode — reading decision logs and promoting patterns into rules.
/// </summary>
public interface IMinister
{
    string Name { get; }

    /// <summary>Called by the Orchestrator when this minister's briefing has changed.</summary>
    Task RunPlayCycle(CancellationToken ct);

    /// <summary>Called manually or on schedule to tighten the rules layer.</summary>
    Task RunRefinement(CancellationToken ct);
}
