using System.Text.Json;
using FluentAssertions;
using Xunit.Abstractions;

namespace RimBob.Tests.Live;

public sealed class LiveHostSmokeTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task FoodBriefing_DoesNotLoseLiveAnimalsWhenHostAndRimApiAreRunning()
    {
        Uri hostBaseUri = LiveTestHelpers.ResolveBaseUri("RIMBOB_HOST_BASE_URL", "http://localhost:5000/");
        using HttpClient host = new() { BaseAddress = hostBaseUri, Timeout = TimeSpan.FromSeconds(3) };

        string? healthJson = await LiveTestHelpers.TryGetStringAsync(host, "api/health", output, "RimBob Host");
        if (healthJson is null)
            return;

        healthJson.Should().Contain("ok");
        string foodJson = await host.GetStringAsync("api/briefings/food/latest");
        using JsonDocument foodDocument = JsonDocument.Parse(foodJson);
        JsonElement food = foodDocument.RootElement;

        int? briefingVersion = LiveTestHelpers.TryGetInt32(food, "briefingVersion") ??
                               LiveTestHelpers.TryGetInt32(food, "briefing_version");
        int? hostAnimalCount = LiveTestHelpers.TryGetInt32(food, "wildAnimalCount") ??
                               LiveTestHelpers.TryGetInt32(food, "wild_animal_count");
        hostAnimalCount.Should().NotBeNull("Food briefing should expose wild animal count");

        Uri rimApiBaseUri = LiveTestHelpers.ResolveBaseUri("RIMAPI_BASE_URL", "http://localhost:8765/");
        using HttpClient rimApi = new() { BaseAddress = rimApiBaseUri, Timeout = TimeSpan.FromSeconds(3) };

        string? mapsJson = await LiveTestHelpers.TryGetStringAsync(rimApi, "api/v1/maps", output, "RIMAPI");
        if (mapsJson is null)
            return;

        int? mapId = LiveTestHelpers.SelectPlayerHomeMapId(mapsJson);
        if (mapId is null)
            return;

        string animalsJson = await rimApi.GetStringAsync($"api/v1/map/animals?map_id={mapId.Value}");
        int rawAnimalCount = LiveTestHelpers.CountDataArrayItems(animalsJson);
        if (rawAnimalCount > 0 && briefingVersion.GetValueOrDefault() > 0)
            hostAnimalCount!.Value.Should().BeGreaterThan(0,
                "RIMAPI reports animals and Host has a refreshed Food briefing");
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task FoodBriefing_DoesNotLoseLiveFarmCropsWhenHostAndRimApiAreRunning()
    {
        Uri hostBaseUri = LiveTestHelpers.ResolveBaseUri("RIMBOB_HOST_BASE_URL", "http://localhost:5000/");
        using HttpClient host = new() { BaseAddress = hostBaseUri, Timeout = TimeSpan.FromSeconds(3) };

        string? healthJson = await LiveTestHelpers.TryGetStringAsync(host, "api/health", output, "RimBob Host");
        if (healthJson is null)
            return;

        healthJson.Should().Contain("ok");
        string foodJson = await host.GetStringAsync("api/briefings/food/latest");
        using JsonDocument foodDocument = JsonDocument.Parse(foodJson);
        JsonElement food = foodDocument.RootElement;

        int? briefingVersion = LiveTestHelpers.TryGetInt32(food, "briefingVersion") ??
                               LiveTestHelpers.TryGetInt32(food, "briefing_version");
        int? hostCropBreakdownCount = LiveTestHelpers.TryGetArrayLength(food, "cropBreakdown") ??
                                      LiveTestHelpers.TryGetArrayLength(food, "crop_breakdown");
        hostCropBreakdownCount.Should().NotBeNull("Food briefing should expose crop breakdown rows");

        Uri rimApiBaseUri = LiveTestHelpers.ResolveBaseUri("RIMAPI_BASE_URL", "http://localhost:8765/");
        using HttpClient rimApi = new() { BaseAddress = rimApiBaseUri, Timeout = TimeSpan.FromSeconds(3) };

        string? mapsJson = await LiveTestHelpers.TryGetStringAsync(rimApi, "api/v1/maps", output, "RIMAPI");
        if (mapsJson is null)
            return;

        int? mapId = LiveTestHelpers.SelectPlayerHomeMapId(mapsJson);
        if (mapId is null)
            return;

        string farmJson = await rimApi.GetStringAsync($"api/v1/map/farm/summary?map_id={mapId.Value}");
        int rawCropTypeCount = LiveTestHelpers.CountFarmCropTypes(farmJson);
        if (rawCropTypeCount > 0 && briefingVersion.GetValueOrDefault() > 0)
            hostCropBreakdownCount!.Value.Should().BeGreaterThan(0,
                "RIMAPI reports crops and Host has a refreshed Food briefing");
    }
}
