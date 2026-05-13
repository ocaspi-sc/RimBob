using RimAI.Coordination;
using RimAI.Core.Ministers;

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

        app.MapGet("/api/ministers/{minister}/trace/latest", (
            string minister,
            MinisterTraceStore traces) =>
        {
            MinisterScopeInfo? scope = Scopes.FirstOrDefault(
                s => s.Key.Equals(minister, StringComparison.OrdinalIgnoreCase));

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

    private sealed record MinisterScopeInfo(
        string Key,
        string Label,
        string Kind,
        bool Ready,
        IReadOnlyList<string> EnabledViews);
}
