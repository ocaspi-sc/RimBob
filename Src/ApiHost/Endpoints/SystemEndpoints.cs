using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RimBob.Coordination;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Host;
using RimBob.Knowledge;
using RimBob.LLM;
using RimBob.State;

namespace RimBob.Host.Endpoints;

public static class SystemEndpoints
{
    private static readonly Regex TestMethodAttribute = new(
        @"^\s*\[(?:Fact|Theory)(?:\(|\])",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private const int UpstreamRimApiEndpointTotal = 167;
    private static readonly TimeSpan ReplayCorpusMetadataCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TestInventoryMetadataCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly object MetadataCacheGate = new();
    private static CachedHealthMetadata? replayCorpusMetadataCache;
    private static CachedHealthMetadata? testInventoryMetadataCache;

    private static readonly RimApiCoverageRow[] ActiveRimApiReads =
    [
        new("GET", "/api/v1/maps", "active_read", "State store", "Selects the player-home map and updates map context."),
        new("GET", "/api/v1/game/state", "active_read", "State store", "Ticks, wealth, colonist count, storyteller, pause state."),
        new("GET", "/api/v1/datetime", "active_read", "State store", "In-game date string parsed into briefing date fields."),
        new("GET", "/api/v2/colonists/detailed?map_id", "active_read", "Mayor/Chef", "Colonist bio, needs, skills, traits, jobs, and medical flags."),
        new("GET", "/api/v1/map/farm/summary?map_id", "active_read", "Chef", "Crop totals and average growth per crop type."),
        new("GET", "/api/v1/map/plants?map_id", "active_read", "Chef", "Plant and harvest opportunity source data."),
        new("GET", "/api/v1/def/all", "active_read", "State store", "Thing definition catalog used to classify item nutrition and stack semantics."),
        new("GET", "/api/v1/map/animals?map_id", "active_read", "Chef", "Wild/tame animal source data for hunting assessment."),
        new("GET", "/api/v1/map/zones?map_id", "active_read", "Chef/State store", "Growing and stockpile zones with cell lists."),
        new("GET", "/api/v1/map/rooms?map_id", "active_read", "Willie/Welfare", "Room role, temperature, bed ids, roof/open-room signals, and room quality stats."),
        new("GET", "/api/v1/map/buildings?map_id", "active_read", "Willie/Chef", "Buildings, HP, power state, and working flags."),
        new("GET", "/api/v1/buildings/bills?building_id", "active_read", "Chef", "Current cooking work-table bills ingested after building refresh so Chef can suppress already-satisfied bill advice."),
        new("GET", "/api/v1/map/power/info?map_id", "active_read", "Willie/Chef", "Power production, consumption, storage, and capacity."),
        new("GET", "/api/v1/map/weather?map_id", "active_read", "Chef/Defense", "Weather and outdoor temperature."),
        new("GET", "/api/v1/map/things?map_id", "active_read", "Chef/State store", "Broad item and thing list; used as fallback/debug source behind stored resources."),
        new("GET", "/api/v1/lords?map_id", "active_read", "Defense", "Active AI lords such as raids, sieges, and caravans."),
        new("GET", "/api/v1/incidents?map_id", "active_read", "Defense/Chef", "Recent incidents used for threat and food-event context."),
        new("GET", "/api/v1/resources/summary?map_id", "active_read", "Mayor/Chef", "Food, nutrition, medicine, weapons, market-value rollups."),
        new("GET", "/api/v1/resources/stored?map_id", "active_read", "Mayor/Chef", "Stored item stacks grouped by category; primary source for meal/raw-food classification."),
        new("GET", "/api/v1/research/progress", "active_read", "Mayor/Research", "Current research project and progress.")
    ];

    private static readonly RimApiCoverageRow[] RepresentedButNotRefreshed =
    [
        new("GET", "/api/v1/map/pawns?map_id", "handshake_only", "Startup", "Used by RIMAPI handshake only; detailed colonists feed the state store."),
        new("GET", "/api/v1/map/creatures/summary?map_id", "client_only", "Defense/Welfare", "Client method exists, but RefreshAllAsync does not ingest it yet."),
        new("GET", "/api/v1/item/image?name", "icon_gateway", "Dashboard", "Read-only item icon fetch through /api/icons/item/{defName}."),
        new("GET", "/api/v1/terrain/image?name", "icon_gateway", "Dashboard", "Read-only terrain icon fetch through /api/icons/terrain/{defName}."),
        new("GET", "/api/v1/factions", "icon_warm", "Dashboard", "Current-world faction load ids used by the icon cache warmer."),
        new("GET", "/api/v1/faction/icon?id", "icon_gateway", "Dashboard", "Read-only faction icon fetch through /api/icons/faction/{loadId}."),
        new("GET", "/api/v1/pawn/portrait/image", "icon_gateway", "Dashboard", "Read-only lazy pawn portrait fetch; not prewarmed."),
        new("GET", "/api/v1/colonist/body/image?id", "icon_gateway", "Dashboard", "Read-only colonist body/head fetch; not prewarmed."),
        new("GET", "/api/v1/buildings/recipes?building_id", "assisted_read", "Chef Assisted Apply", "On-demand recipe resolution for the simple-meal bill upsert; not cached in ColonyState.")
    ];

    private static readonly RimApiCoverageRow[] DeferredWriteStubs =
    [
        new("POST", "/api/v1/map/zone/growing", "deferred_write_stub", "Chef Auto", "Stub exists; body shape unverified and not called in suggest-only MVP."),
        new("POST", "/api/v1/order/designate/area", "assisted_write", "Chef Assisted Apply", "Used for player-confirmed harvest and hunt designations over bounded rects."),
        new("POST", "/api/v1/order/unforbid", "assisted_write", "Chef Assisted Apply", "Used for player-confirmed safe item-id unforbid over explicit haulable thing ids; destructive forbidden endpoints are not used."),
        new("POST", "/api/v1/buildings/bills/add", "assisted_write", "Chef Assisted Apply", "Creates only an allowlisted simple-meal TargetCount bill after player click and fresh validation."),
        new("PUT", "/api/v1/buildings/bill/update", "assisted_write", "Chef Assisted Apply", "Updates only an existing simple-meal bill target; never deletes, reorders, or suspends bills.")
    ];

    private static readonly RimApiCoverageRow[] MissingRimApiPriorities =
    [
        new("GET", "/api/v1/resources/storages/summary?map_id", "missing", "Chef/Willie", "Needed for stockpile utilization and storage pressure."),
        new("GET", "/api/v1/map/work-tables?map_id", "missing", "Chef/Industry", "Needed before Chef can reason about cooking/butchering bench coverage."),
        new("GET", "/api/v1/research/finished|tree|summary", "missing", "Research", "Needed for tech-path reasoning beyond the current project."),
        new("GET", "/api/v1/factions", "missing", "Economy/Defense", "Needed for diplomacy, trade context, and faction threat posture."),
        new("GET", "/api/v1/world/caravans|settlements|sites", "missing", "Economy", "Needed for caravan, trade, and world-opportunity advice."),
        new("GET/POST", "Pawn Info/Edit/Job/Spawn controllers", "not_cached_yet", "Labor/Welfare/Auto", "Controller shapes are not cached; fetch live docs before Auto pawn writes.")
    ];

    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        EndpointCoverageCatalog coverage = app.ServiceProvider.GetRequiredService<EndpointCoverageCatalog>();
        coverage.Register("/api/system/health", "available", "Runtime, LLM, RAG, logs, traces, tests, lightweight icon summary, Host endpoint coverage, and RIMAPI coverage metadata.");
        coverage.Register("/api/system/logs/recent", "not_exposed_yet", "Planned bounded log tail.");

        app.MapGet("/api/system/health", async (
            IOptions<RimBobOptions> options,
            IWebHostEnvironment env,
            ColonyState colony,
            ColonyStateSnapshotStore colonySnapshotStore,
            MinisterOutputStore outputStore,
            AdviceBus adviceBus,
            BriefingCache briefings,
            FlagChannel flags,
            KnowledgeBase knowledge,
            LlmClient llm,
            RawLlmOutputStore rawOutputs,
            MayorStatus mayor,
            SseDiagnostics sse,
            HostRuntimeIdentity hostIdentity,
            MinisterRegistry registry,
            EndpointCoverageCatalog endpointCoverage,
            MinisterTraceStore traces,
            IconCacheService iconCache,
            AssistedApplyService assistedApply,
            RimApiRuntimeProbe rimApiRuntime,
            CancellationToken ct) =>
        {
            RimBobOptions opts = options.Value;
            RimApiRuntimeSnapshot rimApi = await rimApiRuntime.ProbeAsync(ct);
            MayorBriefing mayorBriefing = briefings.GetMayorBriefing();
            FoodBriefing foodBriefing = briefings.GetFoodBriefing();
            WelfareSourceBriefing welfareBriefing = briefings.GetWelfareBriefing();
            string logsDir = HostLogPaths.ResolveLogsDirectory(env.ContentRootPath, opts.LogsRoot);
            string dataRoot = HostLogPaths.ResolveDataRootDirectory(env.ContentRootPath, opts.DataRoot);
            string ministerOutputRoot = Path.Combine(dataRoot, "ministers");
            string colonyStateSnapshotPath = Path.Combine(dataRoot, "state", "latest-colony-state.json");
            string runtimeRoot = HostLogPaths.ResolveRuntimeRoot(env.ContentRootPath);
            string guidesRoot = ResolvePath(env.ContentRootPath, opts.Rag.GuidesRoot);
            string cacheRoot = HostLogPaths.ResolveDataDirectory(
                env.ContentRootPath,
                opts.DataRoot,
                opts.Rag.CacheRoot,
                "embeddings");
            IReadOnlyList<AgentFlag> activeFlags = flags.Active();
            RawLlmOutputSnapshot? latestLlm = rawOutputs.LatestAny();
            ColonySnapshotStatus colonySnapshot = colonySnapshotStore.GetStatus();
            MinisterOutputStoreStatus outputStatus = outputStore.GetStatus();

            return Results.Ok(new
            {
                generated_at = DateTimeOffset.UtcNow,
                version = new
                {
                    product = "RimBob",
                    rim_bob_version = hostIdentity.RimBobVersion,
                    running_version = hostIdentity.RunningVersion,
                    build_number = hostIdentity.BuildNumber,
                    build_datetime = hostIdentity.BuildDateTime,
                    build_version = hostIdentity.BuildVersion,
                    build_informational_version = hostIdentity.BuildInformationalVersion,
                    build_revision = hostIdentity.BuildRevision,
                    build_revision_short = hostIdentity.BuildRevisionShort,
                    dashboard_asset_version = hostIdentity.DashboardAssetVersion,
                    reload_token = hostIdentity.ReloadToken,
                    host_started_at = hostIdentity.StartedAt,
                    host_instance_id = hostIdentity.InstanceId,
                },
                runtime = new
                {
                    server = "ok",
                    host_started_at = hostIdentity.StartedAt,
                    host_instance_id = hostIdentity.InstanceId,
                    rim_bob_version = hostIdentity.RimBobVersion,
                    running_version = hostIdentity.RunningVersion,
                    build_number = hostIdentity.BuildNumber,
                    build_datetime = hostIdentity.BuildDateTime,
                    build_version = hostIdentity.BuildVersion,
                    build_informational_version = hostIdentity.BuildInformationalVersion,
                    dashboard_asset_version = hostIdentity.DashboardAssetVersion,
                    host_process_path = Environment.ProcessPath ?? "unknown",
                    content_root = env.ContentRootPath,
                    runtime_root = runtimeRoot,
                    rimapi_reachable = rimApi.Reachable,
                    rimapi_last_error = rimApi.LastError,
                    rimworld = RimWorldPayload(rimApi),
                    colony_state_origin = colony.LastRefreshSource.ToString().ToLowerInvariant(),
                    last_live_refresh_at = colony.LastLiveRefreshAt,
                    briefing_version = mayorBriefing.BriefingVersion,
                    food_briefing_version = foodBriefing.BriefingVersion,
                    welfare_briefing_version = welfareBriefing.BriefingVersion,
                    mayor_snapshot_version = outputStore.CurrentMayorAgenda?.Version,
                    active_advice_count = adviceBus.ActiveAdvice().Count,
                    active_flag_count = activeFlags.Count,
                    mayor_running = mayor.IsRunning,
                    mayor_started_at = mayor.StartedAt,
                    mayor_completed_at = mayor.CompletedAt,
                    mayor_last_llm_success_at = mayor.LastLlmSuccessAt,
                    mayor_last_error = mayor.LastError,
                },
                storage = new
                {
                    data_root = dataRoot,
                    minister_output_root = ministerOutputRoot,
                    colony_state_snapshot_path = colonyStateSnapshotPath,
                    embedding_cache_root = cacheRoot,
                },
                minister_outputs = outputStatus,
                colony_snapshot = ColonySnapshotMetadata(colonySnapshot),
                llm = new
                {
                    provider = "Gemini",
                    configured = llm.IsConfigured,
                    configured_key_count = llm.ConfiguredKeyCount,
                    status = LlmStatus(llm.IsConfigured, latestLlm),
                    last_event_at = latestLlm?.CapturedAt,
                    last_success_at = mayor.LastLlmSuccessAt,
                    last_error = LlmLastError(latestLlm) ?? mayor.LastError,
                    token_usage = "not_exposed_yet",
                },
                rag = new
                {
                    enabled = opts.Rag.Enabled,
                    top_k = opts.Rag.TopK,
                    embedding_model = opts.Rag.EmbeddingModel,
                    guides_root = guidesRoot,
                    cache_root = cacheRoot,
                    chunk_count = knowledge.Count,
                    last_ingest_error = "not_exposed_yet",
                },
                sse = sse.Snapshot(),
                logs = new
                {
                    directory = logsDir,
                    human_log_pattern = Path.Combine(logsDir, "rimbob-*.log"),
                    decision_log_pattern = Path.Combine(logsDir, "decisions-*.jsonl"),
                    replay_corpus = CachedReplayCorpusMetadata(logsDir),
                    recent_endpoint = "not_exposed_yet",
                },
                tests = CachedTestInventoryMetadata(env.ContentRootPath),
                icons = iconCache.GetStatus(includeFiles: false),
                traces = traces.LatestAll(),
                assisted_apply = new
                {
                    recent_attempts = assistedApply.LatestAttempts(),
                },
                endpoint_coverage = endpointCoverage.Snapshot(new EndpointCoverageContext(outputStore, registry)),
                rimapi_coverage = RimApiCoverageMetadata(),
            });
        });

        return app;
    }

