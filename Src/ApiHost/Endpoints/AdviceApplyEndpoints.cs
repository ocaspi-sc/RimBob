using RimAI.Host;

namespace RimAI.Host.Endpoints;

public static class AdviceApplyEndpoints
{
    public static IEndpointRouteBuilder MapAdviceApplyEndpoints(this IEndpointRouteBuilder app)
    {
        EndpointCoverageCatalog coverage = app.ServiceProvider.GetRequiredService<EndpointCoverageCatalog>();
        coverage.Register(
            "/api/advice/{adviceId}/steps/{stepIndex}/apply",
            "available",
            "Player-confirmed Assisted Apply for allowlisted Food steps.");

        app.MapPost("/api/advice/{adviceId}/steps/{stepIndex:int}/apply", async (
            string adviceId,
            int stepIndex,
            AssistedApplyService apply,
            CancellationToken ct) =>
        {
            AssistedApplyResponse result = await apply.ApplyAsync(adviceId, stepIndex, ct);
            return Results.Ok(result);
        });

        return app;
    }
}
