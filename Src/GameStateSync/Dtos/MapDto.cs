using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimBob.Ingestion.Dtos;

// ── GET /map/farm/summary?map_id ──────────────────────────────────────────────
public sealed record FarmSummaryDto
{
    public FarmSummaryDto() { }

    public FarmSummaryDto(
        int totalCrops,
        float avgGrowth,
        int readyToHarvest,
        IReadOnlyList<CropBreakdownDto>? cropBreakdown)
    {
        DocumentedTotalCrops = totalCrops;
        DocumentedAverageGrowth = avgGrowth;
        DocumentedReadyToHarvest = readyToHarvest;
        DocumentedCropBreakdown = cropBreakdown;
    }

    [JsonPropertyName("total_crops")]
    public int? DocumentedTotalCrops { get; init; }

    [JsonPropertyName("total_plants")]
    public int? LiveTotalPlants { get; init; }

    [JsonPropertyName("avg_growth")]
    public float? DocumentedAverageGrowth { get; init; }

    [JsonPropertyName("growth_progress_average")]
    public float? LiveGrowthProgressAverage { get; init; }

    [JsonPropertyName("ready_to_harvest")]
    public int? DocumentedReadyToHarvest { get; init; }

    [JsonPropertyName("crop_breakdown")]
    public IReadOnlyList<CropBreakdownDto>? DocumentedCropBreakdown { get; init; }

    [JsonPropertyName("crop_types")]
    public IReadOnlyList<CropBreakdownDto>? LiveCropTypes { get; init; }

    [JsonIgnore]
    public IReadOnlyList<CropBreakdownDto> CropBreakdown => DocumentedCropBreakdown ?? LiveCropTypes ?? [];

    [JsonIgnore]
    public int TotalCrops => DocumentedTotalCrops ?? LiveTotalPlants ?? CropBreakdown.Sum(crop => crop.Count);

    [JsonIgnore]
    public float AvgGrowth => NormalizeGrowth(DocumentedAverageGrowth ?? LiveGrowthProgressAverage ?? 0f);

    [JsonIgnore]
    public int ReadyToHarvest => DocumentedReadyToHarvest ?? CropBreakdown.Sum(crop => crop.ReadyCount);

    internal static float NormalizeGrowth(float value)
    {
        float normalized = value > 1f ? value / 100f : value;
        return Math.Clamp(normalized, 0f, 1f);
    }
}

public sealed record CropBreakdownDto
{
    public CropBreakdownDto() { }

    public CropBreakdownDto(string def, int count, float avgGrowth)
    {
        DocumentedDef = def;
        DocumentedCount = count;
        DocumentedAverageGrowth = avgGrowth;
    }

    [JsonPropertyName("def")]
    public string? DocumentedDef { get; init; }

    [JsonPropertyName("plant_def_name")]
    public string? LivePlantDefName { get; init; }

    [JsonPropertyName("count")]
    public int? DocumentedCount { get; init; }

    [JsonPropertyName("total_plants")]
    public int? LiveTotalPlants { get; init; }

    [JsonPropertyName("avg_growth")]
    public float? DocumentedAverageGrowth { get; init; }

    [JsonPropertyName("growth_progress_average")]
    public float? LiveGrowthProgressAverage { get; init; }

    [JsonPropertyName("harvestable_plants")]
    public int? LiveHarvestablePlants { get; init; }

    [JsonPropertyName("zone_id")]
    [JsonConverter(typeof(FlexibleStringIdJsonConverter))]
    public string? ZoneId { get; init; }

    [JsonIgnore]
    public string Def => LivePlantDefName ?? DocumentedDef ?? "";

    [JsonIgnore]
    public int Count => LiveTotalPlants ?? DocumentedCount ?? 0;

    [JsonIgnore]
    public float AvgGrowth => FarmSummaryDto.NormalizeGrowth(DocumentedAverageGrowth ?? LiveGrowthProgressAverage ?? 0f);

    [JsonIgnore]
    public int ReadyCount => LiveHarvestablePlants ?? 0;
}

