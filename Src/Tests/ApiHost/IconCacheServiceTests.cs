using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RimBob.Host;
using RimBob.Ingestion;
using RimBob.Ingestion.Dtos;

namespace RimBob.Tests.ApiHost;

public sealed class IconCacheServiceTests
{
    private const string PngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=";

    [Fact]
    public async Task GetItemIconAsync_WhenKeyIsUnsafe_RejectsTraversalAndWritesNothing()
    {
        string root = NewTempRoot();
        try
        {
            IconCacheService sut = NewService(root, _ => ImageEnvelope("MealSimple"));

            Func<Task> act = () => sut.GetItemIconAsync("../MealSimple");

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*Unsafe icon key*");
            Directory.Exists(root).Should().BeFalse();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GetTerrainIconAsync_WritesPngOnlyUnderCacheRoot()
    {
        string root = NewTempRoot();
        try
        {
            IconCacheService sut = NewService(root, _ => ImageEnvelope("Soil"));

            IconFile file = await sut.GetTerrainIconAsync("Soil");
            IconCacheStatus status = sut.GetStatus();

            Path.GetFullPath(file.Path)
                .StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
                .Should().BeTrue();
            File.Exists(file.Path).Should().BeTrue();
            File.ReadAllBytes(file.Path)[..8].Should().Equal([137, 80, 78, 71, 13, 10, 26, 10]);
            Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
            status.FileCount.Should().Be(1);
            status.FilesByKind.Should().ContainKey("terrain").WhoseValue.Should().Be(1);
            status.Files.Should().ContainSingle().Which.Should().BeEquivalentTo(
                new
                {
                    Kind = "terrain",
                    Id = "Soil",
                    Name = "Soil.png",
                    RelativePath = "terrain/Soil.png",
                    PublicPath = "/api/icons/terrain/Soil"
                },
                options => options.ExcludingMissingMembers());
            status.Files.Single().SizeBytes.Should().BeGreaterThan(0);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task WarmStaticAsync_RecordsSuccessesPerDefFailuresSkippedCandidatesAndStatus()
    {
        string root = NewTempRoot();
        try
        {
            IconCacheService sut = NewService(root, request =>
            {
                string path = request.RequestUri?.PathAndQuery ?? string.Empty;
                if (path.Contains("def/all", StringComparison.OrdinalIgnoreCase))
                {
                    return Json("""
                        {
                          "success": true,
                          "data": {
                            "things_defs": [
                              {
                                "def_name": "MealSimple",
                                "label": "simple meal",
                                "category": "Item",
                                "thing_class": "ThingWithComps",
                                "is_item": true,
                                "is_plant": false,
                                "is_medicine": false,
                                "is_drug": false,
                                "nutrition": 0.9,
                                "stack_limit": 10
                              },
                              {
                                "def_name": "BadThing",
                                "label": "bad thing",
                                "category": "Item",
                                "thing_class": "ThingWithComps",
                                "is_item": true,
                                "is_plant": false,
                                "is_medicine": false,
                                "is_drug": false,
                                "nutrition": 0.0,
                                "stack_limit": 1
                              },
                              {
                                "def_name": "Projectile_Shell",
                                "label": "shell",
                                "category": "Projectile",
                                "thing_class": "Projectile",
                                "is_item": false,
                                "is_plant": false,
                                "is_medicine": false,
                                "is_drug": false,
                                "nutrition": 0.0,
                                "stack_limit": 1
                              }
                            ],
                            "terrain_defs": [
                              {
                                "def_name": "Soil",
                                "label": "soil"
                              }
                            ]
                          },
                          "errors": null,
                          "warnings": null,
                          "timestamp": null
                        }
                        """);
                }

                if (path.Equals("/api/v1/factions", StringComparison.OrdinalIgnoreCase))
                {
                    return Json("""
                        {
                          "success": true,
                          "data": [
                            {
                              "load_id": 7,
                              "def_name": "OutlanderCivil",
                              "name": "Union",
                              "is_player": false,
                              "relation": "Neutral",
                              "goodwill": 10
                            }
                          ],
                          "errors": null,
                          "warnings": null,
                          "timestamp": null
                        }
                        """);
                }

                if (path.Contains("item/image", StringComparison.OrdinalIgnoreCase) &&
                    path.Contains("BadThing", StringComparison.OrdinalIgnoreCase))
                {
                    return Json("""
                        {
                          "success": true,
                          "data": {
                            "name": "BadThing",
                            "result": "missing",
                            "image_base64": null
                          },
                          "errors": null,
                          "warnings": null,
                          "timestamp": null
                        }
                        """);
                }

                if (path.Contains("item/image", StringComparison.OrdinalIgnoreCase))
                {
                    return ImageEnvelope("MealSimple");
                }

                if (path.Contains("terrain/image", StringComparison.OrdinalIgnoreCase))
                {
                    return ImageEnvelope("Soil");
                }

                if (path.Contains("faction/icon", StringComparison.OrdinalIgnoreCase))
                {
                    return Json($$"""
                        {
                          "success": true,
                          "data": {
                            "image": {
                              "result": "ok",
                              "image_base_64": "{{PngBase64}}"
                            },
                            "color": "#ffffff"
                          },
                          "errors": null,
                          "warnings": null,
                          "timestamp": null
                        }
                        """);
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            IconWarmSummary summary = await sut.WarmStaticAsync();
            IconCacheStatus status = sut.GetStatus();

            summary.TotalCandidates.Should().Be(4);
            summary.Succeeded.Should().Be(3);
            summary.Failed.Should().Be(1);
            summary.Skipped.Should().Be(1);
            summary.ItemCandidates.Should().Be(2);
            summary.TerrainCandidates.Should().Be(1);
            summary.FactionCandidates.Should().Be(1);
            summary.Failures.Should().Contain(failure => failure.Kind == "item" && failure.Id == "BadThing");
            status.FileCount.Should().Be(3);
            status.Files.Should().HaveCount(3);
            status.Files.Select(file => $"{file.Kind}:{file.Id}").Should().BeEquivalentTo(
            [
                "faction:7",
                "item:MealSimple",
                "terrain:Soil"
            ]);
            status.LastWarm.Should().NotBeNull();
            status.LastWarm?.Succeeded.Should().Be(3);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task WarmStaticAsync_WhenCatalogFails_TreatsFailureAsFatal()
    {
        string root = NewTempRoot();
        try
        {
            IconCacheService sut = NewService(root, _ => Json("""
                {
                  "success": false,
                  "data": null,
                  "errors": ["catalog unavailable"],
                  "warnings": null,
                  "timestamp": null
                }
                """));

            Func<Task> act = () => sut.WarmStaticAsync();

            await act.Should().ThrowAsync<RimApiException>()
                .WithMessage("*catalog unavailable*");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task WarmStaticAsync_StoresAllFailuresButBoundsStatusSample()
    {
        string root = NewTempRoot();
        try
        {
            IconCacheService sut = NewService(root, request =>
            {
                string path = request.RequestUri?.PathAndQuery ?? string.Empty;
                if (path.Contains("def/all", StringComparison.OrdinalIgnoreCase))
                {
                    StringBuilder things = new();
                    for (int i = 0; i < 105; i++)
                    {
                        if (i > 0) things.Append(',');
                        things.Append($$"""
                            {
                              "def_name": "BadThing{{i}}",
                              "label": "bad thing {{i}}",
                              "category": "Item",
                              "thing_class": "ThingWithComps",
                              "is_item": true,
                              "is_plant": false,
                              "is_medicine": false,
                              "is_drug": false,
                              "nutrition": 0.0,
                              "stack_limit": 1
                            }
                            """);
                    }

                    return Json($$"""
                        {
                          "success": true,
                          "data": {
                            "things_defs": [{{things}}],
                            "terrain_defs": []
                          },
                          "errors": null,
                          "warnings": null,
                          "timestamp": null
                        }
                        """);
                }

                if (path.Equals("/api/v1/factions", StringComparison.OrdinalIgnoreCase))
                {
                    return Json("""
                        {
                          "success": true,
                          "data": [],
                          "errors": null,
                          "warnings": null,
                          "timestamp": null
                        }
                        """);
                }

                if (path.Contains("item/image", StringComparison.OrdinalIgnoreCase))
                {
                    return Json("""
                        {
                          "success": true,
                          "data": {
                            "result": "missing",
                            "image_base64": null
                          },
                          "errors": null,
                          "warnings": null,
                          "timestamp": null
                        }
                        """);
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            IconWarmSummary summary = await sut.WarmStaticAsync();
            IconCacheStatus status = sut.GetStatus();
            string manifestJson = await File.ReadAllTextAsync(Path.Combine(root, "icon-cache-manifest.json"));
            using JsonDocument manifest = JsonDocument.Parse(manifestJson);
            int manifestFailureCount = manifest.RootElement
                .GetProperty("lastWarm")
                .GetProperty("failures")
                .GetArrayLength();

            summary.Failures.Should().HaveCount(105);
            status.LastWarm?.Failures.Should().HaveCount(100);
            manifestFailureCount.Should().Be(105);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task WarmStaticAsync_RetriesTransientImageFailureThenSucceeds()
    {
        string root = NewTempRoot();
        try
        {
            int imageCalls = 0;
            IconCacheService sut = NewService(root, request =>
            {
                string path = request.RequestUri?.PathAndQuery ?? string.Empty;
                if (path.Contains("def/all", StringComparison.OrdinalIgnoreCase))
                {
                    return Json("""
                        {
                          "success": true,
                          "data": {
                            "things_defs": [
                              {
                                "def_name": "FlakyThing",
                                "label": "flaky thing",
                                "category": "Item",
                                "thing_class": "ThingWithComps",
                                "is_item": true,
                                "is_plant": false,
                                "is_medicine": false,
                                "is_drug": false,
                                "nutrition": 0.0,
                                "stack_limit": 1
                              }
                            ],
                            "terrain_defs": []
                          },
                          "errors": null,
                          "warnings": null,
                          "timestamp": null
                        }
                        """);
                }

                if (path.Equals("/api/v1/factions", StringComparison.OrdinalIgnoreCase))
                {
                    return Json("""
                        { "success": true, "data": [], "errors": null, "warnings": null, "timestamp": null }
                        """);
                }

                if (path.Contains("item/image", StringComparison.OrdinalIgnoreCase))
                {
                    int call = Interlocked.Increment(ref imageCalls);
                    if (call == 1)
                    {
                        return Json("""
                            {
                              "success": true,
                              "data": { "result": "missing", "image_base64": null },
                              "errors": null, "warnings": null, "timestamp": null
                            }
                            """);
                    }

                    return ImageEnvelope("FlakyThing");
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            IconWarmSummary summary = await sut.WarmStaticAsync();

            imageCalls.Should().Be(2);
            summary.Succeeded.Should().Be(1);
            summary.Failed.Should().Be(0);
            summary.Failures.Should().BeEmpty();
            sut.GetStatus().FileCount.Should().Be(1);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static IconCacheService NewService(
        string root,
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        int warmAttempts = 3)
    {
        HttpClient http = new(new DelegateHandler(handler))
        {
            BaseAddress = new Uri("http://localhost:8765/")
        };
        RimApiClient rimApi = new(http);
        return new IconCacheService(
            root,
            rimApi,
            NullLogger<IconCacheService>.Instance,
            warmAttempts,
            TimeSpan.Zero);
    }

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), "rimbob-icon-cache-tests", Guid.NewGuid().ToString("N"));

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static HttpResponseMessage ImageEnvelope(string name) =>
        Json($$"""
            {
              "success": true,
              "data": {
                "name": "{{name}}",
                "result": "ok",
                "image_base64": "{{PngBase64}}"
              },
              "errors": null,
              "warnings": null,
              "timestamp": null
            }
            """);

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(handler(request));
    }
}
