using RimAI.State;

namespace RimAI.Host.Endpoints;

/// <summary>
/// GET /api/colony/snapshot — returns the latest MayorBriefing the Mayor sees.
/// The dashboard sidebar reads this to render dry facts (date, wealth, food,
/// power, etc.) so the agenda body can stay interpretive rather than repeat numbers.
/// </summary>
public static class ColonyEndpoints
{
    public static IEndpointRouteBuilder MapColonyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/colony/snapshot", (BriefingCache cache) =>
            Results.Ok(cache.GetMayorBriefing()));

        return app;
    }
}