// ── GET /map/plants?map_id ────────────────────────────────────────────────────
public sealed record PlantDto
{
    public PlantDto() { }

    public PlantDto(
        string id,
        string def,
        float growth,
        PositionDto? position,
        bool isCrop,
        string? zoneId)
    {
        DocumentedId = id;
        DocumentedDef = def;
        DocumentedGrowth = growth;
        Position = position;
        DocumentedIsCrop = isCrop;
        ZoneId = zoneId;
    }

    [JsonPropertyName("id")]
    [JsonConverter(typeof(FlexibleStringIdJsonConverter))]
    public string? DocumentedId { get; init; }

    [JsonPropertyName("thing_id")]
    [JsonConverter(typeof(FlexibleStringIdJsonConverter))]
    public string? LiveThingId { get; init; }

    [JsonPropertyName("def")]
    public string? DocumentedDef { get; init; }

    [JsonPropertyName("def_name")]
    public string? LiveDefName { get; init; }

    [JsonPropertyName("growth")]
    public float? DocumentedGrowth { get; init; }

    [JsonPropertyName("growth_progress")]
    public float? LiveGrowthProgress { get; init; }

    [JsonPropertyName("position")]
    public PositionDto? Position { get; init; }

    [JsonPropertyName("is_crop")]
    public bool? DocumentedIsCrop { get; init; }

    [JsonPropertyName("is_harvestable")]
    public bool? LiveIsHarvestable { get; init; }

    [JsonPropertyName("zone_id")]
    [JsonConverter(typeof(FlexibleStringIdJsonConverter))]
    public string? ZoneId { get; init; }

    [JsonIgnore]
    public string Id => DocumentedId ?? LiveThingId ?? "";

    [JsonIgnore]
    public string Def => LiveDefName ?? DocumentedDef ?? "";

    [JsonIgnore]
    public float Growth => FarmSummaryDto.NormalizeGrowth(DocumentedGrowth ?? LiveGrowthProgress ?? 0f);

    [JsonIgnore]
    public bool IsCrop => DocumentedIsCrop ?? false;

    [JsonIgnore]
    public bool? IsHarvestable => LiveIsHarvestable;
}

// ── GET /map/animals?map_id ───────────────────────────────────────────────────
public record AnimalDto(
    [property: JsonPropertyName("id")]
    [property: JsonConverter(typeof(FlexibleStringIdJsonConverter))] string Id,
    [property: JsonPropertyName("def")]     string Def,
    [property: JsonPropertyName("name")]    string? Name,
    [property: JsonPropertyName("tame")]    bool Tame,
    [property: JsonPropertyName("health")]  float? Health,
    [property: JsonPropertyName("position")] PositionDto? Position
);

// ── GET /map/zones?map_id ─────────────────────────────────────────────────────
// Zone rows expose labels, types, and cell counts; stockpile contents come from stored resources.
public record ZoneDto(
    [property: JsonPropertyName("id")]
    [property: JsonConverter(typeof(FlexibleStringIdJsonConverter))] string Id,
    [property: JsonPropertyName("type")]    string Type,        // GrowingZone | StockpileZone | etc.
    [property: JsonPropertyName("label")]   string? Label,
    [property: JsonPropertyName("cells")]   IReadOnlyList<PositionDto>? Cells,
    [property: JsonPropertyName("plant_def")] string? PlantDef, // GrowingZone only
    [property: JsonPropertyName("cells_count")] int? CellsCount = null
);

// ── GET /map/buildings?map_id ─────────────────────────────────────────────────
// Live wire fields (RIMAPI 1.9): id (int), def, label, position, rotation, size, type.
// hp / power_on / is_working are NOT in the wire response — defaulted in MapBuildings.
// TODO: expand when Willie begins or RIMAPI exposes hp/power state.
public record BuildingDto(
    [property: JsonPropertyName("id")]       int          Id,
    [property: JsonPropertyName("def")]      string       Def,
    [property: JsonPropertyName("label")]    string?      Label,
    [property: JsonPropertyName("type")]     string?      Type,
    [property: JsonPropertyName("position")] PositionDto? Position
);

