using RimBob.Coordination;
using RimBob.Core.Ministers;

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
            "/api/ministers/{minister}/trigger/rules",
            _ => "partial",
            context => $"Manual rules-only dashboard trigger for wired ministers: {context.CapabilityNames(descriptor => descriptor.CanRunRules)}. Planned scopes are not wired yet.");
        coverage.Register(
            "/api/ministers/{minister}/trigger/llm",
            _ => "partial",
            context => $"Manual forced-LLM dashboard trigger for LLM-backed ministers: {context.CapabilityNames(descriptor => descriptor.CanRunLlm)}. Non-LLM scopes stay disabled.");

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

        app.MapPost("/api/ministers/{minister}/trigger/rules", async (
            string minister,
            MinisterRegistry registry,
            CabinetCycle cabinet,
            CancellationToken ct) =>
            await TriggerMinisterAsync(minister, MinisterRunMode.RulesOnly, registry, cabinet, ct));

        app.MapPost("/api/ministers/{minister}/trigger/llm", async (
            string minister,
            MinisterRegistry registry,
            CabinetCycle cabinet,
            CancellationToken ct) =>
            await TriggerMinisterAsync(minister, MinisterRunMode.ForceLlm, registry, cabinet, ct));

        return app;
    }

    private static async Task<IResult> TriggerMinisterAsync(
        string minister,
        MinisterRunMode runMode,
        MinisterRegistry registry,
        CabinetCycle cabinet,
        CancellationToken ct)
    {
        MinisterDescriptor? descriptor = registry.FindMinister(minister);
        if (descriptor is null)
            return Results.NotFound(new { error = $"Unknown minister scope '{minister}'." });

        MinisterTriggerResult? result;
        try
        {
            result = await cabinet.TriggerMinisterAsync(minister, runMode, ct);
        }
        catch (Exception ex)
        {
            if (ManualTriggerErrorResults.TryMap(ex, out IResult mapped)) return mapped;
            throw;
        }

        if (result is null)
        {
            string title = runMode == MinisterRunMode.ForceLlm ? "LLM trigger not wired" : "Rules trigger not wired";
            string detail = runMode == MinisterRunMode.ForceLlm
                ? $"{descriptor.Label} does not have an LLM trigger wired yet."
                : $"{descriptor.Label} does not have a rules-only trigger wired yet.";
            return Results.Problem(
                title: title,
                detail: detail,
                statusCode: StatusCodes.Status501NotImplemented);
        }

        return Results.Ok(new
        {
            triggered = true,
            scope = result.Scope,
            minister = result.Minister,
            trigger = result.Trigger,
            run_mode = result.RunMode,
            state_source = result.StateSource,
            used_restored_snapshot = result.UsedRestoredSnapshot
        });
    }
}
