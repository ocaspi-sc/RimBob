using RimBob.Core.Aggregates;
using RimBob.Core.Versioning;

namespace RimBob.State;

public enum ColonyStateOrigin
{
    None,
    Live,
    Snapshot
}

/// <summary>
/// Single root container for all colony aggregates. Ingestion writes here;
/// briefings (via BriefingCache) read here. Each aggregate carries its own
/// monotonic version. See Docs/design/state-store.md.
/// </summary>
public sealed class ColonyState
{
    public Versioned<MapInfoSnapshot>  Map        { get; } = new(AggregateDefaults.Map);
    public Versioned<EconomyLedger>    Economy    { get; } = new(AggregateDefaults.Economy);
    public Versioned<ColonistRegistry> Colonists  { get; } = new(AggregateDefaults.Colonists);
    public Versioned<RoomRegistry>     Rooms      { get; } = new(AggregateDefaults.Rooms);
    public Versioned<StockpileLedger>  Stockpiles { get; } = new(AggregateDefaults.Stockpiles);
    public Versioned<MapAreaRegistry>  Areas      { get; } = new(AggregateDefaults.Areas);
    public Versioned<BuildingRegistry> Buildings  { get; } = new(AggregateDefaults.Buildings);
    public Versioned<WorkTableRegistry> WorkTables { get; } = new(AggregateDefaults.WorkTables);
    public Versioned<PowerNetwork>     Power      { get; } = new(AggregateDefaults.Power);
    public Versioned<ThreatBoard>      Threats    { get; } = new(AggregateDefaults.Threats);
    public Versioned<WeatherSnapshot>  Weather    { get; } = new(AggregateDefaults.Weather);
    public Versioned<FarmSnapshot>     Farm       { get; } = new(AggregateDefaults.Farm);
    public Versioned<PlantRegistry>    Plants     { get; } = new(AggregateDefaults.Plants);
    public Versioned<ThingRegistry>    Things     { get; } = new(AggregateDefaults.Things);
    public Versioned<ThingDefRegistry> ThingDefs  { get; } = new(AggregateDefaults.ThingDefs);
    public Versioned<AnimalDefRegistry> AnimalDefs { get; } = new(AggregateDefaults.AnimalDefs);
    public Versioned<TerrainSnapshot>  Terrain    { get; } = new(AggregateDefaults.Terrain);
    public Versioned<StoredResourceRegistry> StoredResources { get; } = new(AggregateDefaults.StoredResources);
    public Versioned<AnimalRegistry>   Animals    { get; } = new(AggregateDefaults.Animals);
    public Versioned<ResourceSummary>  Resources  { get; } = new(AggregateDefaults.Resources);
    public Versioned<ResearchInfo>     Research   { get; } = new(AggregateDefaults.Research);
    public Versioned<WillieConstructionBacklog> WillieBacklog { get; } = new(AggregateDefaults.WillieBacklog);

    public ColonyStateOrigin LastRefreshSource { get; set; } = ColonyStateOrigin.None;

    public DateTimeOffset? LastLiveRefreshAt { get; set; }

    /// <summary>
    /// Canonical name → version pairs for every aggregate the MayorBriefing reads.
    /// Order must stay in sync with GetVersionsForMayorBriefing().
    /// </summary>
    public static readonly string[] MayorBriefingAggregateNames =
        ["Map", "Economy", "Colonists", "Stockpiles", "Buildings", "Power", "Threats", "Weather", "Farm", "Things", "ThingDefs", "StoredResources", "Resources", "Research"];

    public static readonly string[] FoodBriefingAggregateNames =
        ["Economy", "Colonists", "Stockpiles", "Buildings", "WorkTables", "Power", "Threats", "Weather", "Farm", "Plants", "Things", "ThingDefs", "AnimalDefs", "Terrain", "StoredResources", "Animals", "Resources"];

    public static readonly string[] WelfareBriefingAggregateNames =
        ["Economy", "Colonists", "Rooms"];

    public static readonly string[] WillieBriefingAggregateNames =
        ["Map", "Economy", "Colonists", "Rooms", "Stockpiles", "Areas", "Buildings", "Power", "WillieBacklog"];

    /// <summary>
    /// Versions of every aggregate the MayorBriefing reads, in canonical order.
    /// Used by BriefingCache to detect input changes.
    /// </summary>
    public long[] GetVersionsForMayorBriefing() =>
    [
        Map.Version, Economy.Version, Colonists.Version, Stockpiles.Version,
        Buildings.Version, Power.Version, Threats.Version, Weather.Version, Farm.Version,
        Things.Version, ThingDefs.Version, StoredResources.Version, Resources.Version, Research.Version
    ];

    public long[] GetVersionsForFoodBriefing() =>
    [
        Economy.Version, Colonists.Version, Stockpiles.Version, Buildings.Version,
        WorkTables.Version, Power.Version, Threats.Version, Weather.Version, Farm.Version, Plants.Version,
        Things.Version, ThingDefs.Version, AnimalDefs.Version, Terrain.Version, StoredResources.Version, Animals.Version,
        Resources.Version
    ];

    public long[] GetVersionsForWelfareBriefing() =>
    [
        Economy.Version, Colonists.Version, Rooms.Version
    ];

    public long[] GetVersionsForWillieBriefing() =>
    [
        Map.Version, Economy.Version, Colonists.Version, Rooms.Version, Stockpiles.Version,
        Areas.Version, Buildings.Version, Power.Version, WillieBacklog.Version
    ];
}
