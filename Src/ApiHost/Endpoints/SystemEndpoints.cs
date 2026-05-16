using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RimAI.Coordination;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;
using RimAI.Host;
using RimAI.Knowledge;
using RimAI.LLM;
using RimAI.State;

namespace RimAI.Host.Endpoints;

public static class SystemEndpoints
{
    private static readonly Regex TestMethodAttribute = new(
        @"^\s*\[(?:Fact|Theory)(?:\(|\])",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private const int UpstreamRimApiEndpointTotal = 167;

    private static readonly RimApiCoverageRow[] ActiveRimApiReads =
    [
        new("GET", "/api/v1/maps", "active_read", "State store", "Selects the player-home map and updates map context."),
        new("GET", "/api/v1/game/state", "active_read", "State store", "Ticks, wealth, colonist count, storyteller, pause state."),
        new("GET", "/api/v1/datetime", "active_read", "State store", "In-game date string parsed into briefing date fields."),
        new("GET", "/api/v2/colonists/detailed?map_id", "active_read", "Mayor/Food", "Colonist bio, needs, skills, traits, jobs, and medical flags."),
        new("GET", "/api/v1/map/farm/summary?map_id", "active_read", "Food", "Crop totals and average growth per crop type."),
        new("GET", "/api/v1/map/plants?map_id", "active_read", "Food", "Plant and harvest opportunity source data."),
        new("GET", "/api/v1/def/all", "active_read", "State store", "Thing definition catalog used to classify item nutrition and stack semantics."),
        new("GET", "/api/v1/map/animals?map_id", "active_read", "Food", "Wild/tame animal source data for hunting assessment."),
        new("GET", "/api/v1/map/zones?map_id", "active_read", "Food/State store", "Growing and stockpile zones with cell lists."),
        new("GET", "/api/v1/map/buildings?map_id", "active_read", "Construction/Food", "Buildings, HP, power state, and working flags."),
        new("GET", "/api/v1/map/power/info?map_id", "active_read", "Construction/Food", "Power production, consumption, storage, and capacity."),
        new("GET", "/api/v1/map/weather?map_id", "active_read", "Food/Defense", "Weather and outdoor temperature."),
        new("GET", "/api/v1/map/things?map_id", "active_read", "Food/State store", "Broad item and thing list; used as fallback/debug source behind stored resources."),
        new("GET", "/api/v1/lords?map_id", "active_read", "Defense", "Active AI lords such as raids, sieges, and caravans."),
        new("GET", "/api/v1/incidents?map_id", "active_read", "Defense/Food", "Recent incidents used for threat and food-event context."),
        new("GET", "/api/v1/resources/summary?map_id", "active_read", "Mayor/Food", "Food, nutrition, medicine, weapons, market-value rollups."),
        new("GET", "/api/v1/resources/stored?map_id", "active_read", "Mayor/Food", "Stored item stacks grouped by category; primary source for meal/raw-food classification."),
        new("GET", "/api/v1/research/progress", "active_read", "Mayor/Research", "Current research project and progress.")
    ];

    private static readonly RimApiCoverageRow[] RepresentedButNotRefreshed =
    [
        new("GET", "/api/v1/map/pawns?map_id", "handshake_only", "Startup", "Used by RIMAPI handshake only; detailed colonists feed the state store."),
        new("GET", "/api/v1/map/rooms?map_id", "client_only", "Construction/Welfare", "Client method exists, but RefreshAllAsync does not ingest it yet."),
        new("GET", "/api/v1/map/creatures/summary?map_id", "client_only", "Defense/Welfare", "Client method exists, but RefreshAllAsync does not ingest it yet."),
        new("GET", "/api/v1/item/image?name", "icon_gateway", "Dashboard", "Read-only item icon fetch through /api/icons/item/{defName}."),
        new("GET", "/api/v1/terrain/image?name", "icon_gateway", "Dashboard", "Read-only terrain icon fetch through /api/icons/terrain/{defName}."),
        new("GET", "/api/v1/factions", "icon_warm", "Dashboard", "Current-world faction load ids used by the icon cache warmer."),
        new("GET", "/api/v1/faction/icon?id", "icon_gateway", "Dashboard", "Read-only faction icon fetch through /api/icons/faction/{loadId}."),
        new("GET", "/api/v1/pawn/portrait/image", "icon_gateway", "Dashboard", "Read-only lazy pawn portrait fetch; not prewarmed."),
        new("GET", "/api/v1/colonist/body/image?id", "icon_gateway", "Dashboard", "Read-only colonist body/head fetch; not prewarmed.")
    ];

    private static readonly RimApiCoverageRow[] DeferredWriteStubs =
    [
        new("POST", "/api/v1/map/zone/growing", "deferred_write_stub", "Food Auto", "Stub exists; body shape unverified and not called in suggest-only MVP."),
        new("POST", "/api/v1/order/designate/area", "assisted_write", "Food Assisted Apply", "Used for player-confirmed harvest designation over bounded rects."),
        new("POST", "/api/v1/order/unforbid", "upstream_dependency", "Food Assisted Apply", "Safe item-id unforbid endpoint expected from the companion RIMAPI change; destructive forbidden endpoints are not used.")
    ];

    private static readonly RimApiCoverageRow[] MissingRimApiPriorities =
    [
        new("GET", "/api/v1/resources/storages/summary?map_id", "missing", "Food/Construction", "Needed for stockpile utilization and storage pressure."),
        new("GET", "/api/v1/map/work-tables?map_id", "missing", "Food/Industry", "Needed before Food can reason about cooking/butchering bench coverage."),
        new("GET", "/api/v1/buildings/bills?building_id", "missing", "Food/Industry", "Needed for cooking, butchering, and production bill state."),
        new("GET", "/api/v1/research/finished|tree|summary", "missing", "Research", "Needed for tech-path reasoning beyond the current project."),
        new("GET", "/api/v1/factions", "missing", "Economy/Defense", "Needed for diplomacy, trade context, and faction threat posture."),
        new("GET", "/api/v1/world/caravans|settlements|sites", "missing", "Economy", "Needed for caravan, trade, and world-opportunity advice."),
        new("GET/POST", "Pawn Info/Edit/Job/Spawn controllers", "not_cached_yet", "Labor/Welfare/Auto", "Controller shapes are not cached; fetch live docs before Auto pawn writes.")
    ];

    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        EndpointCoverageCatalog coverage = app.ServiceProvider.GetRequiredService<EndpointCoverageCatalog>();
        coverage.Register("/api/system/health", "available", "Runtime, LLM, RAG, logs, traces, tests, Host endpoint coverage, and RIMAPI coverage metadata.");
        coverage.Register("/api/system/logs/recent", "not_exposed_yet", "Planned bounded log tail.");

        app.MapGet("/api/system/health", (
            IOptions<RimAiOptions> options,
            IWebHostEnvironment env,
            ColonyState colony,
            AgendaStore agendaStore,
            AdviceBus adviceBus,
            BriefingCache briefings,
            FlagChannel flags,
            KnowledgeBase knowledge,
            LlmClient llm,
            RawLlmOutputStore rawOutputs,
            MayorStatus mayor,
            SseDiagnostics sse,
            MinisterRegistry registry,
            EndpointCoverageCatalog endpointCoverage,
            MinisterTraceStore traces,
            IconCacheService iconCache,
            AssistedApplyService assistedApply) =>
        {
            RimAiOptions opts = options.Value;
            MayorBriefing mayorBriefing = briefings.GetMayorBriefing();
            FoodBriefing foodBriefing = briefings.GetFoodBriefing();
            string logsDir = HostLogPaths.ResolveLogsDirectory(env.ContentRootPath);
            string guidesRoot = ResolvePath(env.ContentRootPath, opts.Rag.GuidesRoot);
            string cacheRoot = ResolvePath(env.ContentRootPath, opts.Rag.CacheRoot);
            IReadOnlyList<AgentFlag> activeFlags = flags.Active();
            RawLlmOutputSnapshot? latestLlm = rawOutputs.LatestAny();

            return Results.Ok(new
            {
                generated_at = DateTimeOffset.UtcNow,
                runtime = new
                {
                    server = "ok",
                    rimapi_reachable = colony.Economy.Version > 0,
                    briefing_version = mayorBriefing.BriefingVersion,
                    food_briefing_version = foodBriefing.BriefingVersion,
                    agenda_version = agendaStore.Current?.Version,
                    active_advice_count = adviceBus.ActiveAdvice().Count,
                    active_flag_count = activeFlags.Count,
                    mayor_running = mayor.IsRunning,
                    mayor_started_at = mayor.StartedAt,
                    mayor_completed_at = mayor.CompletedAt,
                    mayor_last_llm_success_at = mayor.LastLlmSuccessAt,
                    mayor_last_error = mayor.LastError,
                },
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
                    human_log_pattern = Path.Combine(logsDir, "rimai-*.log"),
                    decision_log_pattern = Path.Combine(logsDir, "decisions-*.jsonl"),
                    replay_corpus = ReplayCorpusMetadata(logsDir),
                    recent_endpoint = "not_exposed_yet",
                },
                tests = TestInventoryMetadata(env.ContentRootPath),
                icons = iconCache.GetStatus(),
                traces = traces.LatestAll(),
                assisted_apply = new
                {
                    recent_attempts = assistedApply.LatestAttempts(),
                },
                endpoint_coverage = endpointCoverage.Snapshot(new EndpointCoverageContext(agendaStore, registry)),
                rimapi_coverage = RimApiCoverageMetadata(),
            });
        });

        return app;
    }

    private static string ResolvePath(string contentRoot, string configuredPath) =>
        Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(contentRoot, configuredPath);

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
        string projectPath = Path.Combine(testsDir, "RimAI.Tests.csproj");

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
}
