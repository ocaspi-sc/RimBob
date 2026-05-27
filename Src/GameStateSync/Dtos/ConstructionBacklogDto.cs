using System.Text.Json.Serialization;

namespace RimBob.Ingestion.Dtos;

public sealed record ConstructionBacklogGroupDto
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("def_name")]
    public string? DefName { get; init; }

    [JsonPropertyName("stuff_def_name")]
    public string? StuffDefName { get; init; }

    [JsonPropertyName("allowed")]
    public bool Allowed { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("thing_ids")]
    public IReadOnlyList<int> ThingIds { get; init; } = [];

    [JsonPropertyName("sample_cells")]
    public IReadOnlyList<MapCellDto> SampleCells { get; init; } = [];

    [JsonPropertyName("total_work_left")]
    public float TotalWorkLeft { get; init; }

    [JsonPropertyName("cost")]
    public IReadOnlyList<ConstructionMaterialCountDto> Cost { get; init; } = [];

    [JsonPropertyName("materials_available")]
    public IReadOnlyList<ConstructionMaterialAvailabilityDto> MaterialsAvailable { get; init; } = [];

    [JsonPropertyName("materials_missing")]
    public IReadOnlyList<ConstructionMaterialAvailabilityDto> MaterialsMissing { get; init; } = [];

    [JsonPropertyName("blocked_count")]
    public int BlockedCount { get; init; }

    [JsonPropertyName("disallowed_count")]
    public int DisallowedCount { get; init; }
}

public sealed record ConstructionMaterialCountDto
{
    [JsonPropertyName("def_name")]
    public string? DefName { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }
}

public sealed record ConstructionMaterialAvailabilityDto
{
    [JsonPropertyName("def_name")]
    public string? DefName { get; init; }

    [JsonPropertyName("required")]
    public int Required { get; init; }

    [JsonPropertyName("available")]
    public int Available { get; init; }

    [JsonPropertyName("missing")]
    public int Missing { get; init; }
}