// ── GET /map/power/info?map_id ────────────────────────────────────────────────
// RIMAPI serializes this DTO with PascalCase names; power flow is W, battery storage is Wd.
public record PowerInfoDto(
    [property: JsonPropertyName("CurrentPower")]          int CurrentPower,          // W
    [property: JsonPropertyName("TotalPossiblePower")]    int TotalPossiblePower,    // W
    [property: JsonPropertyName("CurrentlyStoredPower")]  int CurrentlyStoredPower,  // Wd
    [property: JsonPropertyName("TotalPowerStorage")]     int TotalPowerStorage,     // Wd
    [property: JsonPropertyName("TotalConsumption")]      int TotalConsumption,      // W nameplate
    [property: JsonPropertyName("ConsumptionPowerOn")]    int ConsumptionPowerOn,    // W live draw
    [property: JsonPropertyName("ProducePowerBuildings")] IReadOnlyList<int> ProducePowerBuildings,
    [property: JsonPropertyName("ConsumePowerBuildings")] IReadOnlyList<int> ConsumePowerBuildings,
    [property: JsonPropertyName("StorePowerBuildings")]   IReadOnlyList<int> StorePowerBuildings
);

// ── GET /map/weather?map_id ───────────────────────────────────────────────────
public record WeatherDto(
    [property: JsonPropertyName("def")]         string Def,
    [property: JsonPropertyName("temperature")] float Temperature, // °C
    [property: JsonPropertyName("rain_rate")]   float RainRate
);

// ── GET /map/things?map_id ────────────────────────────────────────────────────
// Broad thing inventory. Live RIMAPI includes forbidden map items here, so Food
// should prefer /resources/stored for reachable stockpile counts.
public sealed record ThingDto
{
    [JsonPropertyName("thing_id")]
    [JsonConverter(typeof(FlexibleStringIdJsonConverter))]
    public string? ThingId { get; init; }

    [JsonPropertyName("id")]
    [JsonConverter(typeof(FlexibleStringIdJsonConverter))]
    public string? Id { get; init; }

    [JsonPropertyName("def_name")]
    public string? DefName { get; init; }

    [JsonPropertyName("def")]
    public string? Def { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("categories")]
    public IReadOnlyList<string>? Categories { get; init; }

    [JsonPropertyName("position")]
    public PositionDto? Position { get; init; }

    [JsonPropertyName("stack_count")]
    public int? StackCount { get; init; }

    [JsonPropertyName("count")]
    public int? Count { get; init; }

    [JsonPropertyName("market_value")]
    public float? MarketValue { get; init; }

    [JsonPropertyName("is_forbidden")]
    public bool IsForbidden { get; init; }

    [JsonIgnore]
    public string StableId => ThingId ?? Id ?? "";

    [JsonIgnore]
    public string StableDef => DefName ?? Def ?? "";

    [JsonIgnore]
    public int EffectiveStackCount => Math.Max(1, StackCount ?? Count ?? 1);
}

// ── GET /map/creatures/summary?map_id ────────────────────────────────────────
public record CreaturesSummaryDto(
    [property: JsonPropertyName("colonists")]   int Colonists,
    [property: JsonPropertyName("enemies")]     int Enemies,
    [property: JsonPropertyName("animals")]     int Animals,
    [property: JsonPropertyName("prisoners")]   int Prisoners
);

