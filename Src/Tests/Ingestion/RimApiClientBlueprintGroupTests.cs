using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using RimBob.Ingestion;
using RimBob.Ingestion.Dtos;

namespace RimBob.Tests.Ingestion;

public sealed class RimApiClientBlueprintGroupTests
{
    [Fact]
    public async Task PostBlueprintGroupValidateAsync_PostsSnakeCasePayloadAndReturnsDto()
    {
        CaptureHandler handler = new(Json("""
            {
              "success": true,
              "data": {
                "can_place_all": true,
                "items": [
                  {
                    "index": 0,
                    "item": {
                      "role": "wall",
                      "def_name": "Wall",
                      "stuff_def_name": "BlocksGranite",
                      "cell": { "x": 11, "z": 22 },
                      "rotation": 0
                    },
                    "can_place": true,
                    "reason": null,
                    "def_type": "Building",
                    "occupies_cells": [{ "x": 11, "z": 22 }],
                    "cost": [{ "def_name": "BlocksGranite", "count": 5 }],
                    "work_to_build": 135,
                    "already_blueprinted": false,
                    "already_built": false
                  }
                ],
                "cost": [{ "def_name": "BlocksGranite", "count": 5 }],
                "overlap_conflicts": []
              },
              "errors": []
            }
            """));
        using HttpClient http = MakeClient(handler);
        BlueprintGroupValidateRequestDto request = new(
            MapId: 7,
            Items:
            [
                new BlueprintGroupItemDto(
                    Role: "wall",
                    DefName: "Wall",
                    StuffDefName: "BlocksGranite",
                    Cell: new MapCellDto(11, 22),
                    Rotation: 0)
            ]);

        BlueprintGroupValidateResponseDto response = await new RimApiClient(http)
            .PostBlueprintGroupValidateAsync(request);

        response.CanPlaceAll.Should().BeTrue();
        response.Items.Should().ContainSingle().Which.Cost.Should()
            .ContainSingle(cost => cost.DefName == "BlocksGranite" && cost.Count == 5);
        handler.Path.Should().Be("/api/v1/builder/blueprint-group/validate");
        JsonDocument body = JsonDocument.Parse(handler.Body);
        body.RootElement.GetProperty("map_id").GetInt32().Should().Be(7);
        JsonElement first = body.RootElement.GetProperty("items")[0];
        first.GetProperty("role").GetString().Should().Be("wall");
        first.GetProperty("def_name").GetString().Should().Be("Wall");
        first.GetProperty("stuff_def_name").GetString().Should().Be("BlocksGranite");
        first.GetProperty("cell").GetProperty("z").GetInt32().Should().Be(22);
    }

    [Fact]
    public async Task PostBlueprintGroupValidateAsync_WhenEnvelopeFails_ThrowsRimApiException()
    {
        using HttpClient http = MakeClient(new CaptureHandler(Json("""
            {
              "success": false,
              "data": null,
              "errors": ["items are required"]
            }
            """)));
        BlueprintGroupValidateRequestDto request = new(MapId: 7, Items: []);

        Func<Task> act = () => new RimApiClient(http).PostBlueprintGroupValidateAsync(request);

        await act.Should().ThrowAsync<RimApiException>()
            .WithMessage("*items are required*");
    }

    [Fact]
    public async Task PostBlueprintGroupPlaceAsync_PostsSnakeCasePayloadAndReturnsDto()
    {
        CaptureHandler handler = new(Json("""
            {
              "success": true,
              "data": {
                "status": "placed",
                "require_all": true,
                "placement_order": "default",
                "items": [
                  {
                    "index": 0,
                    "item": {
                      "role": "wall",
                      "def_name": "Wall",
                      "stuff_def_name": "BlocksGranite",
                      "cell": { "x": 11, "z": 22 },
                      "rotation": 0
                    },
                    "status": "placed",
                    "placed": true,
                    "thing_id": 123,
                    "reason": "",
                    "validate": {
                      "can_place": true,
                      "reason": null,
                      "def_type": "Building",
                      "occupies_cells": [{ "x": 11, "z": 22 }],
                      "cost": [{ "def_name": "BlocksGranite", "count": 5 }],
                      "work_to_build": 135,
                      "already_blueprinted": false,
                      "already_built": false
                    }
                  }
                ],
                "cost": [{ "def_name": "BlocksGranite", "count": 5 }],
                "validate": {
                  "can_place_all": true,
                  "items": [],
                  "cost": [{ "def_name": "BlocksGranite", "count": 5 }],
                  "overlap_conflicts": []
                }
              },
              "errors": []
            }
            """));
        using HttpClient http = MakeClient(handler);
        BlueprintGroupPlaceRequestDto request = new(
            MapId: 7,
            Items:
            [
                new BlueprintGroupItemDto(
                    Role: "wall",
                    DefName: "Wall",
                    StuffDefName: "BlocksGranite",
                    Cell: new MapCellDto(11, 22),
                    Rotation: 0)
            ],
            PlacementOrder: "default",
            RequireAll: true);

        BlueprintGroupPlaceResultDto response = await new RimApiClient(http)
            .PostBlueprintGroupPlaceAsync(request);

        response.Status.Should().Be("placed");
        response.RequireAll.Should().BeTrue();
        response.Items.Should().ContainSingle().Which.ThingId.Should().Be(123);
        handler.Path.Should().Be("/api/v1/builder/blueprint-group/place");
        JsonDocument body = JsonDocument.Parse(handler.Body);
        body.RootElement.GetProperty("map_id").GetInt32().Should().Be(7);
        body.RootElement.GetProperty("placement_order").GetString().Should().Be("default");
        body.RootElement.GetProperty("require_all").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("items")[0].GetProperty("def_name").GetString().Should().Be("Wall");
    }

    [Fact]
    public async Task PostBlueprintGroupPlaceAsync_WhenEnvelopeFails_ThrowsRimApiException()
    {
        using HttpClient http = MakeClient(new CaptureHandler(Json("""
            {
              "success": false,
              "data": null,
              "errors": ["validation failed: placement_order must be 'default' or 'request'"]
            }
            """)));
        BlueprintGroupPlaceRequestDto request = new(
            MapId: 7,
            Items: [],
            PlacementOrder: "floor-wall-door",
            RequireAll: true);

        Func<Task> act = () => new RimApiClient(http).PostBlueprintGroupPlaceAsync(request);

        await act.Should().ThrowAsync<RimApiException>()
            .WithMessage("*placement_order*");
    }

    private static HttpClient MakeClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://localhost:8765/") };

    private static HttpContent Json(string json) =>
        new StringContent(json, Encoding.UTF8, "application/json");

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