    private static string ResolvePath(string contentRoot, string configuredPath) =>
        Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(contentRoot, configuredPath);

    private static object ColonySnapshotMetadata(ColonySnapshotStatus status) =>
        new
        {
            path = status.Path,
            has_snapshot = status.HasSnapshot,
            snapshot_id = status.SnapshotId,
            captured_at = status.CapturedAt,
            age_seconds = status.Age is null ? (double?)null : Math.Round(status.Age.Value.TotalSeconds, 1),
            game_tick = status.GameTick,
            game_date = status.GameDate,
            map_id = status.MapId,
            source = status.Source,
            schema_version = status.SchemaVersion,
            last_save_at = status.LastSaveAt,
            last_save_error = status.LastSaveError,
            load_error = status.LoadError,
        };

    private static object RimWorldPayload(RimApiRuntimeSnapshot runtime) => new
    {
        live = runtime.HasLoadedColony,
        program_state = runtime.ProgramState,
        map_count = runtime.MapCount,
        colonist_count = runtime.ColonistCount,
        game_tick = runtime.GameTick,
        is_paused = runtime.Paused,
    };

    private static object ReplayCorpusMetadata(string logsDir)
    {
        string replayDir = Path.Combine(logsDir, "replay");
        string pattern = Path.Combine(replayDir, "*-*.jsonl");
        if (!Directory.Exists(replayDir))
        {
            return new
            {
                directory = replayDir,
                pattern,
                exists = false,
                file_count = 0,
                total_bytes = 0L,
                latest_write_at = (DateTimeOffset?)null,
                files = Array.Empty<object>(),
            };
        }

