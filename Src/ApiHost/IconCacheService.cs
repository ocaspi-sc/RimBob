using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using RimBob.Ingestion;
using RimBob.Ingestion.Dtos;

namespace RimBob.Host;

public sealed class IconCacheService
{
    private const string ContentTypePng = "image/png";
    private const int DefaultWarmConcurrency = 2;
    private const int DefaultWarmAttempts = 3;
    private const int DefaultWarmCheckpointInterval = 25;
    private const int MaxStatusFailures = 100;
    private const int TerrainIconMaxDimension = 128;
    private const int RedXPlaceholderPngLength = 1132;
    private const string RedXPlaceholderPngSha256 = "0D25BE2358B9CC93B0154E9BE5CE4769FBB30023C2D6CBC46E636798917DC13B";
    private static readonly TimeSpan DefaultStatusCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultWarmRetryDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan DefaultWarmRequestInterval = TimeSpan.FromMilliseconds(100);
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
    private readonly int warmConcurrency;
    private readonly int warmCheckpointInterval;
    private readonly TimeSpan warmRetryDelay;
    private readonly TimeSpan warmRequestInterval;
    private readonly TimeSpan statusCacheDuration;
    private readonly CancellationToken applicationStopping;
    private readonly SemaphoreSlim manifestLock = new(1, 1);
    private readonly SemaphoreSlim paceLock = new(1, 1);
    private readonly object warmJobLock = new();
    private readonly object statusCacheLock = new();
    private DateTimeOffset nextWarmRequestAt = DateTimeOffset.MinValue;
    private IconWarmJobStatus? activeWarmJob;
    private IconCacheStatus? cachedSummaryStatus;
    private DateTimeOffset cachedSummaryStatusExpiresAt = DateTimeOffset.MinValue;

    public IconCacheService(
        string rootDirectory,
        RimApiClient rimApi,
        ILogger<IconCacheService> log,
        int warmAttempts = DefaultWarmAttempts,
        TimeSpan? warmRetryDelay = null,
        int warmConcurrency = DefaultWarmConcurrency,
        TimeSpan? warmRequestInterval = null,
        int warmCheckpointInterval = DefaultWarmCheckpointInterval,
        CancellationToken applicationStopping = default,
        TimeSpan? statusCacheDuration = null)
    {
        this.rootDirectory = Path.GetFullPath(rootDirectory);
        manifestPath = Path.Combine(this.rootDirectory, "icon-cache-manifest.json");
        this.rimApi = rimApi;
        this.log = log;
        this.warmAttempts = Math.Max(1, warmAttempts);
        this.warmConcurrency = Math.Max(1, warmConcurrency);
        this.warmCheckpointInterval = Math.Max(1, warmCheckpointInterval);
        this.warmRetryDelay = warmRetryDelay ?? DefaultWarmRetryDelay;
        this.warmRequestInterval = warmRequestInterval ?? DefaultWarmRequestInterval;
        this.statusCacheDuration = statusCacheDuration ?? DefaultStatusCacheDuration;
        this.applicationStopping = applicationStopping;
    }

    public async Task<IconFile> GetItemIconAsync(string defName, CancellationToken ct = default) =>
        await GetCachedPngAsync("item", defName, async token =>
        {
            RimApiImageDto image = await rimApi.GetItemImageAsync(defName, token);
            return RequiredBase64(image.Base64, $"item {defName}", image.Result);
        }, ct);

    public async Task<IconFile> GetTerrainIconAsync(string defName, CancellationToken ct = default) =>
        await GetCachedPngAsync("terrain", defName, async token =>
        {
            RimApiImageDto image = await rimApi.GetTerrainImageAsync(defName, token);
            return RequiredBase64(image.Base64, $"terrain {defName}", image.Result);
        }, ct);

