using RimAI.Coordination;
using RimAI.Core.Advice;

namespace RimAI.Host.Endpoints;

/// <summary>
/// GET /api/agenda/latest and /api/agenda/history.
/// </summary>
public static class AgendaEndpoints
{
    public static IEndpointRouteBuilder MapAgendaEndpoints(this IEndpointRouteBuilder app)
    {
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

        return app;
    }
}
