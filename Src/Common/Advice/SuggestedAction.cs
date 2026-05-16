using System.Text.Json.Serialization;

namespace RimAI.Core.Advice;

/// <summary>
/// A player-facing recommendation. In MVP this is rendered only; Auto wiring is
/// deferred until a minister graduates a specific advice type.
/// </summary>
public sealed record SuggestedAction(
    [property: JsonPropertyName("kind")]
    SuggestedActionKind Kind,
    [property: JsonPropertyName("instruction")]
    string What,
    [property: JsonPropertyName("icon")]
    IconRef? Icon = null);

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<SuggestedActionKind>))]
public enum SuggestedActionKind
{
    DesignateZone,
    MarkHarvest,
    MarkHunt,
    PlaceBlueprint,
    ProductionBill,
    SetPriority,
    SetSchedule,
    SetStockpileZone,
    Draft,
    Forbid,
    Unforbid,
    Research,
    Trade,
    Note
}
