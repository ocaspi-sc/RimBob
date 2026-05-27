using System.Text.Json.Serialization;

namespace RimBob.Ingestion.Dtos;

public sealed record MapCellDto(
    [property: JsonPropertyName("x")]
    int X,
    [property: JsonPropertyName("z")]
    int Z);

public sealed record MapReachResponseDto(
    [property: JsonPropertyName("can_reach")]
    bool CanReach,
    [property: JsonPropertyName("from")]
    MapCellDto From,
    [property: JsonPropertyName("to")]
    MapCellDto To,
    [property: JsonPropertyName("mode")]
    string Mode,
    [property: JsonPropertyName("pe_mode")]
    string PeMode);

public sealed record MapPathCostRequestDto(
    [property: JsonPropertyName("map_id")]
    int MapId,
    [property: JsonPropertyName("from")]
    MapCellDto From,
    [property: JsonPropertyName("to")]
    MapCellDto To,
    [property: JsonPropertyName("tier")]
    string Tier = "region",
    [property: JsonPropertyName("mode")]
    string Mode = "pass_doors",
    [property: JsonPropertyName("pe_mode")]
    string PeMode = "on_cell",
    [property: JsonPropertyName("max_cost")]
    int? MaxCost = null);

public sealed record MapPathCostPairRequestDto(
    [property: JsonPropertyName("from")]
    MapCellDto From,
    [property: JsonPropertyName("to")]
    MapCellDto To);

public sealed record MapPathCostBatchRequestDto(
    [property: JsonPropertyName("map_id")]
    int MapId,
    [property: JsonPropertyName("pairs")]
    IReadOnlyList<MapPathCostPairRequestDto> Pairs,
    [property: JsonPropertyName("tier")]
    string Tier = "region",
    [property: JsonPropertyName("mode")]
    string Mode = "pass_doors",
    [property: JsonPropertyName("pe_mode")]
    string PeMode = "on_cell",
    [property: JsonPropertyName("max_cost")]
    int? MaxCost = null);

public sealed record MapPathCostResultDto(
    [property: JsonPropertyName("reachable")]
    bool Reachable,
    [property: JsonPropertyName("cost")]
    int Cost,
    [property: JsonPropertyName("from")]
    MapCellDto From,
    [property: JsonPropertyName("to")]
    MapCellDto To);

public sealed record MapPathCostResponseDto(
    [property: JsonPropertyName("reachable")]
    bool Reachable,
    [property: JsonPropertyName("cost")]
    int Cost,
    [property: JsonPropertyName("from")]
    MapCellDto From,
    [property: JsonPropertyName("to")]
    MapCellDto To,
    [property: JsonPropertyName("tier")]
    string Tier);

public sealed record MapPathCostBatchResponseDto(
    [property: JsonPropertyName("results")]
    IReadOnlyList<MapPathCostResultDto> Results);
