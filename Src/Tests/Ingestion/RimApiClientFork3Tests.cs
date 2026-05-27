using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using RimBob.Ingestion;
using RimBob.Ingestion.Dtos;

namespace RimBob.Tests.Ingestion;

public sealed class RimApiClientFork3Tests
{
    [Fact]
    public async Task GetReachAsync_WhenApiReturnsReachability_ReturnsDto()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("map/reach", Json("""
                {
                  "success": true,
                  "data": {
                    "can_reach": true,
                    "from": { "x": 80, "z": 80 },
                    "to": { "x": 120, "z": 95 },
                    "mode": "pass_doors",
                    "pe_mode": "on_cell"
                  },
                  "errors": []
                }
                """)));

        MapReachResponseDto response = await new RimApiClient(http)
            .GetReachAsync(7, 80, 80, 120, 95);

        response.CanReach.Should().BeTrue();
        response.From.Should().Be(new MapCellDto(80, 80));
        response.To.Should().Be(new MapCellDto(120, 95));
    }

    [Fact]
    public async Task PostPathCostBatchAsync_WhenTooManyPairs_ThrowsBeforeHttpCall()
    {
        CountingHandler handler = new();
        using HttpClient http = MakeClient(handler);
        MapPathCostBatchRequestDto request = new(
            MapId: 7,
            Pairs: Enumerable.Range(0, 4097)
                .Select(index => new MapPathCostPairRequestDto(new MapCellDto(index, 1), new MapCellDto(index, 2)))
                .ToList());

        Func<Task> act = () => new RimApiClient(http).PostPathCostBatchAsync(request);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*4097*4096*");
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task PostPathCostBatchAsync_WhenEnvelopeFails_ThrowsRimApiException()
    {
        using HttpClient http = MakeClient(new PathRouter()
            .Add("map/path-cost/batch", Json("""
                {
                  "success": false,
                  "data": null,
                  "errors": ["pairs are required"]
                }
                """)));
        MapPathCostBatchRequestDto request = new(
            MapId: 7,
            Pairs: [new MapPathCostPairRequestDto(new MapCellDto(1, 2), new MapCellDto(3, 4))]);

        Func<Task> act = () => new RimApiClient(http).PostPathCostBatchAsync(request);

        await act.Should().ThrowAsync<RimApiException>()
            .WithMessage("*pairs are required*");
    }

    [Fact]
    public async Task PostPathCostAsync_PostsSnakeCasePayloadAndReturnsDto()
    {
        CaptureHandler handler = new(Envelope(new MapPathCostResponseDto(
            Reachable: true,
            Cost: 47,
            From: new MapCellDto(80, 80),
            To: new MapCellDto(120, 95),
            Tier: "region")));
        using HttpClient http = MakeClient(handler);
        MapPathCostRequestDto request = new(
            MapId: 7,
            From: new MapCellDto(80, 80),
            To: new MapCellDto(120, 95),
            Tier: "region",
            Mode: "pass_doors",
            PeMode: "on_cell",
            MaxCost: 1000);

        MapPathCostResponseDto response = await new RimApiClient(http)
            .PostPathCostAsync(request);

        response.Cost.Should().Be(47);
        handler.Path.Should().Be("/api/v1/map/path-cost");
        JsonDocument body = JsonDocument.Parse(handler.Body);
        body.RootElement.GetProperty("map_id").GetInt32().Should().Be(7);
        body.RootElement.GetProperty("pe_mode").GetString().Should().Be("on_cell");
        body.RootElement.GetProperty("from").GetProperty("x").GetInt32().Should().Be(80);
    }

    private static HttpClient MakeClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://localhost:8765/") };

    private static HttpContent Envelope<T>(T data) =>
        JsonContent.Create(new RimApiEnvelope<T>(
            Success: true,
            Data: data,
            Errors: null,
            Warnings: null,
            Timestamp: null));

    private static HttpContent Json(string json) =>
        new StringContent(json, Encoding.UTF8, "application/json");

    private sealed class PathRouter : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpContent> _routes = new();

        public PathRouter Add(string pathSegment, HttpContent content)
        {
            _routes[pathSegment] = content;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            string path = request.RequestUri?.PathAndQuery ?? "";
            foreach ((string segment, HttpContent content) in _routes)
            {
                if (path.Contains(segment, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = Envelope(new { }) });
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
