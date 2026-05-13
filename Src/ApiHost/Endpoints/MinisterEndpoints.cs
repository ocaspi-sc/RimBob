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
    private static readonly IReadOnlyList<string> MinisterViews =
        ["prompt", "raw_llm", "briefing", "rag", "rules", "advice"];

    private static readonly IReadOnlyList<MinisterScopeInfo> Scopes =
    [
        new("system", "SYSTEM", "system", true, ["overview"]),
        new("mayor", "Mayor", "minister", true, MinisterViews),
        new("food", "Food", "minister", true, MinisterViews),
        new("construction", "Construction", "minister", false, MinisterViews),
        new("defense", "Defense", "minister", false, MinisterViews),
        new("welfare", "Welfare", "minister", false, MinisterViews),
        new("medical", "Medical", "minister", false, MinisterViews),
        new("research", "Research", "minister", false, MinisterViews),
        new("industry", "Industry", "minister", false, MinisterViews),
        new("economy", "Economy", "minister", false, MinisterViews),
        new("chief_of_staff", "Chief of Staff", "minister", false, MinisterViews),
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

        app.MapGet("/api/ministers/{minister}/llm-output/latest", (
            string minister,
            RawLlmOutputStore outputs) =>
        {
            MinisterScopeInfo? scope = FindScope(minister);
            if (scope is null || scope.Kind != "minister")
                return Results.NotFound(new { error = $"Unknown minister scope '{minister}'." });

            if (!scope.Ready)
            {
                return Results.Problem(
                    title: "Raw LLM output not wired",
                    detail: $"{scope.Label} is a planned minister scope; raw LLM output is not wired yet.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            RawLlmOutputSnapshot? snapshot = outputs.Latest(scope.Label);
            if (snapshot is null)
            {
                return Results.Ok(new RawLlmOutputSnapshot(
                    Minister: scope.Label,
                    Provider: "Gemini",
                    Model: LlmClient.DefaultModel,
                    CapturedAt: DateTimeOffset.UtcNow,
                    LatencyMs: 0,
                    Status: "not_seen_yet",
                    ParseMode: "not_applicable",
                    SystemPromptChars: 0,
                    UserPromptChars: 0,
                    Text: ""));
            }

            return Results.Ok(snapshot);
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
