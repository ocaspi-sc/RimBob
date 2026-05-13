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
                    recent_endpoint = "not_exposed_yet",
                },
                traces = traces.LatestAll(),
                endpoint_coverage = new[]
                {
                    Coverage("/api/status", "available", "Host, RIMAPI, LLM, briefing, and Mayor status."),
                    Coverage("/api/advice/stream", "available", "SSE agenda and active advice feed."),
                    Coverage("/api/agenda/latest", agendaStore.Current is null ? "missing" : "available", "Current Mayor agenda."),
                    Coverage("/api/colony/snapshot", "available", "Latest Mayor briefing for sidebar telemetry."),
                    Coverage("/api/briefings/mayor/latest", "available", "Mayor briefing inspector source."),
                    Coverage("/api/briefings/food/latest", "available", "Food briefing inspector source."),
                    Coverage("/api/mayor/prompt", "available", "Mayor prompt inspector source."),
                    Coverage("/api/ministers/{minister}/prompt", "partial", "Generalized prompt inspector for Mayor and Food."),
                    Coverage("/api/ministers/{minister}/llm-output/latest", "partial", "Latest raw Gemini response for Mayor and Food after an LLM call occurs."),
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

    private static string ResolvePath(string contentRoot, string configuredPath) =>
        Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(contentRoot, configuredPath);

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
