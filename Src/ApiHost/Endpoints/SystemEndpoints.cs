using Microsoft.Extensions.Options;
using RimAI.Coordination;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;
using RimAI.Knowledge;
using RimAI.LLM;
using RimAI.State;

namespace RimAI.Host.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
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
            MinisterTraceStore traces) =>
        {
            RimAiOptions opts = options.Value;
            MayorBriefing mayorBriefing = briefings.GetMayorBriefing();
            FoodBriefing foodBriefing = briefings.GetFoodBriefing();
            string logsDir = HostLogPaths.ResolveLogsDirectory(env.ContentRootPath);
            string guidesRoot = ResolvePath(env.ContentRootPath, opts.Rag.GuidesRoot);
            string cacheRoot = ResolvePath(env.ContentRootPath, opts.Rag.CacheRoot);
            IReadOnlyList<AgentFlag> activeFlags = flags.Active();
            RawLlmOutputSnapshot? latestLlm = rawOutputs.LatestAny();
            string promptMinisterNames = CapabilityNames(registry, descriptor => descriptor.HasPrompt);
            string triggerMinisterNames = CapabilityNames(registry, descriptor => descriptor.CanManualTrigger);
            string rawOutputMinisterNames = CapabilityNames(registry, descriptor => descriptor.HasRawLlmOutput);

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
                traces = traces.LatestAll(),
                endpoint_coverage = new[]
                {
                    Coverage("/api/status", "available", "Host, RIMAPI, LLM, briefing, and Mayor status."),
                    Coverage("/api/advice/stream", "available", "SSE agenda and active advice feed."),
                    Coverage("/api/cabinet/trigger", "available", "Manual dashboard trigger for all live ministers; suggest-only, no RIMAPI writes."),
                    Coverage("/api/agenda/latest", agendaStore.Current is null ? "missing" : "available", "Current Mayor agenda."),
                    Coverage("/api/colony/snapshot", "available", "Latest Mayor briefing for sidebar telemetry."),
                    Coverage("/api/briefings/mayor/latest", "available", "Mayor briefing inspector source."),
                    Coverage("/api/briefings/food/latest", "available", "Food briefing inspector source."),
                    Coverage("/api/mayor/prompt", "available", "Mayor prompt inspector source."),
                    Coverage("/api/ministers/{minister}/prompt", "partial", $"Generalized prompt inspector for {promptMinisterNames}."),
                    Coverage("/api/ministers/{minister}/trigger", "partial", $"Manual dashboard trigger for wired ministers: {triggerMinisterNames}. Planned scopes are not wired yet."),
                    Coverage("/api/ministers/{minister}/llm-output/latest", "partial", $"Latest raw Gemini response for {rawOutputMinisterNames} after an LLM call occurs."),
                    Coverage("/api/ministers/{minister}/trace/latest", "partial", "Wake trigger visible; rule/LLM path details not exposed yet."),
                    Coverage("/api/ministers/{minister}/rag/latest", "not_exposed_yet", "Planned RAG retrieval inspector."),
                    Coverage("/api/system/logs/recent", "not_exposed_yet", "Planned bounded log tail."),
                },
            });
        });

        return app;
    }

    private static object Coverage(string endpoint, string state, string note) =>
        new { endpoint, state, note };

    private static string CapabilityNames(MinisterRegistry registry, Func<MinisterDescriptor, bool> capability)
    {
        string[] labels = registry.Ministers
            .Where(descriptor => descriptor.Ready && capability(descriptor))
            .Select(descriptor => descriptor.Label)
            .ToArray();

        return labels.Length == 0 ? "no wired ministers" : string.Join(" and ", labels);
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
}
