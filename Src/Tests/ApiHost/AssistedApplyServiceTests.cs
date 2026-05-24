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
    public async Task ApplyAsync_WhenAdviceExpiredByGameTick_ReturnsStaleWithoutRimApi()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_harvest_mature_crops", new AdviceAction(
            AdviceActionKind.MarkHarvest,
            "Mark harvest.",
            Apply: new MarkHarvestAreaApply(
                "Mark harvest",
                "4 rice plants",
                MapId: 1,
                Rect: new MapRect(10, 20, 13, 20),
                TargetIds: ["plant-1", "plant-2", "plant-3", "plant-4"],
                TargetCount: 4))) with
        {
            IssuedGameTick = 1_000,
            ExpiresGameTick = 2_000
        });
        ColonyState state = new();
        state.Economy.Update(new EconomyLedger(2_000, 0, "Cassandra", "Playing", false, "5th of Aprimay, 5500, 14h"));
        AssistedApplyService service = Service(bus, state);

        AssistedApplyResponse response = await service.ApplyAsync("food_harvest_mature_crops", 0);

        response.Status.Should().Be("stale_advice");
        response.Message.Should().Contain("game tick 2000");
    }

    [Fact]
    public async Task ApplyAsync_WhenHarvestRectTooBroad_ReturnsValidationFailedWithoutRimApi()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_harvest_mature_crops", new AdviceAction(
            AdviceActionKind.MarkHarvest,
            "Mark broad harvest.",
            Apply: new MarkHarvestAreaApply(
                "Mark harvest",
                "too broad",
                MapId: 1,
                Rect: new MapRect(0, 0, 20, 20),
                TargetIds: ["plant-1"],
                TargetCount: 1))));
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
            Apply: new MarkHarvestAreaApply(
                "Mark harvest",
                "4 rice plants",
                MapId: 1,
                Rect: new MapRect(10, 20, 13, 20),
                TargetIds: ["plant-1", "plant-2", "plant-3", "plant-4"],
                TargetCount: 4))));
        var handler = new MinimalRefreshHandler();
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_harvest_mature_crops", 0);

        response.Status.Should().Be("stale_advice");
        handler.DesignatePosted.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WhenHuntTargetsRemainLowRisk_PostsHuntDesignation()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_hunt_low_risk_animals", HuntAction()));
        MinimalRefreshHandler handler = HandlerWithHares();
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_hunt_low_risk_animals", 0);

        response.Status.Should().Be("applied");
        response.Kind.Should().Be(AdviceApplyKind.MarkHuntArea);
        response.Message.Should().Contain("Hunt designation");
        bus.ActiveAdvice().Should().BeEmpty();
        handler.DesignatePosted.Should().BeTrue();
        handler.LastDesignateBody.Should().Contain("\"designation\":\"Hunt\"");
    }

    [Fact]
    public async Task ApplyAsync_WhenHuntAreaContainsUnsafeAnimal_ReturnsStaleWithoutPosting()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_hunt_low_risk_animals", HuntAction()));
        MinimalRefreshHandler handler = new()
        {
            MapAnimalsJson = """
                {"success":true,"data":[
                  {"id":"hare-1","def":"Hare","tame":false,"health":1.0,"position":{"x":40,"y":0,"z":50}},
                  {"id":"hare-2","def":"Hare","tame":false,"health":1.0,"position":{"x":41,"y":0,"z":50}},
                  {"id":"wolf-1","def":"Wolf","tame":false,"health":1.0,"position":{"x":41,"y":0,"z":50}}
                ],"errors":null}
                """
        };
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_hunt_low_risk_animals", 0);

        response.Status.Should().Be("stale_advice");
        response.Message.Should().Contain("unsafe");
        handler.DesignatePosted.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WhenProductionBillMissing_AddsBillAndReadsBack()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_meals_understocked", ProductionBillAction()));
        MinimalRefreshHandler handler = HandlerWithSingleStove();
        handler.EnqueueBillResponse(BillsJson());
        handler.EnqueueBillResponse(BillsJson());
        handler.EnqueueBillResponse(BillsJson(SimpleMealBillJson(403, 12)));
        handler.EnqueueBillResponse(BillsJson(SimpleMealBillJson(403, 12)));
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_meals_understocked", 0);

        response.Status.Should().Be("applied");
        response.Kind.Should().Be(AdviceApplyKind.UpsertProductionBill);
        response.Message.Should().Contain("created");
        bus.ActiveAdvice().Should().BeEmpty();
        handler.AddBillPosted.Should().BeTrue();
        handler.UpdateBillPosted.Should().BeFalse();
        handler.LastBillWriteBody.Should().Contain("\"recipe_def_name\":\"CookMealSimple\"");
        handler.LastBillWriteBody.Should().Contain("\"target_count\":12");
    }

    [Fact]
    public async Task ApplyAsync_WhenProductionBillExistsWithDifferentTarget_UpdatesInsteadOfAdding()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_meals_understocked", ProductionBillAction()));
        MinimalRefreshHandler handler = HandlerWithSingleStove();
        handler.EnqueueBillResponse(BillsJson(SimpleMealBillJson(402, 6)));
        handler.EnqueueBillResponse(BillsJson(SimpleMealBillJson(402, 6)));
        handler.EnqueueBillResponse(BillsJson(SimpleMealBillJson(402, 12)));
        handler.EnqueueBillResponse(BillsJson(SimpleMealBillJson(402, 12)));
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_meals_understocked", 0);

        response.Status.Should().Be("applied");
        response.Message.Should().Contain("updated");
        handler.AddBillPosted.Should().BeFalse();
        handler.UpdateBillPosted.Should().BeTrue();
        handler.LastBillWritePath.Should().Contain("bill_id=402");
        handler.LastBillWriteBody.Should().Contain("\"target_count\":12");
    }

    [Fact]
    public async Task ApplyAsync_WhenProductionBillAlreadySatisfied_DoesNotWrite()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_meals_understocked", ProductionBillAction()));
        MinimalRefreshHandler handler = HandlerWithSingleStove();
        handler.EnqueueBillResponse(BillsJson(SimpleMealBillJson(402, 12)));
        handler.EnqueueBillResponse(BillsJson(SimpleMealBillJson(402, 12)));
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_meals_understocked", 0);

        response.Status.Should().Be("already_satisfied");
        handler.AddBillPosted.Should().BeFalse();
        handler.UpdateBillPosted.Should().BeFalse();
        handler.BillListCalls.Should().Be(2);
    }

    [Fact]
    public async Task ApplyAsync_WhenProductionBillTargetOverCap_ReturnsValidationFailedWithoutRimApi()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_meals_understocked", ProductionBillAction(AssistedApplyLimits.MaxProductionBillTarget + 1)));
        AssistedApplyService service = Service(bus, new ColonyState());

        AssistedApplyResponse response = await service.ApplyAsync("food_meals_understocked", 0);

        response.Status.Should().Be("validation_failed");
        response.Message.Should().Contain("too high");
    }

    [Fact]
    public async Task ApplyAsync_WhenProductionBillWorkbenchIsStale_ReturnsStaleWithoutBillCalls()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_meals_understocked", ProductionBillAction()));
        MinimalRefreshHandler handler = new();
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_meals_understocked", 0);

        response.Status.Should().Be("stale_advice");
        handler.BillListCalls.Should().Be(0);
        handler.AddBillPosted.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WhenProductionBillReadbackDoesNotConfirm_ReturnsInconclusive()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_meals_understocked", ProductionBillAction()));
        MinimalRefreshHandler handler = HandlerWithSingleStove();
        handler.EnqueueBillResponse(BillsJson());
        handler.EnqueueBillResponse(BillsJson());
        handler.EnqueueBillResponse(BillsJson());
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_meals_understocked", 0);

        response.Status.Should().Be("readback_inconclusive");
        handler.AddBillPosted.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAsync_WhenBlueprintGroupApplyInvoked_ReturnsNotExecutableWithoutRimApi()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_freezer_missing", BlueprintGroupAction()));
        AssistedApplyService service = Service(bus, new ColonyState());

        AssistedApplyResponse response = await service.ApplyAsync("food_freezer_missing", 0);

        response.Status.Should().Be("validation_failed");
        response.Kind.Should().Be(AdviceApplyKind.PlaceBlueprintGroup);
        response.Message.Should().Contain("not executable");
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

    private static AdviceAction ProductionBillAction(int targetCount = 12, string workbenchId = "10") =>
        new(
            AdviceActionKind.ProductionBill,
            "Set/check simple meal bill.",
            Apply: new UpsertProductionBillApply(
                "Set simple meal bill",
                "simple meal bill on one cooking station",
                MapId: 1,
                WorkbenchBuildingId: workbenchId,
                RecipeSelectorKey: "simple_meal",
                RepeatMode: "TargetCount",
                TargetCount: targetCount));

    private static AdviceAction HuntAction() =>
        new(
            AdviceActionKind.MarkHunt,
            "Mark up to 2 hares for hunting.",
            Apply: new MarkHuntAreaApply(
                "Mark hunt",
                "2 hare hunt targets",
                MapId: 1,
                Rect: new MapRect(40, 50, 41, 50),
                TargetIds: ["hare-1", "hare-2"],
                TargetCount: 2));

    private static AdviceAction BlueprintGroupAction() =>
        new(
            AdviceActionKind.PlaceBlueprint,
            "Review compact freezer placement.",
            Apply: new PlaceBlueprintGroupApply(
                Label: "Place freezer shell",
                TargetSummary: "Compact freezer shell with one cooler",
                MapId: 1,
                BlueprintGroup: new BlueprintGroup(
                    Label: "Compact freezer",
                    MapId: 1,
                    Assets:
                    [
                        new BlueprintAsset(
                            Role: "building",
                            DefName: "Cooler",
                            StuffDefName: "Steel",
                            Cell: new MapCell(12, 34),
                            Rotation: 2)
                    ]),
                AssetCount: 1));

    private static MinimalRefreshHandler HandlerWithSingleStove() =>
        new()
        {
            MapBuildingsJson = """
                {"success":true,"data":[
                  {"id":10,"def":"FueledStove","label":"fueled stove","type":"Building_WorkTable","position":{"x":10,"y":0,"z":10}}
                ],"errors":null}
                """
        };

    private static MinimalRefreshHandler HandlerWithHares() =>
        new()
        {
            MapAnimalsJson = """
                {"success":true,"data":[
                  {"id":"hare-1","def":"Hare","tame":false,"health":1.0,"position":{"x":40,"y":0,"z":50}},
                  {"id":"hare-2","def":"Hare","tame":false,"health":1.0,"position":{"x":41,"y":0,"z":50}}
                ],"errors":null}
                """
        };

    private static string BillsJson(params string[] billJson) =>
        $$"""
          {"success":true,"data":[{{string.Join(",", billJson)}}],"errors":null}
          """;

    private static string SimpleMealBillJson(int billId, int targetCount) =>
        $$"""
          {"load_id":{{billId}},"recipe_def_name":"CookMealSimple","recipe_label":"cook simple meal","repeat_mode":"TargetCount","target_count":{{targetCount}},"suspended":false,"paused":false}
          """;

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            throw new HttpRequestException("unexpected RIMAPI call", null, HttpStatusCode.ServiceUnavailable);
        }
    }

    private sealed class MinimalRefreshHandler : HttpMessageHandler
    {
        private readonly Queue<string> _billResponses = [];

        public bool DesignatePosted { get; private set; }
        public bool AddBillPosted { get; private set; }
        public bool UpdateBillPosted { get; private set; }
        public int BillListCalls { get; private set; }
        public string LastDesignateBody { get; private set; } = "";
        public string LastBillWritePath { get; private set; } = "";
        public string LastBillWriteBody { get; private set; } = "";
        public string MapAnimalsJson { get; init; } = """{"success":true,"data":[],"errors":null}""";
        public string MapBuildingsJson { get; init; } = """{"success":true,"data":[],"errors":null}""";
        public string RecipesJson { get; init; } = """
            {"success":true,"data":[
              {"def_name":"CookMealSimple","label":"cook simple meal","description":"Cook a simple meal.","work_amount":300,"work_skill":"Cooking","products":[{"thing_def":"MealSimple","count":1}],"ingredients":[]}
            ],"errors":null}
            """;

        public void EnqueueBillResponse(string json)
        {
            _billResponses.Enqueue(json);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri?.PathAndQuery ?? "";
            if (path.Contains("order/designate/area", StringComparison.OrdinalIgnoreCase))
            {
                return CaptureDesignateAsync(request, ct);
            }

            if (path.Contains("buildings/bills/add", StringComparison.OrdinalIgnoreCase))
                return CaptureBillWriteAsync(request, isAdd: true, ct);
            if (path.Contains("buildings/bill/update", StringComparison.OrdinalIgnoreCase))
                return CaptureBillWriteAsync(request, isAdd: false, ct);
            if (path.Contains("buildings/recipes", StringComparison.OrdinalIgnoreCase))
                return JsonResponse(RecipesJson);
            if (path.Contains("buildings/bills", StringComparison.OrdinalIgnoreCase))
            {
                BillListCalls++;
                string json = _billResponses.Count == 0
                    ? """{"success":true,"data":[],"errors":null}"""
                    : _billResponses.Dequeue();
                return JsonResponse(json);
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
                return JsonResponse(MapAnimalsJson);
            if (path.Contains("map/zones", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{},"errors":null}""");
            if (path.Contains("map/terrain", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"width":0,"height":0,"palette":[],"grid":[]},"errors":null}""");
            if (path.Contains("map/buildings", StringComparison.OrdinalIgnoreCase))
                return JsonResponse(MapBuildingsJson);
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

        private async Task<HttpResponseMessage> CaptureDesignateAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            DesignatePosted = true;
            LastDesignateBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":true,"data":{},"errors":null}""", Encoding.UTF8, "application/json")
            };
        }

        private async Task<HttpResponseMessage> CaptureBillWriteAsync(
            HttpRequestMessage request,
            bool isAdd,
            CancellationToken ct)
        {
            if (isAdd)
                AddBillPosted = true;
            else
                UpdateBillPosted = true;

            LastBillWritePath = request.RequestUri?.PathAndQuery ?? "";
            LastBillWriteBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":true,"data":{},"errors":null}""", Encoding.UTF8, "application/json")
            };
        }

        private static Task<HttpResponseMessage> JsonResponse(string json) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }
}
