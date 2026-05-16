using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using RimAI.Ingestion;
using RimAI.Ingestion.Dtos;

namespace RimAI.Tests.Ingestion;

public sealed class RimApiClientTests
{
    private const string PngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=";

    // ── GetMapPawnsAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetMapPawns_WhenApiReturnsPawns_ReturnsMappedList()
    {
        var pawn = new MapPawnDto(1, "Bob", "Male", 30, 1.0f, 0.8f, 0.9f, null);
        using var http = MakeClient(new PathRouter()
            .Add("map/pawns", Envelope(new List<MapPawnDto> { pawn })));

        var result = await new RimApiClient(http).GetMapPawnsAsync(0);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Bob");
    }

    [Fact]
    public async Task GetMapPawns_WhenApiReturnsEmpty_ReturnsEmptyList()
    {
        using var http = MakeClient(new PathRouter()
            .Add("map/pawns", Envelope(Array.Empty<MapPawnDto>().ToList())));

        var result = await new RimApiClient(http).GetMapPawnsAsync(0);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAnimals_WhenApiReturnsNumericIds_ReturnsMappedList()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("map/animals", Json("""
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
                """)));

        IReadOnlyList<AnimalDto> result = await new RimApiClient(http).GetAnimalsAsync(0);

        AnimalDto animal = result.Should().ContainSingle().Subject;
        animal.Id.Should().Be("123");
        animal.Def.Should().Be("Hare");
    }

    [Fact]
    public async Task GetAnimals_WhenLivePayloadOmitsHealth_LeavesHealthNull()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("map/animals", Json("""
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
                """)));

        IReadOnlyList<AnimalDto> result = await new RimApiClient(http).GetAnimalsAsync(0);

        AnimalDto animal = result.Should().ContainSingle().Subject;
        animal.Id.Should().Be("37382");
        animal.Def.Should().Be("Ibex");
        animal.Health.Should().BeNull();
    }

    [Fact]
    public async Task GetFarmSummary_WhenApiReturnsLiveCropTypes_ReturnsMappedBreakdown()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("map/farm/summary", Json("""
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
                """)));

        FarmSummaryDto result = await new RimApiClient(http).GetFarmSummaryAsync(0);

        result.TotalCrops.Should().Be(36);
        result.AvgGrowth.Should().BeApproximately(0.050219f, 0.00001f);
        CropBreakdownDto crop = result.CropBreakdown.Should().ContainSingle().Which;
        crop.Def.Should().Be("Plant_Rice");
        crop.Count.Should().Be(36);
        crop.AvgGrowth.Should().BeApproximately(0.050219f, 0.00001f);
        crop.ZoneId.Should().Be("2");
    }

    [Fact]
    public async Task GetPlants_WhenApiReturnsLiveThingShape_ReturnsStablePlantIdsAndDefs()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("map/plants", Json("""
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
                """)));

        IReadOnlyList<PlantDto> result = await new RimApiClient(http).GetPlantsAsync(0);

        PlantDto plant = result.Should().ContainSingle().Which;
        plant.Id.Should().Be("44187");
        plant.Def.Should().Be("Plant_Rice");
        plant.Position.Should().BeEquivalentTo(new { X = 85, Y = 0, Z = 190 });
    }

    [Fact]
    public async Task GetThings_WhenApiReturnsLiveMealStacks_ReturnsMappedList()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("map/things", Json("""
                {
                  "success": true,
                  "data": [
                    {
                      "thing_id": "Thing_100",
                      "def_name": "MealSurvivalPack",
                      "label": "packaged survival meal",
                      "categories": ["FoodMeals"],
                      "position": { "x": 24, "y": 0, "z": 42 },
                      "stack_count": 9,
                      "market_value": 216.0,
                      "is_forbidden": false
                    }
                  ],
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """)));

        IReadOnlyList<ThingDto> result = await new RimApiClient(http).GetThingsAsync(0);

        ThingDto thing = result.Should().ContainSingle().Subject;
        thing.StableId.Should().Be("Thing_100");
        thing.StableDef.Should().Be("MealSurvivalPack");
        thing.EffectiveStackCount.Should().Be(9);
        thing.IsForbidden.Should().BeFalse();
    }

    [Fact]
    public async Task GetStoredResources_WhenApiReturnsGroupedObject_PreservesCategories()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("resources/stored", Json("""
                {
                  "success": true,
                  "data": {
                    "food_meals": [
                      {
                        "thing_id": "Thing_100",
                        "def_name": "MealSurvivalPack",
                        "label": "packaged survival meal",
                        "stack_count": 9,
                        "is_forbidden": false
                      }
                    ],
                    "plant_food_raw": [
                      {
                        "thing_id": "Thing_101",
                        "def_name": "RawBerries",
                        "label": "berries",
                        "stack_count": 12,
                        "is_forbidden": false
                      }
                    ]
                  },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """)));

        StoredResourcesDto result = await new RimApiClient(http).GetStoredResourcesAsync(0);

        result.Categories.Keys.Should().Contain(["food_meals", "plant_food_raw"]);
        result.Categories["food_meals"].GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetThingDefs_WhenApiReturnsDefAllNestedObject_ReturnsThingDefs()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("def/all", Json("""
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
                      }
                    ],
                    "incidents_defs": []
                  },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """)));

        IReadOnlyList<ThingDefDto> result = await new RimApiClient(http).GetThingDefsAsync();

        ThingDefDto def = result.Should().ContainSingle().Subject;
        def.DefName.Should().Be("MealSurvivalPack");
        def.Nutrition.Should().Be(0.9f);
        def.StackLimit.Should().Be(10);
    }

    [Fact]
    public async Task GetDefCatalog_WhenApiReturnsThingAndTerrainDefs_ReturnsFullCatalog()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("def/all", Json("""
                {
                  "success": true,
                  "data": {
                    "things_defs": [
                      {
                        "def_name": "MealSimple",
                        "label": "simple meal",
                        "category": "Item",
                        "thing_class": "ThingWithComps",
                        "is_item": true,
                        "is_plant": false,
                        "is_medicine": false,
                        "is_drug": false,
                        "nutrition": 0.9,
                        "stack_limit": 10
                      }
                    ],
                    "terrain_defs": [
                      {
                        "def_name": "Soil",
                        "label": "soil",
                        "fertility": 1.0,
                        "affordances": ["Walkable", "GrowSoil"]
                      }
                    ]
                  },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """)));

        DefCatalogDto result = await new RimApiClient(http).GetDefCatalogAsync();

        result.ThingsDefs.Should().ContainSingle().Which.DefName.Should().Be("MealSimple");
        TerrainDefDto terrain = result.TerrainDefs.Should().ContainSingle().Subject;
        terrain.DefName.Should().Be("Soil");
        terrain.Fertility.Should().Be(1.0f);
        terrain.Affordances.Should().Contain("GrowSoil");
    }

    [Fact]
    public async Task GetTerrain_WhenApiReturnsRleGrid_ReturnsTerrainGrid()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("map/terrain", Json("""
                {
                  "success": true,
                  "data": {
                    "width": 3,
                    "height": 2,
                    "palette": ["Soil", "SoilRich"],
                    "grid": [4, 0, 2, 1],
                    "floor_palette": [],
                    "floor_grid": [6, 0]
                  },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """)));

        TerrainGridDto result = await new RimApiClient(http).GetTerrainAsync(0);

        result.Width.Should().Be(3);
        result.Height.Should().Be(2);
        result.Palette.Should().Equal("Soil", "SoilRich");
        result.Grid.Should().Equal(4, 0, 2, 1);
    }

    [Fact]
    public async Task IconImageEndpoints_ParseKnownImageEnvelopeShapes()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("item/image", Json($$"""
                {
                  "success": true,
                  "data": {
                    "name": "MealSimple",
                    "result": "ok",
                    "image_base64": "{{PngBase64}}"
                  },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """))
            .Add("terrain/image", Json($$"""
                {
                  "success": true,
                  "data": {
                    "name": "Soil",
                    "result": "ok",
                    "image_base_64": "{{PngBase64}}"
                  },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """))
            .Add("faction/icon", Json($$"""
                {
                  "success": true,
                  "data": {
                    "image": {
                      "result": "ok",
                      "image_base_64": "{{PngBase64}}"
                    },
                    "color": "#ffffff"
                  },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """))
            .Add("pawn/portrait/image", Json($$"""
                {
                  "success": true,
                  "data": {
                    "name": "123",
                    "result": "ok",
                    "image_base64": "{{PngBase64}}"
                  },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """)));
        RimApiClient client = new(http);

        RimApiImageDto item = await client.GetItemImageAsync("MealSimple");
        RimApiImageDto terrain = await client.GetTerrainImageAsync("Soil");
        FactionIconDto faction = await client.GetFactionIconAsync(7);
        RimApiImageDto pawn = await client.GetPawnPortraitImageAsync(123, 64, 64, "South");

        item.Base64.Should().Be(PngBase64);
        terrain.Base64.Should().Be(PngBase64);
        faction.Image?.Base64.Should().Be(PngBase64);
        pawn.Base64.Should().Be(PngBase64);
    }

    [Fact]
    public async Task GetZones_WhenApiReturnsEmptyObjectData_ReturnsEmptyList()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("map/zones", Json("""
                {
                  "success": true,
                  "data": {},
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """)));

        IReadOnlyList<ZoneDto> result = await new RimApiClient(http).GetZonesAsync(0);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetZones_WhenApiReturnsWrappedZonesObject_ReturnsZones()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("map/zones", Json("""
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
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """)));

        IReadOnlyList<ZoneDto> result = await new RimApiClient(http).GetZonesAsync(0);

        ZoneDto zone = result.Should().ContainSingle().Subject;
        zone.Id.Should().Be("0");
        zone.Type.Should().Be("Zone_Stockpile");
        zone.CellsCount.Should().Be(56);
    }

    [Fact]
    public async Task GetIncidents_WhenApiReturnsWrappedIncidentsObject_ReturnsIncidents()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("incidents", Json("""
                {
                  "success": true,
                  "data": {
                    "incidents": [
                      {
                        "incident_def": "VisitorGroup",
                        "label": "visitor group",
                        "category": "None",
                        "incident_hour": 60.0,
                        "days_since_occurred": 0.24195
                      }
                    ]
                  },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """)));

        IReadOnlyList<IncidentDto> result = await new RimApiClient(http).GetIncidentsAsync(0);

        IncidentDto incident = result.Should().ContainSingle().Subject;
        incident.Def.Should().Be("VisitorGroup");
        incident.DaysSince.Should().BeApproximately(0.24195f, 0.00001f);
        incident.Label.Should().Be("visitor group");
    }

    [Fact]
    public async Task GetAnimals_WhenApiReturnsMalformedArrayItem_ThrowsSchemaDrift()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("map/animals", Json("""
                {
                  "success": true,
                  "data": [
                    {
                      "id": 123,
                      "def": "Hare",
                      "name": null,
                      "tame": false,
                      "health": "healthy"
                    }
                  ],
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """)));

        Func<Task> act = () => new RimApiClient(http).GetAnimalsAsync(0);

        await act.Should().ThrowAsync<RimApiException>()
            .WithMessage("*schema drift*AnimalDto*health*");
    }

    [Fact]
    public async Task GetAnimals_WhenApiReturnsNonEmptyObjectData_ThrowsSchemaDrift()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("map/animals", Json("""
                {
                  "success": true,
                  "data": { "id": 1 },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """)));

        Func<Task> act = () => new RimApiClient(http).GetAnimalsAsync(0);

        await act.Should().ThrowAsync<RimApiException>()
            .WithMessage("*expected data to be an array*AnimalDto*Object*");
    }

    // ── HandshakeAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handshake_WhenColonyLoaded_ReturnsOkWithFirstPawnName()
    {
        var state = new GameStateDto(1000, 5000f, 3, "Cassandra", false, "Playing", 1);
        var map   = new MapInfoDto(0, 0, true, false, "10", 1, "(250,1,250)");
        var pawn  = new MapPawnDto(1, "Alice", "Female", 25, 1.0f, 0.9f, 1.0f, null);

        using var http = MakeClient(new PathRouter()
            .Add("game/state", Envelope(state))
            .Add("maps",       Envelope(new List<MapInfoDto> { map }))
            .Add("map/pawns",  Envelope(new List<MapPawnDto> { pawn })));

        var (ok, message) = await new RimApiClient(http).HandshakeAsync();

        ok.Should().BeTrue();
        message.Should().Be("Alice");
    }

    [Fact]
    public async Task Handshake_WhenNoColonists_ReturnsFailWithHint()
    {
        var state = new GameStateDto(1000, 0f, 0, "Cassandra", false, "Playing", 0);

        using var http = MakeClient(new PathRouter()
            .Add("game/state", Envelope(state)));

        var (ok, message) = await new RimApiClient(http).HandshakeAsync();

        ok.Should().BeFalse();
        message.Should().Contain("no colonists");
    }

    [Fact]
    public async Task Handshake_WhenApiUnreachable_ReturnsFailWithErrorMessage()
    {
        using var http = MakeClient(new PathRouter(throwOnAnyRequest: true));

        var (ok, message) = await new RimApiClient(http).HandshakeAsync();

        ok.Should().BeFalse();
        message.Should().Contain("unreachable");
    }

    // ── RimApiException ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetGameState_WhenApiReturnsSuccessFalse_ThrowsRimApiException()
    {
        var failEnvelope = new RimApiEnvelope<GameStateDto>(
            Success: false, Data: null,
            Errors: ["Map not loaded"], Warnings: null, Timestamp: null);

        using var http = MakeClient(new PathRouter()
            .Add("game/state", JsonContent.Create(failEnvelope)));

        var act = () => new RimApiClient(http).GetGameStateAsync();

        await act.Should().ThrowAsync<RimApiException>()
            .WithMessage("*Map not loaded*");
    }

    [Fact]
    public async Task DesignateArea_PostsHarvestAreaPayload()
    {
        var handler = new CaptureHandler(Envelope(new { changed = 4 }));
        using HttpClient http = MakeClient(handler);

        await new RimApiClient(http).DesignateAreaAsync(7, "Harvest", 10, 20, 12, 22);

        handler.Path.Should().Be("/api/v1/order/designate/area");
        JsonDocument body = JsonDocument.Parse(handler.Body);
        body.RootElement.GetProperty("map_id").GetInt32().Should().Be(7);
        body.RootElement.GetProperty("designation").GetString().Should().Be("Harvest");
        body.RootElement.GetProperty("rect").GetProperty("x1").GetInt32().Should().Be(10);
        body.RootElement.GetProperty("rect").GetProperty("z2").GetInt32().Should().Be(22);
    }

    [Fact]
    public async Task UnforbidThings_PostsSafeEndpointPayload()
    {
        var handler = new CaptureHandler(Envelope(new { changed = 2 }));
        using HttpClient http = MakeClient(handler);

        await new RimApiClient(http).UnforbidThingsAsync(7, ["thing-1", "thing-2"]);

        handler.Path.Should().Be("/api/v1/order/unforbid");
        JsonDocument body = JsonDocument.Parse(handler.Body);
        body.RootElement.GetProperty("map_id").GetInt32().Should().Be(7);
        body.RootElement.GetProperty("thing_ids").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal("thing-1", "thing-2");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static HttpClient MakeClient(PathRouter router) =>
        new(router) { BaseAddress = new Uri("http://localhost:8765/") };

    private static HttpClient MakeClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://localhost:8765/") };

    /// <summary>Wraps a value in a success envelope, serialised to HttpContent.</summary>
    private static HttpContent Envelope<T>(T data) =>
        JsonContent.Create(new RimApiEnvelope<T>(
            Success: true, Data: data,
            Errors: null, Warnings: null, Timestamp: null));

    private static HttpContent Json(string json) =>
        new StringContent(json, Encoding.UTF8, "application/json");

    /// <summary>
    /// Stub handler that routes requests by path segment.
    /// E.g. a request to "api/v1/game/state" matches the key "game/state".
    /// </summary>
    private sealed class PathRouter(bool throwOnAnyRequest = false) : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpContent> _routes = new();

        public PathRouter Add(string pathSegment, HttpContent content)
        {
            _routes[pathSegment] = content;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            if (throwOnAnyRequest)
                throw new HttpRequestException("Connection refused");

            var path = request.RequestUri?.PathAndQuery ?? "";

            foreach (var (segment, content) in _routes)
            {
                if (path.Contains(segment, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class CaptureHandler(HttpContent responseContent) : HttpMessageHandler
    {
        public string Path { get; private set; } = "";
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            Path = request.RequestUri?.AbsolutePath ?? "";
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = responseContent };
        }
    }
}
