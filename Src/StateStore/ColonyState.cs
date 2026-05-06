using RimAI.Core.Aggregates;
using RimAI.Core.Versioning;

namespace RimAI.State;

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
    public Versioned<StockpileLedger>  Stockpiles { get; } = new(AggregateDefaults.Stockpiles);
    public Versioned<BuildingRegistry> Buildings  { get; } = new(AggregateDefaults.Buildings);
    public Versioned<PowerNetwork>     Power      { get; } = new(AggregateDefaults.Power);
    public Versioned<ThreatBoard>      Threats    { get; } = new(AggregateDefaults.Threats);
    public Versioned<WeatherSnapshot>  Weather    { get; } = new(AggregateDefaults.Weather);
    public Versioned<FarmSnapshot>     Farm       { get; } = new(AggregateDefaults.Farm);

    /// <summary>
    /// Canonical name → version pairs for every aggregate the MayorBriefing reads.
    /// Order must stay in sync with GetVersionsForMayorBriefing().
    /// </summary>
    public static readonly string[] MayorBriefingAggregateNames =
        ["Map", "Economy", "Colonists", "Stockpiles", "Buildings", "Power", "Threats", "Weather", "Farm"];

    /// <summary>
    /// Versions of every aggregate the MayorBriefing reads, in canonical order.
    /// Used by BriefingCache to detect input changes.
    /// </summary>
    public long[] GetVersionsForMayorBriefing() =>
    [
        Map.Version, Economy.Version, Colonists.Version, Stockpiles.Version,
        Buildings.Version, Power.Version, Threats.Version, Weather.Version, Farm.Version
    ];
}
