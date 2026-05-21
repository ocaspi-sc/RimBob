using System.Text.Json.Serialization;

namespace RimBob.Ingestion.Dtos;

// ── GET /api/v1/game/state ────────────────────────────────────────────────────
// Verified against live RIMAPI (RedEyeDev fork, Mono-HTTPAPI/1.0).
public record GameStateDto(
    [property: JsonPropertyName("game_tick")]      long Tick,
    [property: JsonPropertyName("colony_wealth")]  float Wealth,
    [property: JsonPropertyName("colonist_count")] int ColonistCount,
    [property: JsonPropertyName("storyteller")]    string Storyteller,
    [property: JsonPropertyName("is_paused")]      bool Paused,
    [property: JsonPropertyName("program_state")]  string ProgramState,  // "Playing" | "Entry" | ...
    [property: JsonPropertyName("map_count")]      int MapCount
);

// ── GET /api/v1/maps ──────────────────────────────────────────────────────────
// List of currently-loaded maps. The handshake picks the one with is_player_home=true.
public record MapInfoDto(
    [property: JsonPropertyName("id")]              int Id,
    [property: JsonPropertyName("index")]           int Index,
    [property: JsonPropertyName("is_player_home")]  bool IsPlayerHome,
    [property: JsonPropertyName("is_pocket_map")]   bool IsPocketMap,
    [property: JsonPropertyName("faction_id")]      string? FactionId,
    [property: JsonPropertyName("seed")]            long Seed,
    [property: JsonPropertyName("size")]            string? Size
);

// ── GET /api/v1/datetime ──────────────────────────────────────────────────────
// Live RIMAPI returns a single human-readable string like "1st of Aprimay, 5500, 6h".
// Structured DateStamp parsing lives in StateStore.Parsing.RimDateParser.
public record DateTimeDto(
    [property: JsonPropertyName("datetime")] string DateTime
);
