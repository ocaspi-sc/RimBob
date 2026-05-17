using Microsoft.Extensions.Logging;
using RimBob.Core.Aggregates;
using RimBob.Ingestion;
using RimBob.Ingestion.Dtos;
using RimBob.State.Derivations.Common;

namespace RimBob.State;

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
        Task<DefCatalogDto>                     defCatalogTask = rimApi.GetDefCatalogAsync(ct);
        Task<StoredResourcesDto>                storedTask    = rimApi.GetStoredResourcesAsync(home.Id, ct);
        Task<IReadOnlyList<AnimalDto>>          animalsTask   = rimApi.GetAnimalsAsync(home.Id, ct);
        Task<IReadOnlyList<ZoneDto>>            zonesTask     = rimApi.GetZonesAsync(home.Id, ct);
        Task<TerrainGridDto>                    terrainTask   = rimApi.GetTerrainAsync(home.Id, ct);
        Task<IReadOnlyList<BuildingDto>>        buildingsTask = rimApi.GetBuildingsAsync(home.Id, ct);
        Task<PowerInfoDto>                      powerTask     = rimApi.GetPowerInfoAsync(home.Id, ct);
        Task<WeatherDto>                        weatherTask   = rimApi.GetWeatherAsync(home.Id, ct);
        Task<IReadOnlyList<LordDto>>            lordsTask     = rimApi.GetLordsAsync(home.Id, ct);
        Task<IReadOnlyList<IncidentDto>>        incidentsTask = rimApi.GetIncidentsAsync(home.Id, ct);
        Task<ResourcesSummaryDto>               resourcesTask = rimApi.GetResourcesSummaryAsync(home.Id, ct);
        Task<ResearchProgressDto>               researchTask  = rimApi.GetResearchProgressAsync(ct);

        await Task.WhenAll(stateTask, dateTask, pawnsTask, farmTask, plantsTask, thingsTask, defCatalogTask,
                           storedTask, animalsTask, zonesTask, terrainTask, buildingsTask, powerTask, weatherTask, lordsTask, incidentsTask,
                           resourcesTask, researchTask);

        GameStateDto gs = stateTask.Result;
        state.Economy.Update(MapAggregateMapper.FromGameState(gs, dateTask.Result));

        state.Colonists.Update(PawnAggregateMapper.FromColonists(pawnsTask.Result));

        FarmSnapshot farm = MapAggregateMapper.FromFarm(farmTask.Result);
        state.Farm.Update(farm);
        state.Plants.Update(MapAggregateMapper.FromPlants(plantsTask.Result, farm));
        state.Things.Update(MapAggregateMapper.FromThings(thingsTask.Result));
        DefCatalogDto defCatalog = defCatalogTask.Result;
        state.ThingDefs.Update(MapAggregateMapper.FromThingDefs(defCatalog.ThingsDefs ?? []));
        state.Terrain.Update(MapAggregateMapper.FromTerrain(terrainTask.Result, defCatalog.TerrainDefs ?? []));
        StoredResourceRegistry storedResources = MapAggregateMapper.FromStoredResources(storedTask.Result);
        state.StoredResources.Update(storedResources);
        state.Animals.Update(MapAggregateMapper.FromAnimals(animalsTask.Result));

        state.Stockpiles.Update(MapAggregateMapper.FromStockpiles(zonesTask.Result, storedResources));

        BuildingRegistry buildings = MapAggregateMapper.FromBuildings(buildingsTask.Result);
        state.Buildings.Update(buildings);
        state.WorkTables.Update(await ReadWorkTableBillsAsync(buildings, ct));

        state.Power.Update(MapAggregateMapper.FromPower(powerTask.Result));

        state.Weather.Update(MapAggregateMapper.FromWeather(weatherTask.Result));

        state.Threats.Update(ThreatAggregateMapper.FromThreats(lordsTask.Result, incidentsTask.Result));

        state.Resources.Update(ResourceAggregateMapper.FromResources(resourcesTask.Result));
        state.Research.Update(ResourceAggregateMapper.FromResearch(researchTask.Result));
    }

    private async Task<WorkTableRegistry> ReadWorkTableBillsAsync(
        BuildingRegistry buildings,
        CancellationToken ct)
    {
        List<WorkTableRecord> workTables = [];
        foreach (BuildingRecord building in buildings.Buildings.Where(BuildingClassifier.IsCookingBuilding))
        {
            if (!int.TryParse(building.Id, out int buildingId))
                continue;

            try
            {
                IReadOnlyList<WorkTableBillDto> bills = await rimApi.GetWorkTableBillsAsync(buildingId, ct);
                workTables.Add(MapAggregateMapper.FromWorkTableBills(building.Id, bills));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogWarning(
                    ex,
                    "Could not refresh bills for cooking work table {BuildingId}; Food bill state will be unknown.",
                    building.Id);
            }
        }

        return new WorkTableRegistry(workTables);
    }
}
