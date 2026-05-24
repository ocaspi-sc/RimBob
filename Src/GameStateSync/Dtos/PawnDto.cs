using System.Text.Json.Serialization;

namespace RimBob.Ingestion.Dtos;

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
// Full bio + needs + skills + medical per colonist (v2 controller).
// Wire shape verified against live RIMAPI 1.9.0 — basic identity sits under
// `pawn`, everything else under `detailes` (yes, the upstream spelling).
public record ColonistDetailedDto(
    [property: JsonPropertyName("pawn")]     ColonistBasicDto?   Pawn,
    [property: JsonPropertyName("detailes")] ColonistDetailsDto? Detailes
);

public record ColonistBasicDto(
    [property: JsonPropertyName("id")]       int Id,
    [property: JsonPropertyName("name")]     string? Name,
    [property: JsonPropertyName("gender")]   string? Gender,
    [property: JsonPropertyName("age")]      int Age,
    [property: JsonPropertyName("health")]   float Health,
    [property: JsonPropertyName("mood")]     float Mood,
    [property: JsonPropertyName("hunger")]   float Hunger,
    [property: JsonPropertyName("position")] PositionDto? Position
);

public record ColonistDetailsDto(
    [property: JsonPropertyName("work_info")]     PawnWorkInfoDto?    WorkInfo,
    [property: JsonPropertyName("medical_info")]  PawnMedicalInfoDto? MedicalInfo,
    [property: JsonPropertyName("sleep")]         float Sleep = 0f,
    [property: JsonPropertyName("comfort")]       float Comfort = 0f,
    [property: JsonPropertyName("beauty")]        float Beauty = 0f,
    [property: JsonPropertyName("joy")]           float Joy = 0f,
    [property: JsonPropertyName("fresh_air")]     float FreshAir = 0f,
    [property: JsonPropertyName("drugs_desire")]  float DrugsDesire = 0f,
    [property: JsonPropertyName("mood_thoughts")] IReadOnlyList<MoodThoughtDto>? MoodThoughts = null
);

public record MoodThoughtDto(
    [property: JsonPropertyName("def_name")]     string DefName,
    [property: JsonPropertyName("label")]        string? Label,
    [property: JsonPropertyName("mood_offset")]  float MoodOffset,
    [property: JsonPropertyName("stage_index")]  int StageIndex
);

public record PawnWorkInfoDto(
    [property: JsonPropertyName("skills")]      IReadOnlyList<SkillDto>? Skills,
    [property: JsonPropertyName("current_job")] string?                  CurrentJob,
    [property: JsonPropertyName("traits")]      IReadOnlyList<TraitDto>? Traits
);

public record PawnMedicalInfoDto(
    [property: JsonPropertyName("is_dead")]   bool IsDead,
    [property: JsonPropertyName("is_downed")] bool IsDowned,
    [property: JsonPropertyName("hediffs")]   IReadOnlyList<HediffDto>? Hediffs
);

public record SkillDto(
    [property: JsonPropertyName("name")]    string Name,    // "Cooking", "Plants", etc.
    [property: JsonPropertyName("level")]   int    Level,   // 0–20
    [property: JsonPropertyName("passion")] int    Passion  // 0=None, 1=Minor, 2=Major
);

public record TraitDto(
    [property: JsonPropertyName("name")]  string  Name,
    [property: JsonPropertyName("label")] string? Label
);

public record HediffDto(
    [property: JsonPropertyName("def_name")]                       string  DefName,
    [property: JsonPropertyName("label")]                          string? Label,
    [property: JsonPropertyName("severity")]                       float   Severity,
    [property: JsonPropertyName("bleeding")]                       bool    Bleeding,
    [property: JsonPropertyName("is_lethal")]                      bool    IsLethal,
    [property: JsonPropertyName("is_currently_life_threatening")]  bool    IsCurrentlyLifeThreatening,
    [property: JsonPropertyName("tendable_now")]                   bool    TendableNow
);

// ── Shared ────────────────────────────────────────────────────────────────────
public record PositionDto(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("z")] int Z
);
