using System.Text.Json.Serialization;
using RimBob.Core.Advice;

namespace RimBob.Core.Ministers;

/// <summary>
/// Cross-minister signal. Ministers emit flags to the FlagChannel; the Chief of
/// Staff routes them. Ministers never talk to each other directly.
/// </summary>
public record AgentFlag(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("source_minister")]
    string SourceMinister,
    [property: JsonPropertyName("priority")]
    Priority Priority,
    [property: JsonPropertyName("domain")]
    string Domain,
    [property: JsonPropertyName("summary")]
    string Summary,
    [property: JsonPropertyName("building_requests")]
    IReadOnlyList<BuildingRequest>? BuildingRequests = null,
    [property: JsonPropertyName("labor_requests")]
    IReadOnlyList<LaborRequest>? LaborRequests = null,
    [property: JsonPropertyName("item_requests")]
    IReadOnlyList<ItemRequest>? ItemRequests = null,
    [property: JsonPropertyName("attention")]
    IReadOnlyList<AttentionRequest>? Attention = null,
    [property: JsonPropertyName("detail")]
    string? Detail = null,
    [property: JsonPropertyName("expires_at")]
    DateTimeOffset? ExpiresAt = null);
