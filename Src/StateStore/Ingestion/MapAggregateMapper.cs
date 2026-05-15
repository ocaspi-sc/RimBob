using RimAI.Core.Aggregates;
using RimAI.Ingestion.Dtos;

namespace RimAI.State;

public static class MapAggregateMapper
{
    public static MapInfoSnapshot FromMap(MapInfoDto map) =>
        new(map.Id, map.Size);

    public static EconomyLedger FromGameState(GameStateDto state, DateTimeDto date) =>
        new(state.Tick, state.Wealth, state.Storyteller, state.ProgramState, state.Paused, date.DateTime);

    public static FarmSnapshot FromFarm(FarmSummaryDto farm) =>
        new(
            TotalCrops: farm.TotalCrops,
            AverageGrowth: farm.AvgGrowth,
            ReadyToHarvest: farm.ReadyToHarvest,
            CropBreakdown: (farm.CropBreakdown ?? [])
                .Select(crop => new CropTypeCount(crop.Def, crop.Count, crop.AvgGrowth))
                .ToList());

    public static PlantRegistry FromPlants(IReadOnlyList<PlantDto> plants) =>
        new(plants
            .Select(plant => new PlantRecord(
                plant.Id,
                plant.Def,
                plant.Growth,
                plant.IsCrop,
                plant.ZoneId,
                MapPosition(plant.Position)))
            .ToList());

    public static AnimalRegistry FromAnimals(IReadOnlyList<AnimalDto> animals) =>
        new(animals
            .Select(animal => new AnimalRecord(
                animal.Id,
                animal.Def,
                animal.Tame,
                animal.Health,
                MapPosition(animal.Position)))
            .ToList());

    public static StockpileLedger FromStockpiles(IReadOnlyList<ZoneDto> zones)
    {
        // /map/zones gives zone metadata but no item lists. Per-def counts come
        // from /api/v1/resources/summary, so ItemsByDef stays empty by design.
        List<StockpileZone> stockpileZones = zones
            .Where(IsStockpileZone)
            .Select(zone => new StockpileZone(
                zone.Id,
                zone.Type,
                zone.Label,
                zone.Cells?.Count ?? zone.CellsCount ?? 0,
                CenterOf(zone.Cells)))
            .ToList();

        return new StockpileLedger(stockpileZones, new Dictionary<string, int>());
    }

    private static bool IsStockpileZone(ZoneDto zone) =>
        string.Equals(zone.Type, "StockpileZone", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(zone.Type, "Zone_Stockpile", StringComparison.OrdinalIgnoreCase) ||
        zone.Type.Contains("Stockpile", StringComparison.OrdinalIgnoreCase);

    public static BuildingRegistry FromBuildings(IReadOnlyList<BuildingDto> buildings) =>
        new(buildings
            .Select(building => new BuildingRecord(
                building.Id.ToString(),
                building.Def,
                Hp: 1.0f,
                PowerOn: null,
                IsWorking: null,
                Position: MapPosition(building.Position)))
            .ToList());

    public static PowerNetwork FromPower(PowerInfoDto power) =>
        new(power.Production, power.Consumption, power.Stored, power.Capacity);

    public static WeatherSnapshot FromWeather(WeatherDto weather) =>
        new(weather.Def, weather.Temperature, weather.RainRate);

    public static MapPosition? MapPosition(PositionDto? position) =>
        position is null ? null : new MapPosition(position.X, position.Y, position.Z);

    private static MapPosition? CenterOf(IReadOnlyList<PositionDto>? cells)
    {
        if (cells is null || cells.Count == 0) return null;
        int x = (int)Math.Round(cells.Average(cell => cell.X));
        int y = (int)Math.Round(cells.Average(cell => cell.Y));
        int z = (int)Math.Round(cells.Average(cell => cell.Z));
        return new MapPosition(x, y, z);
    }
}
