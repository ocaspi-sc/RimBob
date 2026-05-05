using System.Text.Json.Serialization;

namespace RimAI.Ingestion.Dtos;

// ── GET /api/v1/map/pawns?map_id ──────────────────────────────────────────────
// Basic pawn list — verified against live RIMAPI.
public record MapPawnDto(
    [property: JsonPropertyName("id")]       int Id,
    [property: JsonPropertyName("name")]     string Name,
    [property: JsonPropertyName("gender")]   string? Gender,
    [property: JsonPropertyName("age")]      int Age,
    [property: JsonPropertyName("health")]   float Health,     // 0–1
    [property: JsonPropertyName("mood")]     float Mood,       // 0–1
    [property: JsonPropertyName("hunger")]   float Hunger,     // 0–1 (1 = full)
    [property: JsonPropertyName("position")] PositionDto? Position
);

// ── GET /api/v2/colonists/detailed?map_id ─────────────────────────────────────
// Full bio + needs + skills + health per colonist (v2 controller).
// TODO: field list is not fully cached — verify against live RIMAPI and expand.
public record ColonistDetailedDto(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("name")]        string Name,
    [property: JsonPropertyName("age")]         int Age,
    [property: JsonPropertyName("gender")]      string Gender,
    [property: JsonPropertyName("health")]      float Health,
    [property: JsonPropertyName("mood")]        float Mood,
    [property: JsonPropertyName("hunger")]      float Hunger,
    [property: JsonPropertyName("skills")]      IReadOnlyList<SkillDto>? Skills,
    [property: JsonPropertyName("traits")]      IReadOnlyList<string>? Traits,
    [property: JsonPropertyName("current_job")] string? CurrentJob,
    [property: JsonPropertyName("position")]    PositionDto? Position
);

public record SkillDto(
    [property: JsonPropertyName("def")]     string Def,       // "Cooking", "Plants", etc.
    [property: JsonPropertyName("level")]   int Level,        // 0–20
    [property: JsonPropertyName("passion")] string Passion    // None | Minor | Major
);

// ── Shared ────────────────────────────────────────────────────────────────────
public record PositionDto(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("z")] int Z
);
