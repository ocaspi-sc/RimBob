using System.Text.Json.Serialization;

namespace RimBob.Core.Aggregates;

// Aggregate snapshots — raw "what is" data. No derived facts (those live in views/briefings).
// Pure domain records; no external dependencies, per CLAUDE.md.

public sealed record MapInfoSnapshot(int Id, string? Size);

public sealed record EconomyLedger(
    long   Tick,
    float  ColonyWealth,
    string Storyteller,
    string ProgramState,
    bool   Paused,
    string DateTimeRaw   // RIMAPI's "5th of Aprimay, 5500, 14h" string
);

public sealed record ColonistRegistry(IReadOnlyList<ColonistRecord> Colonists);

public sealed record MapPosition(int X, int Y, int Z);

public sealed record MapRect(
    [property: JsonPropertyName("x1")]
    int X1,
    [property: JsonPropertyName("z1")]
    int Z1,
    [property: JsonPropertyName("x2")]
    int X2,
    [property: JsonPropertyName("z2")]
    int Z2)
{
    [JsonIgnore]
    public int Area => Math.Max(0, X2 - X1 + 1) * Math.Max(0, Z2 - Z1 + 1);
}

public sealed record ColonistRecord(
    string Id,
    string Name,
    int    Age,
    string Gender,
    float  Health,
    float  Mood,
    float  Hunger,
    bool   IsDowned,
    bool   IsDead,
    MapPosition? Position,
    string? CurrentJob,
    IReadOnlyList<ColonistSkill> Skills,
    IReadOnlyList<string>        Traits,
    float Sleep = 0f,
    float Comfort = 0f,
    float Beauty = 0f,
    float Joy = 0f,
    float FreshAir = 0f,
    float DrugsDesire = 0f,
    IReadOnlyList<MoodThoughtRecord>? MoodThoughts = null
);

public sealed record ColonistSkill(string Def, int Level, string Passion);

public sealed record MoodThoughtRecord(
    string DefName,
    string? Label,
    float MoodOffset,
    int StageIndex
);

public sealed record RoomRegistry(IReadOnlyList<RoomRecord> Rooms);

public sealed record RoomRecord(
    string Id,
    string RoleLabel,
    float Temperature,
    int CellsCount,
    bool TouchesMapEdge,
    bool IsPrisonCell,
    bool IsDoorway,
    int OpenRoofCount,
    IReadOnlyList<string> ContainedBedIds,
    float? Impressiveness,
    float? Beauty,
    float? Cleanliness,
    float? Space,
    float? Wealth,
    MapRect? Bounds = null,
    IReadOnlyList<MapPosition>? Cells = null,
    IReadOnlyList<MapPosition>? EntryCells = null,
    int? RegionId = null,
    IReadOnlyList<string>? ContainedBuildingIds = null)
{
    public IReadOnlyList<MapPosition> Cells { get; init; } = Cells ?? [];

    public IReadOnlyList<MapPosition> EntryCells { get; init; } = EntryCells ?? [];

    public IReadOnlyList<string> ContainedBuildingIds { get; init; } =
        ContainedBuildingIds ?? ContainedBedIds;
}

public sealed record StockpileLedger(
    IReadOnlyList<StockpileZone>     Zones,
    // Per-def counts aggregated from stored resources when available.
    IReadOnlyDictionary<string, int> ItemsByDef
);

public sealed record StockpileZone(string Id, string Type, string? Label, int CellCount, MapPosition? Center = null);

public sealed record MapAreaRegistry(IReadOnlyList<MapArea> Areas)
{
    public static MapAreaRegistry Empty { get; } = new([]);
}

public sealed record MapArea(
    string Id,
    string Type,
    string? Label,
    int CellCount,
    MapRect? Bounds = null,
    MapPosition? Centroid = null);

public sealed record BuildingRegistry(IReadOnlyList<BuildingRecord> Buildings);

public sealed record BuildingPower(
    bool Required,
    bool On,
    float ConsumptionW
);

public sealed record BuildingFuel(
    float Current,
    float Capacity,
    string? FuelDef
);

