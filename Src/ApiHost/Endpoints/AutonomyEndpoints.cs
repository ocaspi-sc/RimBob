using RimAI.Core.Advice;

namespace RimAI.Host.Endpoints;

/// <summary>
/// Per-minister autonomy dial. M1 hard-codes everyone to Suggest; PUT is a 200
/// no-op so the dashboard can render the panel and the contract is set for M7.
/// </summary>
public static class AutonomyEndpoints
{
    public static IEndpointRouteBuilder MapAutonomyEndpoints(this IEndpointRouteBuilder app)
    {
        EndpointCoverageCatalog coverage = app.ServiceProvider.GetRequiredService<EndpointCoverageCatalog>();
        coverage.Register("/api/autonomy", "available", "Per-minister autonomy dial read surface; MVP values remain suggest-only.");

        app.MapGet("/api/autonomy", () =>
            Results.Ok(new Dictionary<string, AutonomyMode> { ["Mayor"] = AutonomyMode.Suggest }));

        app.MapPut("/api/autonomy", (Dictionary<string, AutonomyMode> _) => Results.Ok());

        return app;
    }
}
