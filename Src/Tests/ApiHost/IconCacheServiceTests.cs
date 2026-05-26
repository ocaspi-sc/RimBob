using System.Buffers.Binary;
using System.IO.Compression;
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
    private const string RedXPlaceholderPngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAEM0lEQVR4Ae1bzUsbQRSfJCopEttSCbUGUZCix3qRCr147Z/Qv6/HHu2x0EMpvdijIoVAUeqhYogU08aPzm9lktnnZj7fLsTdgXHmzey8j9++92Z2N9bON9ZuRYlLvcS2J6aXHoAZ3QPeH77WyQfbf7fxdWRbCgCM3hsYXfowOlfEjNKHQAUA8YjSkZUHlO6WE4MrDyCAlI709gDso3rFuWG1dZacH9DX5+ieG4Iu5TczMxCrj08FWlQ67yvDGwAqYHv5SKB2JAhFlM58T2wvdZPKIS8KgDea4UWAsLZwNjJcARELQhAAK/Juw3i0egEIm4un+hBbH8bvrhyl+AGE3c6BaD+6SI37EN5H/6bk3lnoi3brjxiIppit/UvJ21rsiuHfWXF88SwZH6Rm7c8aNG80ZZyruz2gk5L30+ZArD/5LfqXrURSxiVEgzQZ5AH7J2tpLoTiDAdlPBGRIvd/mfVJXUyIIADAY+/wFWGVJjlA0GM+zX1M7f0w6zG+MrsXDADYAYThcC6bsxyNyQlZMU8FfYw0Hvy8cwCNMdyBtxvfx7p55oTxwrueLeaRd1CUB1J97ri4/43yACVGKaNo2vqEg0vM2+RR+SaaBQAIsCnlAoJTzFtyj8nYrDk2ABQIoTmhqJinIHjnAMqAxqBvThCWfZ475qn+rB6gmPuEQ9Exr3RUbS4AgLkLCMgLeLAxFRsf01qXudwAgHAob8oJtidIjn3eBgI7AMgJekVOaNzejKqoy7Sj1etaXegVMY/6QX6kubxqpnjRfGMzzmWeHYAsoTDGp+Tt9rouhQAAgS4gXN80rLlDV56jXxgAtniHMY36tVhq9TjscubBDoAe/0nMYp+X7/CQ8ZNyI0f1SlTdWT5IvV67x49cH0uyA0AVctnn6RqXYzNdE0rnCoDL2X6S4kWBkBsALmf7T8ebxnMCQMjrHaMCnv1ZwPd53vfZwfcdozJ0UsvuAS4xT/d5SlNl8wwHVgBcYn6SsZPGFRh5gcAGgEvM2872AMH07JBHTvDOAcnerm6LbH1jnq7XWCXdonNCtAeExDw1mtJFhkMUADExT42mdFEgBAPAEfPUaEoXkRO8AcBb+XX5UXTnxZHI+lYX+zxPz/7ICcPbuVHV3x2gj2+R0Ad63X0xoDCaaW8AwA7Z2FRs7mtamzVn42fTJ4unGgsCQH35VUz01qasfq1P38TXpI9NRhAA305ejj5/6wJs+7x+bUgfINBzAoyHPqHFGwCcxVE/S6EHvedJH3Re7/BMOeFnvy2+SD30a3yB8D4I6QK6vbZoNoZiP+IO6Pxc+/CEreWulBv+uwAly9sD1EK054P5wo1X8jmMB68oAJQy09x6hwBdYDvbc4PDLa/0HlABwO2i08av8oBpu2Pc+lYewI3otPGj23pyrp42I2L0rVX/PB0D3wNYW/ok+B9TbtOWqkx3MAAAAABJRU5ErkJggg==";

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
            IconCacheStatus status = sut.GetStatus(includeFiles: true);

            Path.GetFullPath(file.Path)
                .StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
                .Should().BeTrue();
            File.Exists(file.Path).Should().BeTrue();
            File.ReadAllBytes(file.Path)[..8].Should().Equal([137, 80, 78, 71, 13, 10, 26, 10]);
            Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
            status.FileCount.Should().Be(1);
            status.FilesByKind.Should().ContainKey("terrain").WhoseValue.Should().Be(1);
            status.FilesIncluded.Should().BeTrue();
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
    public async Task GetStatus_ByDefault_ReturnsSummaryWithoutFileInventory()
    {
        string root = NewTempRoot();
        try
        {
            IconCacheService sut = NewService(root, _ => ImageEnvelope("Soil"));

            await sut.GetTerrainIconAsync("Soil");
            IconCacheStatus status = sut.GetStatus();

            status.Directory.Should().Be(Path.GetFullPath(root));
            status.Exists.Should().BeTrue();
            status.FileCount.Should().Be(1);
            status.TotalBytes.Should().BeGreaterThan(0);
            status.LatestWriteAt.Should().NotBeNull();
            status.FilesByKind.Should().ContainKey("terrain").WhoseValue.Should().Be(1);
            status.FilesIncluded.Should().BeFalse();
            status.Files.Should().BeEmpty();
            status.LastWarm.Should().BeNull();
            status.WarmJob.State.Should().Be("idle");

            JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
            string json = JsonSerializer.Serialize(status, jsonOptions);
            using JsonDocument document = JsonDocument.Parse(json);
            document.RootElement.GetProperty("filesIncluded").GetBoolean().Should().BeFalse();
            document.RootElement.GetProperty("files").GetArrayLength().Should().Be(0);
            document.RootElement.TryGetProperty("files_included", out JsonElement _).Should().BeFalse();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GetTerrainIconAsync_WhenRimApiReturnsLargeTerrainPng_CachesThumbnail()
    {
        string root = NewTempRoot();
        try
        {
            byte[] source = CreateRgbaPng(width: 256, height: 128);
            IconCacheService sut = NewService(root, request =>
            {
                string path = request.RequestUri?.PathAndQuery ?? string.Empty;
                return path.Contains("terrain/image", StringComparison.OrdinalIgnoreCase)
                    ? ImageEnvelope("Soil", Convert.ToBase64String(source))
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            IconFile file = await sut.GetTerrainIconAsync("Soil");
            (int width, int height) = ReadPngSize(file.Path);

            width.Should().Be(128);
            height.Should().Be(64);
            File.ReadAllBytes(file.Path).Length.Should().BeLessThan(source.Length);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GetTerrainIconAsync_WhenExistingLargeTerrainPng_DownscalesInPlaceWithoutRefetch()
    {
        string root = NewTempRoot();
        try
        {
            string terrainDir = Path.Combine(root, "terrain");
            Directory.CreateDirectory(terrainDir);
            string iconPath = Path.Combine(terrainDir, "Soil.png");
            await File.WriteAllBytesAsync(iconPath, CreateRgbaPng(width: 256, height: 128));
            int imageCalls = 0;
            IconCacheService sut = NewService(root, request =>
            {
                string path = request.RequestUri?.PathAndQuery ?? string.Empty;
                if (path.Contains("terrain/image", StringComparison.OrdinalIgnoreCase))
                {
                    imageCalls++;
                    return ImageEnvelope("Soil");
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            IconFile file = await sut.GetTerrainIconAsync("Soil");
            (int width, int height) = ReadPngSize(file.Path);

            imageCalls.Should().Be(0);
            width.Should().Be(128);
            height.Should().Be(64);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GetItemIconAsync_WhenRimApiReturnsPlaceholderRedX_RejectsAndWritesNothing()
    {
        string root = NewTempRoot();
        try
        {
            IconCacheService sut = NewService(root, _ => ImageEnvelope("Frame_Cooler", RedXPlaceholderPngBase64));

            Func<Task> act = () => sut.GetItemIconAsync("Frame_Cooler");

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*placeholder red-X image*");
            sut.GetStatus().FileCount.Should().Be(0);
            Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GetStatus_IgnoresExistingPlaceholderRedXPngFiles()
    {
        string root = NewTempRoot();
        try
        {
            string itemDir = Path.Combine(root, "item");
            Directory.CreateDirectory(itemDir);
            await File.WriteAllBytesAsync(
                Path.Combine(itemDir, "Frame_Cooler.png"),
                Convert.FromBase64String(RedXPlaceholderPngBase64));
            await File.WriteAllBytesAsync(
                Path.Combine(itemDir, "MealSimple.png"),
                Convert.FromBase64String(PngBase64));
            IconCacheService sut = NewService(root, _ => ImageEnvelope("MealSimple"));

            IconCacheStatus status = sut.GetStatus(includeFiles: true);

            status.FileCount.Should().Be(1);
            status.Files.Should().ContainSingle(file => file.Kind == "item" && file.Id == "MealSimple");
            status.Files.Should().NotContain(file => file.Id == "Frame_Cooler");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GetItemIconAsync_WhenCachedPlaceholderExists_RefetchesAndOverwritesWithUsablePng()
    {
        string root = NewTempRoot();
        try
        {
            string itemDir = Path.Combine(root, "item");
            Directory.CreateDirectory(itemDir);
            string iconPath = Path.Combine(itemDir, "Frame_Cooler.png");
            await File.WriteAllBytesAsync(iconPath, Convert.FromBase64String(RedXPlaceholderPngBase64));
            int imageCalls = 0;
            IconCacheService sut = NewService(root, request =>
            {
                string path = request.RequestUri?.PathAndQuery ?? string.Empty;
                if (path.Contains("item/image", StringComparison.OrdinalIgnoreCase))
                {
                    imageCalls++;
                    return ImageEnvelope("Frame_Cooler");
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            IconFile file = await sut.GetItemIconAsync("Frame_Cooler");

            imageCalls.Should().Be(1);
            file.Path.Should().Be(iconPath);
            File.ReadAllBytes(iconPath).Should().Equal(Convert.FromBase64String(PngBase64));
            sut.GetStatus(includeFiles: true).Files.Should().ContainSingle(entry => entry.Id == "Frame_Cooler");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GetStatus_WhenSuccessfulManifestEntryNowHasPlaceholderFile_ReclassifiesItAsFailure()
    {
        string root = NewTempRoot();
        try
        {
            IconCacheService sut = NewService(root, request =>
            {
                string path = request.RequestUri?.PathAndQuery ?? string.Empty;
                if (path.Contains("def/all", StringComparison.OrdinalIgnoreCase))
                {
                    return SingleThingCatalog("Frame_Cooler");
                }

                if (path.Equals("/api/v1/factions", StringComparison.OrdinalIgnoreCase))
                {
                    return Json("""{ "success": true, "data": [], "errors": null, "warnings": null, "timestamp": null }""");
                }

                if (path.Contains("item/image", StringComparison.OrdinalIgnoreCase))
                {
                    return ImageEnvelope("Frame_Cooler");
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            IconWarmSummary first = await sut.WarmStaticAsync();
            string iconPath = Path.Combine(root, "item", "Frame_Cooler.png");
            await File.WriteAllBytesAsync(iconPath, Convert.FromBase64String(RedXPlaceholderPngBase64));

            IconCacheStatus status = sut.GetStatus(includeFiles: true);

            first.Succeeded.Should().Be(1);
            status.FileCount.Should().Be(0);
            status.LastWarm?.Succeeded.Should().Be(0);
            status.LastWarm?.Failed.Should().Be(1);
            status.LastWarm?.Failures.Should().ContainSingle(failure =>
                failure.Id == "Frame_Cooler" &&
                failure.FailureKind == "placeholder");
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
            IconCacheStatus status = sut.GetStatus(includeFiles: true);

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
    public async Task WarmStaticAsync_WhenIconReturnsPlaceholderRedX_RecordsPlaceholderFailureAndDoesNotCache()
    {
        string root = NewTempRoot();
        try
        {
            IconCacheService sut = NewService(root, request =>
            {
                string path = request.RequestUri?.PathAndQuery ?? string.Empty;
                if (path.Contains("def/all", StringComparison.OrdinalIgnoreCase))
                {
                    return SingleThingCatalog("Frame_Cooler");
                }

                if (path.Equals("/api/v1/factions", StringComparison.OrdinalIgnoreCase))
                {
                    return Json("""{ "success": true, "data": [], "errors": null, "warnings": null, "timestamp": null }""");
                }

                if (path.Contains("item/image", StringComparison.OrdinalIgnoreCase))
                {
                    return ImageEnvelope("Frame_Cooler", RedXPlaceholderPngBase64);
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            IconWarmSummary summary = await sut.WarmStaticAsync();
            IconCacheStatus status = sut.GetStatus();

            summary.Succeeded.Should().Be(0);
            summary.Failed.Should().Be(1);
            summary.Failures.Should().ContainSingle(failure =>
                failure.Kind == "item" &&
                failure.Id == "Frame_Cooler" &&
                failure.FailureKind == "placeholder");
            status.FileCount.Should().Be(0);
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

    [Fact]
    public async Task WarmStaticAsync_RespectsConfiguredRequestPacing()
    {
        string root = NewTempRoot();
        try
        {
            List<DateTimeOffset> imageCalls = [];
            IconCacheService sut = NewService(
                root,
                request =>
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
                                    "def_name": "Steel",
                                    "label": "steel",
                                    "category": "Item",
                                    "thing_class": "ThingWithComps",
                                    "is_item": true,
                                    "is_plant": false,
                                    "is_medicine": false,
                                    "is_drug": false,
                                    "nutrition": 0.0,
                                    "stack_limit": 75
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
                        return Json("""{ "success": true, "data": [], "errors": null, "warnings": null, "timestamp": null }""");
                    }

                    if (path.Contains("item/image", StringComparison.OrdinalIgnoreCase))
                    {
                        imageCalls.Add(DateTimeOffset.UtcNow);
                        return ImageEnvelope(path.Contains("Steel", StringComparison.OrdinalIgnoreCase) ? "Steel" : "MealSimple");
                    }

                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                },
                warmConcurrency: 2,
                warmRequestInterval: TimeSpan.FromMilliseconds(30));

            IconWarmSummary summary = await sut.WarmStaticAsync();

            summary.Succeeded.Should().Be(2);
            imageCalls.Should().HaveCount(2);
            (imageCalls[1] - imageCalls[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(20));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task StartWarm_RunsInBackgroundAndCompletesAfterCallerReturns()
    {
        string root = NewTempRoot();
        using ManualResetEventSlim releaseImage = new(false);
        try
        {
            IconCacheService sut = NewService(root, request =>
            {
                string path = request.RequestUri?.PathAndQuery ?? string.Empty;
                if (path.Contains("def/all", StringComparison.OrdinalIgnoreCase))
                {
                    return SingleThingCatalog("MealSimple");
                }

                if (path.Equals("/api/v1/factions", StringComparison.OrdinalIgnoreCase))
                {
                    return Json("""{ "success": true, "data": [], "errors": null, "warnings": null, "timestamp": null }""");
                }

                if (path.Contains("item/image", StringComparison.OrdinalIgnoreCase))
                {
                    releaseImage.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                    return ImageEnvelope("MealSimple");
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            IconWarmJobStatus started = sut.StartWarm();
            IconWarmJobStatus running = sut.GetStatus().WarmJob;
            releaseImage.Set();
            IconWarmJobStatus completed = await WaitForWarmJobAsync(sut);

            started.State.Should().Be("running");
            running.State.Should().Be("running");
            completed.State.Should().Be("completed");
            completed.Succeeded.Should().Be(1);
            sut.GetStatus().FileCount.Should().Be(1);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task WarmStaticAsync_WhenCancelled_CheckpointsManifestForPersistedPngs()
    {
        string root = NewTempRoot();
        using CancellationTokenSource cts = new();
        using ManualResetEventSlim secondImageStarted = new(false);
        try
        {
            int imageCalls = 0;
            IconCacheService sut = NewService(
                root,
                request =>
                {
                    string path = request.RequestUri?.PathAndQuery ?? string.Empty;
                    if (path.Contains("def/all", StringComparison.OrdinalIgnoreCase))
                    {
                        return TwoThingCatalog("MealSimple", "Steel");
                    }

                    if (path.Equals("/api/v1/factions", StringComparison.OrdinalIgnoreCase))
                    {
                        return Json("""{ "success": true, "data": [], "errors": null, "warnings": null, "timestamp": null }""");
                    }

                    if (path.Contains("item/image", StringComparison.OrdinalIgnoreCase))
                    {
                        int call = Interlocked.Increment(ref imageCalls);
                        if (call == 2)
                        {
                            secondImageStarted.Set();
                            SpinWait.SpinUntil(() => cts.IsCancellationRequested, TimeSpan.FromSeconds(5))
                                .Should().BeTrue();
                            throw new OperationCanceledException(cts.Token);
                        }

                        return ImageEnvelope(path.Contains("Steel", StringComparison.OrdinalIgnoreCase) ? "Steel" : "MealSimple");
                    }

                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                },
                warmConcurrency: 1,
                warmCheckpointInterval: 1);

            Task<IconWarmSummary> warm = sut.WarmStaticAsync(cts.Token);
            secondImageStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            cts.Cancel();

            await warm.Awaiting(task => task).Should().ThrowAsync<OperationCanceledException>();
            IconCacheStatus status = sut.GetStatus();
            status.FileCount.Should().Be(1);
            status.LastWarm.Should().NotBeNull();
            status.LastWarm?.Succeeded.Should().Be(1);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task StartWarm_WithFailedScope_RewarmsOnlyStillMissingFailures()
    {
        string root = NewTempRoot();
        try
        {
            int badThingResolved = 0;
            int goodImageCalls = 0;
            int badImageCalls = 0;
            IconCacheService sut = NewService(root, request =>
            {
                string path = request.RequestUri?.PathAndQuery ?? string.Empty;
                if (path.Contains("def/all", StringComparison.OrdinalIgnoreCase))
                {
                    return TwoThingCatalog("MealSimple", "BadThing");
                }

                if (path.Equals("/api/v1/factions", StringComparison.OrdinalIgnoreCase))
                {
                    return Json("""{ "success": true, "data": [], "errors": null, "warnings": null, "timestamp": null }""");
                }

                if (path.Contains("item/image", StringComparison.OrdinalIgnoreCase) &&
                    path.Contains("BadThing", StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref badImageCalls);
                    if (Volatile.Read(ref badThingResolved) == 0)
                    {
                        return MissingImageEnvelope();
                    }

                    return ImageEnvelope("BadThing");
                }

                if (path.Contains("item/image", StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref goodImageCalls);
                    return ImageEnvelope("MealSimple");
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            IconWarmSummary first = await sut.WarmStaticAsync();
            Volatile.Write(ref badThingResolved, 1);
            IconWarmJobStatus started = sut.StartWarm("failed");
            IconWarmJobStatus completed = await WaitForWarmJobAsync(sut);

            first.Failed.Should().Be(1);
            started.Scope.Should().Be("failed");
            completed.State.Should().Be("completed");
            goodImageCalls.Should().Be(1);
            badImageCalls.Should().Be(4);
            sut.GetStatus().LastWarm?.Failed.Should().Be(0);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task GetItemIconAsync_WhenPreviouslyFailedAndNowSucceeds_HealsManifestState()
    {
        string root = NewTempRoot();
        try
        {
            bool resolved = false;
            IconCacheService sut = NewService(root, request =>
            {
                string path = request.RequestUri?.PathAndQuery ?? string.Empty;
                if (path.Contains("def/all", StringComparison.OrdinalIgnoreCase))
                {
                    return SingleThingCatalog("BadThing");
                }

                if (path.Equals("/api/v1/factions", StringComparison.OrdinalIgnoreCase))
                {
                    return Json("""{ "success": true, "data": [], "errors": null, "warnings": null, "timestamp": null }""");
                }

                if (path.Contains("item/image", StringComparison.OrdinalIgnoreCase))
                {
                    return resolved ? ImageEnvelope("BadThing") : MissingImageEnvelope();
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            IconWarmSummary first = await sut.WarmStaticAsync();
            resolved = true;
            await sut.GetItemIconAsync("BadThing");
            IconCacheStatus status = sut.GetStatus();

            first.Failed.Should().Be(1);
            status.LastWarm?.Failed.Should().Be(0);
            status.LastWarm?.Succeeded.Should().Be(1);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task WarmStaticAsync_WhenNoWorldLoaded_DefersFactionWarmInsteadOfFailing()
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
                          "data": { "things_defs": [], "terrain_defs": [] },
                          "errors": null,
                          "warnings": null,
                          "timestamp": null
                        }
                        """);
                }

                if (path.Equals("/api/v1/game/state", StringComparison.OrdinalIgnoreCase))
                {
                    return Json("""
                        {
                          "success": true,
                          "data": {
                            "game_tick": 0,
                            "colony_wealth": 0,
                            "colonist_count": 0,
                            "storyteller": "none",
                            "is_paused": false,
                            "program_state": "Entry",
                            "map_count": 0
                          },
                          "errors": null,
                          "warnings": null,
                          "timestamp": null
                        }
                        """);
                }

                if (path.Equals("/api/v1/factions", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("factions should not be fetched without a loaded world");
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            IconWarmSummary summary = await sut.WarmStaticAsync();

            summary.Failed.Should().Be(0);
            summary.Deferred.Should().Be(1);
            summary.Failures.Should().ContainSingle(failure =>
                failure.Kind == "faction" &&
                failure.Id == "all" &&
                failure.Status == "deferred");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static IconCacheService NewService(
        string root,
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        int warmAttempts = 3,
        int warmConcurrency = 2,
        TimeSpan? warmRequestInterval = null,
        int warmCheckpointInterval = 25,
        CancellationToken applicationStopping = default)
    {
        HttpClient http = new(new DelegateHandler(request =>
        {
            HttpResponseMessage response = handler(request);
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                return response;
            }

            return DefaultHandshakeResponse(request) ?? response;
        }))
        {
            BaseAddress = new Uri("http://localhost:8765/")
        };
        RimApiClient rimApi = new(http);
        return new IconCacheService(
            root,
            rimApi,
            NullLogger<IconCacheService>.Instance,
            warmAttempts,
            TimeSpan.Zero,
            warmConcurrency,
            warmRequestInterval ?? TimeSpan.Zero,
            warmCheckpointInterval,
            applicationStopping);
    }

    private static HttpResponseMessage? DefaultHandshakeResponse(HttpRequestMessage request)
    {
        string path = request.RequestUri?.PathAndQuery ?? string.Empty;
        if (path.Equals("/api/v1/game/state", StringComparison.OrdinalIgnoreCase))
        {
            return Json("""
                {
                  "success": true,
                  "data": {
                    "game_tick": 100,
                    "colony_wealth": 1000,
                    "colonist_count": 1,
                    "storyteller": "Cassandra",
                    "is_paused": false,
                    "program_state": "Playing",
                    "map_count": 1
                  },
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """);
        }

        if (path.Equals("/api/v1/maps", StringComparison.OrdinalIgnoreCase))
        {
            return Json("""
                {
                  "success": true,
                  "data": [
                    {
                      "id": 1,
                      "index": 0,
                      "is_player_home": true,
                      "is_pocket_map": false,
                      "faction_id": "PlayerColony",
                      "seed": 1,
                      "size": "250x250"
                    }
                  ],
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """);
        }

        if (path.Equals("/api/v1/map/pawns?map_id=1", StringComparison.OrdinalIgnoreCase))
        {
            return Json("""
                {
                  "success": true,
                  "data": [
                    {
                      "id": 1,
                      "name": "Tester",
                      "gender": "Female",
                      "age": 30,
                      "health": 1.0,
                      "mood": 0.8,
                      "hunger": 0.9,
                      "position": null
                    }
                  ],
                  "errors": null,
                  "warnings": null,
                  "timestamp": null
                }
                """);
        }

        return null;
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
        ImageEnvelope(name, PngBase64);

    private static HttpResponseMessage ImageEnvelope(string name, string imageBase64) =>
        Json($$"""
            {
              "success": true,
              "data": {
                "name": "{{name}}",
                "result": "ok",
                "image_base64": "{{imageBase64}}"
              },
              "errors": null,
              "warnings": null,
              "timestamp": null
            }
            """);

    private static HttpResponseMessage MissingImageEnvelope() =>
        Json("""
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

    private static HttpResponseMessage SingleThingCatalog(string defName) =>
        Json(TwoThingCatalogJson(defName, null));

    private static HttpResponseMessage TwoThingCatalog(string firstDefName, string secondDefName) =>
        Json(TwoThingCatalogJson(firstDefName, secondDefName));

    private static string TwoThingCatalogJson(string firstDefName, string? secondDefName)
    {
        string second = string.IsNullOrWhiteSpace(secondDefName)
            ? string.Empty
            : $$"""
              ,
                                  {
                                    "def_name": "{{secondDefName}}",
                                    "label": "{{secondDefName}}",
                                    "category": "Item",
                                    "thing_class": "ThingWithComps",
                                    "is_item": true,
                                    "is_plant": false,
                                    "is_medicine": false,
                                    "is_drug": false,
                                    "nutrition": 0.0,
                                    "stack_limit": 75
                                  }
              """;

        return $$"""
            {
              "success": true,
              "data": {
                "things_defs": [
                  {
                    "def_name": "{{firstDefName}}",
                    "label": "{{firstDefName}}",
                    "category": "Item",
                    "thing_class": "ThingWithComps",
                    "is_item": true,
                    "is_plant": false,
                    "is_medicine": false,
                    "is_drug": false,
                    "nutrition": 0.0,
                    "stack_limit": 75
                  }{{second}}
                ],
                "terrain_defs": []
              },
              "errors": null,
              "warnings": null,
              "timestamp": null
            }
            """;
    }

    private static async Task<IconWarmJobStatus> WaitForWarmJobAsync(IconCacheService sut)
    {
        for (int i = 0; i < 100; i++)
        {
            IconWarmJobStatus status = sut.GetStatus().WarmJob;
            if (status.State != "running")
            {
                return status;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Icon warm job did not finish in time.");
    }

    private static (int Width, int Height) ReadPngSize(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return (
            checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4))),
            checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4))));
    }

    private static byte[] CreateRgbaPng(int width, int height)
    {
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        byte[] ihdrType = Encoding.ASCII.GetBytes("IHDR");
        byte[] idatType = Encoding.ASCII.GetBytes("IDAT");
        byte[] iendType = Encoding.ASCII.GetBytes("IEND");

        using MemoryStream raw = new();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < width; x++)
            {
                raw.WriteByte((byte)(x % 256));
                raw.WriteByte((byte)(y % 256));
                raw.WriteByte(96);
                raw.WriteByte(255);
            }
        }

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        using MemoryStream png = new();
        png.Write(signature);
        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), checked((uint)height));
        ihdr[8] = 8;
        ihdr[9] = 6;
        WritePngChunk(png, ihdrType, ihdr);
        WritePngChunk(png, idatType, compressed.ToArray());
        WritePngChunk(png, iendType, []);
        return png.ToArray();
    }

    private static void WritePngChunk(Stream output, byte[] type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        output.Write(length);
        output.Write(type);
        output.Write(data);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, PngCrc(type, data));
        output.Write(crc);
    }

    private static uint PngCrc(byte[] type, byte[] data)
    {
        uint crc = 0xffffffffu;
        foreach (byte value in type)
        {
            crc = PngCrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        foreach (byte value in data)
        {
            crc = PngCrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        return crc ^ 0xffffffffu;
    }

    private static readonly uint[] PngCrcTable = BuildPngCrcTable();

    private static uint[] BuildPngCrcTable()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint crc = i;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 1 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
            }

            table[i] = crc;
        }

        return table;
    }

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
