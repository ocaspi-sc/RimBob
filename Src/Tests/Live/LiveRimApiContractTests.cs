using FluentAssertions;
using RimAI.Core.Aggregates;
using RimAI.Core.Briefings;
using RimAI.Ingestion;
using RimAI.Ingestion.Dtos;
using RimAI.State;
using RimAI.State.Derivations;
using Xunit.Abstractions;

namespace RimAI.Tests.Live;

public sealed class LiveRimApiContractTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task GetAnimals_LivePayload_DeserializesWhenRimApiIsRunning()
    {
        Uri baseUri = LiveTestHelpers.ResolveBaseUri("RIMAPI_BASE_URL", "http://localhost:8765/");
        using HttpClient http = new() { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(3) };

        string? mapsJson = await LiveTestHelpers.TryGetStringAsync(http, "api/v1/maps", output, "RIMAPI");
        if (mapsJson is null)
            return;

        int? mapId = LiveTestHelpers.SelectPlayerHomeMapId(mapsJson);
        mapId.Should().NotBeNull("RIMAPI is reachable, so a loaded map should be discoverable");

        string animalsPath = $"api/v1/map/animals?map_id={mapId!.Value}";
        string animalsJson = await http.GetStringAsync(animalsPath);
        int rawAnimalCount = LiveTestHelpers.CountDataArrayItems(animalsJson);

        IReadOnlyList<AnimalDto> animals = await new RimApiClient(http).GetAnimalsAsync(mapId.Value);

        if (rawAnimalCount > 0)
            animals.Should().NotBeEmpty("the live animal endpoint returned at least one raw animal");
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task StoredFood_LivePayload_ClassifiesSurvivalMealsWhenRimApiIsRunning()
    {
        Uri baseUri = LiveTestHelpers.ResolveBaseUri("RIMAPI_BASE_URL", "http://localhost:8765/");
        using HttpClient http = new() { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(5) };

        string? mapsJson = await LiveTestHelpers.TryGetStringAsync(http, "api/v1/maps", output, "RIMAPI");
        if (mapsJson is null)
            return;

        int? mapId = LiveTestHelpers.SelectPlayerHomeMapId(mapsJson);
        mapId.Should().NotBeNull("RIMAPI is reachable, so a loaded map should be discoverable");

        string storedPath = $"api/v1/resources/stored?map_id={mapId!.Value}";
        string storedJson = await http.GetStringAsync(storedPath);
        int rawSurvivalMealCount = LiveTestHelpers.CountStoredResourceDef(storedJson, "MealSurvivalPack");

        RimApiClient rimApi = new(http);
        StoredResourceRegistry storedResources = MapAggregateMapper.FromStoredResources(
            await rimApi.GetStoredResourcesAsync(mapId.Value));
        ThingDefRegistry thingDefs = MapAggregateMapper.FromThingDefs(await rimApi.GetThingDefsAsync());

        if (rawSurvivalMealCount > 0)
        {
            storedResources.CountByDef.Should().ContainKey("MealSurvivalPack")
                .WhoseValue.Should().Be(rawSurvivalMealCount);

            ColonyState state = new();
            state.Resources.Update(new ResourceSummary(
                TotalItems: rawSurvivalMealCount,
                TotalMarketValue: 0f,
                FoodTotal: rawSurvivalMealCount,
                TotalNutrition: 0f,
                MealsCount: 0,
                RawFoodCount: 0,
                MedicineTotal: 0,
                WeaponCount: 0,
                WeaponValue: 0f));
            state.StoredResources.Update(storedResources);
            state.ThingDefs.Update(thingDefs);

            FoodBriefing briefing = FoodBriefingDerivation.Compute(state);
            briefing.MealsCount.Should().BeGreaterThanOrEqualTo(rawSurvivalMealCount);
            briefing.NutritionSource.Should().Be("item_def_catalog");
        }
    }
}
