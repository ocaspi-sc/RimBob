using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using RimBob.Host;
using RimBob.Ingestion;
using RimBob.Ingestion.Dtos;

namespace RimBob.Tests.ApiHost;

public sealed class RimApiRuntimeProbeTests
{
    [Fact]
    public async Task Probe_WhenGameStateLoads_ReportsReachableRuntime()
    {
        GameStateDto gameState = new(
            Tick: 173673,
            Wealth: 16069.75f,
            ColonistCount: 3,
            Storyteller: "Cassandra",
            Paused: true,
            ProgramState: "Playing",
            MapCount: 1);
        using HttpClient http = new(new StaticHandler(JsonContent.Create(
            new RimApiEnvelope<GameStateDto>(
                Success: true,
                Data: gameState,
                Errors: null,
                Warnings: null,
                Timestamp: null))))
        {
            BaseAddress = new Uri("http://localhost:8765/")
        };

        RimApiRuntimeSnapshot snapshot = await new RimApiRuntimeProbe(new RimApiClient(http)).ProbeAsync();

        snapshot.Reachable.Should().BeTrue();
        snapshot.HasLoadedColony.Should().BeTrue();
        snapshot.ProgramState.Should().Be("Playing");
        snapshot.MapCount.Should().Be(1);
        snapshot.ColonistCount.Should().Be(3);
        snapshot.Paused.Should().BeTrue();
    }

    [Fact]
    public async Task Probe_WhenRimApiRequestFails_ReportsOffline()
    {
        using HttpClient http = new(new ThrowingHandler())
        {
            BaseAddress = new Uri("http://localhost:8765/")
        };

        RimApiRuntimeSnapshot snapshot = await new RimApiRuntimeProbe(new RimApiClient(http)).ProbeAsync();

        snapshot.Reachable.Should().BeFalse();
        snapshot.HasLoadedColony.Should().BeFalse();
        snapshot.LastError.Should().Contain("RIMAPI unreachable");
    }

    private sealed class StaticHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = content
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("connection refused");
        }
    }
}
