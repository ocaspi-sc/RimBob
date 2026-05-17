using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using RimBob.Ingestion;
using RimBob.Ingestion.Dtos;

namespace RimBob.Host;

public sealed class IconCacheService
{
    private const string ContentTypePng = "image/png";
    private const int WarmConcurrency = 2;
    private const int DefaultWarmAttempts = 3;
    private const int MaxStatusFailures = 100;
    private static readonly TimeSpan DefaultWarmRetryDelay = TimeSpan.FromMilliseconds(400);
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly Regex PawnPortraitKeyPattern = new(
        "^(?<pawnId>[0-9]+)-(?<width>[0-9]+)x(?<height>[0-9]+)-(?<direction>[A-Za-z]+)$",
        RegexOptions.Compiled);
    private static readonly Regex SafeKeyPattern = new("^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string rootDirectory;
    private readonly string manifestPath;
    private readonly RimApiClient rimApi;
    private readonly ILogger<IconCacheService> log;
    private readonly int warmAttempts;
    private readonly TimeSpan warmRetryDelay;

    public IconCacheService(
        string rootDirectory,
        RimApiClient rimApi,
        ILogger<IconCacheService> log,
        int warmAttempts = DefaultWarmAttempts,
        TimeSpan? warmRetryDelay = null)
    {
        this.rootDirectory = Path.GetFullPath(rootDirectory);
        manifestPath = Path.Combine(this.rootDirectory, "icon-cache-manifest.json");
        this.rimApi = rimApi;
        this.log = log;
        this.warmAttempts = Math.Max(1, warmAttempts);
        this.warmRetryDelay = warmRetryDelay ?? DefaultWarmRetryDelay;
    }

    public async Task<IconFile> GetItemIconAsync(string defName, CancellationToken ct = default) =>
        await GetCachedPngAsync("item", defName, async token =>
        {
            RimApiImageDto image = await rimApi.GetItemImageAsync(defName, token);
            return RequiredBase64(image.Base64, $"item {defName}");
        }, ct);

    public async Task<IconFile> GetTerrainIconAsync(string defName, CancellationToken ct = default) =>
        await GetCachedPngAsync("terrain", defName, async token =>
        {
            RimApiImageDto image = await rimApi.GetTerrainImageAsync(defName, token);
            return RequiredBase64(image.Base64, $"terrain {defName}");
        }, ct);

    public async Task<IconFile> GetFactionIconAsync(int loadId, CancellationToken ct = default) =>
        await GetCachedPngAsync("faction", loadId.ToString(CultureInfo.InvariantCulture), async token =>
        {
            FactionIconDto icon = await rimApi.GetFactionIconAsync(loadId, token);
            return RequiredBase64(icon.Image?.Base64, $"faction {loadId}");
        }, ct);

    public async Task<IconFile> GetPawnPortraitAsync(
        int pawnId,
        int width,
        int height,
        string direction,
        CancellationToken ct = default)
    {
        ValidatePortraitParameters(width, height, direction);
        string key = $"{pawnId}-{width}x{height}-{direction}";
        return await GetCachedPngAsync("pawn-portrait", key, async token =>
        {
            RimApiImageDto image = await rimApi.GetPawnPortraitImageAsync(pawnId, width, height, direction, token);
            return RequiredBase64(image.Base64, $"pawn portrait {pawnId}");
        }, ct);
    }

    public async Task<ColonistBodyIconResponse> GetColonistBodyAsync(int pawnId, CancellationToken ct = default)
    {
        string key = pawnId.ToString(CultureInfo.InvariantCulture);
        ColonistBodyImageDto image = await rimApi.GetColonistBodyImageAsync(pawnId, ct);
        bool bodyCached = false;
        bool headCached = false;

        if (!string.IsNullOrWhiteSpace(image.BodyImage))
        {
            await WriteCachedPngAsync("colonist-body", $"{key}-body", image.BodyImage, ct);
            bodyCached = true;
        }

        if (!string.IsNullOrWhiteSpace(image.HeadImage))
        {
            await WriteCachedPngAsync("colonist-body", $"{key}-head", image.HeadImage, ct);
            headCached = true;
        }

        return new ColonistBodyIconResponse(
            PawnId: pawnId,
            BodyCached: bodyCached,
            BodyColor: image.BodyColor,
            HeadCached: headCached,
            HeadColor: image.HeadColor);
    }

    public async Task<IconWarmSummary> WarmStaticAsync(CancellationToken ct = default)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        DefCatalogDto catalog = await rimApi.GetDefCatalogAsync(ct);
        List<IconWarmCandidate> itemCandidates = [];
        List<IconWarmCandidate> terrainCandidates = [];
        List<IconWarmCandidate> factionCandidates = [];
        int skipped = 0;

        IReadOnlyList<ThingDefDto> things = catalog.ThingsDefs ?? [];
        foreach (ThingDefDto def in things)
        {
            if (IsStaticThingCandidate(def))
            {
                itemCandidates.Add(new IconWarmCandidate("item", def.DefName));
            }
            else if (!string.IsNullOrWhiteSpace(def.DefName))
            {
                skipped++;
            }
        }

        IReadOnlyList<TerrainDefDto> terrains = catalog.TerrainDefs ?? [];
        foreach (TerrainDefDto def in terrains.Where(def => !string.IsNullOrWhiteSpace(def.DefName)))
        {
            terrainCandidates.Add(new IconWarmCandidate("terrain", def.DefName));
        }

        List<IconWarmFailure> setupFailures = [];
        try
        {
            IReadOnlyList<FactionDto> factions = await rimApi.GetFactionsAsync(ct);
            foreach (FactionDto faction in factions)
            {
                factionCandidates.Add(new IconWarmCandidate("faction", faction.LoadId.ToString(CultureInfo.InvariantCulture)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            setupFailures.Add(new IconWarmFailure("faction", "all", $"faction catalog unavailable: {ex.Message}"));
        }

        IconWarmCandidate[] uniqueCandidates = terrainCandidates
            .Concat(factionCandidates)
            .Concat(itemCandidates)
            .GroupBy(candidate => $"{candidate.Kind}:{candidate.Id}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        using SemaphoreSlim gate = new(WarmConcurrency);
        Task<IconWarmResult>[] tasks = uniqueCandidates
            .Select(candidate => WarmCandidateAsync(candidate, gate, ct))
            .ToArray();
        IconWarmResult[] results = await Task.WhenAll(tasks);

        DateTimeOffset completed = DateTimeOffset.UtcNow;
        List<IconWarmFailure> failures = results
            .Where(result => !result.Success)
            .Select(result => new IconWarmFailure(result.Kind, result.Id, result.Error ?? "unknown error"))
            .Concat(setupFailures)
            .ToList();

        IconWarmSummary summary = new(
            StartedAt: started,
            CompletedAt: completed,
            TotalCandidates: uniqueCandidates.Length,
            Succeeded: results.Count(result => result.Success),
            Failed: results.Count(result => !result.Success) + setupFailures.Count,
            Skipped: skipped,
            ItemCandidates: uniqueCandidates.Count(candidate => candidate.Kind == "item"),
            TerrainCandidates: uniqueCandidates.Count(candidate => candidate.Kind == "terrain"),
            FactionCandidates: uniqueCandidates.Count(candidate => candidate.Kind == "faction"),
            Failures: failures);

        await WriteManifestAsync(new IconCacheManifest(summary), ct);
        return summary;
    }

    public IconCacheStatus GetStatus()
    {
        Directory.CreateDirectory(rootDirectory);
        IconCacheManifest? manifest = ReadManifest();
        FileInfo[] files = new DirectoryInfo(rootDirectory)
            .EnumerateFiles("*.png", SearchOption.AllDirectories)
            .ToArray();
        IconCacheFile[] fileEntries = files
            .Select(ToCacheFile)
            .OrderBy(file => file.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Dictionary<string, int> byKind = files
            .GroupBy(file => file.Directory?.Name ?? "unknown", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        DateTimeOffset? latest = files.Length == 0
            ? null
            : new DateTimeOffset(files.Max(file => DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc)));

        return new IconCacheStatus(
            Directory: rootDirectory,
            Exists: Directory.Exists(rootDirectory),
            FileCount: files.Length,
            TotalBytes: files.Sum(file => file.Length),
            LatestWriteAt: latest,
            FilesByKind: byKind,
            Files: fileEntries,
            LastWarm: BoundForStatus(manifest?.LastWarm));
    }

    private async Task<IconWarmResult> WarmCandidateAsync(
        IconWarmCandidate candidate,
        SemaphoreSlim gate,
        CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (candidate.Kind is not ("item" or "terrain" or "faction"))
            {
                return new IconWarmResult(candidate.Kind, candidate.Id, false, "unknown icon kind");
            }

            Exception? lastError = null;
            for (int attempt = 1; attempt <= warmAttempts; attempt++)
            {
                try
                {
                    await FetchCandidateAsync(candidate, ct);
                    return new IconWarmResult(candidate.Kind, candidate.Id, true, null);
                }
                catch (Exception ex) when (ex is ArgumentException)
                {
                    // Bad/unsafe key — deterministic, retrying cannot help.
                    log.LogDebug(ex, "Icon warm rejected {IconKind} {IconId}", candidate.Kind, candidate.Id);
                    return new IconWarmResult(candidate.Kind, candidate.Id, false, ex.Message);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = ex;
                    log.LogDebug(
                        ex,
                        "Icon warm attempt {Attempt}/{Attempts} failed for {IconKind} {IconId}",
                        attempt,
                        warmAttempts,
                        candidate.Kind,
                        candidate.Id);
                    if (attempt < warmAttempts && warmRetryDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(warmRetryDelay * attempt, ct);
                    }
                }
            }

            return new IconWarmResult(
                candidate.Kind,
                candidate.Id,
                false,
                lastError?.Message ?? "unknown error");
        }
        finally
        {
            gate.Release();
        }
    }

    private Task<IconFile> FetchCandidateAsync(IconWarmCandidate candidate, CancellationToken ct) =>
        candidate.Kind switch
        {
            "item" => GetItemIconAsync(candidate.Id, ct),
            "terrain" => GetTerrainIconAsync(candidate.Id, ct),
            "faction" => GetFactionIconAsync(int.Parse(candidate.Id, CultureInfo.InvariantCulture), ct),
            _ => throw new ArgumentException($"unknown icon kind: {candidate.Kind}")
        };

    private async Task<IconFile> GetCachedPngAsync(
        string kind,
        string id,
        Func<CancellationToken, Task<string>> fetchBase64,
        CancellationToken ct)
    {
        string path = ResolveIconPath(kind, id);
        if (File.Exists(path))
        {
            return new IconFile(path, ContentTypePng, PublicPath(kind, id));
        }

        string base64 = await fetchBase64(ct);
        return await WriteCachedPngAsync(kind, id, base64, ct);
    }

    private async Task<IconFile> WriteCachedPngAsync(
        string kind,
        string id,
        string base64,
        CancellationToken ct)
    {
        string path = ResolveIconPath(kind, id);
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Could not resolve icon directory for {path}");
        Directory.CreateDirectory(directory);

        byte[] bytes = DecodePng(base64);
        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes, ct);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        return new IconFile(path, ContentTypePng, PublicPath(kind, id));
    }

    private string ResolveIconPath(string kind, string id)
    {
        string safeKind = SafeKey(kind);
        string safeId = SafeKey(id);
        string directory = Path.GetFullPath(Path.Combine(rootDirectory, safeKind));
        string path = Path.GetFullPath(Path.Combine(directory, $"{safeId}.png"));
        string rootWithSeparator = rootDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? rootDirectory
            : rootDirectory + Path.DirectorySeparatorChar;

        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Icon path escaped the cache root.");
        }

        return path;
    }

    private IconCacheFile ToCacheFile(FileInfo file)
    {
        string kind = file.Directory?.Name ?? "unknown";
        string id = Path.GetFileNameWithoutExtension(file.Name);
        string relativePath = Path.GetRelativePath(rootDirectory, file.FullName)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        DateTimeOffset lastWriteAt = new(DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc));

        return new IconCacheFile(
            Kind: kind,
            Id: id,
            Name: file.Name,
            RelativePath: relativePath,
            SizeBytes: file.Length,
            LastWriteAt: lastWriteAt,
            PublicPath: PublicPathForCachedFile(kind, id));
    }

    private static string PublicPath(string kind, string id) =>
        PublicPathForCachedFile(kind, id) ?? string.Empty;

    private static string? PublicPathForCachedFile(string kind, string id) => kind switch
    {
        "item" => $"/api/icons/item/{Uri.EscapeDataString(id)}",
        "terrain" => $"/api/icons/terrain/{Uri.EscapeDataString(id)}",
        "faction" => $"/api/icons/faction/{Uri.EscapeDataString(id)}",
        "pawn-portrait" => PawnPortraitPublicPath(id),
        _ => null
    };

    private static string? PawnPortraitPublicPath(string id)
    {
        Match match = PawnPortraitKeyPattern.Match(id);
        if (!match.Success) return null;

        string pawnId = match.Groups["pawnId"].Value;
        string width = match.Groups["width"].Value;
        string height = match.Groups["height"].Value;
        string direction = match.Groups["direction"].Value;
        return $"/api/icons/pawn/{Uri.EscapeDataString(pawnId)}/portrait?width={Uri.EscapeDataString(width)}&height={Uri.EscapeDataString(height)}&direction={Uri.EscapeDataString(direction)}";
    }

    private static string SafeKey(string value)
    {
        string trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || !SafeKeyPattern.IsMatch(trimmed))
        {
            throw new ArgumentException($"Unsafe icon key: {value}");
        }

        return trimmed;
    }

