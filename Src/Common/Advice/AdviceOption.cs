using System.Text.Json.Serialization;

namespace RimBob.Core.Advice;

public sealed record AdviceOption(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("label")]
    string Label,
    [property: JsonPropertyName("summary")]
    string Summary,
    [property: JsonPropertyName("blueprint_group")]
    BlueprintGroup BlueprintGroup,
    [property: JsonPropertyName("est_materials")]
    IReadOnlyList<MaterialEstimate> EstimatedMaterials,
    [property: JsonPropertyName("tradeoff_note")]
    string? TradeoffNote = null,
    [property: JsonPropertyName("readiness")]
    AdviceOptionReadiness? Readiness = null);

public sealed record AdviceOptionReadiness(
    [property: JsonPropertyName("draftable")]
    string Draftable,
    [property: JsonPropertyName("placement_valid")]
    string PlacementValid,
    [property: JsonPropertyName("materials_ready")]
    string MaterialsReady,
    [property: JsonPropertyName("apply_ready")]
    string ApplyReady);

public sealed record BlueprintGroup(
    [property: JsonPropertyName("label")]
    string Label,
    [property: JsonPropertyName("map_id")]
    int MapId,
    [property: JsonPropertyName("assets")]
    IReadOnlyList<BlueprintAsset> Assets);

public sealed record BlueprintAsset(
    [property: JsonPropertyName("role")]
    string Role,
    [property: JsonPropertyName("def_name")]
    string DefName,
    [property: JsonPropertyName("stuff_def_name")]
    string? StuffDefName,
    [property: JsonPropertyName("cell")]
    MapCell Cell,
    [property: JsonPropertyName("rotation")]
    int Rotation);

public sealed record MapCell(
    [property: JsonPropertyName("x")]
    int X,
    [property: JsonPropertyName("z")]
    int Z);

public sealed record MaterialEstimate(
    [property: JsonPropertyName("def_name")]
    string DefName,
    [property: JsonPropertyName("count")]
    int Count);
