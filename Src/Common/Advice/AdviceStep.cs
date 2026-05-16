using System.Text.Json.Serialization;
using RimAI.Core.Aggregates;

namespace RimAI.Core.Advice;

/// <summary>
/// One player-facing step in an AdviceItem. Most steps are rendered only; a
/// narrow Assisted Apply allowlist can attach server-owned apply metadata.
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
    IconRef? Icon = null,
    [property: JsonPropertyName("apply")]
    AdviceStepApply? Apply = null);

public sealed record AdviceStepApply(
    [property: JsonPropertyName("kind")]
    AdviceApplyKind Kind,
    [property: JsonPropertyName("label")]
    string Label,
    [property: JsonPropertyName("target_summary")]
    string TargetSummary,
    [property: JsonPropertyName("map_id")]
    int MapId,
    [property: JsonPropertyName("target_count")]
    int TargetCount,
    [property: JsonPropertyName("rect")]
    MapRect? Rect = null,
    [property: JsonPropertyName("target_ids")]
    IReadOnlyList<string>? TargetIds = null,
    [property: JsonPropertyName("thing_ids")]
    IReadOnlyList<string>? ThingIds = null,
    [property: JsonPropertyName("thing_targets")]
    IReadOnlyList<AdviceThingApplyTarget>? ThingTargets = null);

public sealed record AdviceThingApplyTarget(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("def")]
    string Def,
    [property: JsonPropertyName("kind")]
    string Kind,
    [property: JsonPropertyName("source")]
    string Source,
    [property: JsonPropertyName("position")]
    MapPosition Position);

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<AdviceApplyKind>))]
public enum AdviceApplyKind
{
    MarkHarvestArea,
    UnforbidThings
}

public static class AssistedApplyLimits
{
    public const int MaxHarvestTargets = 80;
    public const int MaxHarvestRectArea = 120;
    public const int MaxUnforbidTargets = 50;
    public const double MaxMissingTargetFraction = 0.25d;
}

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
