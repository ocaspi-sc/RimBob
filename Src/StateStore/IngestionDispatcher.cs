using Microsoft.Extensions.Logging;
using RimAI.Core.Aggregates;
using RimAI.Ingestion;
using RimAI.Ingestion.Dtos;

namespace RimAI.State;

/// <summary>
/// Pulls a full snapshot from RIMAPI and writes it into ColonyState aggregates.
/// M1: explicit RefreshAllAsync; the polling cadences in state-store.md (Slow /
/// Medium / Fast / Event-diff) are not built yet — the daily-tick poller will
/// drive this for now.
/// </summary>
public sealed class IngestionDispatcher(
    RimApiClient rimApi,
    ColonyState state,
    ILogger<IngestionDispatcher> log)
{
    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        var maps = await rimApi.GetMapsAsync(ct);
        var home = maps.FirstOrDefault(m => m.IsPlayerHome) ?? maps.FirstOrDefault()
            ?? throw new InvalidOperationException("RIMAPI returned no maps.");

        state.Map.Update(new MapInfoSnapshot(home.Id, home.Size));

        var stateTask     = rimApi.GetGameStateAsync(ct);
        var dateTask      = rimApi.GetDateTimeAsync(ct);
        var pawnsTask     = rimApi.GetColonistsDetailedAsync(home.Id, ct);
        var farmTask      = rimApi.GetFarmSummaryAsync(home.Id, ct);
        var zonesTask     = rimApi.GetZonesAsync(home.Id, ct);
        var buildingsTask = rimApi.GetBuildingsAsync(home.Id, ct);
        var powerTask     = rimApi.GetPowerInfoAsync(home.Id, ct);
        var weatherTask   = rimApi.GetWeatherAsync(home.Id, ct);
        var lordsTask     = rimApi.GetLordsAsync(home.Id, ct);
        var incidentsTask = rimApi.GetIncidentsAsync(home.Id, ct);

        await Task.WhenAll(stateTask, dateTask, pawnsTask, farmTask, zonesTask,
                           buildingsTask, powerTask, weatherTask, lordsTask, incidentsTask);

        var gs = stateTask.Result;
        state.Economy.Update(new EconomyLedger(
            gs.Tick, gs.Wealth, gs.Storyteller, gs.ProgramState, gs.Paused, dateTask.Result.DateTime));

        state.Colonists.Update(new ColonistRegistry(MapPawns(pawnsTask.Result)));

        state.Farm.Update(MapFarm(farmTask.Result));

        state.Stockpiles.Update(MapStockpiles(zonesTask.Result));

        state.Buildings.Update(new BuildingRegistry(MapBuildings(buildingsTask.Result)));

        var p = powerTask.Result;
        state.Power.Update(new PowerNetwork(p.Production, p.Consumption, p.Stored, p.Capacity));

        var w = weatherTask.Result;
        state.Weather.Update(new WeatherSnapshot(w.Def, w.Temperature, w.RainRate));

        state.Threats.Update(new ThreatBoard(
            MapLords(lordsTask.Result),
            MapIncidents(incidentsTask.Result)));
    }

    // ── Mappers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<ColonistRecord> MapPawns(IReadOnlyList<ColonistDetailedDto> pawns) =>
        pawns.Select(p => new ColonistRecord(
            Id:         p.Id,
            Name:       p.Name,
            Age:        p.Age,
            Gender:     p.Gender,
            Health:     p.Health,
            Mood:       p.Mood,
            Hunger:     p.Hunger,
            CurrentJob: p.CurrentJob,
            Skills:     (p.Skills ?? [])
                            .Select(s => new ColonistSkill(s.Def, s.Level, s.Passion))
                            .ToList(),
            Traits:     p.Traits ?? []
        )).ToList();

    private static FarmSnapshot MapFarm(FarmSummaryDto f) =>
        new(
            TotalCrops:     f.TotalCrops,
            AverageGrowth:  f.AvgGrowth,
            ReadyToHarvest: f.ReadyToHarvest,
            CropBreakdown:  (f.CropBreakdown ?? [])
                                .Select(c => new CropTypeCount(c.Def, c.Count, c.AvgGrowth))
                                .ToList()
        );

    private StockpileLedger MapStockpiles(IReadOnlyList<ZoneDto> zones)
    {
        var stockpileZones = zones
            .Where(z => string.Equals(z.Type, "StockpileZone", StringComparison.OrdinalIgnoreCase))
            .Select(z => new StockpileZone(z.Id, z.Type, z.Label, z.Cells?.Count ?? 0))
            .ToList();

        // TODO: ZoneDto does not currently surface item counts (see MapDto.cs:41-49).
        // Once a stockpile-inventory endpoint is added to RimApiClient, populate
        // ItemsByDef here. For now the dictionary is empty and we log once per refresh.
        if (stockpileZones.Count > 0)
            log.LogWarning("Stockpile contents unavailable: ZoneDto carries no item list. " +
                           "ResourceSnapshot will be empty until an inventory endpoint is added.");

        return new StockpileLedger(stockpileZones, new Dictionary<string, int>());
    }

    private static IReadOnlyList<BuildingRecord> MapBuildings(IReadOnlyList<BuildingDto> bs) =>
        bs.Select(b => new BuildingRecord(b.Id, b.Def, b.Hp, b.PowerOn, b.IsWorking)).ToList();

    private static IReadOnlyList<HostileLord> MapLords(IReadOnlyList<LordDto> lords) =>
        lords.Select(l => new HostileLord(
            Id:           l.Id,
            JobType:      l.JobType,
            FactionId:    l.FactionId,
            ThreatPoints: l.ThreatPoints,
            PawnCount:    l.PawnIds?.Count ?? 0
        )).ToList();

    private static IReadOnlyList<IncidentRecord> MapIncidents(IReadOnlyList<IncidentDto> incidents) =>
        incidents
            .OrderBy(i => i.DaysSince)
            .Take(5)
            .Select(i => new IncidentRecord(i.Def, i.DaysSince, i.Label))
            .ToList();
}
