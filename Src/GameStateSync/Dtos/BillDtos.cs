using System.Text.Json.Serialization;

namespace RimBob.Ingestion.Dtos;

public sealed record WorkTableRecipeDto(
    [property: JsonPropertyName("def_name")]
    string? DefName,
    [property: JsonPropertyName("label")]
    string? Label,
    [property: JsonPropertyName("description")]
    string? Description,
    [property: JsonPropertyName("work_amount")]
    float WorkAmount,
    [property: JsonPropertyName("work_skill")]
    string? WorkSkill,
    [property: JsonPropertyName("products")]
    IReadOnlyList<RecipeProductDto>? Products,
    [property: JsonPropertyName("ingredients")]
    IReadOnlyList<BillRecipeIngredientDto>? Ingredients);

public sealed record RecipeProductDto(
    [property: JsonPropertyName("thing_def")]
    string? ThingDef,
    [property: JsonPropertyName("count")]
    int Count);

public sealed record BillRecipeIngredientDto(
    [property: JsonPropertyName("filter_label")]
    string? FilterLabel,
    [property: JsonPropertyName("count")]
    float Count);

public sealed record WorkTableBillDto(
    [property: JsonPropertyName("load_id")]
    int LoadId,
    [property: JsonPropertyName("recipe_def_name")]
    string? RecipeDefName,
    [property: JsonPropertyName("recipe_label")]
    string? RecipeLabel,
    [property: JsonPropertyName("suspended")]
    bool Suspended,
    [property: JsonPropertyName("paused")]
    bool Paused,
    [property: JsonPropertyName("repeat_mode")]
    string? RepeatMode,
    [property: JsonPropertyName("repeat_count")]
    int RepeatCount,
    [property: JsonPropertyName("target_count")]
    int TargetCount);
