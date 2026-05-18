using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
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
            AgendaStore     agendaStore,
            BriefingCache   briefings,
            LlmClient       llm,
            RawLlmOutputStore rawOutputs,
            MayorStatus     mayor) =>
        {
            MayorBriefing briefing = briefings.GetMayorBriefing();
            RawLlmOutputSnapshot? latestLlm = rawOutputs.LatestAny();
            return Results.Ok(new
            {
                server            = "ok",
                rimapi_reachable  = colony.LastLiveRefreshAt is not null,
                colony_state_origin = colony.LastRefreshSource.ToString().ToLowerInvariant(),
                llm_configured    = llm.IsConfigured,
                llm_key_count      = llm.ConfiguredKeyCount,
                llm_status        = LlmStatus(llm.IsConfigured, latestLlm),
                llm_last_event_at = latestLlm?.CapturedAt,
                llm_last_error    = LlmLastError(latestLlm),
                briefing_version  = briefing.BriefingVersion,
                agenda_version    = agendaStore.Current?.Version,
                mayor_running     = mayor.IsRunning,
                mayor_started_at  = mayor.StartedAt,
                mayor_completed_at = mayor.CompletedAt,
                mayor_last_llm_success_at = mayor.LastLlmSuccessAt,
                mayor_last_error  = mayor.LastError,
            });
        });

        app.MapGet("/api/mayor/prompt", async (
            BriefingCache   briefings,
            AgendaStore     agendaStore,
            PromptBuilder   prompts,
            MayorRagRetriever  retriever,
            FlagChannel flags,
            CancellationToken ct) =>
        {
            MayorBriefing briefing = briefings.GetMayorBriefing();
            IReadOnlyList<GuideCitation> retrieved = await retriever.RetrieveAsync(briefing, [], ct);
            IReadOnlyList<AgentFlag> activeFlags = flags.Active(FlagSeverity.Medium);
            string user = prompts.BuildMayorUserMessage(briefing, agendaStore.Current, [], retrieved, activeFlags);
            string system;
            try   { system = prompts.MayorSystemPrompt; }
            catch (FileNotFoundException ex) { system = $"(prompt file not found: {ex.FileName})"; }
            return Results.Ok(new { system, user });
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
}