public sealed record BuildingRecord(
    string Id,
    string Def,
    float? Hp,
    bool?  PowerOn,
    bool?  IsWorking,
    MapPosition? Position = null,
    string? Label = null,
    float? MaxHp = null,
    string? Stuff = null,
    int? RoomId = null,
    BuildingPower? Power = null,
    BuildingFuel? Fuel = null,
    bool? FlickableOn = null,
    float? Flammability = null
);

public sealed record WorkTableRegistry(IReadOnlyList<WorkTableRecord> WorkTables);

public sealed record WorkTableRecord(
    string BuildingId,
    IReadOnlyList<WorkTableBillRecord> Bills
);

public sealed record WorkTableBillRecord(
    int LoadId,
    string? RecipeDefName,
    string? RecipeLabel,
    bool Suspended,
    bool Paused,
    string? RepeatMode,
    int RepeatCount,
    int TargetCount
);

public sealed record PowerNetwork(float ProductionW, float ConsumptionW, float StoredWd, float CapacityWd);

public sealed record ThreatBoard(
    IReadOnlyList<HostileLord>    Lords,
    IReadOnlyList<IncidentRecord> RecentIncidents
);

public sealed record HostileLord(
    string  Id,
    string? JobType,
    string? FactionId,
    float?  ThreatPoints,
    int     PawnCount
);

public sealed record IncidentRecord(string Def, float DaysSince, string? Label);

public sealed record WeatherSnapshot(string Def, float TemperatureC, float RainRate);

public sealed record FarmSnapshot(
    int   TotalCrops,
    float AverageGrowth,
    int   ReadyToHarvest,
    IReadOnlyList<CropTypeCount> CropBreakdown
);

public sealed record CropTypeCount(
    string Def,
    int Count,
    float AverageGrowth,
    string? ZoneId = null,
    int ReadyCount = 0
);

public sealed record PlantRegistry(IReadOnlyList<PlantRecord> Plants);

public sealed record PlantRecord(
    string Id,
    string Def,
    float Growth,
    bool IsCrop,
    string? ZoneId,
    MapPosition? Position = null,
    bool? IsHarvestable = null
);

public sealed record ThingRegistry(IReadOnlyList<ThingRecord> Things);

public sealed record ThingRecord(
    string Id,
    string Def,
    string? Label,
    int StackCount,
    IReadOnlyList<string> Categories,
    bool IsForbidden,
    MapPosition? Position = null,
    float? MarketValue = null
);

public sealed record ThingDefRegistry(IReadOnlyDictionary<string, ThingDefRecord> DefsByName);

public sealed record ThingDefRecord(
    string Def,
    string? Label,
    string? Category,
    string? ThingClass,
    bool IsItem,
    bool IsPlant,
    bool IsMedicine,
    bool IsDrug,
    float Nutrition,
    int? StackLimit
);

public sealed record AnimalDefRegistry(IReadOnlyDictionary<string, AnimalDefRecord> DefsByName);

public sealed record AnimalDefRecord(
    string Def,
    string? Label,
    float BodySize,
    float HealthScale,
    bool Predator,
    bool HerdAnimal,
    bool PackAnimal,
    bool IsInsect,
    bool Explosive,
    float ManhunterOnDamageChance,
    float Wildness,
    float MeatAmount,
    float EstimatedMeatNutrition,
    float LeatherAmount,
    string? LeatherDef,
    float Petness
);

public sealed record TerrainSnapshot(
    int Width,
    int Height,
    IReadOnlyDictionary<string, int> CellCountsByDef,
    IReadOnlyDictionary<string, TerrainDefRecord> DefsByName
);

public sealed record TerrainDefRecord(
    string Def,
    string? Label,
    float Fertility,
    IReadOnlyList<string> Affordances
)
{
    public bool SupportsGrowing =>
        Fertility > 0f &&
        Affordances.Any(affordance => affordance.Equals("GrowSoil", StringComparison.OrdinalIgnoreCase));
}

public sealed record StoredResourceRegistry(
    IReadOnlyList<StoredResourceRecord> Items,
    IReadOnlyDictionary<string, int> CountByDef,
    IReadOnlyDictionary<string, int> CountByCategory
);

public sealed record StoredResourceRecord(
    string Category,
    string Id,
    string Def,
    string? Label,
    int StackCount,
    bool IsForbidden,
    MapPosition? Position = null,
    float? MarketValue = null
);

