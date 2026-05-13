using RimAI.Coordination;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;
using RimAI.Knowledge;
using RimAI.LLM;
using RimAI.Ministers.Food;
using RimAI.State;

namespace RimAI.Host.Endpoints;

public static class MinisterEndpoints
{
    private static readonly IReadOnlyList<MinisterScopeInfo> Scopes =
    [
        new("system", "SYSTEM", "system", true, ["overview"]),
        new("mayor", "Mayor", "minister", true, ["prompt", "briefing", "rag", "rules", "advice"]),
        new("food", "Food", "minister", true, ["prompt", "briefing", "rag", "rules", "advice"]),
        new("construction", "Construction", "minister", false, ["prompt", "briefing", "rag", "rules", "advice"]),
        new("defense", "Defense", "minister", false, ["prompt", "briefing", "rag", "rules", "advice"]),
        new("welfare", "Welfare", "minister", false, ["prompt", "briefing", "rag", "rules", "advice"]),
        new("medical", "Medical", "minister", false, ["prompt", "briefing", "rag", "rules", "advice"]),
        new("research", "Research", "minister", false, ["prompt", "briefing", "rag", "rules", "advice"]),
        new("industry", "Industry", "minister", false, ["prompt", "briefing", "rag", "rules", "advice"]),
        new("economy", "Economy", "minister", false, ["prompt", "briefing", "rag", "rules", "advice"]),
        new("chief_of_staff", "Chief of Staff", "minister", false, ["prompt", "briefing", "rag", "rules", "advice"]),
    ];

    public static IEndpointRouteBuilder MapMinisterEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/ministers", () => Results.Ok(Scopes));

        app.MapGet("/api/ministers/{minister}/prompt", async (
            string minister,
            BriefingCache briefings,
            AgendaStore agendaStore,
            PromptBuilder prompts,
            MayorRagRetriever mayorRetriever,
            FoodRagRetriever foodRetriever,
            FlagChannel flags,
            CancellationToken ct) =>
        {
            MinisterScopeInfo? scope = FindScope(minister);
            if (scope is null || scope.Kind != "minister")
                return Results.NotFound(new { error = $"Unknown minister scope '{minister}'." });

            if (!scope.Ready)
            {
                return Results.Problem(
                    title: "Prompt not wired",
                    detail: $"{scope.Label} is a planned minister scope; prompt introspection is not wired yet.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            if (scope.Key == "mayor")
            {
                MayorBriefing briefing = briefings.GetMayorBriefing();
                IReadOnlyList<GuideCitation> retrieved = await mayorRetriever.RetrieveAsync(briefing, [], ct);
                IReadOnlyList<AgentFlag> activeFlags = flags.Active(FlagSeverity.Medium);
                string user = prompts.BuildMayorUserMessage(briefing, agendaStore.Current, [], retrieved, activeFlags);
                return Results.Ok(new { system = ReadPromptOrPlaceholder(() => prompts.MayorSystemPrompt), user });
            }

            if (scope.Key == "food")
            {
                FoodBriefing briefing = briefings.GetFoodBriefing();
                MinisterBriefingContext context = MinisterOfFood.BuildContext(agendaStore.Current);
                IReadOnlyList<GuideCitation> retrieved = await foodRetriever.RetrieveAsync(briefing, ct);
                string user = prompts.BuildFoodUserMessage(briefing, context, retrieved);
                return Results.Ok(new { system = ReadPromptOrPlaceholder(() => prompts.FoodSystemPrompt), user });
            }

            return Results.Problem(
                title: "Prompt not wired",
                detail: $"{scope.Label} prompt introspection is not wired yet.",
                statusCode: StatusCodes.Status501NotImplemented);
        });

        app.MapGet("/api/ministers/{minister}/trace/latest", (
            string minister,
            MinisterTraceStore traces) =>
        {
            MinisterScopeInfo? scope = FindScope(minister);

            if (scope is null || scope.Kind != "minister")
                return Results.NotFound(new { error = $"Unknown minister scope '{minister}'." });

            MinisterTraceSnapshot? snapshot = traces.Latest(scope.Label);
            if (snapshot is null)
            {
                return Results.Ok(new MinisterTraceSnapshot(
                    Minister: scope.Label,
                    Trigger: "not_seen_yet",
                    Status: scope.Ready ? "waiting" : "not_wired",
                    StartedAt: DateTimeOffset.UtcNow,
                    CompletedAt: null,
                    Path: "not_exposed_yet",
                    RuleFired: null,
                    EscalationReason: null,
                    WakeupPayload: null,
                    Flag: null,
                    Note: scope.Ready
                        ? "No live trace recorded since Host startup."
                        : "Minister scope is planned but not wired yet."));
            }

            return Results.Ok(snapshot);
        });

        return app;
    }

    private static MinisterScopeInfo? FindScope(string key) =>
        Scopes.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    private static string ReadPromptOrPlaceholder(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (FileNotFoundException ex)
        {
            return $"(prompt file not found: {ex.FileName})";
        }
    }

    private sealed record MinisterScopeInfo(
        string Key,
        string Label,
        string Kind,
        bool Ready,
        IReadOnlyList<string> EnabledViews);
}