// ── GET /map/rooms?map_id ─────────────────────────────────────────────────────
// Live wire fields are nested under data.rooms.
public record RoomDto(
    [property: JsonPropertyName("id")]
    [property: JsonConverter(typeof(FlexibleStringIdJsonConverter))] string Id,
    [property: JsonPropertyName("role_label")]         string? RoleLabel,
    [property: JsonPropertyName("temperature")]        float Temperature,
    [property: JsonPropertyName("cells_count")]        int CellsCount,
    [property: JsonPropertyName("touches_map_edge")]   bool TouchesMapEdge,
    [property: JsonPropertyName("is_prison_cell")]     bool IsPrisonCell,
    [property: JsonPropertyName("is_doorway")]         bool IsDoorway,
    [property: JsonPropertyName("open_roof_count")]    int OpenRoofCount,
    [property: JsonPropertyName("contained_beds_ids")] IReadOnlyList<int>? ContainedBedsIds,
    [property: JsonPropertyName("impressiveness")]     float? Impressiveness,
    [property: JsonPropertyName("beauty")]             float? Beauty,
    [property: JsonPropertyName("cleanliness")]        float? Cleanliness,
    [property: JsonPropertyName("space")]              float? Space,
    [property: JsonPropertyName("wealth")]             float? Wealth
);

// ── GET /api/v1/resources/summary?map_id ──────────────────────────────────────
// Colony-wide stockpile rollup. Verified against live RIMAPI 1.9.0.
// `total_nutrition` is sometimes 0 even when food_total > 0 — treat as a hint
// rather than authoritative; days-of-food is null when nutrition is zero.
public record ResourcesSummaryDto(
    [property: JsonPropertyName("total_items")]        int                  TotalItems,
    [property: JsonPropertyName("total_market_value")] float                TotalMarketValue,
    [property: JsonPropertyName("critical_resources")] CriticalResourcesDto? CriticalResources
);

public record CriticalResourcesDto(
    [property: JsonPropertyName("food_summary")]    FoodSummaryDto? FoodSummary,
    [property: JsonPropertyName("medicine_total")]  int             MedicineTotal,
    [property: JsonPropertyName("weapon_count")]    int             WeaponCount,
    [property: JsonPropertyName("weapon_value")]    float           WeaponValue
);

public record FoodSummaryDto(
    [property: JsonPropertyName("food_total")]       int   FoodTotal,
    [property: JsonPropertyName("total_nutrition")]  float TotalNutrition,
    [property: JsonPropertyName("meals_count")]      int   MealsCount,
    [property: JsonPropertyName("raw_food_count")]   int   RawFoodCount
);

// ── GET /api/v1/resources/stored?map_id ──────────────────────────────────────
// Live shape is a category object, e.g. data.food_meals = [ThingDto...].
public sealed record StoredResourcesDto
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Categories { get; init; } = [];
}

// ── GET /api/v1/def/all ──────────────────────────────────────────────────────
// The useful list is nested at data.things_defs.
public sealed record ThingDefDto(
    [property: JsonPropertyName("def_name")]    string DefName,
    [property: JsonPropertyName("label")]       string? Label,
    [property: JsonPropertyName("category")]    string? Category,
    [property: JsonPropertyName("thing_class")] string? ThingClass,
    [property: JsonPropertyName("is_weapon")]   bool IsWeapon,
    [property: JsonPropertyName("is_apparel")]  bool IsApparel,
    [property: JsonPropertyName("is_item")]     bool IsItem,
    [property: JsonPropertyName("is_plant")]    bool IsPlant,
    [property: JsonPropertyName("is_building")] bool IsBuilding,
    [property: JsonPropertyName("is_medicine")] bool IsMedicine,
    [property: JsonPropertyName("is_drug")]     bool IsDrug,
    [property: JsonPropertyName("nutrition")]   float Nutrition,
    [property: JsonPropertyName("stack_limit")] int? StackLimit
);

public sealed record TerrainDefDto(
    [property: JsonPropertyName("def_name")] string DefName,
    [property: JsonPropertyName("label")]    string? Label,
    [property: JsonPropertyName("fertility")] float Fertility = 0f,
    [property: JsonPropertyName("affordances")] IReadOnlyList<string>? Affordances = null
);

