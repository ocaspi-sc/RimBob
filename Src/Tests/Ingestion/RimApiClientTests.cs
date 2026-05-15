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

    // ── helpers ───────────────────────────────────────────────────────────────

    private static HttpClient MakeClient(PathRouter router) =>
        new(router) { BaseAddress = new Uri("http://localhost:8765/") };

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
}
