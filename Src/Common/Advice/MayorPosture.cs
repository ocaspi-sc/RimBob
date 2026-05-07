using System.Text.Json.Serialization;

namespace RimAI.Core.Advice;

/// <summary>
/// Strategic stance the Mayor sets each turn. Free-text strings (LLM-produced)
/// rather than enums; allowed values are conventions, not enforced.
/// economic: growth | consolidation | survival
/// military: defensive | offensive | neutral
/// </summary>
public sealed record MayorPosture(
    [property: JsonPropertyName("economic")] string Economic,
    [property: JsonPropertyName("military")] string Military,
    [property: JsonPropertyName("summary")]  string Summary
);