public sealed record AnimalDefDto(
    [property: JsonPropertyName("def_name")] string DefName,
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("base_body_size")] float BaseBodySize = 0f,
    [property: JsonPropertyName("base_health_scale")] float BaseHealthScale = 0f,
    [property: JsonPropertyName("predator")] bool Predator = false,
    [property: JsonPropertyName("herd_animal")] bool HerdAnimal = false,
    [property: JsonPropertyName("pack_animal")] bool PackAnimal = false,
    [property: JsonPropertyName("is_insect")] bool IsInsect = false,
    [property: JsonPropertyName("explosive")] bool Explosive = false,
    [property: JsonPropertyName("manhunter_on_damage_chance")] float ManhunterOnDamageChance = 0f,
    [property: JsonPropertyName("wildness")] float Wildness = 0f,
    [property: JsonPropertyName("meat_amount")] float MeatAmount = 0f,
    [property: JsonPropertyName("estimated_meat_nutrition")] float EstimatedMeatNutrition = 0f,
    [property: JsonPropertyName("leather_amount")] float LeatherAmount = 0f,
    [property: JsonPropertyName("leather_def")] string? LeatherDef = null,
    [property: JsonPropertyName("petness")] float Petness = 0f
);

public sealed record DefCatalogDto(
    [property: JsonPropertyName("things_defs")]  IReadOnlyList<ThingDefDto>? ThingsDefs,
    [property: JsonPropertyName("terrain_defs")] IReadOnlyList<TerrainDefDto>? TerrainDefs,
    [property: JsonPropertyName("animal_defs")] IReadOnlyList<AnimalDefDto>? AnimalDefs = null
);

public sealed record TerrainGridDto(
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("palette")] IReadOnlyList<string>? Palette,
    [property: JsonPropertyName("grid")] IReadOnlyList<int>? Grid,
    [property: JsonPropertyName("floor_palette")] IReadOnlyList<string>? FloorPalette = null,
    [property: JsonPropertyName("floor_grid")] IReadOnlyList<int>? FloorGrid = null
);

public sealed record RimApiImageDto(
    [property: JsonPropertyName("name")]         string? Name,
    [property: JsonPropertyName("result")]       string? Result,
    [property: JsonPropertyName("image_base64")] string? ImageBase64,
    [property: JsonPropertyName("image_base_64")] string? ImageBase64Legacy
)
{
    public string? Base64 => ImageBase64 ?? ImageBase64Legacy;
}

public sealed record FactionDto(
    [property: JsonPropertyName("load_id")]  int LoadId,
    [property: JsonPropertyName("def_name")] string? DefName,
    [property: JsonPropertyName("name")]     string? Name,
    [property: JsonPropertyName("is_player")] bool IsPlayer,
    [property: JsonPropertyName("relation")] string? Relation,
    [property: JsonPropertyName("goodwill")] int Goodwill
);

public sealed record FactionIconDto(
    [property: JsonPropertyName("image")] FactionIconImageDto? Image,
    [property: JsonPropertyName("color")] string? Color
);

public sealed record FactionIconImageDto(
    [property: JsonPropertyName("result")]        string? Result,
    [property: JsonPropertyName("image_base_64")] string? ImageBase64Legacy,
    [property: JsonPropertyName("image_base64")]  string? ImageBase64
)
{
    public string? Base64 => ImageBase64 ?? ImageBase64Legacy;
}

public sealed record ColonistBodyImageDto(
    [property: JsonPropertyName("body_image")] string? BodyImage,
    [property: JsonPropertyName("body_color")] string? BodyColor,
    [property: JsonPropertyName("head_image")] string? HeadImage,
    [property: JsonPropertyName("head_color")] string? HeadColor
);

// ── GET /api/v1/research/progress ─────────────────────────────────────────────
// Current research project + progress. Returns name="none", label="None",
// progress_percent=0 when nothing is selected.
public record ResearchProgressDto(
    [property: JsonPropertyName("name")]             string  Name,
    [property: JsonPropertyName("label")]            string? Label,
    [property: JsonPropertyName("progress")]         float   Progress,            // ticks of work done
    [property: JsonPropertyName("research_points")]  float   ResearchPoints,
    [property: JsonPropertyName("is_finished")]      bool    IsFinished,
    [property: JsonPropertyName("can_start_now")]    bool    CanStartNow,
    [property: JsonPropertyName("progress_percent")] float   ProgressPercent      // 0–100
);
