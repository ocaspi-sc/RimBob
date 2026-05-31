using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RimBob.Host.Endpoints;
using RimBob.State;

namespace RimBob.Tests.ApiHost;

public sealed class ManualTriggerErrorResultsTests
{
    [Fact]
    public async Task TryMap_NoLoadedMap_ReturnsServiceUnavailableProblemCode()
    {
        RimApiLiveStateUnavailableException exception = new(
            RimApiLiveStateUnavailableReason.NoLoadedMap,
            "RIMAPI returned no maps. Load a colony map before refreshing live state.");

        bool mapped = ManualTriggerErrorResults.TryMap(exception, out IResult result);

        mapped.Should().BeTrue();
        await using MemoryStream body = new();
        await using ServiceProvider services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();
        DefaultHttpContext context = new();
        context.RequestServices = services;
        context.Response.Body = body;

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        body.Position = 0;
        using JsonDocument document = await JsonDocument.ParseAsync(body);
        JsonElement root = document.RootElement;
        root.GetProperty("title").GetString().Should().Be("RimWorld map is not loaded");
        root.GetProperty("code").GetString().Should().Be("rimworld_map_not_loaded");
        root.GetProperty("source").GetString().Should().Be("rimapi");
        root.GetProperty("reason").GetString().Should().Be(nameof(RimApiLiveStateUnavailableReason.NoLoadedMap));
    }
}
