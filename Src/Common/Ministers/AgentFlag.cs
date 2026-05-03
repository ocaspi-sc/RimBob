namespace RimAI.Core.Ministers;

/// <summary>
/// Cross-minister signal. Ministers emit flags to the FlagChannel; the Chief of
/// Staff routes them. Ministers never talk to each other directly.
/// </summary>
public record AgentFlag(
    string Id,
    string SourceMinister,
    FlagSeverity Severity,
    string Domain,
    string Summary,
    string? Detail = null,
    DateTimeOffset? ExpiresAt = null);

public enum FlagSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3  // Only Defense emits Critical; others require CoS approval
}
