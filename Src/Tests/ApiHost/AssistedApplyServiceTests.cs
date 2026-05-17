using System.Net;
using System.Text;
using FluentAssertions;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Host;
using RimBob.Ingestion;
using RimBob.State;
using RimBob.Tests.Infrastructure;

namespace RimBob.Tests.ApiHost;

public sealed class AssistedApplyServiceTests
{
    [Fact]
    public async Task ApplyAsync_WhenAdviceMissing_ReturnsStaleAdviceShape()
    {
        AssistedApplyService service = Service(new AdviceBus(), new ColonyState());

        AssistedApplyResponse response = await service.ApplyAsync("missing", 0);

        response.Status.Should().Be("stale_advice");
        response.AdviceId.Should().Be("missing");
        response.ActionIndex.Should().Be(0);
        response.Kind.Should().BeNull();
        service.LatestAttempts().Should().ContainSingle().Which.Status.Should().Be("stale_advice");
    }

    [Fact]
    public async Task ApplyAsync_WhenHarvestRectTooBroad_ReturnsValidationFailedWithoutRimApi()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_harvest_mature_crops", new AdviceAction(
            AdviceActionKind.MarkHarvest,
            "Mark broad harvest.",
            Apply: new AdviceActionApply(
                AdviceApplyKind.MarkHarvestArea,
                "Mark harvest",
                "too broad",
                MapId: 1,
                TargetCount: 1,
                Rect: new MapRect(0, 0, 20, 20),
                TargetIds: ["plant-1"]))));
        AssistedApplyService service = Service(bus, new ColonyState());

        AssistedApplyResponse response = await service.ApplyAsync("food_harvest_mature_crops", 0);

        response.Status.Should().Be("validation_failed");
        response.Kind.Should().Be(AdviceApplyKind.MarkHarvestArea);
        response.Message.Should().Contain("too broad");
    }

    [Fact]
    public async Task ApplyAsync_WhenMostHarvestTargetsAreNoLongerReady_ReturnsStaleWithoutPosting()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_harvest_mature_crops", new AdviceAction(
            AdviceActionKind.MarkHarvest,
            "Mark harvest.",
            Apply: new AdviceActionApply(
                AdviceApplyKind.MarkHarvestArea,
                "Mark harvest",
                "4 rice plants",
                MapId: 1,
                TargetCount: 4,
                Rect: new MapRect(10, 20, 13, 20),
                TargetIds: ["plant-1", "plant-2", "plant-3", "plant-4"]))));
        var handler = new MinimalRefreshHandler();
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_harvest_mature_crops", 0);

        response.Status.Should().Be("stale_advice");
        handler.DesignatePosted.Should().BeFalse();
    }

    private static AssistedApplyService Service(AdviceBus bus, ColonyState state)
    {
        return Service(bus, state, new ThrowingHandler());
    }

    private static AssistedApplyService Service(AdviceBus bus, ColonyState state, HttpMessageHandler handler)
    {
        var rimApi = new RimApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8765/")
        });
        var ingestion = new IngestionDispatcher(rimApi, state, new TestLogger<IngestionDispatcher>());
        return new AssistedApplyService(bus, state, ingestion, rimApi, new TestLogger<AssistedApplyService>());
    }

    private static AdviceItem Advice(string id, AdviceAction action)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new AdviceItem(
            Id: id,
            Minister: "Food",
            AdviceType: "harvest_now",
            Priority: AdvicePriority.High,
            Title: "Mature crops are ready",
            Body: "Body",
            Rationale: "Rationale",
            Actions: [action],
            GuideCitationIds: [],
            IssuedAt: now,
            ExpiresAt: now.AddHours(1));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            throw new HttpRequestException("unexpected RIMAPI call", null, HttpStatusCode.ServiceUnavailable);
        }
    }

    private sealed class MinimalRefreshHandler : HttpMessageHandler
    {
        public bool DesignatePosted { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri?.PathAndQuery ?? "";
            if (path.Contains("order/designate/area", StringComparison.OrdinalIgnoreCase))
            {
                DesignatePosted = true;
                return JsonResponse("""{"success":true,"data":{},"errors":null}""");
            }

            if (path.Contains("maps", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":[{"id":1,"index":0,"is_player_home":true,"is_pocket_map":false,"faction_id":"10","seed":1,"size":"250"}],"errors":null}""");
            if (path.Contains("game/state", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"game_tick":1000,"colony_wealth":0,"colonist_count":0,"storyteller":"Cassandra","is_paused":false,"program_state":"Playing","map_count":1},"errors":null}""");
            if (path.Contains("datetime", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"datetime":"5th of Aprimay, 5500, 14h"},"errors":null}""");
            if (path.Contains("colonists/detailed", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":[],"errors":null}""");
            if (path.Contains("map/farm/summary", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"total_crops":4,"avg_growth":0.5,"ready_to_harvest":1,"crop_breakdown":[{"def":"Plant_Rice","count":4,"avg_growth":0.5}]},"errors":null}""");
            if (path.Contains("map/plants", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""
                    {"success":true,"data":[
                      {"id":"plant-1","def":"Plant_Rice","growth":0.9,"is_crop":true,"position":{"x":10,"y":0,"z":20}},
                      {"id":"plant-2","def":"Plant_Rice","growth":0.4,"is_crop":true,"position":{"x":11,"y":0,"z":20}},
                      {"id":"plant-3","def":"Plant_Rice","growth":0.4,"is_crop":true,"position":{"x":12,"y":0,"z":20}},
                      {"id":"plant-4","def":"Plant_Rice","growth":0.4,"is_crop":true,"position":{"x":13,"y":0,"z":20}}
                    ],"errors":null}
                    """);
            if (path.Contains("map/things", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":[],"errors":null}""");
            if (path.Contains("def/all", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"things_defs":[],"terrain_defs":[]},"errors":null}""");
            if (path.Contains("resources/stored", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{},"errors":null}""");
            if (path.Contains("map/animals", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":[],"errors":null}""");
            if (path.Contains("map/zones", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{},"errors":null}""");
            if (path.Contains("map/terrain", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"width":0,"height":0,"palette":[],"grid":[]},"errors":null}""");
            if (path.Contains("map/buildings", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":[],"errors":null}""");
            if (path.Contains("map/power/info", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"production":0,"consumption":0,"stored":0,"capacity":0},"errors":null}""");
            if (path.Contains("map/weather", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"def":"Clear","temperature":21,"rain_rate":0},"errors":null}""");
            if (path.Contains("lords", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":[],"errors":null}""");
            if (path.Contains("incidents", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"incidents":[]},"errors":null}""");
            if (path.Contains("resources/summary", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"total_items":0,"total_market_value":0,"critical_resources":{"food_summary":{"food_total":0,"total_nutrition":0,"meals_count":0,"raw_food_count":0},"medicine_total":0,"weapon_count":0,"weapon_value":0}},"errors":null}""");
            if (path.Contains("research/progress", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"name":"none","label":"None","progress":0,"research_points":0,"is_finished":false,"can_start_now":false,"progress_percent":0},"errors":null}""");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> JsonResponse(string json) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }
}
