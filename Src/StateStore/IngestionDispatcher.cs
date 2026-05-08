using Microsoft.Extensions.Logging;
using RimAI.Core.Aggregates;
using RimAI.Ingestion;
using RimAI.Ingestion.Dtos;

namespace RimAI.State;

/// <summary>
/// Pulls a full snapshot from RIMAPI and writes it into ColonyState aggregates.
/// M1.5: explicit RefreshAllAsync; the slow / fast / event-diff polling cadences
/// in state-store.md are deferred — DayTickOrchestrator drives this every poll.
/// </summary>
public sealed class IngestionDispatcher(
    RimApiClient rimApi,
    ColonyState state,
    ILogger<IngestionDispatcher> log)
{
    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<MapInfoDto> maps = await rimApi.GetMapsAsync(ct);
        MapInfoDto home = maps.FirstOrDefault(m => m.IsPlayerHome) ?? maps.FirstOrDefault()
            ?? throw new InvalidOperationException("RIMAPI returned no maps.");

        state.Map.Update(new MapInfoSnapshot(home.Id, home.Size));

        Task<GameStateDto>                      stateTask     = rimApi.GetGameStateAsync(ct);
        Task<DateTimeDto>                       dateTask      = rimApi.GetDateTimeAsync(ct);
        Task<IReadOnlyList<ColonistDetailedDto>> pawnsTask    = rimApi.GetColonistsDetailedAsync(home.Id, ct);
        Task<FarmSummaryDto>                    farmTask      = rimApi.GetFarmSummaryAsync(home.Id, ct);
        Task<IReadOnlyList<ZoneDto>>            zonesTask     = rimApi.GetZonesAsync(home.Id, ct);
        Task<IReadOnlyList<BuildingDto>>        buildingsTask = rimApi.GetBuildingsAsync(home.Id, ct);
        Task<PowerInfoDto>                      powerTask     = rimApi.GetPowerInfoAsync(home.Id, ct);
        Task<WeatherDto>                        weatherTask   = rimApi.GetWeatherAsync(home.Id, ct);
        Task<IReadOnlyList<LordDto>>            lordsTask     = rimApi.GetLordsAsync(home.Id, ct);
        Task<IReadOnlyList<IncidentDto>>        incidentsTask = rimApi.GetIncidentsAsync(home.Id, ct);
        Task<ResourcesSummaryDto>               resourcesTask = rimApi.GetResourcesSummaryAsync(home.Id, ct);
        Task<ResearchProgressDto>               researchTask  = rimApi.GetResearchProgressAsync(ct);

        await Task.WhenAll(stateTask, dateTask, pawnsTask, farmTask, zonesTask,
                           buildingsTask, powerTask, weatherTask, lordsTask, incidentsTask,
                           resourcesTask, researchTask);

        GameStateDto gs = stateTask.Result;
        state.Economy.Update(new EconomyLedger(
            gs.Tick, gs.Wealth, gs.Storyteller, gs.ProgramState, gs.Paused, dateTask.Result.DateTime));

        state.Colonists.Update(new ColonistRegistry(MapPawns(pawnsTask.Result)));

        state.Farm.Update(MapFarm(farmTask.Result));

        state.Stockpiles.Update(MapStockpiles(zonesTask.Result));

        state.Buildings.Update(new BuildingRegistry(MapBuildings(buildingsTask.Result)));

        PowerInfoDto p = powerTask.Result;
        state.Power.Update(new PowerNetwork(p.Production, p.Consumption, p.Stored, p.Capacity));

        WeatherDto w = weatherTask.Result;
        state.Weather.Update(new WeatherSnapshot(w.Def, w.Temperature, w.RainRate));

        state.Threats.Update(new ThreatBoard(
            MapLords(lordsTask.Result),
            MapIncidents(incidentsTask.Result)));

        state.Resources.Update(MapResources(resourcesTask.Result));
        state.Research.Update(MapResearch(researchTask.Result));
    }

    // ── Mappers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<ColonistRecord> MapPawns(IReadOnlyList<ColonistDetailedDto> pawns) =>
        pawns
            .Where(p => p.Pawn is not null)
            .Select(p =>
            {
                ColonistBasicDto basic    = p.Pawn!;
                PawnWorkInfoDto? work     = p.Detailes?.WorkInfo;
                PawnMedicalInfoDto? med   = p.Detailes?.MedicalInfo;
                IReadOnlyList<SkillDto> sk = work?.Skills ?? [];
                IReadOnlyList<TraitDto> tr = work?.Traits ?? [];

                return new ColonistRecord(
                    Id:         basic.Id.ToString(),
                    Name:       basic.Name ?? $"pawn_{basic.Id}",
                    Age:        basic.Age,
                    Gender:     basic.Gender ?? "",
                    Health:     basic.Health,
                    Mood:       basic.Mood,
                    Hunger:     basic.Hunger,
                    IsDowned:   med?.IsDowned ?? false,
                    IsDead:     med?.IsDead   ?? false,
                    CurrentJob: work?.CurrentJob,
                    Skills:     sk.Select(s => new ColonistSkill(s.Name, s.Level, PassionName(s.Passion))).ToList(),
                    Traits:     tr.Select(t => t.Name).ToList()
                );
            })
            .ToList();

    private static string PassionName(int p) => p switch
    {
        1 => "Minor",
        2 => "Major",
        _ => "None"
    };

    private static FarmSnapshot MapFarm(FarmSummaryDto f) =>
        new(
            TotalCrops:     f.TotalCrops,
            AverageGrowth:  f.AvgGrowth,
            ReadyToHarvest: f.ReadyToHarvest,
            CropBreakdown:  (f.CropBreakdown ?? [])
                                .Select(c => new CropTypeCount(c.Def, c.Count, c.AvgGrowth))
                                .ToList()
        );

    private static StockpileLedger MapStockpiles(IReadOnlyList<ZoneDto> zones)
    {
        // /map/zones gives us zone metadata but no item lists. Per-def counts now
        // come from /api/v1/resources/summary (see ResourceSummary aggregate);
        // ItemsByDef here stays empty by design.
        List<StockpileZone> stockpileZones = zones
            .Where(z => string.Equals(z.Type, "StockpileZone", StringComparison.OrdinalIgnoreCase))
            .Select(z => new StockpileZone(z.Id, z.Type, z.Label, z.Cells?.Count ?? 0))
            .ToList();

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

    private static ResourceSummary MapResources(ResourcesSummaryDto r)
    {
        FoodSummaryDto? food = r.CriticalResources?.FoodSummary;
        return new ResourceSummary(
            TotalItems:       r.TotalItems,
            TotalMarketValue: r.TotalMarketValue,
            FoodTotal:        food?.FoodTotal      ?? 0,
            TotalNutrition:   food?.TotalNutrition ?? 0f,
            MealsCount:       food?.MealsCount     ?? 0,
            RawFoodCount:     food?.RawFoodCount   ?? 0,
            MedicineTotal:    r.CriticalResources?.MedicineTotal ?? 0,
            WeaponCount:      r.CriticalResources?.WeaponCount   ?? 0,
            WeaponValue:      r.CriticalResources?.WeaponValue   ?? 0f
        );
    }

    private static ResearchInfo MapResearch(ResearchProgressDto r)
    {
        // RIMAPI sentinel for "no project selected": name == "none", label == "None".
        bool hasProject = !string.IsNullOrEmpty(r.Name) &&
                          !r.Name.Equals("none", StringComparison.OrdinalIgnoreCase);

        return new ResearchInfo(
            CurrentProject: hasProject ? (r.Label ?? r.Name) : null,
            Progress:       hasProject ? r.ProgressPercent / 100f : null,
            IsFinished:     r.IsFinished
        );
    }
}
