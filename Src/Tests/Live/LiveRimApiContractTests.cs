using FluentAssertions;
using RimAI.Ingestion;
using RimAI.Ingestion.Dtos;
using Xunit.Abstractions;

namespace RimAI.Tests.Live;

public sealed class LiveRimApiContractTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task GetAnimals_LivePayload_DeserializesWhenRimApiIsRunning()
    {
        Uri baseUri = LiveTestHelpers.ResolveBaseUri("RIMAPI_BASE_URL", "http://127.0.0.1:8765/");
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
}
