using System.Text.Json.Serialization;

namespace RimBob.Ingestion;

/// <summary>
/// Every RIMAPI response is wrapped in this envelope.
/// { "success": bool, "data": T, "errors": [], "warnings": [], "timestamp": "ISO-8601" }
/// </summary>
public sealed record RimApiEnvelope<T>(
    [property: JsonPropertyName("success")]   bool Success,
    [property: JsonPropertyName("data")]      T? Data,
    [property: JsonPropertyName("errors")]    IReadOnlyList<string>? Errors,
    [property: JsonPropertyName("warnings")]  IReadOnlyList<string>? Warnings,
    [property: JsonPropertyName("timestamp")] string? Timestamp
);
