using System.Text.Json.Serialization;

namespace RimAI.Core.Advice;

/// <summary>
/// A resource a minister needs in order to resolve its advice. Requests are
/// advisory in MVP; they do not allocate pawns, reserve tiles, or write to RIMAPI.
/// </summary>
public sealed record ResourceRequest(
    [property: JsonPropertyName("kind")]
    ResourceRequestKind Kind,
    [property: JsonPropertyName("request")]
    string What,
    [property: JsonPropertyName("reason")]
    string Why,
    [property: JsonPropertyName("quantity")]
    int? Quantity = null,
    [property: JsonPropertyName("priority")]
    AdvicePriority? Priority = null,
    [property: JsonPropertyName("requested_from")]
    string? RequestedFrom = null,
    [property: JsonPropertyName("work_type")]
    WorkType? WorkType = null,
    [property: JsonPropertyName("skill")]
    string? Skill = null,
    [property: JsonPropertyName("icon")]
    IconRef? Icon = null);

public sealed record IconRef(
    [property: JsonPropertyName("kind")]
    string Kind,
    [property: JsonPropertyName("id")]
    string Id
);

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<ResourceRequestKind>))]
public enum ResourceRequestKind
{
    Labor,
    Tile,
    Item,
    Building,
    Bill,
    StockpileSpace,
    Attention,
    TradeCapacity
}
