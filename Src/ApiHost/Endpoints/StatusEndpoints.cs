using RimAI.Coordination;
using RimAI.LLM;
using RimAI.State;

namespace RimAI.Host.Endpoints;

/// <summary>
/// GET /api/status   — server + RIMAPI + LLM health, briefing/agenda versions, Mayor run state.
/// GET /api/mayor/prompt — the exact system + user message that would be sent to Gemini.
/// </summary>
public static class StatusEndpoints
{
    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/status", (
            ColonyState     colony,
            AgendaStore     agendaStore,
            BriefingCache   briefings,
            LlmClient       llm,
            MayorStatus     mayor) =>
        {
            var briefing = briefings.GetMayorBriefing();
            return Results.Ok(new
            {
                server            = "ok",
                rimapi_reachable  = colony.Economy.Version > 0,
                llm_configured    = llm.IsConfigured,
                briefing_version  = briefing.BriefingVersion,
                agenda_version    = agendaStore.Current?.Version,
                mayor_running     = mayor.IsRunning,
                mayor_started_at  = mayor.StartedAt,
                mayor_completed_at = mayor.CompletedAt,
                mayor_last_error  = mayor.LastError,
            });
        });

        app.MapGet("/api/mayor/prompt", (
            BriefingCache   briefings,
            AgendaStore     agendaStore,
            PromptBuilder   prompts) =>
        {
            var briefing    = briefings.GetMayorBriefing();
            string user     = prompts.BuildMayorUserMessage(briefing, agendaStore.Current, []);
            string system;
            try   { system = prompts.MayorSystemPrompt; }
            catch (FileNotFoundException ex) { system = $"(prompt file not found: {ex.FileName})"; }
            return Results.Ok(new { system, user });
        });

        return app;
    }
}
