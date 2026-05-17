using System.Text.Json.Serialization;
using RimBob.Core.Aggregates;

namespace RimBob.Core.Advice;

/// <summary>
/// One player-facing action in an AdviceItem. Most actions are rendered only; a
/// narrow Assisted Apply allowlist can attach server-owned apply metadata.
/// </summary>
public sealed record AdviceAction(
    [property: JsonPropertyName("kind")]
    AdviceActionKind Kind,
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
    AdviceActionApply? Apply = null);

public sealed record AdviceActionApply(
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
    IReadOnlyList<AdviceThingApplyTarget>? ThingTargets = null,
    [property: JsonPropertyName("workbench_building_id")]
    string? WorkbenchBuildingId = null,
    [property: JsonPropertyName("recipe_selector_key")]
    string? RecipeSelectorKey = null,
    [property: JsonPropertyName("repeat_mode")]
    string? RepeatMode = null);

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
    MarkHuntArea,
    UnforbidThings,
    UpsertProductionBill
}

public static class AssistedApplyLimits
{
    public const int MaxHarvestTargets = 80;
    public const int MaxHarvestRectArea = 120;
    public const int MaxHuntTargets = 12;
    public const int MaxHuntRectArea = 120;
    public const int MaxUnforbidTargets = 50;
    public const int MaxProductionBillTarget = 50;
    public const double MaxMissingTargetFraction = 0.25d;
}

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<AdviceActionKind>))]
public enum AdviceActionKind
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
