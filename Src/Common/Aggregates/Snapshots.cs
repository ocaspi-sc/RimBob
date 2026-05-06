namespace RimAI.Core.Aggregates;

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

public sealed record ColonistRecord(
    string Id,
    string Name,
    int    Age,
    string Gender,
    float  Health,
    float  Mood,
    float  Hunger,
    string? CurrentJob,
    IReadOnlyList<ColonistSkill> Skills,
    IReadOnlyList<string>        Traits
);

public sealed record ColonistSkill(string Def, int Level, string Passion);

public sealed record StockpileLedger(
    IReadOnlyList<StockpileZone>     Zones,
    // Per-def counts aggregated across all stockpile zones. Empty if RIMAPI's zone
    // shape doesn't carry item lists — see TODO at MapDto.cs.
    IReadOnlyDictionary<string, int> ItemsByDef
);

public sealed record StockpileZone(string Id, string Type, string? Label, int CellCount);

public sealed record BuildingRegistry(IReadOnlyList<BuildingRecord> Buildings);

public sealed record BuildingRecord(
    string Id,
    string Def,
    float  Hp,
    bool?  PowerOn,
    bool?  IsWorking
);

public sealed record PowerNetwork(float ProductionW, float ConsumptionW, float StoredWd, float CapacityWd);

public sealed record ThreatBoard(
    IReadOnlyList<HostileLord>    Lords,
    IReadOnlyList<IncidentRecord> RecentIncidents
);

public sealed record HostileLord(
    string Id,
    string JobType,
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

public sealed record CropTypeCount(string Def, int Count, float AverageGrowth);

// Empty defaults — used at ColonyState construction so Versioned<T>.Value is never null.
public static class AggregateDefaults
{
    public static readonly MapInfoSnapshot   Map         = new(0, null);
    public static readonly EconomyLedger     Economy     = new(0, 0f, "", "", false, "");
    public static readonly ColonistRegistry  Colonists   = new([]);
    public static readonly StockpileLedger   Stockpiles  = new([], new Dictionary<string, int>());
    public static readonly BuildingRegistry  Buildings   = new([]);
    public static readonly PowerNetwork      Power       = new(0f, 0f, 0f, 0f);
    public static readonly ThreatBoard       Threats     = new([], []);
    public static readonly WeatherSnapshot   Weather     = new("", 0f, 0f);
    public static readonly FarmSnapshot      Farm        = new(0, 0f, 0, []);
}
