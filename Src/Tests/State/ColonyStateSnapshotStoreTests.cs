using FluentAssertions;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.State;
using RimBob.Tests.Infrastructure;
using AggregateWeatherSnapshot = RimBob.Core.Aggregates.WeatherSnapshot;

namespace RimBob.Tests.State;

public sealed class ColonyStateSnapshotStoreTests
{
    [Fact]
    public async Task SaveLoadRestore_RoundTripsCuratedSnapshotAndBriefings()
    {
        string path = NewTempSnapshotPath();
        try
        {
            ColonyState original = PopulatedState();
            ColonyStateSnapshotStore store = new(path, new TestLogger<ColonyStateSnapshotStore>());

            await store.SaveAsync(original);
            ColonyStateSnapshotStore loaded = await ColonyStateSnapshotStore.LoadAsync(
                path,
                new TestLogger<ColonyStateSnapshotStore>());

            loaded.Latest.Should().NotBeNull();
            ColonyStateSnapshot snapshot = loaded.Latest!;
            snapshot.SchemaVersion.Should().Be(ColonyStateSnapshot.CurrentSchemaVersion);
            snapshot.Source.Should().Be("live");
            snapshot.MapId.Should().Be(7);
            snapshot.GameTick.Should().Be(98_765);
            snapshot.GameDateRaw.Should().Be("6th of Aprimay, 5500, 9h");
            snapshot.SnapshotId.Should().NotBeNullOrWhiteSpace();
            snapshot.CapturedAt.Should().Be(original.LastLiveRefreshAt!.Value);
            snapshot.AggregateVersions.Should().ContainKey("Terrain").WhoseValue.Should().Be(1);
            snapshot.AggregateVersions.Should().ContainKey("Research").WhoseValue.Should().Be(1);

            ColonyState restored = new();
            loaded.RestoreInto(restored);

            restored.LastRefreshSource.Should().Be(ColonyStateOrigin.Snapshot);
            restored.LastLiveRefreshAt.Should().BeNull();
            restored.Terrain.Value.Should().BeEquivalentTo(original.Terrain.Value);
            restored.Stockpiles.Value.Should().BeEquivalentTo(original.Stockpiles.Value);
            restored.Colonists.Value.Should().BeEquivalentTo(original.Colonists.Value);
            restored.Terrain.Version.Should().Be(1);
            restored.Stockpiles.Version.Should().Be(1);
            restored.Colonists.Version.Should().Be(1);

            BriefingCache cache = new(restored, new TestLogger<BriefingCache>());
            MayorBriefing mayor = cache.GetMayorBriefing();
            FoodBriefing food = cache.GetFoodBriefing();
            mayor.GameTick.Should().Be(98_765);
            mayor.Colonists.Count.Should().Be(2);
            food.MealsCount.Should().Be(12);
            food.RawFoodCount.Should().Be(24);
            food.DataCoverage.HasLiveState.Should().BeFalse();
        }
        finally
        {
            DeleteTempFile(path);
        }
    }

    [Fact]
    public async Task LoadAsync_MissingMalformedAndSchemaMismatch_SoftFail()
    {
        string missingPath = NewTempSnapshotPath();
        string malformedPath = NewTempSnapshotPath();
        string schemaPath = NewTempSnapshotPath();

        try
        {
            ColonyStateSnapshotStore missing = await ColonyStateSnapshotStore.LoadAsync(
                missingPath,
                new TestLogger<ColonyStateSnapshotStore>());
            ColonySnapshotStatus missingStatus = missing.GetStatus();
            missing.Latest.Should().BeNull();
            missingStatus.HasSnapshot.Should().BeFalse();
            missingStatus.LoadError.Should().BeNull();

            Directory.CreateDirectory(Path.GetDirectoryName(malformedPath)!);
            await File.WriteAllTextAsync(malformedPath, "{");
            ColonyStateSnapshotStore malformed = await ColonyStateSnapshotStore.LoadAsync(
                malformedPath,
                new TestLogger<ColonyStateSnapshotStore>());
            malformed.Latest.Should().BeNull();
            malformed.GetStatus().LoadError.Should().Contain("Could not load");

            ColonyStateSnapshotStore seed = new(schemaPath, new TestLogger<ColonyStateSnapshotStore>());
            await seed.SaveAsync(PopulatedState());
            string validJson = await File.ReadAllTextAsync(schemaPath);
            string invalidSchemaJson = validJson.Replace("\"schema_version\": 1", "\"schema_version\": 999");
            await File.WriteAllTextAsync(schemaPath, invalidSchemaJson);

            ColonyStateSnapshotStore schemaMismatch = await ColonyStateSnapshotStore.LoadAsync(
                schemaPath,
                new TestLogger<ColonyStateSnapshotStore>());
            schemaMismatch.Latest.Should().BeNull();
            schemaMismatch.GetStatus().LoadError.Should().Contain("Unsupported colony state snapshot schema");
        }
        finally
        {
            DeleteTempFile(missingPath);
            DeleteTempFile(malformedPath);
            DeleteTempFile(schemaPath);
        }
    }

