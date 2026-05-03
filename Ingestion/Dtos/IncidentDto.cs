using System.Text.Json.Serialization;

namespace RimAI.Ingestion.Dtos;

// ── GET /incidents?map_id ─────────────────────────────────────────────────────
public record IncidentDto(
    [property: JsonPropertyName("def")]         string Def,
    [property: JsonPropertyName("days_since")]  float DaysSince,
    [property: JsonPropertyName("label")]       string? Label
);

// ── GET /lords?map_id ─────────────────────────────────────────────────────────
// Lords = active AI groups (raids, caravans, sieges). Presence of any lord
// with a hostile faction is the primary raid-detection signal for Defense.
// TODO: confirm pawn_ids field name and whether threat_points is included.
public record LordDto(
    [property: JsonPropertyName("id")]           string Id,
    [property: JsonPropertyName("job_type")]     string JobType,   // Raid | Siege | Caravan | etc.
    [property: JsonPropertyName("faction_id")]   string? FactionId,
    [property: JsonPropertyName("pawn_ids")]     IReadOnlyList<string>? PawnIds,
    [property: JsonPropertyName("threat_points")] float? ThreatPoints
);

// ── GET /quests?map_id ────────────────────────────────────────────────────────
// TODO: quest field list not cached — flesh out when CoS/Mayor need quest awareness.
public record QuestDto(
    [property: JsonPropertyName("id")]      string Id,
    [property: JsonPropertyName("label")]   string Label,
    [property: JsonPropertyName("state")]   string State    // Active | Historical | etc.
);
