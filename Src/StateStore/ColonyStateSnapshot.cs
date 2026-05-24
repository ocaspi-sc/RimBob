using System.Text.Json.Serialization;
using RimBob.Core.Aggregates;

namespace RimBob.State;

public sealed record ColonyStateSnapshot
{
    public const int CurrentSchemaVersion = 2;

    [JsonPropertyName("schema_version")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("snapshot_id")]
    public required string SnapshotId { get; init; }

    [JsonPropertyName("captured_at")]
    public required DateTimeOffset CapturedAt { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("map_id")]
    public required int MapId { get; init; }

    [JsonPropertyName("game_tick")]
    public required long GameTick { get; init; }

    [JsonPropertyName("game_date_raw")]
    public required string GameDateRaw { get; init; }

    /// <summary>
    /// Informational only. RestoreInto uses Versioned.Update, so aggregate
    /// version counters restart from zero in the new Host process.
    /// </summary>
    [JsonPropertyName("aggregate_versions")]
    public required IReadOnlyDictionary<string, long> AggregateVersions { get; init; }

    [JsonPropertyName("map")]
    public required MapInfoSnapshot Map { get; init; }

    [JsonPropertyName("economy")]
    public required EconomyLedger Economy { get; init; }

    [JsonPropertyName("colonists")]
    public required ColonistRegistry Colonists { get; init; }

    [JsonPropertyName("rooms")]
    public required RoomRegistry Rooms { get; init; }

    [JsonPropertyName("stockpiles")]
    public required StockpileLedger Stockpiles { get; init; }

    [JsonPropertyName("buildings")]
    public required BuildingRegistry Buildings { get; init; }

    [JsonPropertyName("work_tables")]
    public required WorkTableRegistry WorkTables { get; init; }

    [JsonPropertyName("power")]
    public required PowerNetwork Power { get; init; }

    [JsonPropertyName("threats")]
    public required ThreatBoard Threats { get; init; }

    [JsonPropertyName("weather")]
    public required WeatherSnapshot Weather { get; init; }

    [JsonPropertyName("farm")]
    public required FarmSnapshot Farm { get; init; }

    [JsonPropertyName("plants")]
    public required PlantRegistry Plants { get; init; }

    [JsonPropertyName("things")]
    public required ThingRegistry Things { get; init; }

    [JsonPropertyName("thing_defs")]
    public required ThingDefRegistry ThingDefs { get; init; }

    [JsonPropertyName("animal_defs")]
    public AnimalDefRegistry AnimalDefs { get; init; } = AggregateDefaults.AnimalDefs;

    [JsonPropertyName("terrain")]
    public required TerrainSnapshot Terrain { get; init; }

    [JsonPropertyName("stored_resources")]
    public required StoredResourceRegistry StoredResources { get; init; }

    [JsonPropertyName("animals")]
    public required AnimalRegistry Animals { get; init; }

    [JsonPropertyName("resources")]
    public required ResourceSummary Resources { get; init; }

    [JsonPropertyName("research")]
    public required ResearchInfo Research { get; init; }
}

public sealed record ColonySnapshotStatus(
    string? Path,
    bool HasSnapshot,
    string? SnapshotId,
    DateTimeOffset? CapturedAt,
    TimeSpan? Age,
    long? GameTick,
    int? MapId,
    string? Source,
    int? SchemaVersion,
    DateTimeOffset? LastSaveAt,
    string? LastSaveError,
    string? LoadError);
