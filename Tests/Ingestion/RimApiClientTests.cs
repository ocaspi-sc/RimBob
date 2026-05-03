using System.Net;
using System.Net.Http.Json;
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
        var pawn = new MapPawnDto("p1", "Bob", 1.0f, 0.8f, 0.9f, null);
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

    // ── HandshakeAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handshake_WhenColonyLoaded_ReturnsOkWithFirstPawnName()
    {
        var state = new GameStateDto(1000, 5000f, 3, "Cassandra", false, 0);
        var pawn  = new MapPawnDto("p1", "Alice", 1.0f, 0.9f, 1.0f, null);

        using var http = MakeClient(new PathRouter()
            .Add("game/state", Envelope(state))
            .Add("map/pawns",  Envelope(new List<MapPawnDto> { pawn })));

        var (ok, message) = await new RimApiClient(http).HandshakeAsync();

        ok.Should().BeTrue();
        message.Should().Be("Alice");
    }

    [Fact]
    public async Task Handshake_WhenNoColonists_ReturnsFailWithHint()
    {
        var state = new GameStateDto(1000, 0f, 0, "Cassandra", false, 0);

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