        DirectoryInfo directory = new(replayDir);
        FileInfo[] allFiles = directory
            .EnumerateFiles("*.jsonl")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();
        FileInfo[] recentFiles = allFiles.Take(20).ToArray();
        object[] files = recentFiles
            .Select(file => (object)new
            {
                minister = ReplayMinisterFromFileName(file.Name),
                name = file.Name,
                path = file.FullName,
                size_bytes = file.Length,
                last_write_at = UtcDateTimeOffset(file.LastWriteTimeUtc),
            })
            .ToArray();

        return new
        {
            directory = replayDir,
            pattern,
            exists = true,
            file_count = allFiles.Length,
            total_bytes = allFiles.Sum(file => file.Length),
            latest_write_at = recentFiles.Length == 0
                ? (DateTimeOffset?)null
                : UtcDateTimeOffset(recentFiles[0].LastWriteTimeUtc),
            files,
        };
    }

    private static string ReplayMinisterFromFileName(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        int dash = stem.LastIndexOf('-');
        if (dash <= 0) return stem;

        string suffix = stem[(dash + 1)..];
        return suffix.Length == 8 && suffix.All(char.IsDigit)
            ? stem[..dash]
            : stem;
    }

    private static DateTimeOffset UtcDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string LlmStatus(bool configured, RawLlmOutputSnapshot? latest)
    {
        if (!configured) return "missing_key";
        if (latest is null) return "ready";
        return latest.Status;
    }