    private static ColonyState PopulatedState()
    {
        ColonyState state = new()
        {
            LastRefreshSource = ColonyStateOrigin.Live,
            LastLiveRefreshAt = DateTimeOffset.Parse("2026-05-18T07:00:00Z")
        };

        state.Map.Update(new MapInfoSnapshot(7, "(250,1,250)"));
        state.Economy.Update(new EconomyLedger(98_765, 12_500f, "Cassandra", "Playing", false, "6th of Aprimay, 5500, 9h"));
        state.Colonists.Update(new ColonistRegistry([
            new ColonistRecord(
                Id: "p1",
                Name: "Alice",
                Age: 28,
                Gender: "Female",
                Health: 0.93f,
                Mood: 0.71f,
                Hunger: 0.62f,
                IsDowned: false,
                IsDead: false,
                Position: new MapPosition(10, 0, 20),
                CurrentJob: "Sowing",
                Skills: [new ColonistSkill("Plants", 12, "Major")],
                Traits: ["Industrious"]),
            new ColonistRecord(
                Id: "p2",
                Name: "Bob",
                Age: 31,
                Gender: "Male",
                Health: 0.81f,
                Mood: 0.54f,
                Hunger: 0.44f,
                IsDowned: false,
                IsDead: false,
                Position: null,
                CurrentJob: "Cooking",
                Skills: [new ColonistSkill("Cooking", 9, "Minor")],
                Traits: ["Fast walker"])
        ]));
        state.Stockpiles.Update(new StockpileLedger(
            [new StockpileZone("z1", "StockpileZone", "main", 12, new MapPosition(12, 0, 20))],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["MealSurvivalPack"] = 12,
                ["RawBerries"] = 24
            }));
        state.Buildings.Update(new BuildingRegistry([
            new BuildingRecord("stove-1", "FueledStove", 1f, true, true, new MapPosition(11, 0, 19), "fueled stove"),
            new BuildingRecord("cooler-1", "Cooler", 1f, true, true, new MapPosition(14, 0, 20), "cooler")
        ]));
        state.WorkTables.Update(new WorkTableRegistry([
            new WorkTableRecord("stove-1", [
                new WorkTableBillRecord(5, "CookMealSimple", "cook simple meal", false, false, "TargetCount", 1, 16)
            ])
        ]));
        state.Power.Update(new PowerNetwork(2_000f, 1_250f, 300f, 600f));
        state.Threats.Update(new ThreatBoard(
            [new HostileLord("lord-1", "Raid", "Pirates", 150f, 2)],
            [new IncidentRecord("RaidEnemy", 0.3f, "raider attack")]));
        state.Weather.Update(new AggregateWeatherSnapshot("Clear", 22f, 0f));
        state.Farm.Update(new FarmSnapshot(40, 0.72f, 8, [new CropTypeCount("Plant_Rice", 40, 0.72f, "grow-1", 8)]));
        state.Plants.Update(new PlantRegistry([
            new PlantRecord("plant-1", "Plant_Rice", 0.72f, true, "grow-1", new MapPosition(15, 0, 22), true)
        ]));
        state.Things.Update(new ThingRegistry([
            new ThingRecord("meal-1", "MealSurvivalPack", "packaged survival meal", 12, ["FoodMeals"], false, new MapPosition(12, 0, 20), 216f),
            new ThingRecord("berries-1", "RawBerries", "berries", 24, ["PlantFoodRaw"], false, new MapPosition(13, 0, 20), 8f)
        ]));
        state.ThingDefs.Update(new ThingDefRegistry(new Dictionary<string, ThingDefRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["MealSurvivalPack"] = new("MealSurvivalPack", "packaged survival meal", "Item", "ThingWithComps", true, false, false, false, 0.9f, 10),
            ["RawBerries"] = new("RawBerries", "berries", "Item", "ThingWithComps", true, false, false, false, 0.05f, 75)
        }));
        state.Terrain.Update(new TerrainSnapshot(
            Width: 3,
            Height: 3,
            CellCountsByDef: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Soil"] = 6,
                ["Sand"] = 3
            },
            DefsByName: new Dictionary<string, TerrainDefRecord>(StringComparer.OrdinalIgnoreCase)
            {
                ["Soil"] = new("Soil", "soil", 1f, ["Walkable", "GrowSoil"]),
                ["Sand"] = new("Sand", "sand", 0.1f, ["Walkable"])
            }));
        state.StoredResources.Update(new StoredResourceRegistry(
            [
                new StoredResourceRecord("food_meals", "meal-1", "MealSurvivalPack", "packaged survival meal", 12, false, new MapPosition(12, 0, 20), 216f),
                new StoredResourceRecord("plant_food_raw", "berries-1", "RawBerries", "berries", 24, false, new MapPosition(13, 0, 20), 8f)
            ],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["MealSurvivalPack"] = 12,
                ["RawBerries"] = 24
            },
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["food_meals"] = 12,
                ["plant_food_raw"] = 24
            }));
        state.Animals.Update(new AnimalRegistry([
            new AnimalRecord("hare-1", "Hare", false, 1f, new MapPosition(21, 0, 25))
        ]));
        state.Resources.Update(new ResourceSummary(120, 3_400f, 36, 0f, 0, 0, 4, 2, 320f));
        state.Research.Update(new ResearchInfo("Microelectronics", 0.45f, false));

        return state;
    }

    private static string NewTempSnapshotPath() =>
        Path.Combine(Path.GetTempPath(), "rimbob-colony-snapshot-tests", Guid.NewGuid().ToString("N"), "latest-colony-state.json");

    private static void DeleteTempFile(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
