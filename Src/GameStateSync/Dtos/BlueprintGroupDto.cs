using System.Text.Json.Serialization;

namespace RimBob.Ingestion.Dtos;

public sealed record BlueprintGroupValidateRequestDto(
    [property: JsonPropertyName("map_id")]
    int MapId,
    [property: JsonPropertyName("items")]
    IReadOnlyList<BlueprintGroupItemDto> Items);

public sealed record BlueprintGroupItemDto(
    [property: JsonPropertyName("role")]
    string? Role,
    [property: JsonPropertyName("def_name")]
    string DefName,
    [property: JsonPropertyName("stuff_def_name")]
    string? StuffDefName,
    [property: JsonPropertyName("cell")]
    MapCellDto Cell,
    [property: JsonPropertyName("rotation")]
    int Rotation);

public sealed record BlueprintGroupValidateResponseDto(
    [property: JsonPropertyName("can_place_all")]
    bool CanPlaceAll,
    [property: JsonPropertyName("items")]
    IReadOnlyList<BlueprintGroupValidateItemResultDto> Items,
    [property: JsonPropertyName("cost")]
    IReadOnlyList<BlueprintCostDto> Cost,
    [property: JsonPropertyName("overlap_conflicts")]
    IReadOnlyList<BlueprintGroupOverlapConflictDto> OverlapConflicts);

public sealed record BlueprintGroupValidateItemResultDto(
    [property: JsonPropertyName("index")]
    int Index,
    [property: JsonPropertyName("item")]
    BlueprintGroupItemDto Item,
    [property: JsonPropertyName("can_place")]
    bool CanPlace,
    [property: JsonPropertyName("reason")]
    string? Reason,
    [property: JsonPropertyName("def_type")]
    string? DefType,
    [property: JsonPropertyName("occupies_cells")]
    IReadOnlyList<MapCellDto> OccupiesCells,
    [property: JsonPropertyName("cost")]
    IReadOnlyList<BlueprintCostDto> Cost,
    [property: JsonPropertyName("work_to_build")]
    float WorkToBuild,
    [property: JsonPropertyName("already_blueprinted")]
    bool AlreadyBlueprinted,
    [property: JsonPropertyName("already_built")]
    bool AlreadyBuilt);

public sealed record BlueprintCostDto(
    [property: JsonPropertyName("def_name")]
    string DefName,
    [property: JsonPropertyName("count")]
    int Count);

public sealed record BlueprintGroupOverlapConflictDto(
    [property: JsonPropertyName("cell")]
    MapCellDto Cell,
    [property: JsonPropertyName("first_item_index")]
    int FirstItemIndex,
    [property: JsonPropertyName("second_item_index")]
    int SecondItemIndex);

// TODO: Add BlueprintGroupPlaceResultDto and its require_all / placement_order request fields when the Apply path lands.