    public async Task<IconFile> GetFactionIconAsync(int loadId, CancellationToken ct = default) =>
        await GetCachedPngAsync("faction", loadId.ToString(CultureInfo.InvariantCulture), async token =>
        {
            FactionIconDto icon = await rimApi.GetFactionIconAsync(loadId, token);
            return RequiredBase64(icon.Image?.Base64, $"faction {loadId}", icon.Image?.Result);
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
            return RequiredBase64(image.Base64, $"pawn portrait {pawnId}", image.Result);
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

    public IconWarmJobStatus StartWarm(string? scope = null)
    {
        IconWarmScope warmScope = ParseWarmScope(scope);
        IconWarmJobStatus status;
        lock (warmJobLock)
        {
            if (activeWarmJob is { State: IconWarmJobStates.Running } running)
            {
                return running;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            status = new IconWarmJobStatus(
                State: IconWarmJobStates.Running,
                JobId: Guid.NewGuid().ToString("N"),
                Scope: WarmScopeText(warmScope),
                StartedAt: now,
                UpdatedAt: now,
                CompletedAt: null,
                Total: 0,
                Done: 0,
                Succeeded: 0,
                Failed: 0,
                Deferred: 0,
                Error: null);
            activeWarmJob = status;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                using CancellationTokenSource cts =
                    CancellationTokenSource.CreateLinkedTokenSource(applicationStopping);
                IconWarmSummary summary = await WarmStaticAsync(warmScope, status.JobId, cts.Token);
                CompleteWarmJob(status.JobId, summary, null);
            }
            catch (OperationCanceledException ex) when (applicationStopping.IsCancellationRequested)
            {
                CompleteWarmJob(status.JobId, null, $"application stopping: {ex.Message}");
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Icon warm job {JobId} failed", status.JobId);
                CompleteWarmJob(status.JobId, null, ex.Message);
            }
        }, CancellationToken.None);

        return status;
    }

    public async Task<IconWarmSummary> WarmStaticAsync(CancellationToken ct = default) =>
        await WarmStaticAsync(IconWarmScope.All, null, ct);

    public IconCacheStatus GetStatus(bool includeFiles = false)
    {
        if (includeFiles || statusCacheDuration <= TimeSpan.Zero)
        {
            return BuildStatus(includeFiles);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (statusCacheLock)
        {
            if (cachedSummaryStatus is not null && cachedSummaryStatusExpiresAt > now)
            {
                return cachedSummaryStatus with
                {
                    WarmJob = CurrentWarmJob(cachedSummaryStatus.WarmJob)
                };
            }
        }

        IconCacheStatus status = BuildStatus(includeFiles: false);
        lock (statusCacheLock)
        {
            cachedSummaryStatus = status;
            cachedSummaryStatusExpiresAt = DateTimeOffset.UtcNow + statusCacheDuration;
        }

        return status;
    }

    private IconCacheStatus BuildStatus(bool includeFiles)
    {
        Directory.CreateDirectory(rootDirectory);
        IconCacheManifest? manifest = ReadManifest();
        FileInfo[] files = new DirectoryInfo(rootDirectory)
            .EnumerateFiles("*.png", SearchOption.AllDirectories)
            .Where(file => !IsKnownPlaceholderPng(file))
            .ToArray();
        IconCacheFile[] fileEntries = includeFiles
            ? files
                .Select(ToCacheFile)
                .OrderBy(file => file.Kind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(file => file.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        Dictionary<string, int> byKind = files
            .GroupBy(file => file.Directory?.Name ?? "unknown", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        DateTimeOffset? latest = files.Length == 0
            ? null
            : new DateTimeOffset(files.Max(file => DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc)));

        IconWarmSummary? lastWarm = manifest?.LastWarm;
        Dictionary<string, IconWarmEntry> entries = ManifestEntries(manifest);
        if (lastWarm is not null && entries.Count > 0)
        {
            lastWarm = DeriveSummaryFromEntries(lastWarm.StartedAt, lastWarm.CompletedAt, entries, lastWarm.Skipped);
        }

        return new IconCacheStatus(
            Directory: rootDirectory,
            Exists: Directory.Exists(rootDirectory),
            FileCount: files.Length,
            TotalBytes: files.Sum(file => file.Length),
            LatestWriteAt: latest,
            FilesByKind: byKind,
            FilesIncluded: includeFiles,
            Files: fileEntries,
            LastWarm: BoundForStatus(lastWarm),
            WarmJob: CurrentWarmJob(manifest?.LastJob));
    }

    private async Task<IconWarmSummary> WarmStaticAsync(
        IconWarmScope scope,
        string? jobId,
        CancellationToken ct)
    {
        InvalidateStatusCache();
        DateTimeOffset started = DateTimeOffset.UtcNow;
        IconCacheManifest? existingManifest = ReadManifest();
        Dictionary<string, IconWarmEntry> entries = ManifestEntries(existingManifest);
        IconWarmPlan plan = scope == IconWarmScope.Failed
            ? BuildFailedWarmPlan(entries)
            : await BuildAllWarmPlanAsync(ct);

        foreach (IconWarmResult setupResult in plan.SetupResults)
        {
            ApplyWarmResult(entries, setupResult, started);
        }

        IconWarmSummary initialSummary = DeriveSummaryFromEntries(started, started, entries, plan.Skipped);
        IconWarmJobStatus? initialJob = RunningJobStatus(
            jobId,
            scope,
            started,
            total: plan.Candidates.Count + plan.SetupResults.Count,
            done: plan.SetupResults.Count,
            succeeded: 0,
            failed: plan.SetupResults.Count(result => result.Status == IconWarmEntryStatus.Failed),
            deferred: plan.SetupResults.Count(result => result.Status == IconWarmEntryStatus.Deferred),
            error: null);
        await WriteManifestAsync(new IconCacheManifest(initialSummary, entries, initialJob), ct);
        if (initialJob is not null)
        {
            UpdateWarmJob(initialJob);
        }

        int done = plan.SetupResults.Count;
        int succeeded = 0;
        int failed = plan.SetupResults.Count(result => result.Status == IconWarmEntryStatus.Failed);
        int deferred = plan.SetupResults.Count(result => result.Status == IconWarmEntryStatus.Deferred);

        using SemaphoreSlim gate = new(warmConcurrency);
        List<Task<IconWarmResult>> pending = plan.Candidates
            .Select(candidate => WarmCandidateAsync(candidate, gate, ct))
            .ToList();

        try
        {
            while (pending.Count > 0)
            {
                Task<IconWarmResult> finished = await Task.WhenAny(pending);
                pending.Remove(finished);
                IconWarmResult result = await finished;

                done++;
                if (result.Status == IconWarmEntryStatus.Succeeded) succeeded++;
                else if (result.Status == IconWarmEntryStatus.Deferred) deferred++;
                else failed++;

                ApplyWarmResult(entries, result, DateTimeOffset.UtcNow);
                IconWarmJobStatus? progressJob = RunningJobStatus(
                    jobId,
                    scope,
                    started,
                    plan.Candidates.Count + plan.SetupResults.Count,
                    done,
                    succeeded,
                    failed,
                    deferred,
                    null);

                if (progressJob is not null)
                {
                    UpdateWarmJob(progressJob);
                }

                if (done % warmCheckpointInterval == 0)
                {
                    IconWarmSummary checkpoint = DeriveSummaryFromEntries(
                        started,
                        DateTimeOffset.UtcNow,
                        entries,
                        plan.Skipped);
                    await WriteManifestAsync(new IconCacheManifest(checkpoint, entries, progressJob), ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            DateTimeOffset interruptedAt = DateTimeOffset.UtcNow;
            IconWarmSummary interrupted = DeriveSummaryFromEntries(started, interruptedAt, entries, plan.Skipped);
            IconWarmJobStatus? failedJob = null;
            if (jobId is not null)
            {
                IconWarmJobStatus failedJobBase = RunningJobStatus(
                    jobId,
                    scope,
                    started,
                    plan.Candidates.Count + plan.SetupResults.Count,
                    done,
                    succeeded,
                    failed,
                    deferred,
                    "warm cancelled")!;
                failedJob = failedJobBase with
                {
                    State = IconWarmJobStates.Failed,
                    CompletedAt = interruptedAt
                };
            }
            await WriteManifestAsync(new IconCacheManifest(interrupted, entries, failedJob), CancellationToken.None);
            InvalidateStatusCache();
            throw;
        }

        DateTimeOffset completed = DateTimeOffset.UtcNow;
        IconWarmSummary summary = DeriveSummaryFromEntries(started, completed, entries, plan.Skipped);
        IconWarmJobStatus? completedJob = jobId is null
            ? null
            : new IconWarmJobStatus(
                State: summary.Failed > 0 ? IconWarmJobStates.Completed : IconWarmJobStates.Completed,
                JobId: jobId,
                Scope: WarmScopeText(scope),
                StartedAt: started,
                UpdatedAt: completed,
                CompletedAt: completed,
                Total: plan.Candidates.Count + plan.SetupResults.Count,
                Done: plan.Candidates.Count + plan.SetupResults.Count,
                Succeeded: succeeded,
                Failed: failed,
                Deferred: deferred,
                Error: null);
        await WriteManifestAsync(new IconCacheManifest(summary, entries, completedJob), ct);
        InvalidateStatusCache();
        return summary;
    }

    private async Task<IconWarmPlan> BuildAllWarmPlanAsync(CancellationToken ct)
    {
        DefCatalogDto catalog = await rimApi.GetDefCatalogAsync(ct);
        List<IconWarmCandidate> itemCandidates = [];
        List<IconWarmCandidate> terrainCandidates = [];
        List<IconWarmCandidate> factionCandidates = [];
        List<IconWarmResult> setupResults = [];
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

        try
        {
            (bool ok, string message) = await rimApi.HandshakeAsync(ct);
            if (!ok)
            {
                setupResults.Add(IconWarmResult.Deferred(
                    "faction",
                    "all",
                    $"faction warming deferred until a world is loaded: {message}",
                    attempts: 0));
            }
            else
            {
                IReadOnlyList<FactionDto> factions = await rimApi.GetFactionsAsync(ct);
                foreach (FactionDto faction in factions)
                {
                    factionCandidates.Add(new IconWarmCandidate(
                        "faction",
                        faction.LoadId.ToString(CultureInfo.InvariantCulture)));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            setupResults.Add(IconWarmResult.Deferred(
                "faction",
                "all",
                $"faction warming deferred until faction data is available: {ex.Message}",
                attempts: 0));
        }

        IconWarmCandidate[] uniqueCandidates = terrainCandidates
            .Concat(factionCandidates)
            .Concat(itemCandidates)
            .GroupBy(candidate => CandidateKey(candidate.Kind, candidate.Id), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return new IconWarmPlan(uniqueCandidates, setupResults, skipped);
    }

    private IconWarmPlan BuildFailedWarmPlan(IReadOnlyDictionary<string, IconWarmEntry> entries)
    {
        IconWarmCandidate[] candidates = entries.Values
            .Where(entry => entry.Status == IconWarmEntryStatus.Failed)
            .Where(entry => IsStaticWarmKind(entry.Kind))
            .Where(entry => !HasCachedFile(entry.Kind, entry.Id))
            .Select(entry => new IconWarmCandidate(entry.Kind, entry.Id))
            .GroupBy(candidate => CandidateKey(candidate.Kind, candidate.Id), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return new IconWarmPlan(candidates, [], Skipped: 0);
    }

    private async Task<IconWarmResult> WarmCandidateAsync(
        IconWarmCandidate candidate,
        SemaphoreSlim gate,
        CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!IsStaticWarmKind(candidate.Kind))
            {
                return IconWarmResult.Failed(
                    candidate.Kind,
                    candidate.Id,
                    "unknown icon kind",
                    IconWarmFailureKinds.BadKey,
                    attempts: 0);
            }

            Exception? lastError = null;
            string lastFailureKind = IconWarmFailureKinds.Transport;
            for (int attempt = 1; attempt <= warmAttempts; attempt++)
            {
                try
                {
                    await PaceWarmRequestAsync(ct);
                    await FetchCandidateAsync(candidate, ct);
                    return IconWarmResult.Succeeded(candidate.Kind, candidate.Id, attempt);
                }
                catch (Exception ex) when (ex is ArgumentException)
                {
                    log.LogDebug(ex, "Icon warm rejected {IconKind} {IconId}", candidate.Kind, candidate.Id);
                    return IconWarmResult.Failed(
                        candidate.Kind,
                        candidate.Id,
                        ex.Message,
                        IconWarmFailureKinds.BadKey,
                        attempt);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = ex;
                    lastFailureKind = ClassifyFailureKind(ex);
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

            return IconWarmResult.Failed(
                candidate.Kind,
                candidate.Id,
                lastError?.Message ?? "unknown error",
                lastFailureKind,
                warmAttempts);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task PaceWarmRequestAsync(CancellationToken ct)
    {
        if (warmRequestInterval <= TimeSpan.Zero)
        {
            return;
        }

        await paceLock.WaitAsync(ct);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (nextWarmRequestAt > now)
            {
                await Task.Delay(nextWarmRequestAt - now, ct);
                now = DateTimeOffset.UtcNow;
            }

            nextWarmRequestAt = now + warmRequestInterval;
        }
        finally
        {
            paceLock.Release();
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
        FileInfo existing = new(path);
        if (existing.Exists && !IsKnownPlaceholderPng(existing))
        {
            await NormalizeExistingTerrainIconAsync(path, kind, id, ct);
            await MarkCachedSuccessAsync(kind, id, ct);
            return new IconFile(path, ContentTypePng, PublicPath(kind, id));
        }

        string base64 = await fetchBase64(ct);
        IconFile file = await WriteCachedPngAsync(kind, id, base64, ct);
        await MarkCachedSuccessAsync(kind, id, ct);
        return file;
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

        byte[] bytes = DecodePng(base64, $"{kind} {id}");
        bytes = NormalizeCachedPng(kind, bytes);
        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes, ct);
            File.Move(tempPath, path, overwrite: true);
            InvalidateStatusCache();
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

    private async Task NormalizeExistingTerrainIconAsync(
        string path,
        string kind,
        string id,
        CancellationToken ct)
    {
        if (!IsTerrainIcon(kind))
        {
            return;
        }

        try
        {
            byte[] original = await File.ReadAllBytesAsync(path, ct);
            byte[] normalized = NormalizeCachedPng(kind, original);
            if (ReferenceEquals(original, normalized))
            {
                return;
            }

            string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(tempPath, normalized, ct);
                File.Move(tempPath, path, overwrite: true);
                InvalidateStatusCache();
                log.LogDebug(
                    "Normalized terrain icon {IconId} from {OriginalBytes} bytes to {NormalizedBytes} bytes.",
                    id,
                    original.Length,
                    normalized.Length);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            log.LogWarning(ex, "Could not normalize cached terrain icon {IconId}; serving existing PNG.", id);
        }
    }

    private async Task MarkCachedSuccessAsync(string kind, string id, CancellationToken ct)
    {
        if (!IsStaticWarmKind(kind))
        {
            return;
        }

        await manifestLock.WaitAsync(ct);
        try
        {
            IconCacheManifest? manifest = ReadManifest();
            Dictionary<string, IconWarmEntry> entries = ManifestEntries(manifest);
            string key = CandidateKey(kind, id);
            if (!entries.TryGetValue(key, out IconWarmEntry? entry) ||
                entry.Status == IconWarmEntryStatus.Succeeded)
            {
                return;
            }

            IconWarmEntry healed = entry with
            {
                Status = IconWarmEntryStatus.Succeeded,
                LastAttemptAt = DateTimeOffset.UtcNow,
                LastError = null,
                FailureKind = null
            };
            entries[key] = healed;
            IconWarmSummary? summary = manifest?.LastWarm is null
                ? null
                : DeriveSummaryFromEntries(
                    manifest.LastWarm.StartedAt,
                    manifest.LastWarm.CompletedAt,
                    entries,
                    manifest.LastWarm.Skipped);
            await WriteManifestFileAsync(new IconCacheManifest(summary, entries, manifest?.LastJob), ct);
            InvalidateStatusCache();
        }
        finally
        {
            manifestLock.Release();
        }
    }

    private async Task WriteManifestAsync(IconCacheManifest manifest, CancellationToken ct)
    {
        await manifestLock.WaitAsync(ct);
        try
        {
            await WriteManifestFileAsync(manifest, ct);
        }
        finally
        {
            manifestLock.Release();
        }
    }

    private async Task WriteManifestFileAsync(IconCacheManifest manifest, CancellationToken ct)
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

    private static string RequiredBase64(string? value, string label, string? result)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        string normalizedResult = result?.Trim() ?? string.Empty;
        if (normalizedResult.Equals("missing", StringComparison.OrdinalIgnoreCase))
        {
            throw new IconImageUnavailableException(
                $"RIMAPI returned missing image data for {label}.",
                IconWarmFailureKinds.Missing);
        }

        throw new IconImageUnavailableException(
            $"RIMAPI did not return image data for {label}.",
            IconWarmFailureKinds.InvalidResponse);
    }

    private static byte[] DecodePng(string base64, string label)
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

        if (IsKnownPlaceholderPng(bytes))
        {
            throw new IconImageUnavailableException(
                $"RIMAPI returned a placeholder red-X image for {label}.",
                IconWarmFailureKinds.Placeholder);
        }

        return bytes;
    }

    private static byte[] NormalizeCachedPng(string kind, byte[] bytes) =>
        IsTerrainIcon(kind)
            ? PngThumbnailer.DownscaleToFit(bytes, TerrainIconMaxDimension)
            : bytes;

    private static bool IsTerrainIcon(string kind) =>
        string.Equals(kind, "terrain", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownPlaceholderPng(FileInfo file)
    {
        if (file.Length != RedXPlaceholderPngLength)
        {
            return false;
        }

        try
        {
            return IsKnownPlaceholderPng(File.ReadAllBytes(file.FullName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsKnownPlaceholderPng(byte[] bytes)
    {
        if (bytes.Length != RedXPlaceholderPngLength)
        {
            return false;
        }

        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        return string.Equals(hash, RedXPlaceholderPngSha256, StringComparison.OrdinalIgnoreCase);
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

    private static Dictionary<string, IconWarmEntry> ManifestEntries(IconCacheManifest? manifest) =>
        manifest?.Entries is null
            ? new Dictionary<string, IconWarmEntry>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, IconWarmEntry>(manifest.Entries, StringComparer.OrdinalIgnoreCase);

    private void ApplyWarmResult(
        IDictionary<string, IconWarmEntry> entries,
        IconWarmResult result,
        DateTimeOffset attemptedAt)
    {
        string key = CandidateKey(result.Kind, result.Id);
        entries.TryGetValue(key, out IconWarmEntry? previous);
        int attempts = (previous?.Attempts ?? 0) + result.Attempts;
        entries[key] = new IconWarmEntry(
            Kind: result.Kind,
            Id: result.Id,
            Status: result.Status,
            LastAttemptAt: attemptedAt,
            Attempts: attempts,
            LastError: result.Error,
            FailureKind: result.FailureKind);
    }

    private IconWarmSummary DeriveSummaryFromEntries(
        DateTimeOffset started,
        DateTimeOffset completed,
        IReadOnlyDictionary<string, IconWarmEntry> entries,
        int skipped)
    {
        List<IconWarmEntry> staticEntries = entries.Values
            .Where(entry => IsStaticWarmKind(entry.Kind))
            .ToList();

        List<IconWarmEntry> succeeded = staticEntries
            .Where(entry => HasCachedFile(entry.Kind, entry.Id))
            .ToList();
        List<IconWarmEntry> placeholderFailures = staticEntries
            .Where(entry => entry.Status == IconWarmEntryStatus.Succeeded && HasPlaceholderFile(entry.Kind, entry.Id))
            .Select(entry => entry with
            {
                Status = IconWarmEntryStatus.Failed,
                LastError = "Cached icon is a placeholder red-X image.",
                FailureKind = IconWarmFailureKinds.Placeholder
            })
            .ToList();
        List<IconWarmEntry> deferred = staticEntries
            .Where(entry => entry.Status == IconWarmEntryStatus.Deferred && !HasCachedFile(entry.Kind, entry.Id))
            .ToList();
        List<IconWarmEntry> failed = staticEntries
            .Where(entry => entry.Status == IconWarmEntryStatus.Failed && !HasCachedFile(entry.Kind, entry.Id))
            .Concat(placeholderFailures)
            .ToList();

        List<IconWarmFailure> issues = failed
            .Concat(deferred)
            .OrderBy(entry => entry.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new IconWarmFailure(
                Kind: entry.Kind,
                Id: entry.Id,
                Status: entry.Status,
                Error: entry.LastError ?? "unknown error",
                FailureKind: entry.FailureKind))
            .ToList();

        return new IconWarmSummary(
            StartedAt: started,
            CompletedAt: completed,
            TotalCandidates: staticEntries.Count,
            Succeeded: succeeded.Count,
            Failed: failed.Count,
            Deferred: deferred.Count,
            Skipped: skipped,
            ItemCandidates: staticEntries.Count(entry => entry.Kind == "item"),
            TerrainCandidates: staticEntries.Count(entry => entry.Kind == "terrain"),
            FactionCandidates: staticEntries.Count(entry => entry.Kind == "faction" && entry.Id != "all"),
            MissingFailures: failed.Count(entry => entry.FailureKind == IconWarmFailureKinds.Missing),
            TransportFailures: failed.Count(entry =>
                entry.FailureKind is IconWarmFailureKinds.Transport or IconWarmFailureKinds.RimApiError),
            Failures: issues);
    }

    private bool HasCachedFile(string kind, string id)
    {
        try
        {
            FileInfo file = new(ResolveIconPath(kind, id));
            return file.Exists && !IsKnownPlaceholderPng(file);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private bool HasPlaceholderFile(string kind, string id)
    {
        try
        {
            FileInfo file = new(ResolveIconPath(kind, id));
            return file.Exists && IsKnownPlaceholderPng(file);
        }
        catch (ArgumentException)
        {
            return false;
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

    private IconWarmJobStatus CurrentWarmJob(IconWarmJobStatus? manifestJob)
    {
        lock (warmJobLock)
        {
            if (activeWarmJob is { State: IconWarmJobStates.Running } running)
            {
                return running;
            }

            if (activeWarmJob is not null &&
                (manifestJob is null ||
                 (activeWarmJob.UpdatedAt ?? DateTimeOffset.MinValue) >=
                 (manifestJob.UpdatedAt ?? DateTimeOffset.MinValue)))
            {
                return activeWarmJob;
            }
        }

        if (manifestJob is { State: IconWarmJobStates.Running } interrupted)
        {
            return interrupted with
            {
                State = IconWarmJobStates.Failed,
                CompletedAt = interrupted.UpdatedAt,
                Error = "warm was interrupted before completion"
            };
        }

        return manifestJob ?? IconWarmJobStatus.Idle();
    }

    private IconWarmJobStatus? RunningJobStatus(
        string? jobId,
        IconWarmScope scope,
        DateTimeOffset started,
        int total,
        int done,
        int succeeded,
        int failed,
        int deferred,
        string? error)
    {
        if (jobId is null)
        {
            return null;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new IconWarmJobStatus(
            State: IconWarmJobStates.Running,
            JobId: jobId,
            Scope: WarmScopeText(scope),
            StartedAt: started,
            UpdatedAt: now,
            CompletedAt: null,
            Total: total,
            Done: done,
            Succeeded: succeeded,
            Failed: failed,
            Deferred: deferred,
            Error: error);
    }

    private void UpdateWarmJob(IconWarmJobStatus status)
    {
        lock (warmJobLock)
        {
            if (activeWarmJob?.JobId == status.JobId)
            {
                activeWarmJob = status;
            }
        }
    }

    private void CompleteWarmJob(string? jobId, IconWarmSummary? summary, string? error)
    {
        if (jobId is null)
        {
            return;
        }

        lock (warmJobLock)
        {
            if (activeWarmJob?.JobId != jobId)
            {
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            activeWarmJob = activeWarmJob with
            {
                State = error is null ? IconWarmJobStates.Completed : IconWarmJobStates.Failed,
                UpdatedAt = now,
                CompletedAt = now,
                Succeeded = summary?.Succeeded ?? activeWarmJob.Succeeded,
                Failed = summary?.Failed ?? activeWarmJob.Failed,
                Deferred = summary?.Deferred ?? activeWarmJob.Deferred,
                Error = error
            };
        }

        InvalidateStatusCache();
    }

    private void InvalidateStatusCache()
    {
        lock (statusCacheLock)
        {
            cachedSummaryStatus = null;
            cachedSummaryStatusExpiresAt = DateTimeOffset.MinValue;
        }
    }

    private static string CandidateKey(string kind, string id) =>
        $"{kind}:{id}".ToLowerInvariant();

    private static bool IsStaticWarmKind(string kind) =>
        kind is "item" or "terrain" or "faction";

    private static string ClassifyFailureKind(Exception ex) => ex switch
    {
        IconImageUnavailableException image => image.FailureKind,
        HttpRequestException => IconWarmFailureKinds.Transport,
        TaskCanceledException => IconWarmFailureKinds.Transport,
        RimApiHttpException => IconWarmFailureKinds.Transport,
        RimApiException => IconWarmFailureKinds.RimApiError,
        FormatException => IconWarmFailureKinds.InvalidResponse,
        InvalidOperationException => IconWarmFailureKinds.InvalidResponse,
        IOException => IconWarmFailureKinds.Transport,
        _ => IconWarmFailureKinds.Transport
    };

    private static IconWarmScope ParseWarmScope(string? scope) =>
        string.Equals(scope, "failed", StringComparison.OrdinalIgnoreCase)
            ? IconWarmScope.Failed
            : IconWarmScope.All;

    private static string WarmScopeText(IconWarmScope scope) =>
        scope == IconWarmScope.Failed ? "failed" : "all";
}

public sealed record IconFile(string Path, string ContentType, string PublicPath);

public sealed record IconCacheStatus(
    string Directory,
    bool Exists,
    int FileCount,
    long TotalBytes,
    DateTimeOffset? LatestWriteAt,
    IReadOnlyDictionary<string, int> FilesByKind,
    bool FilesIncluded,
    IReadOnlyList<IconCacheFile> Files,
    IconWarmSummary? LastWarm,
    IconWarmJobStatus WarmJob);

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
    int Deferred,
    int Skipped,
    int ItemCandidates,
    int TerrainCandidates,
    int FactionCandidates,
    int MissingFailures,
    int TransportFailures,
    IReadOnlyList<IconWarmFailure> Failures);

public sealed record IconWarmFailure(
    string Kind,
    string Id,
    string Status,
    string Error,
    string? FailureKind);

public sealed record IconWarmJobStatus(
    string State,
    string? JobId,
    string Scope,
    DateTimeOffset? StartedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? CompletedAt,
    int Total,
    int Done,
    int Succeeded,
    int Failed,
    int Deferred,
    string? Error)
{
    public static IconWarmJobStatus Idle() =>
        new(
            State: IconWarmJobStates.Idle,
            JobId: null,
            Scope: "all",
            StartedAt: null,
            UpdatedAt: null,
            CompletedAt: null,
            Total: 0,
            Done: 0,
            Succeeded: 0,
            Failed: 0,
            Deferred: 0,
            Error: null);
}

public sealed record ColonistBodyIconResponse(
    int PawnId,
    bool BodyCached,
    string? BodyColor,
    bool HeadCached,
    string? HeadColor);

public sealed record IconWarmOptions
{
    public int Attempts { get; init; } = 3;
    public int Concurrency { get; init; } = 2;
    public int RetryDelayMilliseconds { get; init; } = 400;
    public int RequestIntervalMilliseconds { get; init; } = 100;
    public int CheckpointInterval { get; init; } = 25;
}

internal static class IconWarmJobStates
{
    public const string Idle = "idle";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

internal static class IconWarmEntryStatus
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Deferred = "deferred";
}

internal static class IconWarmFailureKinds
{
    public const string Missing = "missing";
    public const string Transport = "transport";
    public const string RimApiError = "rimapi_error";
    public const string InvalidResponse = "invalid_response";
    public const string BadKey = "bad_key";
    public const string Deferred = "deferred";
    public const string Placeholder = "placeholder";
}

internal sealed record IconCacheManifest(
    IconWarmSummary? LastWarm,
    IReadOnlyDictionary<string, IconWarmEntry>? Entries = null,
    IconWarmJobStatus? LastJob = null);

internal sealed record IconWarmEntry(
    string Kind,
    string Id,
    string Status,
    DateTimeOffset? LastAttemptAt,
    int Attempts,
    string? LastError,
    string? FailureKind);

internal sealed record IconWarmCandidate(string Kind, string Id);

internal sealed record IconWarmPlan(
    IReadOnlyList<IconWarmCandidate> Candidates,
    IReadOnlyList<IconWarmResult> SetupResults,
    int Skipped);

internal sealed record IconWarmResult(
    string Kind,
    string Id,
    string Status,
    string? Error,
    string? FailureKind,
    int Attempts)
{
    public static IconWarmResult Succeeded(string kind, string id, int attempts) =>
        new(kind, id, IconWarmEntryStatus.Succeeded, null, null, attempts);

    public static IconWarmResult Failed(string kind, string id, string error, string failureKind, int attempts) =>
        new(kind, id, IconWarmEntryStatus.Failed, error, failureKind, attempts);

    public static IconWarmResult Deferred(string kind, string id, string error, int attempts) =>
        new(kind, id, IconWarmEntryStatus.Deferred, error, IconWarmFailureKinds.Deferred, attempts);
}

internal enum IconWarmScope
{
    All,
    Failed
}

internal sealed class IconImageUnavailableException : InvalidOperationException
{
    public IconImageUnavailableException(string message, string failureKind)
        : base(message)
    {
        FailureKind = failureKind;
    }

    public string FailureKind { get; }
}
