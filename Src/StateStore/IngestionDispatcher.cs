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

        log.LogDebug("RIMAPI refresh selected home map {MapId} (size={MapSize})", home.Id, home.Size);
        state.Map.Update(MapAggregateMapper.FromMap(home));

        Task<GameStateDto>                      stateTask     = rimApi.GetGameStateAsync(ct);
        Task<DateTimeDto>                       dateTask      = rimApi.GetDateTimeAsync(ct);
        Task<IReadOnlyList<ColonistDetailedDto>> pawnsTask    = rimApi.GetColonistsDetailedAsync(home.Id, ct);
        Task<FarmSummaryDto>                    farmTask      = rimApi.GetFarmSummaryAsync(home.Id, ct);
        Task<IReadOnlyList<PlantDto>>           plantsTask    = rimApi.GetPlantsAsync(home.Id, ct);
        Task<IReadOnlyList<ThingDto>>           thingsTask    = rimApi.GetThingsAsync(home.Id, ct);
        Task<IReadOnlyList<ThingDefDto>>        thingDefsTask = rimApi.GetThingDefsAsync(ct);
        Task<StoredResourcesDto>                storedTask    = rimApi.GetStoredResourcesAsync(home.Id, ct);
        Task<IReadOnlyList<AnimalDto>>          animalsTask   = rimApi.GetAnimalsAsync(home.Id, ct);
        Task<IReadOnlyList<ZoneDto>>            zonesTask     = rimApi.GetZonesAsync(home.Id, ct);
        Task<IReadOnlyList<BuildingDto>>        buildingsTask = rimApi.GetBuildingsAsync(home.Id, ct);
        Task<PowerInfoDto>                      powerTask     = rimApi.GetPowerInfoAsync(home.Id, ct);
        Task<WeatherDto>                        weatherTask   = rimApi.GetWeatherAsync(home.Id, ct);
        Task<IReadOnlyList<LordDto>>            lordsTask     = rimApi.GetLordsAsync(home.Id, ct);
        Task<IReadOnlyList<IncidentDto>>        incidentsTask = rimApi.GetIncidentsAsync(home.Id, ct);
        Task<ResourcesSummaryDto>               resourcesTask = rimApi.GetResourcesSummaryAsync(home.Id, ct);
        Task<ResearchProgressDto>               researchTask  = rimApi.GetResearchProgressAsync(ct);

        await Task.WhenAll(stateTask, dateTask, pawnsTask, farmTask, plantsTask, thingsTask, thingDefsTask,
                           storedTask, animalsTask, zonesTask, buildingsTask, powerTask, weatherTask, lordsTask, incidentsTask,
                           resourcesTask, researchTask);

        GameStateDto gs = stateTask.Result;
        state.Economy.Update(MapAggregateMapper.FromGameState(gs, dateTask.Result));

        state.Colonists.Update(PawnAggregateMapper.FromColonists(pawnsTask.Result));

        state.Farm.Update(MapAggregateMapper.FromFarm(farmTask.Result));
        state.Plants.Update(MapAggregateMapper.FromPlants(plantsTask.Result));
        state.Things.Update(MapAggregateMapper.FromThings(thingsTask.Result));
        state.ThingDefs.Update(MapAggregateMapper.FromThingDefs(thingDefsTask.Result));
        StoredResourceRegistry storedResources = MapAggregateMapper.FromStoredResources(storedTask.Result);
        state.StoredResources.Update(storedResources);
        state.Animals.Update(MapAggregateMapper.FromAnimals(animalsTask.Result));

        state.Stockpiles.Update(MapAggregateMapper.FromStockpiles(zonesTask.Result, storedResources));

        state.Buildings.Update(MapAggregateMapper.FromBuildings(buildingsTask.Result));

        state.Power.Update(MapAggregateMapper.FromPower(powerTask.Result));

        state.Weather.Update(MapAggregateMapper.FromWeather(weatherTask.Result));

        state.Threats.Update(ThreatAggregateMapper.FromThreats(lordsTask.Result, incidentsTask.Result));

        state.Resources.Update(ResourceAggregateMapper.FromResources(resourcesTask.Result));
        state.Research.Update(ResourceAggregateMapper.FromResearch(researchTask.Result));
    }
}
