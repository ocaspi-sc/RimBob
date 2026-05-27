using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Host;
using RimBob.Knowledge;
using RimBob.LLM;
using RimBob.State;

namespace RimBob.Host.Endpoints;

/// <summary>
/// GET /api/status   — server + RIMAPI + LLM health, briefing/agenda versions, Mayor run state.
/// GET /api/mayor/prompt — the exact system + user message that would be sent to Gemini.
/// </summary>
public static class StatusEndpoints
{
    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder app)
    {
        EndpointCoverageCatalog coverage = app.ServiceProvider.GetRequiredService<EndpointCoverageCatalog>();
        coverage.Register("/api/status", "available", "Host, RIMAPI, LLM, briefing, and Mayor status.");
        coverage.Register("/api/mayor/prompt", "available", "Mayor prompt inspector source.");

        app.MapGet("/api/status", (
            ColonyState     colony,
            MinisterOutputStore outputStore,
            BriefingCache   briefings,
            LlmClient       llm,
            RawLlmOutputStore rawOutputs,
            MayorStatus     mayor,
            RimApiRuntimeProbe rimApiRuntime,
            CancellationToken ct) =>
        {
            MayorBriefing briefing = briefings.GetMayorBriefing();
            RawLlmOutputSnapshot? latestLlm = rawOutputs.LatestAny();
            return StatusPayloadAsync(
                colony,
                outputStore,
                llm,
                latestLlm,
                mayor,
                briefing,
                rimApiRuntime,
                ct);
        });

        static async Task<IResult> StatusPayloadAsync(
            ColonyState colony,
            MinisterOutputStore outputStore,
            LlmClient llm,
            RawLlmOutputSnapshot? latestLlm,
            MayorStatus mayor,
            MayorBriefing briefing,
            RimApiRuntimeProbe rimApiRuntime,
            CancellationToken ct)
        {
            RimApiRuntimeSnapshot rimApi = await rimApiRuntime.ProbeAsync(ct);
            return Results.Ok(new
            {
                server            = "ok",
                rimapi_reachable  = rimApi.Reachable,
                rimapi_last_error = rimApi.LastError,
                rimworld = RimWorldPayload(rimApi),
                colony_state_origin = colony.LastRefreshSource.ToString().ToLowerInvariant(),
                last_live_refresh_at = colony.LastLiveRefreshAt,
                llm_configured    = llm.IsConfigured,
                llm_key_count      = llm.ConfiguredKeyCount,
                llm_status        = LlmStatus(llm.IsConfigured, latestLlm),
                llm_last_event_at = latestLlm?.CapturedAt,
                llm_last_error    = LlmLastError(latestLlm),
                briefing_version  = briefing.BriefingVersion,
                mayor_snapshot_version = outputStore.CurrentMayorAgenda?.Version,
                mayor_running     = mayor.IsRunning,
                mayor_started_at  = mayor.StartedAt,
                mayor_completed_at = mayor.CompletedAt,
                mayor_last_llm_success_at = mayor.LastLlmSuccessAt,
                mayor_last_error  = mayor.LastError,
            });
        }

        app.MapGet("/api/mayor/prompt", async (
            BriefingCache   briefings,
            MinisterOutputStore outputStore,
            PromptBuilder   prompts,
            PromptInspectorCache promptCache,
            MayorRagRetriever  retriever,
            FlagChannel flags,
            bool? refresh,
            CancellationToken ct) =>
        {
            MayorBriefing briefing = briefings.GetMayorBriefing();
            IReadOnlyList<AgentFlag> activeFlags = flags.Active(FlagSeverity.Medium);
            string key = PromptInspectorCache.BuildKey(
                "mayor",
                briefing.BriefingVersion,
                outputStore.CurrentMayorAgenda?.Version,
                PromptInspectorCache.HashJson(activeFlags));
            PromptInspectorPayload payload = await promptCache.GetOrCreateAsync(
                key,
                refresh == true,
                async cancellationToken =>
                {
                    IReadOnlyList<GuideCitation> retrieved = await retriever.RetrieveAsync(briefing, [], cancellationToken);
                    string user = prompts.BuildMayorUserMessage(briefing, [], retrieved, activeFlags);
                    string system;
                    try { system = prompts.MayorSystemPrompt; }
                    catch (FileNotFoundException ex) { system = $"(prompt file not found: {ex.FileName})"; }
                    return new PromptInspectorPayload(system, user);
                },
                ct);
            return Results.Ok(payload);
        });

        return app;
    }

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

    private static object RimWorldPayload(RimApiRuntimeSnapshot runtime) => new
    {
        live = runtime.HasLoadedColony,
        program_state = runtime.ProgramState,
        map_count = runtime.MapCount,
        colonist_count = runtime.ColonistCount,
        game_tick = runtime.GameTick,
        is_paused = runtime.Paused,
    };
}
