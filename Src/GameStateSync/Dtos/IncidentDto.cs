using System.Text.Json.Serialization;

namespace RimBob.Ingestion.Dtos;

// ── GET /incidents?map_id ─────────────────────────────────────────────────────
public record IncidentDto(
    [property: JsonPropertyName("incident_def")] string Def,
    [property: JsonPropertyName("days_since_occurred")] float DaysSince,
    [property: JsonPropertyName("label")]       string? Label
);

// ── GET /lords?map_id ─────────────────────────────────────────────────────────
// Lords = active AI groups (raids, caravans, sieges). Presence of any lord
// with a hostile faction is the primary raid-detection signal for Defense.
public sealed record LordDto
{
    [JsonIgnore]
    public string Id => ExplicitId ?? LoadId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [JsonIgnore]
    public string? JobType => LordJobType;

    [JsonIgnore]
    public string? FactionId => FactionDefName;

    [JsonIgnore]
    public IReadOnlyList<string>? PawnIds => OwnedPawnIds;

    [JsonPropertyName("load_id")]
    public int LoadId { get; init; }

    [JsonPropertyName("lord_job_type")]
    public string? LordJobType { get; init; }

    [JsonPropertyName("faction_def_name")]
    public string? FactionDefName { get; init; }

    [JsonPropertyName("owned_pawn_ids")]
    public IReadOnlyList<string>? OwnedPawnIds { get; init; }

    [JsonPropertyName("threat_points")]
    public float? ThreatPoints { get; init; }

    [JsonIgnore]
    private string? ExplicitId { get; init; }

    public LordDto()
    {
    }

    public LordDto(
        string id,
        string? jobType,
        string? factionId,
        IReadOnlyList<string>? pawnIds,
        float? threatPoints)
    {
        ExplicitId = id;
        LordJobType = jobType;
        FactionDefName = factionId;
        OwnedPawnIds = pawnIds;
        ThreatPoints = threatPoints;
    }
}

// ── GET /quests?map_id ────────────────────────────────────────────────────────
// TODO: quest field list not cached — flesh out when CoS/Mayor need quest awareness.
public record QuestDto(
    [property: JsonPropertyName("id")]
    [property: JsonConverter(typeof(FlexibleStringIdJsonConverter))] string Id,
    [property: JsonPropertyName("label")]   string Label,
    [property: JsonPropertyName("state")]   string State    // Active | Historical | etc.
);
