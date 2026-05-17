using System.Text.Json;
using RimBob.Core.Aggregates;
using RimBob.Ingestion.Dtos;

namespace RimBob.State;

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
            CropBreakdown: farm.CropBreakdown
                .Where(crop => !string.IsNullOrWhiteSpace(crop.Def) && crop.Count > 0)
                .Select(crop => new CropTypeCount(
                    crop.Def,
                    crop.Count,
                    crop.AvgGrowth,
                    crop.ZoneId,
                    crop.ReadyCount))
                .ToList());

    public static PlantRegistry FromPlants(IReadOnlyList<PlantDto> plants, FarmSnapshot? farm = null)
    {
        HashSet<string> knownCropDefs = (farm?.CropBreakdown ?? [])
            .Select(crop => crop.Def)
            .Where(def => !string.IsNullOrWhiteSpace(def))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new PlantRegistry(plants
            .Where(plant => !string.IsNullOrWhiteSpace(plant.Def))
            .Select(plant => new PlantRecord(
                plant.Id,
                plant.Def,
                plant.Growth,
                plant.IsCrop || knownCropDefs.Contains(plant.Def),
                plant.ZoneId,
                MapPosition(plant.Position),
                plant.IsHarvestable))
            .ToList());
    }

    public static ThingRegistry FromThings(IReadOnlyList<ThingDto> things) =>
        new(things
            .Where(thing => !string.IsNullOrWhiteSpace(thing.StableDef))
            .Select(thing => new ThingRecord(
                Id: StableThingId(thing),
                Def: thing.StableDef,
                Label: thing.Label,
                StackCount: thing.EffectiveStackCount,
                Categories: thing.Categories ?? [],
                IsForbidden: thing.IsForbidden,
                Position: MapPosition(thing.Position),
                MarketValue: thing.MarketValue))
            .ToList());

    public static ThingDefRegistry FromThingDefs(IReadOnlyList<ThingDefDto> defs) =>
        new(defs
            .Where(def => !string.IsNullOrWhiteSpace(def.DefName))
            .GroupBy(def => def.DefName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    ThingDefDto def = group.First();
                    return new ThingDefRecord(
                        Def: def.DefName,
                        Label: def.Label,
                        Category: def.Category,
                        ThingClass: def.ThingClass,
                        IsItem: def.IsItem,
                        IsPlant: def.IsPlant,
                        IsMedicine: def.IsMedicine,
                        IsDrug: def.IsDrug,
                        Nutrition: def.Nutrition,
                        StackLimit: def.StackLimit);
                },
                StringComparer.OrdinalIgnoreCase));

    public static TerrainSnapshot FromTerrain(
        TerrainGridDto terrain,
        IReadOnlyList<TerrainDefDto> defs)
    {
        Dictionary<string, TerrainDefRecord> defsByName = defs
            .Where(def => !string.IsNullOrWhiteSpace(def.DefName))
            .GroupBy(def => def.DefName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    TerrainDefDto def = group.First();
                    return new TerrainDefRecord(
                        Def: def.DefName,
                        Label: def.Label,
                        Fertility: def.Fertility,
                        Affordances: def.Affordances ?? []);
                },
                StringComparer.OrdinalIgnoreCase);

        return new TerrainSnapshot(
            Width: terrain.Width,
            Height: terrain.Height,
            CellCountsByDef: DecodeTerrainCounts(terrain),
            DefsByName: defsByName);
    }

    public static StoredResourceRegistry FromStoredResources(StoredResourcesDto stored)
    {
        List<StoredResourceRecord> items = [];
        foreach ((string category, JsonElement value) in stored.Categories)
        {
            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;

            if (value.ValueKind == JsonValueKind.Object && !value.EnumerateObject().Any())
                continue;

            if (value.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException($"RIMAPI schema drift at /resources/stored: category '{category}' was {value.ValueKind}, expected array.");

            foreach (JsonElement item in value.EnumerateArray())
            {
                ThingDto dto;
                try
                {
                    dto = item.Deserialize<ThingDto>()
                        ?? throw new JsonException("Stored resource item was null.");
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException(
                        $"RIMAPI schema drift at /resources/stored: category '{category}' item could not deserialize as ThingDto at {ex.Path ?? "unknown path"}.",
                        ex);
                }

                if (string.IsNullOrWhiteSpace(dto.StableDef))
                    continue;

                items.Add(new StoredResourceRecord(
                    Category: category,
                    Id: StableThingId(dto),
                    Def: dto.StableDef,
                    Label: dto.Label,
                    StackCount: dto.EffectiveStackCount,
                    IsForbidden: dto.IsForbidden,
                    Position: MapPosition(dto.Position),
                    MarketValue: dto.MarketValue));
            }
        }

        Dictionary<string, int> countByDef = items
            .Where(item => !item.IsForbidden)
            .GroupBy(item => item.Def, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.StackCount), StringComparer.OrdinalIgnoreCase);

        Dictionary<string, int> countByCategory = items
            .Where(item => !item.IsForbidden)
            .GroupBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.StackCount), StringComparer.OrdinalIgnoreCase);

        return new StoredResourceRegistry(items, countByDef, countByCategory);
    }

    public static AnimalRegistry FromAnimals(IReadOnlyList<AnimalDto> animals) =>
        new(animals
            .Select(animal => new AnimalRecord(
                animal.Id,
                animal.Def,
                animal.Tame,
                animal.Health ?? 1.0f,
                MapPosition(animal.Position)))
            .ToList());

    public static StockpileLedger FromStockpiles(
        IReadOnlyList<ZoneDto> zones,
        StoredResourceRegistry? storedResources = null)
    {
        // /map/zones gives zone metadata but no item lists. Per-def counts come
        // from /api/v1/resources/stored when that endpoint has category data.
        List<StockpileZone> stockpileZones = zones
            .Where(IsStockpileZone)
            .Select(zone => new StockpileZone(
                zone.Id,
                zone.Type,
                zone.Label,
                zone.Cells?.Count ?? zone.CellsCount ?? 0,
                CenterOf(zone.Cells)))
            .ToList();

        return new StockpileLedger(
            stockpileZones,
            storedResources?.CountByDef ?? new Dictionary<string, int>());
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

    private static string StableThingId(ThingDto thing) =>
        string.IsNullOrWhiteSpace(thing.StableId)
            ? $"{thing.StableDef}:{thing.Position?.X}:{thing.Position?.Y}:{thing.Position?.Z}"
            : thing.StableId;

    private static MapPosition? CenterOf(IReadOnlyList<PositionDto>? cells)
    {
        if (cells is null || cells.Count == 0) return null;
        int x = (int)Math.Round(cells.Average(cell => cell.X));
        int y = (int)Math.Round(cells.Average(cell => cell.Y));
        int z = (int)Math.Round(cells.Average(cell => cell.Z));
        return new MapPosition(x, y, z);
    }

    private static IReadOnlyDictionary<string, int> DecodeTerrainCounts(TerrainGridDto terrain)
    {
        IReadOnlyList<string> palette = terrain.Palette ?? [];
        IReadOnlyList<int> grid = terrain.Grid ?? [];
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);

        if (palette.Count == 0 || grid.Count == 0)
            return counts;

        if (grid.Count % 2 != 0)
            throw new InvalidOperationException("RIMAPI schema drift at /map/terrain: grid RLE length must be even.");

        int totalCells = 0;
        for (int i = 0; i < grid.Count; i += 2)
        {
            int runLength = grid[i];
            int paletteIndex = grid[i + 1];
            if (runLength < 0)
                throw new InvalidOperationException("RIMAPI schema drift at /map/terrain: grid RLE run length was negative.");
            if (paletteIndex < 0 || paletteIndex >= palette.Count)
                throw new InvalidOperationException($"RIMAPI schema drift at /map/terrain: palette index {paletteIndex} was outside palette size {palette.Count}.");

            string def = palette[paletteIndex];
            counts[def] = counts.TryGetValue(def, out int current) ? current + runLength : runLength;
            totalCells += runLength;
        }

        int expectedCells = terrain.Width * terrain.Height;
        if (expectedCells > 0 && totalCells != expectedCells)
            throw new InvalidOperationException($"RIMAPI schema drift at /map/terrain: decoded {totalCells} cells, expected {expectedCells}.");

        return counts;
    }
}