public sealed record AnimalRegistry(IReadOnlyList<AnimalRecord> Animals);

public sealed record AnimalRecord(
    string Id,
    string Def,
    bool Tame,
    float Health,
    MapPosition? Position = null,
    bool Bonded = false
);

/// <summary>
/// Colony-wide stockpile rollup from /api/v1/resources/summary.
/// FoodTotal / TotalNutrition come straight from RIMAPI; days-of-food stays a
/// briefing derivation and may use a documented fallback when TotalNutrition is unusable.
/// </summary>
public sealed record ResourceSummary(
    int   TotalItems,
    float TotalMarketValue,
    int   FoodTotal,
    float TotalNutrition,
    int   MealsCount,
    int   RawFoodCount,
    int   MedicineTotal,
    int   WeaponCount,
    float WeaponValue
);

/// <summary>
/// Current research selection from /api/v1/research/progress.
/// CurrentProject is null when nothing is selected (RIMAPI returns "none"/0).
/// </summary>
public sealed record ResearchInfo(
    string? CurrentProject,   // label, e.g. "Microelectronics"; null when nothing is selected
    float?  Progress,         // 0–1, derived from ProgressPercent / 100
    bool    IsFinished
);

public sealed record WillieConstructionBacklog(IReadOnlyList<WillieBacklogGroup> Groups)
{
    public bool SourceAvailable { get; init; }
}

public sealed record WillieBacklogGroup(
    string Kind,
    string DefName,
    string? StuffDefName,
    bool Allowed,
    int Count,
    IReadOnlyList<string> ThingIds,
    IReadOnlyList<MapPosition> SampleCells,
    float TotalWorkLeft,
    IReadOnlyList<MaterialCount> Cost,
    IReadOnlyList<MaterialCount> MaterialsAvailable,
    IReadOnlyList<MaterialCount> MaterialsMissing,
    int BlockedCount,
    int DisallowedCount
)
{
    // TODO: compute frame_age_exceeded via state-store snapshot diff in a follow-on slice.
}

public sealed record MaterialCount(string DefName, int Count)
{
    public int? Required { get; init; }

    public int? Available { get; init; }

    public int? Missing { get; init; }
}

// Empty defaults — used at ColonyState construction so Versioned<T>.Value is never null.
public static class AggregateDefaults
{
    public static readonly MapInfoSnapshot   Map         = new(0, null);
    public static readonly EconomyLedger     Economy     = new(0, 0f, "", "", false, "");
    public static readonly ColonistRegistry  Colonists   = new([]);
    public static readonly RoomRegistry      Rooms       = new([]);
    public static readonly StockpileLedger   Stockpiles  = new([], new Dictionary<string, int>());
    public static readonly MapAreaRegistry   Areas       = MapAreaRegistry.Empty;
    public static readonly BuildingRegistry  Buildings   = new([]);
    public static readonly WorkTableRegistry WorkTables  = new([]);
    public static readonly PowerNetwork      Power       = new(0f, 0f, 0f, 0f);
    public static readonly ThreatBoard       Threats     = new([], []);
    public static readonly WeatherSnapshot   Weather     = new("", 0f, 0f);
    public static readonly FarmSnapshot      Farm        = new(0, 0f, 0, []);
    public static readonly PlantRegistry     Plants      = new([]);
    public static readonly ThingRegistry     Things      = new([]);
    public static readonly ThingDefRegistry  ThingDefs   = new(new Dictionary<string, ThingDefRecord>());
    public static readonly AnimalDefRegistry AnimalDefs  = new(new Dictionary<string, AnimalDefRecord>());
    public static readonly TerrainSnapshot   Terrain     = new(0, 0, new Dictionary<string, int>(), new Dictionary<string, TerrainDefRecord>());
    public static readonly StoredResourceRegistry StoredResources = new([], new Dictionary<string, int>(), new Dictionary<string, int>());
    public static readonly AnimalRegistry    Animals     = new([]);
    public static readonly ResourceSummary   Resources   = new(0, 0f, 0, 0f, 0, 0, 0, 0, 0f);
    public static readonly ResearchInfo      Research    = new(null, null, false);
    public static readonly WillieConstructionBacklog WillieBacklog = new([]);
}
