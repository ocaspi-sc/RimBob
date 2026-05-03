using System.Text.Json.Serialization;

namespace RimAI.Ingestion.Dtos;

// ── GET /game/state ───────────────────────────────────────────────────────────
public record GameStateDto(
    [property: JsonPropertyName("tick")]             long Tick,
    [property: JsonPropertyName("wealth")]           float Wealth,
    [property: JsonPropertyName("colonist_count")]   int ColonistCount,
    [property: JsonPropertyName("storyteller")]      string Storyteller,
    [property: JsonPropertyName("paused")]           bool Paused,
    [property: JsonPropertyName("map_id")]           int MapId = 0
);

// ── GET /datetime ─────────────────────────────────────────────────────────────
public record DateTimeDto(
    [property: JsonPropertyName("year")]     int Year,
    [property: JsonPropertyName("quadrum")]  string Quadrum,   // Aprimay | Jugust | Septober | Decembary
    [property: JsonPropertyName("day")]      int Day,          // 1–15
    [property: JsonPropertyName("hour")]     float Hour,
    [property: JsonPropertyName("season")]   string Season
);
