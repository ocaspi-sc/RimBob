using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Ingestion;
using RimBob.Ingestion.Dtos;
using RimBob.State;
using RimBob.State.Derivations;
using RimBob.Tests.Infrastructure;

namespace RimBob.Tests.State;

public sealed class IngestionDispatcherTests
{
    [Fact]
    public async Task RefreshAllAsync_HappyPath_BumpsEveryAggregateVersion()
    {
        var router = StandardRouter();
        using var http = MakeClient(router);
        var rim = new RimApiClient(http);
        var s = new ColonyState();
        var dispatcher = new IngestionDispatcher(rim, s, new TestLogger<IngestionDispatcher>());

        await dispatcher.RefreshAllAsync();

        s.Map.Version.Should().Be(1);
        s.Economy.Version.Should().Be(1);
        s.Colonists.Version.Should().Be(1);
        s.Stockpiles.Version.Should().Be(1);
        s.Buildings.Version.Should().Be(1);
        s.WorkTables.Version.Should().Be(1);
        s.Power.Version.Should().Be(1);
        s.Threats.Version.Should().Be(1);
        s.Weather.Version.Should().Be(1);
        s.Farm.Version.Should().Be(1);
        s.Plants.Version.Should().Be(1);
        s.Things.Version.Should().Be(1);
        s.ThingDefs.Version.Should().Be(1);
        s.AnimalDefs.Version.Should().Be(1);
        s.Terrain.Version.Should().Be(1);
        s.StoredResources.Version.Should().Be(1);
        s.Animals.Version.Should().Be(1);
        s.Resources.Version.Should().Be(1);
        s.Research.Version.Should().Be(1);
        s.LastRefreshSource.Should().Be(ColonyStateOrigin.Live);
        s.LastLiveRefreshAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RefreshAllAsync_Success_PersistsColonySnapshot()
    {
        string path = NewTempSnapshotPath();
        try
        {
            PathRouter router = StandardRouter();
            using HttpClient http = MakeClient(router);
            ColonyState state = new();
            ColonyStateSnapshotStore store = new(path, new TestLogger<ColonyStateSnapshotStore>());
            IngestionDispatcher dispatcher = new(
                new RimApiClient(http),
                state,
                new TestLogger<IngestionDispatcher>(),
                store);

            await dispatcher.RefreshAllAsync();

            File.Exists(path).Should().BeTrue();
            ColonySnapshotStatus status = store.GetStatus();
            status.HasSnapshot.Should().BeTrue();
            status.LastSaveError.Should().BeNull();

            ColonyStateSnapshotStore loaded = await ColonyStateSnapshotStore.LoadAsync(
                path,
                new TestLogger<ColonyStateSnapshotStore>());
            loaded.Latest.Should().NotBeNull();
            loaded.Latest!.GameTick.Should().Be(12_345);
            loaded.Latest.MapId.Should().Be(0);
        }
        finally
        {
            DeleteTempFile(path);
        }
    }

    [Fact]
    public async Task RefreshAllAsync_Failure_DoesNotPersistColonySnapshot()
    {
        string path = NewTempSnapshotPath();
        try
        {
            PathRouter router = new PathRouter()
                .Add("api/v1/maps", Envelope(new List<MapInfoDto>()));
            using HttpClient http = MakeClient(router);
            ColonyStateSnapshotStore store = new(path, new TestLogger<ColonyStateSnapshotStore>());
            IngestionDispatcher dispatcher = new(
                new RimApiClient(http),
                new ColonyState(),
                new TestLogger<IngestionDispatcher>(),
                store);

            Func<Task> act = () => dispatcher.RefreshAllAsync();

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*no maps*");
            File.Exists(path).Should().BeFalse();
            store.GetStatus().HasSnapshot.Should().BeFalse();
        }
        finally
        {
            DeleteTempFile(path);
        }
    }

    [Fact]
    public async Task RefreshAllAsync_PopulatesAggregatesFromDtos()
    {
        using var http = MakeClient(StandardRouter());
        var s = new ColonyState();
        var dispatcher = new IngestionDispatcher(
            new RimApiClient(http), s, new TestLogger<IngestionDispatcher>());

        await dispatcher.RefreshAllAsync();

        s.Economy.Value.ColonyWealth.Should().Be(7500f);
        s.Economy.Value.DateTimeRaw.Should().Be("5th of Aprimay, 5500, 14h");
        s.Colonists.Value.Colonists.Should().HaveCount(1);
        s.Colonists.Value.Colonists[0].Name.Should().Be("Alice");
        s.Colonists.Value.Colonists[0].Position.Should().BeEquivalentTo(new { X = 10, Y = 0, Z = 20 });
        s.Colonists.Value.Colonists[0].IsDowned.Should().BeFalse();
        s.Colonists.Value.Colonists[0].IsDead.Should().BeFalse();
        s.Plants.Value.Plants.Single().Position.Should().BeEquivalentTo(new { X = 12, Y = 0, Z = 22 });
        s.Things.Value.Things.Should().ContainSingle()
            .Which.Def.Should().Be("MealSurvivalPack");
        s.ThingDefs.Value.DefsByName.Should().ContainKey("MealSurvivalPack");
        s.AnimalDefs.Value.DefsByName.Should().ContainKey("Hare");
        s.Terrain.Value.CellCountsByDef.Should().ContainKey("Soil").WhoseValue.Should().Be(4);
        s.Terrain.Value.DefsByName.Should().ContainKey("Soil").WhoseValue.Fertility.Should().Be(1f);
        s.StoredResources.Value.CountByDef.Should().ContainKey("MealSurvivalPack").WhoseValue.Should().Be(9);
        s.Stockpiles.Value.ItemsByDef.Should().ContainKey("MealSurvivalPack").WhoseValue.Should().Be(9);
        s.Animals.Value.Animals.Single().Position.Should().BeEquivalentTo(new { X = 40, Y = 0, Z = 45 });
        s.Stockpiles.Value.Zones.Single().Center.Should().BeEquivalentTo(new { X = 2, Y = 0, Z = 2 });
        s.Buildings.Value.Buildings.Single().Position.Should().BeEquivalentTo(new { X = 5, Y = 0, Z = 5 });
        s.Buildings.Value.Buildings.Single().Label.Should().Be("wooden bed");
        s.Power.Value.ProductionW.Should().Be(2000f);
        s.Threats.Value.Lords.Should().ContainSingle()
            .Which.JobType.Should().Be("Raid");
    }

    [Fact]
    public async Task RefreshAllAsync_CookingBills_FlowIntoFoodBriefingKitchen()
    {
        PathRouter router = StandardRouter()
            .Add("api/v1/map/buildings?map_id", Envelope(new List<BuildingDto>
            {
                new(10, "FueledStove", "fueled stove", "Building_WorkTable_HeatPush", new PositionDto(5, 0, 5))
            }))
            .Add("api/v1/buildings/bills?building_id=10", Json("""
                {
                  "success": true,
                  "data": [
                    {
                      "load_id": 7,
                      "recipe_def_name": "CookMealSimple",
                      "recipe_label": "cook simple meal",
                      "suspended": false,
                      "paused": false,
                      "repeat_mode": "TargetCount",
                      "repeat_count": 1,
                      "target_count": 12
                    }
                  ],
                  "errors": [],
                  "warnings": [],
                  "timestamp": null
                }
                """));
        using HttpClient http = MakeClient(router);
        ColonyState s = new();
        IngestionDispatcher dispatcher = new(
            new RimApiClient(http), s, new TestLogger<IngestionDispatcher>());

        await dispatcher.RefreshAllAsync();

        s.WorkTables.Value.WorkTables.Should().ContainSingle()
            .Which.Bills.Should().ContainSingle()
            .Which.TargetCount.Should().Be(12);
        FoodBriefing briefing = FoodBriefingDerivation.Compute(s);
        briefing.Kitchen.CookingBills.Should().ContainSingle()
            .Which.RecipeDefName.Should().Be("CookMealSimple");
        briefing.Kitchen.SingleCookingBuilding.Should().NotBeNull();
        briefing.Kitchen.SingleCookingBuilding!.Label.Should().Be("fueled stove");
    }

    [Fact]
    public async Task RefreshAllAsync_LiveZoneWrapperShape_FlowsIntoStockpiles()
    {
        PathRouter router = StandardRouter()
            .Add("api/v1/map/zones?map_id", Json("""
                {
                  "success": true,
                  "data": {
                    "zones": [
                      {
                        "id": 0,
                        "cells_count": 56,
                        "label": "Stockpile zone 1",
                        "base_label": "Stockpile zone",
                        "type": "Zone_Stockpile"
                      }
                    ],
                    "areas": []
                  },
                  "errors": [],
                  "warnings": [],
                  "timestamp": null
                }
                """));
        using HttpClient http = MakeClient(router);
        ColonyState s = new();
        IngestionDispatcher dispatcher = new(
            new RimApiClient(http), s, new TestLogger<IngestionDispatcher>());

        await dispatcher.RefreshAllAsync();

        StockpileZone zone = s.Stockpiles.Value.Zones.Should().ContainSingle().Subject;
        zone.Id.Should().Be("0");
        zone.CellCount.Should().Be(56);
    }

    [Fact]
    public async Task RefreshAllAsync_NumericAnimalIds_FlowIntoFoodBriefing()
    {
        PathRouter router = StandardRouter()
            .Add("api/v1/map/animals?map_id", Json("""
                {
                  "success": true,
                  "data": [
                    {
                      "id": 123,
                      "def": "Hare",
                      "name": null,
                      "tame": false,
                      "health": 1.0,
                      "position": { "x": 40, "y": 0, "z": 45 }
                    }
                  ],
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """));
        using HttpClient http = MakeClient(router);
        ColonyState s = new();
        IngestionDispatcher dispatcher = new(
            new RimApiClient(http), s, new TestLogger<IngestionDispatcher>());

        await dispatcher.RefreshAllAsync();

        s.Animals.Value.Animals.Should().ContainSingle()
            .Which.Id.Should().Be("123");
        FoodBriefing briefing = FoodBriefingDerivation.Compute(s);
        briefing.WildAnimalCount.Should().Be(1);
        briefing.WildHuntTargets.Should().ContainSingle()
            .Which.Def.Should().Be("Hare");
        briefing.DataCoverage.HasAnimalPositions.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshAllAsync_LiveAnimalShapeWithoutHealth_DefaultsToHealthy()
    {
        PathRouter router = StandardRouter()
            .Add("api/v1/map/animals?map_id", Json("""
                {
                  "success": true,
                  "data": [
                    {
                      "id": 37382,
                      "name": "Ibex ram",
                      "def": "Ibex",
                      "position": { "x": 30, "y": 152, "z": 0 },
                      "pregnant": false
                    }
                  ],
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """));
        using HttpClient http = MakeClient(router);
        ColonyState s = new();
        IngestionDispatcher dispatcher = new(
            new RimApiClient(http), s, new TestLogger<IngestionDispatcher>());

        await dispatcher.RefreshAllAsync();

        s.Animals.Value.Animals.Should().ContainSingle()
            .Which.Health.Should().Be(1.0f);
        FoodBriefing briefing = FoodBriefingDerivation.Compute(s);
        briefing.WildAnimalCount.Should().Be(1);
        briefing.WildHuntTargets.Should().ContainSingle()
            .Which.Def.Should().Be("Ibex");
        briefing.DataCoverage.HasAnimalPositions.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshAllAsync_LiveFarmSummaryShape_FlowsIntoFoodBriefingCrops()
    {
        PathRouter router = StandardRouter()
            .Add("api/v1/map/farm/summary?map_id", Json("""
                {
                  "success": true,
                  "data": {
                    "total_growing_zones": 1,
                    "total_plants": 36,
                    "total_expected_yield": 0,
                    "total_infected_plants": 0,
                    "growth_progress_average": 5.0219183,
                    "crop_types": [
                      {
                        "plant_def_name": "Plant_Rice",
                        "plant_label": "rice plant",
                        "plant_category": "Crop",
                        "total_plants": 36,
                        "harvestable_plants": 0,
                        "expected_yield": 0,
                        "infected_count": 0,
                        "growth_progress_average": 5.0219183,
                        "days_until_harvest": 0.0,
                        "is_fully_grown": false,
                        "is_harvestable": false,
                        "zone_id": 2
                      }
                    ]
                  },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """))
            .Add("api/v1/map/plants?map_id", Json("""
                {
                  "success": true,
                  "data": [
                    {
                      "thing_id": 44187,
                      "def_name": "Plant_Rice",
                      "label": "rice plant",
                      "categories": ["Plants"],
                      "position": { "x": 85, "y": 0, "z": 190 },
                      "stack_count": 1,
                      "is_forbidden": false
                    }
                  ],
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """));
        using HttpClient http = MakeClient(router);
        ColonyState s = new();
        IngestionDispatcher dispatcher = new(
            new RimApiClient(http), s, new TestLogger<IngestionDispatcher>());

        await dispatcher.RefreshAllAsync();

        s.Farm.Value.CropBreakdown.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Def = "Plant_Rice",
                Count = 36,
                ZoneId = "2",
                ReadyCount = 0
            });
        s.Plants.Value.Plants.Should().ContainSingle()
            .Which.IsCrop.Should().BeTrue();

        FoodBriefing briefing = FoodBriefingDerivation.Compute(s);
        briefing.CropBreakdown.Should().ContainSingle()
            .Which.Def.Should().Be("Plant_Rice");
        FoodCropZoneSummary cropZone = briefing.CropZoneSummaries.Should().ContainSingle().Which;
        cropZone.Def.Should().Be("Plant_Rice");
        cropZone.ZoneId.Should().Be("2");
        cropZone.Count.Should().Be(36);
        cropZone.AverageGrowth.Should().BeApproximately(0.050219f, 0.00001f);
    }

    [Fact]
    public async Task RefreshAllAsync_LiveStoredSurvivalMeals_FlowIntoFoodBriefing()
    {
        PathRouter router = StandardRouter()
            .Add("api/v1/resources/summary?map_id", Json("""
                {
                  "success": true,
                  "data": {
                    "total_items": 100,
                    "total_market_value": 1200.0,
                    "critical_resources": {
                      "food_summary": {
                        "food_total": 52,
                        "total_nutrition": 1.64,
                        "meals_count": 0,
                        "raw_food_count": 0
                      },
                      "medicine_total": 0,
                      "weapon_count": 0,
                      "weapon_value": 0.0
                    }
                  },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """))
            .Add("api/v1/resources/stored?map_id", Json("""
                {
                  "success": true,
                  "data": {
                    "food_meals": [
                      { "thing_id": "m1", "def_name": "MealSurvivalPack", "label": "packaged survival meal", "stack_count": 9, "is_forbidden": false },
                      { "thing_id": "m2", "def_name": "MealSurvivalPack", "label": "packaged survival meal", "stack_count": 9, "is_forbidden": false },
                      { "thing_id": "m3", "def_name": "MealSurvivalPack", "label": "packaged survival meal", "stack_count": 9, "is_forbidden": false },
                      { "thing_id": "m4", "def_name": "MealSurvivalPack", "label": "packaged survival meal", "stack_count": 5, "is_forbidden": false }
                    ],
                    "plant_food_raw": [
                      { "thing_id": "b1", "def_name": "RawBerries", "label": "berries", "stack_count": 12, "is_forbidden": false }
                    ]
                  },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """));
        using HttpClient http = MakeClient(router);
        ColonyState s = new();
        IngestionDispatcher dispatcher = new(
            new RimApiClient(http), s, new TestLogger<IngestionDispatcher>());

        await dispatcher.RefreshAllAsync();

        s.StoredResources.Value.CountByDef.Should().ContainKey("MealSurvivalPack").WhoseValue.Should().Be(32);
        s.Stockpiles.Value.ItemsByDef.Should().ContainKey("MealSurvivalPack").WhoseValue.Should().Be(32);
        FoodBriefing briefing = FoodBriefingDerivation.Compute(s);
        briefing.NutritionSource.Should().Be("item_def_catalog");
        briefing.MealsCount.Should().Be(32);
        briefing.RawFoodCount.Should().Be(12);
        briefing.FallbackNutrition.Should().BeApproximately(29.4f, 0.001f);
        briefing.UnclassifiedFoodUnits.Should().Be(8);
    }

    [Fact]
    public async Task RefreshAllAsync_PawnMedicalInfo_FlowsThroughToColonistRecord()
    {
        // Use the standard router but swap in a colonists payload with a downed pawn
        // and a pawn with null medical_info, to cover both branches of the mapper.
        var pawnDowned = new ColonistDetailedDto(
            Pawn: new ColonistBasicDto(2, "Bob", "Male", 35, 0.9f, 0.6f, 1.0f, null),
            Detailes: new ColonistDetailsDto(
                WorkInfo: null,
                MedicalInfo: new PawnMedicalInfoDto(IsDead: false, IsDowned: true, Hediffs: [])));
        var pawnNoMedical = new ColonistDetailedDto(
            Pawn: new ColonistBasicDto(3, "Carol", "Female", 22, 1.0f, 0.7f, 1.0f, null),
            Detailes: new ColonistDetailsDto(WorkInfo: null, MedicalInfo: null));
        var router = StandardRouterWithoutColonists()
            .Add("api/v2/colonists/detailed",
                 Envelope(new List<ColonistDetailedDto> { pawnDowned, pawnNoMedical }));

        using var http = MakeClient(router);
        var s = new ColonyState();
        var dispatcher = new IngestionDispatcher(
            new RimApiClient(http), s, new TestLogger<IngestionDispatcher>());

        await dispatcher.RefreshAllAsync();

        var bob   = s.Colonists.Value.Colonists.Single(c => c.Name == "Bob");
        var carol = s.Colonists.Value.Colonists.Single(c => c.Name == "Carol");

        bob.IsDowned.Should().BeTrue();
        bob.IsDead.Should().BeFalse();
        carol.IsDowned.Should().BeFalse();   // null medical_info defaults to false
        carol.IsDead.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAllAsync_NoMaps_Throws()
    {
        var router = new PathRouter()
            .Add("api/v1/maps", Envelope(new List<MapInfoDto>()));
        using var http = MakeClient(router);
        var dispatcher = new IngestionDispatcher(
            new RimApiClient(http), new ColonyState(), new TestLogger<IngestionDispatcher>());

        var act = () => dispatcher.RefreshAllAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no maps*");
    }

    // ── fixtures ──────────────────────────────────────────────────────────────

    private static PathRouter StandardRouter()
    {
        var pawn = new ColonistDetailedDto(
            Pawn: new ColonistBasicDto(
                Id: 1, Name: "Alice", Gender: "Female", Age: 28,
                Health: 1.0f, Mood: 0.7f, Hunger: 1.0f, Position: new PositionDto(10, 0, 20)),
            Detailes: new ColonistDetailsDto(
                WorkInfo: new PawnWorkInfoDto(
                    Skills: [new SkillDto("Plants", 12, 2)],
                    CurrentJob: "Sowing",
                    Traits: [new TraitDto("Industrious", "industrious")]),
                MedicalInfo: new PawnMedicalInfoDto(
                    IsDead: false, IsDowned: false, Hediffs: [])));

        return StandardRouterWithoutColonists()
            .Add("api/v2/colonists/detailed", Envelope(new List<ColonistDetailedDto> { pawn }));
    }

    private static PathRouter StandardRouterWithoutColonists()
    {
        var map = new MapInfoDto(0, 0, true, false, "10", 1, "(250,1,250)");
        var state = new GameStateDto(
            Tick: 12345, Wealth: 7500f, ColonistCount: 1,
            Storyteller: "Cassandra", Paused: false, ProgramState: "Playing", MapCount: 1);
        var date = new DateTimeDto("5th of Aprimay, 5500, 14h");
        var farm = new FarmSummaryDto(50, 0.6f, 5, [new CropBreakdownDto("Rice", 30, 0.7f)]);
        var plants = new List<PlantDto>
        {
            new("plant1", "BerryBush", 1.0f, new PositionDto(12, 0, 22), false, null)
        };
        const string thingsJson = """
            {
              "success": true,
              "data": [
                {
                  "thing_id": "meal-stack-1",
                  "def_name": "MealSurvivalPack",
                  "label": "packaged survival meal",
                  "categories": ["FoodMeals"],
                  "position": { "x": 14, "y": 0, "z": 22 },
                  "stack_count": 9,
                  "market_value": 216.0,
                  "is_forbidden": false
                }
              ],
              "errors": null,
              "warnings": null,
              "timestamp": null
            }
            """;
        const string thingDefsJson = """
            {
              "success": true,
              "data": {
                "things_defs": [
                  {
                    "def_name": "MealSurvivalPack",
                    "label": "packaged survival meal",
                    "category": "Item",
                    "thing_class": "ThingWithComps",
                    "is_item": true,
                    "is_plant": false,
                    "is_medicine": false,
                    "is_drug": false,
                    "nutrition": 0.9,
                    "stack_limit": 10
                  },
                  {
                    "def_name": "RawBerries",
                    "label": "berries",
                    "category": "Item",
                    "thing_class": "ThingWithComps",
                    "is_item": true,
                    "is_plant": false,
                    "is_medicine": false,
                    "is_drug": false,
                    "nutrition": 0.05,
                    "stack_limit": 75
                  }
                ],
                "terrain_defs": [
                  {
                    "def_name": "Soil",
                    "label": "soil",
                    "fertility": 1.0,
                    "affordances": ["Walkable", "GrowSoil"]
                  }
                ],
                "animal_defs": [
                  {
                    "def_name": "Hare",
                    "label": "hare",
                    "base_body_size": 0.2,
                    "base_health_scale": 1.0,
                    "predator": false,
                    "herd_animal": false,
                    "pack_animal": false,
                    "is_insect": false,
                    "explosive": false,
                    "manhunter_on_damage_chance": 0.0,
                    "wildness": 0.6,
                    "meat_amount": 28,
                    "estimated_meat_nutrition": 1.4,
                    "leather_amount": 7,
                    "leather_def": "Leather_Plain",
                    "petness": 0.1
                  }
                ],
                "incidents_defs": []
              },
              "errors": null,
              "warnings": null,
              "timestamp": null
            }
            """;
        const string storedResourcesJson = """
            {
              "success": true,
              "data": {
                "food_meals": [
                  {
                    "thing_id": "meal-stack-1",
                    "def_name": "MealSurvivalPack",
                    "label": "packaged survival meal",
                    "position": { "x": 14, "y": 0, "z": 22 },
                    "stack_count": 9,
                    "market_value": 216.0,
                    "is_forbidden": false
                  }
                ]
              },
              "errors": null,
              "warnings": null,
              "timestamp": null
            }
            """;
        var animals = new List<AnimalDto>
        {
            new("animal1", "Hare", null, false, 1.0f, new PositionDto(40, 0, 45))
        };
        var buildings = new List<BuildingDto>
        {
            new(1, "Bed", "wooden bed", "Building_Bed", new PositionDto(5, 0, 5))
        };
        var power = new PowerInfoDto(2000f, 1500f, 100f, 500f);
        var weather = new WeatherDto("Clear", 18f, 0f);
        var lords = new List<LordDto>
        {
            new("l1", "Raid", "Pirates", ["p1","p2"], 350f)
        };
        var resources = new ResourcesSummaryDto(
            TotalItems: 200, TotalMarketValue: 4500f,
            CriticalResources: new CriticalResourcesDto(
                FoodSummary: new FoodSummaryDto(
                    FoodTotal: 80, TotalNutrition: 45.0f, MealsCount: 20, RawFoodCount: 40),
                MedicineTotal: 15, WeaponCount: 4, WeaponValue: 800f));
        var research = new ResearchProgressDto(
            Name: "Microelectronics", Label: "Microelectronics",
            Progress: 1500f, ResearchPoints: 3000f,
            IsFinished: false, CanStartNow: true,
            ProgressPercent: 50f);

        return new PathRouter()
            .Add("api/v1/maps",                    Envelope(new List<MapInfoDto> { map }))
            .Add("api/v1/game/state",              Envelope(state))
            .Add("api/v1/datetime",                Envelope(date))
            .Add("api/v1/map/farm/summary",        Envelope(farm))
            .Add("api/v1/map/plants",              Envelope(plants))
            .Add("api/v1/map/things",              Json(thingsJson))
            .Add("api/v1/def/all",                 Json(thingDefsJson))
            .Add("api/v1/resources/stored",        Json(storedResourcesJson))
            .Add("api/v1/map/animals",             Envelope(animals))
            .Add("api/v1/map/zones",               Json("""
                {
                  "success": true,
                  "data": {
                    "zones": [
                      {
                        "id": "z1",
                        "type": "StockpileZone",
                        "label": "main",
                        "cells": [
                          { "x": 1, "y": 0, "z": 1 },
                          { "x": 3, "y": 0, "z": 3 }
                        ]
                      }
                    ],
                    "areas": []
                  },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """))
            .Add("api/v1/map/terrain",             Json("""
                {
                  "success": true,
                  "data": {
                    "width": 2,
                    "height": 2,
                    "palette": ["Soil"],
                    "grid": [4, 0],
                    "floor_palette": [],
                    "floor_grid": [4, 0]
                  },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """))
            .Add("api/v1/map/buildings",           Envelope(buildings))
            .Add("api/v1/map/power/info",          Envelope(power))
            .Add("api/v1/map/weather",             Envelope(weather))
            .Add("api/v1/lords",                   Envelope(lords))
            .Add("api/v1/incidents",               Json("""
                {
                  "success": true,
                  "data": {
                    "incidents": [
                      {
                        "incident_def": "RaidEnemy",
                        "days_since_occurred": 0.5,
                        "label": "Raider attack"
                      }
                    ]
                  },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """))
            .Add("api/v1/resources/summary",       Envelope(resources))
            .Add("api/v1/research/progress",       Envelope(research));
    }

    private static string NewTempSnapshotPath() =>
        Path.Combine(Path.GetTempPath(), "rimbob-ingestion-snapshot-tests", Guid.NewGuid().ToString("N"), "latest-colony-state.json");

    private static void DeleteTempFile(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private static HttpClient MakeClient(PathRouter router) =>
        new(router) { BaseAddress = new Uri("http://localhost:8765/") };

    private static HttpContent Envelope<T>(T data) =>
        JsonContent.Create(new RimApiEnvelope<T>(
            Success: true, Data: data, Errors: null, Warnings: null, Timestamp: null));

    private static HttpContent Json(string json) =>
        new StringContent(json, Encoding.UTF8, "application/json");

    private sealed class PathRouter : HttpMessageHandler
    {
        private readonly List<(string segment, HttpContent content)> _routes = [];

        public PathRouter Add(string pathSegment, HttpContent content)
        {
            _routes.Add((pathSegment, content));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.PathAndQuery ?? "";
            // Longest match wins so "api/v1/map/farm/summary" beats "api/v1/map".
            foreach (var (segment, content) in _routes.OrderByDescending(r => r.segment.Length))
            {
                if (path.Contains(segment, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