    private static string? LlmLastError(RawLlmOutputSnapshot? latest) =>
        latest?.Status is "request_failed" or "parse_failed"
            ? latest.Text
            : null;

    private static object CachedReplayCorpusMetadata(string logsDir) =>
        CachedMetadata(
            ref replayCorpusMetadataCache,
            logsDir,
            ReplayCorpusMetadataCacheDuration,
            () => ReplayCorpusMetadata(logsDir));

    private static object CachedTestInventoryMetadata(string contentRoot) =>
        CachedMetadata(
            ref testInventoryMetadataCache,
            HostLogPaths.ResolveRuntimeRoot(contentRoot),
            TestInventoryMetadataCacheDuration,
            () => TestInventoryMetadata(contentRoot));

    private static object CachedMetadata(
        ref CachedHealthMetadata? cache,
        string key,
        TimeSpan duration,
        Func<object> create)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (MetadataCacheGate)
        {
            if (cache is not null &&
                cache.ExpiresAt > now &&
                cache.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return cache.Value;
            }
        }

        object value = create();
        lock (MetadataCacheGate)
        {
            cache = new CachedHealthMetadata(
                key,
                DateTimeOffset.UtcNow + duration,
                value);
        }

        return value;
    }

    private static object RimApiCoverageMetadata()
    {
        int clientMethodCount = ActiveRimApiReads.Length + RepresentedButNotRefreshed.Length;
        int representedEndpointCount = clientMethodCount + DeferredWriteStubs.Length;

        return new
        {
            coverage_basis = "declared integration snapshot",
            source = "Current RimApiClient methods, IngestionDispatcher.RefreshAllAsync wiring, and the cached Docs/design/RimAPI.md upstream catalogue.",
            cached_upstream_endpoint_total = UpstreamRimApiEndpointTotal,
            active_read_count = ActiveRimApiReads.Length,
            client_method_count = clientMethodCount,
            deferred_write_stub_count = DeferredWriteStubs.Length,
            represented_endpoint_count = representedEndpointCount,
            active_read_percent = Percentage(ActiveRimApiReads.Length, UpstreamRimApiEndpointTotal),
            represented_endpoint_percent = Percentage(representedEndpointCount, UpstreamRimApiEndpointTotal),
            coverage_note = "This is not live-discovered from RIMAPI. Update it when RimApiClient or RefreshAllAsync wiring changes. MVP is suggest-only, so write endpoints remain deferred until Auto/Labor work.",
            active_reads = RimApiRows(ActiveRimApiReads),
            represented_not_refreshed = RimApiRows(RepresentedButNotRefreshed),
            deferred_writes = RimApiRows(DeferredWriteStubs),
            missing_priorities = RimApiRows(MissingRimApiPriorities),
        };
    }

    private static decimal Percentage(int count, int total) =>
        total == 0 ? 0m : Math.Round((decimal)count / total * 100m, 1);

    private static object[] RimApiRows(IEnumerable<RimApiCoverageRow> rows) =>
        rows.Select(row => (object)new
        {
            method = row.Method,
            endpoint = row.Endpoint,
            state = row.State,
            owner = row.Owner,
            note = row.Note,
        }).ToArray();

    private static object TestInventoryMetadata(string contentRoot)
    {
        string runtimeRoot = HostLogPaths.ResolveRuntimeRoot(contentRoot);
        string testsDir = Path.Combine(runtimeRoot, "Src", "Tests");
        string projectPath = Path.Combine(testsDir, "RimBob.Tests.csproj");

        if (!Directory.Exists(testsDir))
        {
            return new
            {
                directory = testsDir,
                project = projectPath,
                exists = false,
                total_count = 0,
                file_count = 0,
                source = "declared xUnit [Fact]/[Theory] methods grouped by Src/Tests folder",
                scan_error = (string?)null,
                categories = Array.Empty<object>(),
            };
        }

        try
        {
            DirectoryInfo directory = new(testsDir);
            TestSourceFile[] sources = directory
                .EnumerateFiles("*.cs", SearchOption.AllDirectories)
                .Where(file => !IsIgnoredTestPath(file.FullName))
                .Select(file => new TestSourceFile(
                    TestCategoryFromPath(testsDir, file.FullName),
                    CountDeclaredTests(file.FullName)))
                .Where(source => source.Count > 0)
                .ToArray();

            TestCategorySummary[] categories = sources
                .GroupBy(source => source.Category)
                .Select(group => new TestCategorySummary(
                    group.Key,
                    group.Sum(source => source.Count),
                    group.Count()))
                .OrderBy(category => category.Category)
                .ToArray();

            return new
            {
                directory = testsDir,
                project = projectPath,
                exists = true,
                total_count = categories.Sum(category => category.Count),
                file_count = sources.Length,
                source = "declared xUnit [Fact]/[Theory] methods grouped by Src/Tests folder",
                scan_error = (string?)null,
                categories = categories.Select(category => new
                {
                    category = category.Category,
                    count = category.Count,
                    file_count = category.FileCount,
                }).ToArray(),
            };
        }
        catch (Exception ex)
        {
            return new
            {
                directory = testsDir,
                project = projectPath,
                exists = true,
                total_count = 0,
                file_count = 0,
                source = "declared xUnit [Fact]/[Theory] methods grouped by Src/Tests folder",
                scan_error = ex.Message,
                categories = Array.Empty<object>(),
            };
        }
    }

    private static bool IsIgnoredTestPath(string fullPath)
    {
        string normalized = fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return normalized.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static string TestCategoryFromPath(string testsDir, string fullPath)
    {
        string relative = Path.GetRelativePath(testsDir, fullPath);
        string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length > 1 ? parts[0] : "Root";
    }

    private static int CountDeclaredTests(string fullPath)
    {
        string source = File.ReadAllText(fullPath);
        return TestMethodAttribute.Matches(source).Count;
    }

    private sealed record TestSourceFile(string Category, int Count);

    private sealed record TestCategorySummary(string Category, int Count, int FileCount);

    private sealed record RimApiCoverageRow(
        string Method,
        string Endpoint,
        string State,
        string Owner,
        string Note);

    private sealed record CachedHealthMetadata(string Key, DateTimeOffset ExpiresAt, object Value);
}
