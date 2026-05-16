using System.Text.Json.Serialization;

namespace RimAI.Core.Advice;

/// <summary>
/// One player-facing step in an AdviceItem. In MVP this is rendered only; Auto
/// wiring is deferred until a minister graduates a specific advice type.
/// </summary>
public sealed record AdviceStep(
    [property: JsonPropertyName("kind")]
    AdviceStepKind Kind,
    [property: JsonPropertyName("instruction")]
    string Instruction,
    [property: JsonPropertyName("quantity")]
    int? Quantity = null,
    [property: JsonPropertyName("owner")]
    string? Owner = null,
    [property: JsonPropertyName("work_type")]
    WorkType? WorkType = null,
    [property: JsonPropertyName("skill")]
    string? Skill = null,
    [property: JsonPropertyName("reason")]
    string? Reason = null,
    [property: JsonPropertyName("icon")]
    IconRef? Icon = null);

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<AdviceStepKind>))]
public enum AdviceStepKind
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
    RequestResource,
    Note
}
