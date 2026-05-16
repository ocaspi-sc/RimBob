using RimBob.State;

namespace RimBob.Host.Endpoints;

/// <summary>
/// GET /api/colony/snapshot — returns the latest MayorBriefing the Mayor sees.
/// The dashboard sidebar reads this to render dry facts (date, wealth, food,
/// power, etc.) so the agenda body can stay interpretive rather than repeat numbers.
/// </summary>
public static class ColonyEndpoints
{
    public static IEndpointRouteBuilder MapColonyEndpoints(this IEndpointRouteBuilder app)
    {
        EndpointCoverageCatalog coverage = app.ServiceProvider.GetRequiredService<EndpointCoverageCatalog>();
        coverage.Register("/api/colony/snapshot", "available", "Latest Mayor briefing for sidebar telemetry.");
        coverage.Register("/api/briefings/mayor/latest", "available", "Mayor briefing inspector source.");
        coverage.Register("/api/briefings/food/latest", "available", "Food briefing inspector source.");

        app.MapGet("/api/colony/snapshot", (BriefingCache cache) =>
            Results.Ok(cache.GetMayorBriefing()));

        app.MapGet("/api/briefings/mayor/latest", (BriefingCache cache) =>
            Results.Ok(cache.GetMayorBriefing()));

        app.MapGet("/api/briefings/food/latest", (BriefingCache cache) =>
            Results.Ok(cache.GetFoodBriefing()));

        return app;
    }
}
