using System.Text.Json.Serialization;

namespace RimAI.Ingestion.Dtos;

// ── GET /map/farm/summary?map_id ──────────────────────────────────────────────
public record FarmSummaryDto(
    [property: JsonPropertyName("total_crops")]   int TotalCrops,
    [property: JsonPropertyName("avg_growth")]    float AvgGrowth,    // 0–1
    [property: JsonPropertyName("ready_to_harvest")] int ReadyToHarvest,
    [property: JsonPropertyName("crop_breakdown")] IReadOnlyList<CropBreakdownDto>? CropBreakdown
);

public record CropBreakdownDto(
    [property: JsonPropertyName("def")]      string Def,
    [property: JsonPropertyName("count")]    int Count,
    [property: JsonPropertyName("avg_growth")] float AvgGrowth
);

// ── GET /map/plants?map_id ────────────────────────────────────────────────────
public record PlantDto(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("def")]         string Def,
    [property: JsonPropertyName("growth")]      float Growth,       // 0–1
    [property: JsonPropertyName("position")]    PositionDto? Position,
    [property: JsonPropertyName("is_crop")]     bool IsCrop,
    [property: JsonPropertyName("zone_id")]     string? ZoneId
);

// ── GET /map/animals?map_id ───────────────────────────────────────────────────
// TODO: confirm exact field names against live RIMAPI for tame vs wild flag.
public record AnimalDto(
    [property: JsonPropertyName("id")]      string Id,
    [property: JsonPropertyName("def")]     string Def,
    [property: JsonPropertyName("name")]    string? Name,
    [property: JsonPropertyName("tame")]    bool Tame,
    [property: JsonPropertyName("health")]  float Health,
    [property: JsonPropertyName("position")] PositionDto? Position
);

// ── GET /map/zones?map_id ─────────────────────────────────────────────────────
// TODO: zone response shape not fully verified — check live RIMAPI for stockpile
//       content fields (item list, nutrition totals) vs. needing /map/things instead.
public record ZoneDto(
    [property: JsonPropertyName("id")]      string Id,
    [property: JsonPropertyName("type")]    string Type,        // GrowingZone | StockpileZone | etc.
    [property: JsonPropertyName("label")]   string? Label,
    [property: JsonPropertyName("cells")]   IReadOnlyList<PositionDto>? Cells,
    [property: JsonPropertyName("plant_def")] string? PlantDef // GrowingZone only
);

// ── GET /map/buildings?map_id ─────────────────────────────────────────────────
// TODO: full building field list not cached — expand when Construction minister begins.
public record BuildingDto(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("def")]         string Def,
    [property: JsonPropertyName("position")]    PositionDto? Position,
    [property: JsonPropertyName("hp")]          float Hp,          // 0–1
    [property: JsonPropertyName("power_on")]    bool? PowerOn,
    [property: JsonPropertyName("is_working")]  bool? IsWorking
);

// ── GET /map/power/info?map_id ────────────────────────────────────────────────
public record PowerInfoDto(
    [property: JsonPropertyName("production")]  float Production,  // W
    [property: JsonPropertyName("consumption")] float Consumption, // W
    [property: JsonPropertyName("stored")]      float Stored,      // Wd
    [property: JsonPropertyName("capacity")]    float Capacity     // Wd
);

// ── GET /map/weather?map_id ───────────────────────────────────────────────────
public record WeatherDto(
    [property: JsonPropertyName("def")]         string Def,
    [property: JsonPropertyName("temperature")] float Temperature, // °C
    [property: JsonPropertyName("rain_rate")]   float RainRate
);

// ── GET /map/creatures/summary?map_id ────────────────────────────────────────
public record CreaturesSummaryDto(
    [property: JsonPropertyName("colonists")]   int Colonists,
    [property: JsonPropertyName("enemies")]     int Enemies,
    [property: JsonPropertyName("animals")]     int Animals,
    [property: JsonPropertyName("prisoners")]   int Prisoners
);

// ── GET /map/rooms?map_id ─────────────────────────────────────────────────────
// TODO: room fields not fully cached — needed for Welfare (bedroom impressiveness).
public record RoomDto(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("role")]        string Role,
    [property: JsonPropertyName("temperature")] float Temperature,
    [property: JsonPropertyName("beds")]        int Beds,
    [property: JsonPropertyName("impressiveness")] float? Impressiveness
);
