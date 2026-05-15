using RimAI.Coordination;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;
using RimAI.State;

namespace RimAI.Host.Endpoints;

/// <summary>
/// GET /api/agenda/latest, /api/agenda/history,
/// POST /api/agenda/manual (paste a MayorAgendaInput from another LLM).
/// </summary>
public static class AgendaEndpoints
{
    private const int ShortTermCap = 5;

    public static IEndpointRouteBuilder MapAgendaEndpoints(this IEndpointRouteBuilder app)
    {
        EndpointCoverageCatalog coverage = app.ServiceProvider.GetRequiredService<EndpointCoverageCatalog>();
        coverage.Register(
            "/api/agenda/latest",
            context => context.AgendaStore.Current is null ? "missing" : "available",
            _ => "Current Mayor agenda.");
        coverage.Register("/api/agenda/history", "available", "Bounded Mayor agenda history.");
        coverage.Register("/api/agenda/manual", "available", "Developer manual Mayor agenda fallback ingestion.");

        app.MapGet("/api/agenda/latest", (AgendaStore store) =>
            store.Current is { } current ? Results.Ok(current) : Results.NoContent());

        app.MapGet("/api/agenda/history", (AgendaStore store, string? since) =>
        {
            IReadOnlyList<MayorAgenda> all = store.History;
            if (string.IsNullOrEmpty(since))
                return Results.Ok(all);

            List<MayorAgenda> filtered = new(all.Count);
            foreach (MayorAgenda a in all)
                if (string.CompareOrdinal(a.UpdatedInGameTick, since) > 0)
                    filtered.Add(a);
            return Results.Ok(filtered);
        });

        // Manual fallback: when Gemini is unreachable / rate-limited, paste a
        // MayorAgendaInput JSON produced by another LLM. Same path as a real
        // Mayor cycle: stamp via AgendaStore.UpdateAsync, broadcast over SSE.
        app.MapPost("/api/agenda/manual", async (
            MayorAgendaInput  input,
            BriefingCache     briefings,
            AgendaStore       store,
            AdviceBus         bus,
            CancellationToken ct) =>
        {
            MayorAgendaInput capped = input.ShortTerm.Count > ShortTermCap
                ? input with { ShortTerm = input.ShortTerm.Take(ShortTermCap).ToList() }
                : input;

            MayorBriefing briefing = briefings.GetMayorBriefing();
            string tick = $"Y{briefing.Date.Year ?? 0}{briefing.Date.Quadrum ?? "?"}D{briefing.Date.Day ?? 0}";
            MayorAgenda stamped = await store.UpdateAsync(capped, tick, ct);
            bus.Publish(new AgendaUpdated(stamped));
            return Results.Ok(stamped);
        });

        return app;
    }
}
