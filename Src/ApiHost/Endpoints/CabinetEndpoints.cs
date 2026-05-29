using RimBob.Coordination;

namespace RimBob.Host.Endpoints;

/// <summary>
/// Manual dashboard triggers. These run RimBob evaluation only; they do not issue
/// RimWorld/RIMAPI write commands.
/// </summary>
public static class CabinetEndpoints
{
    public static IEndpointRouteBuilder MapCabinetEndpoints(this IEndpointRouteBuilder app)
    {
        EndpointCoverageCatalog coverage = app.ServiceProvider.GetRequiredService<EndpointCoverageCatalog>();
        coverage.Register("/api/cabinet/trigger", "available", "Manual dashboard trigger for all live ministers; suggest-only, no RIMAPI writes.");
        coverage.Register(
            "/api/ministers/{minister}/trigger",
            _ => "partial",
            context => $"Manual dashboard trigger for wired ministers: {context.CapabilityNames(descriptor => descriptor.CanManualTrigger)}. Planned scopes are not wired yet.");

        app.MapPost("/api/cabinet/trigger", async (
            CabinetCycle cabinet,
            CancellationToken ct) =>
        {
            CabinetTriggerResult result;
            try
            {
                result = await cabinet.TriggerCabinetAsync(ct);
            }
            catch (Exception ex)
            {
                if (ManualTriggerErrorResults.TryMap(ex, out IResult mapped)) return mapped;
                throw;
            }

            return Results.Ok(new
            {
                triggered = true,
                scope = result.Scope,
                trigger = result.Trigger,
                state_source = result.StateSource,
                used_restored_snapshot = result.UsedRestoredSnapshot
            });
        });

        app.MapPost("/api/ministers/{minister}/trigger", async (
            string minister,
            MinisterRegistry registry,
            CabinetCycle cabinet,
            CancellationToken ct) =>
        {
            MinisterDescriptor? descriptor = registry.FindMinister(minister);
            if (descriptor is null)
                return Results.NotFound(new { error = $"Unknown minister scope '{minister}'." });

            MinisterTriggerResult? result;
            try
            {
                result = await cabinet.TriggerMinisterAsync(minister, ct);
            }
            catch (Exception ex)
            {
                if (ManualTriggerErrorResults.TryMap(ex, out IResult mapped)) return mapped;
                throw;
            }

            if (result is null)
            {
                return Results.Problem(
                    title: "Minister not wired",
                    detail: $"{descriptor.Label} is not wired for manual triggering.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            return Results.Ok(new
            {
                triggered = true,
                scope = result.Scope,
                minister = result.Minister,
                trigger = result.Trigger,
                state_source = result.StateSource,
                used_restored_snapshot = result.UsedRestoredSnapshot
            });
        });

        return app;
    }
}
