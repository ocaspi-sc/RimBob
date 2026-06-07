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
    [property: JsonPropertyName("apply")]
    AdviceActionApply? Apply = null,
    [property: JsonPropertyName("apply_result")]
    AdviceActionApplyResult? ApplyResult = null);

public sealed record AdviceActionApplyResult(
    [property: JsonPropertyName("status")]
    string Status,
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("kind")]
    AdviceApplyKind? Kind,
    [property: JsonPropertyName("recorded_at")]
    DateTimeOffset RecordedAt);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MarkHarvestAreaApply), "mark_harvest_area")]
[JsonDerivedType(typeof(MarkHuntAreaApply), "mark_hunt_area")]
[JsonDerivedType(typeof(UnforbidThingsApply), "unforbid_things")]
[JsonDerivedType(typeof(UpsertProductionBillApply), "upsert_production_bill")]
[JsonDerivedType(typeof(PlaceBlueprintGroupApply), "place_blueprint_group")]
[JsonDerivedType(typeof(CreateGrowingZoneApply), "create_growing_zone")]
public abstract record AdviceActionApply(
    [property: JsonPropertyName("label")]
    string Label,
    [property: JsonPropertyName("target_summary")]
    string TargetSummary,
    [property: JsonPropertyName("map_id")]
    int MapId)
{
    [JsonIgnore]
    public abstract AdviceApplyKind Kind { get; }
}

public sealed record MarkHarvestAreaApply(
    string Label,
    string TargetSummary,
    int MapId,
    [property: JsonPropertyName("rect")]
    MapRect Rect,
    [property: JsonPropertyName("target_ids")]
    IReadOnlyList<string> TargetIds,
    [property: JsonPropertyName("target_count")]
    int TargetCount)
    : AdviceActionApply(Label, TargetSummary, MapId)
{
    [JsonIgnore]
    public override AdviceApplyKind Kind => AdviceApplyKind.MarkHarvestArea;
}

public sealed record MarkHuntAreaApply(
    string Label,
    string TargetSummary,
    int MapId,
    [property: JsonPropertyName("rect")]
    MapRect Rect,
    [property: JsonPropertyName("target_ids")]
    IReadOnlyList<string> TargetIds,
    [property: JsonPropertyName("target_count")]
    int TargetCount)
    : AdviceActionApply(Label, TargetSummary, MapId)
{
    [JsonIgnore]
    public override AdviceApplyKind Kind => AdviceApplyKind.MarkHuntArea;
}

public sealed record UnforbidThingsApply(
    string Label,
    string TargetSummary,
    int MapId,
    [property: JsonPropertyName("thing_ids")]
    IReadOnlyList<string> ThingIds,
    [property: JsonPropertyName("thing_targets")]
    IReadOnlyList<AdviceThingApplyTarget> ThingTargets,
    [property: JsonPropertyName("target_count")]
    int TargetCount)
    : AdviceActionApply(Label, TargetSummary, MapId)
{
    [JsonIgnore]
    public override AdviceApplyKind Kind => AdviceApplyKind.UnforbidThings;
}

public sealed record UpsertProductionBillApply(
    string Label,
    string TargetSummary,
    int MapId,
    [property: JsonPropertyName("workbench_building_id")]
    string WorkbenchBuildingId,
    [property: JsonPropertyName("recipe_selector_key")]
    string RecipeSelectorKey,
    [property: JsonPropertyName("repeat_mode")]
    string RepeatMode,
    [property: JsonPropertyName("target_count")]
    int TargetCount)
    : AdviceActionApply(Label, TargetSummary, MapId)
{
    [JsonIgnore]
    public override AdviceApplyKind Kind => AdviceApplyKind.UpsertProductionBill;
}

public sealed record PlaceBlueprintGroupApply(
    string Label,
    string TargetSummary,
    int MapId,
    [property: JsonPropertyName("blueprint_group")]
    BlueprintGroup BlueprintGroup,
    [property: JsonPropertyName("asset_count")]
    int AssetCount)
    : AdviceActionApply(Label, TargetSummary, MapId)
{
    [JsonIgnore]
    public override AdviceApplyKind Kind => AdviceApplyKind.PlaceBlueprintGroup;
}

public sealed record CreateGrowingZoneApply(
    string Label,
    string TargetSummary,
    int MapId,
    [property: JsonPropertyName("plant_def")]
    string PlantDef,
    [property: JsonPropertyName("rect")]
    MapRect Rect,
    [property: JsonPropertyName("target_count")]
    int TargetCount)
    : AdviceActionApply(Label, TargetSummary, MapId)
{
    [JsonIgnore]
    public override AdviceApplyKind Kind => AdviceApplyKind.CreateGrowingZone;
}

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
    UpsertProductionBill,
    PlaceBlueprintGroup,
    CreateGrowingZone
}

public static class AssistedApplyLimits
{
    public const int MaxHarvestTargets = 80;
    public const int MaxHarvestRectArea = 120;
    public const int MaxHuntTargets = 12;
    public const int MaxHuntRectArea = 120;
    public const int MaxUnforbidTargets = 50;
    public const int MaxProductionBillTarget = 50;
    public const int MaxBlueprintGroupAssets = 64;
    public const int MaxGrowingZoneCells = 160;
    public const double MaxMissingTargetFraction = 0.25d;
}

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<AdviceActionKind>))]
public enum AdviceActionKind
{
    DesignateZoneReq,
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
