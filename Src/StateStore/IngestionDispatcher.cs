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
        Task<IReadOnlyList<PlantDto>>           plantsTask    = rimApi.GetPlantsAsync(home.Id, ct);
        Task<IReadOnlyList<AnimalDto>>          animalsTask   = rimApi.GetAnimalsAsync(home.Id, ct);
        Task<IReadOnlyList<ZoneDto>>            zonesTask     = rimApi.GetZonesAsync(home.Id, ct);
        Task<IReadOnlyList<BuildingDto>>        buildingsTask = rimApi.GetBuildingsAsync(home.Id, ct);
        Task<PowerInfoDto>                      powerTask     = rimApi.GetPowerInfoAsync(home.Id, ct);
        Task<WeatherDto>                        weatherTask   = rimApi.GetWeatherAsync(home.Id, ct);
        Task<IReadOnlyList<LordDto>>            lordsTask     = rimApi.GetLordsAsync(home.Id, ct);
        Task<IReadOnlyList<IncidentDto>>        incidentsTask = rimApi.GetIncidentsAsync(home.Id, ct);
        Task<ResourcesSummaryDto>               resourcesTask = rimApi.GetResourcesSummaryAsync(home.Id, ct);
        Task<ResearchProgressDto>               researchTask  = rimApi.GetResearchProgressAsync(ct);

        await Task.WhenAll(stateTask, dateTask, pawnsTask, farmTask, plantsTask, animalsTask, zonesTask,
                           buildingsTask, powerTask, weatherTask, lordsTask, incidentsTask,
                           resourcesTask, researchTask);

        GameStateDto gs = stateTask.Result;
        state.Economy.Update(new EconomyLedger(
            gs.Tick, gs.Wealth, gs.Storyteller, gs.ProgramState, gs.Paused, dateTask.Result.DateTime));

        state.Colonists.Update(new ColonistRegistry(MapPawns(pawnsTask.Result)));

        state.Farm.Update(MapFarm(farmTask.Result));
        state.Plants.Update(MapPlants(plantsTask.Result));
        state.Animals.Update(MapAnimals(animalsTask.Result));

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
                    Position:   MapPosition(basic.Position),
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

    private static PlantRegistry MapPlants(IReadOnlyList<PlantDto> plants) =>
        new(plants.Select(p => new PlantRecord(p.Id, p.Def, p.Growth, p.IsCrop, p.ZoneId, MapPosition(p.Position))).ToList());

    private static AnimalRegistry MapAnimals(IReadOnlyList<AnimalDto> animals) =>
        new(animals.Select(a => new AnimalRecord(a.Id, a.Def, a.Tame, a.Health, MapPosition(a.Position))).ToList());

    private static StockpileLedger MapStockpiles(IReadOnlyList<ZoneDto> zones)
    {
        // /map/zones gives us zone metadata but no item lists. Per-def counts now
        // come from /api/v1/resources/summary (see ResourceSummary aggregate);
        // ItemsByDef here stays empty by design.
        List<StockpileZone> stockpileZones = zones
            .Where(z => string.Equals(z.Type, "StockpileZone", StringComparison.OrdinalIgnoreCase))
            .Select(z => new StockpileZone(z.Id, z.Type, z.Label, z.Cells?.Count ?? 0, CenterOf(z.Cells)))
            .ToList();

        return new StockpileLedger(stockpileZones, new Dictionary<string, int>());
    }

    private static IReadOnlyList<BuildingRecord> MapBuildings(IReadOnlyList<BuildingDto> bs) =>
        // RIMAPI doesn't expose hp / power_on / is_working — default to "fine, unknown".
        bs.Select(b => new BuildingRecord(b.Id.ToString(), b.Def, Hp: 1.0f, PowerOn: null, IsWorking: null, Position: MapPosition(b.Position))).ToList();

    private static MapPosition? MapPosition(PositionDto? position) =>
        position is null ? null : new MapPosition(position.X, position.Y, position.Z);

    private static MapPosition? CenterOf(IReadOnlyList<PositionDto>? cells)
    {
        if (cells is null || cells.Count == 0) return null;
        int x = (int)Math.Round(cells.Average(c => c.X));
        int y = (int)Math.Round(cells.Average(c => c.Y));
        int z = (int)Math.Round(cells.Average(c => c.Z));
        return new MapPosition(x, y, z);
    }

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
