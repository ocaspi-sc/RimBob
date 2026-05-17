using RimBob.Host;

namespace RimBob.Host.Endpoints;

public static class AdviceApplyEndpoints
{
    public static IEndpointRouteBuilder MapAdviceApplyEndpoints(this IEndpointRouteBuilder app)
    {
        EndpointCoverageCatalog coverage = app.ServiceProvider.GetRequiredService<EndpointCoverageCatalog>();
        coverage.Register(
            "/api/advice/{adviceId}/actions/{actionIndex}/apply",
            "available",
            "Player-confirmed Assisted Apply for allowlisted Food actions.");

        app.MapPost("/api/advice/{adviceId}/actions/{actionIndex:int}/apply", async (
            string adviceId,
            int actionIndex,
            AssistedApplyService apply,
            CancellationToken ct) =>
        {
            AssistedApplyResponse result = await apply.ApplyAsync(adviceId, actionIndex, ct);
            return Results.Ok(result);
        });

        return app;
    }
}
