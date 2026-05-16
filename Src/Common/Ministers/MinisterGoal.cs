namespace RimBob.Core.Ministers;

/// <summary>
/// A goal emitted by a minister's rules layer or LLM. The HTN planner maps
/// goal_id 1:1 to a compound task in that minister's domain.
/// </summary>
public record MinisterGoal(
    string GoalId,
    int Priority,          // 1 = highest; larger = lower
    string Rationale,
    string? Notes = null); // Logged but not parsed in v1 (upgrade seam for method hints)