    private static string RequiredBase64(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"RIMAPI did not return image data for {label}.");
        }

        return value;
    }

    private static byte[] DecodePng(string base64)
    {
        string trimmed = base64.Trim();
        int comma = trimmed.IndexOf(',');
        if (comma >= 0)
        {
            trimmed = trimmed[(comma + 1)..];
        }

        byte[] bytes = Convert.FromBase64String(trimmed);
        if (bytes.Length < PngSignature.Length || !PngSignature.SequenceEqual(bytes[..PngSignature.Length]))
        {
            throw new FormatException("RIMAPI icon response was not a PNG image.");
        }

        return bytes;
    }

    private static void ValidatePortraitParameters(int width, int height, string direction)
    {
        if (width is < 16 or > 512 || height is < 16 or > 512)
        {
            throw new ArgumentException("Pawn portrait width and height must be between 16 and 512.");
        }

        SafeKey(direction);
    }

    private static bool IsStaticThingCandidate(ThingDefDto def)
    {
        if (string.IsNullOrWhiteSpace(def.DefName)) return false;
        string category = def.Category ?? string.Empty;
        string thingClass = def.ThingClass ?? string.Empty;
        string combined = $"{def.DefName} {category} {thingClass}";
        if (ContainsAny(combined, ["Projectile", "Mote", "Fleck", "Pawn"])) return false;
        return def.IsItem ||
               def.IsPlant ||
               def.IsBuilding ||
               def.IsApparel ||
               def.IsWeapon ||
               string.Equals(category, "Item", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "Building", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "Plant", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private IconCacheManifest? ReadManifest()
    {
        if (!File.Exists(manifestPath)) return null;
        try
        {
            string json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<IconCacheManifest>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            log.LogWarning(ex, "Could not read icon cache manifest at {ManifestPath}", manifestPath);
            return null;
        }
    }

    private static IconWarmSummary? BoundForStatus(IconWarmSummary? summary)
    {
        if (summary is null || summary.Failures.Count <= MaxStatusFailures)
        {
            return summary;
        }

        return summary with
        {
            Failures = summary.Failures.Take(MaxStatusFailures).ToList()
        };
    }

    private async Task WriteManifestAsync(IconCacheManifest manifest, CancellationToken ct)
    {
        Directory.CreateDirectory(rootDirectory);
        string tempPath = $"{manifestPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using FileStream stream = File.Create(tempPath);
            await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, ct);
            await stream.DisposeAsync();
            File.Move(tempPath, manifestPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}

public sealed record IconFile(string Path, string ContentType, string PublicPath);

public sealed record IconCacheStatus(
    string Directory,
    bool Exists,
    int FileCount,
    long TotalBytes,
    DateTimeOffset? LatestWriteAt,
    IReadOnlyDictionary<string, int> FilesByKind,
    IReadOnlyList<IconCacheFile> Files,
    IconWarmSummary? LastWarm);

public sealed record IconCacheFile(
    string Kind,
    string Id,
    string Name,
    string RelativePath,
    long SizeBytes,
    DateTimeOffset LastWriteAt,
    string? PublicPath);

public sealed record IconWarmSummary(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int TotalCandidates,
    int Succeeded,
    int Failed,
    int Skipped,
    int ItemCandidates,
    int TerrainCandidates,
    int FactionCandidates,
    IReadOnlyList<IconWarmFailure> Failures);

public sealed record IconWarmFailure(string Kind, string Id, string Error);

public sealed record ColonistBodyIconResponse(
    int PawnId,
    bool BodyCached,
    string? BodyColor,
    bool HeadCached,
    string? HeadColor);

internal sealed record IconCacheManifest(IconWarmSummary? LastWarm);

internal sealed record IconWarmCandidate(string Kind, string Id);

internal sealed record IconWarmResult(string Kind, string Id, bool Success, string? Error);
