using RimAI.Ingestion;

namespace RimAI.Host.Endpoints;

public static class IconEndpoints
{
    public static IEndpointRouteBuilder MapIconEndpoints(this IEndpointRouteBuilder app)
    {
        EndpointCoverageCatalog coverage = app.ServiceProvider.GetRequiredService<EndpointCoverageCatalog>();
        coverage.Register("/api/icons/item/{defName}", "available", "Read-only cached RIMAPI item/ThingDef PNG gateway.");
        coverage.Register("/api/icons/terrain/{defName}", "available", "Read-only cached RIMAPI TerrainDef PNG gateway.");
        coverage.Register("/api/icons/faction/{loadId}", "available", "Read-only cached current-world faction icon PNG gateway.");
        coverage.Register("/api/icons/pawn/{pawnId}/portrait", "available", "Read-only cached pawn portrait PNG gateway; not prewarmed.");
        coverage.Register("/api/icons/colonist/{pawnId}/body", "available", "Read-only colonist body/head image cache fetch; not prewarmed.");
        coverage.Register("/api/icons/cache/status", "available", "Local icon cache status and last warm summary.");
        coverage.Register("/api/icons/cache/warm", "available", "Static item, terrain, and faction icon cache warmer.");

        app.MapGet("/api/icons/item/{defName}", async Task<IResult> (
            string defName,
            IconCacheService icons,
            CancellationToken ct) =>
            await ServePngAsync(() => icons.GetItemIconAsync(defName, ct), ct));

        app.MapGet("/api/icons/terrain/{defName}", async Task<IResult> (
            string defName,
            IconCacheService icons,
            CancellationToken ct) =>
            await ServePngAsync(() => icons.GetTerrainIconAsync(defName, ct), ct));

        app.MapGet("/api/icons/faction/{loadId:int}", async Task<IResult> (
            int loadId,
            IconCacheService icons,
            CancellationToken ct) =>
            await ServePngAsync(() => icons.GetFactionIconAsync(loadId, ct), ct));

        app.MapGet("/api/icons/pawn/{pawnId:int}/portrait", async Task<IResult> (
            int pawnId,
            int? width,
            int? height,
            string? direction,
            IconCacheService icons,
            CancellationToken ct) =>
            await ServePngAsync(() => icons.GetPawnPortraitAsync(
                pawnId,
                width ?? 128,
                height ?? 128,
                string.IsNullOrWhiteSpace(direction) ? "South" : direction,
                ct),
                ct));

        app.MapGet("/api/icons/colonist/{pawnId:int}/body", async Task<IResult> (
            int pawnId,
            IconCacheService icons,
            CancellationToken ct) =>
            await JsonAsync(() => icons.GetColonistBodyAsync(pawnId, ct), ct));

        app.MapGet("/api/icons/cache/status", (IconCacheService icons) =>
            Results.Ok(icons.GetStatus()));

        app.MapPost("/api/icons/cache/warm", async Task<IResult> (
            IconCacheService icons,
            CancellationToken ct) =>
            await JsonAsync(() => icons.WarmStaticAsync(ct), ct));

        return app;
    }

    private static async Task<IResult> ServePngAsync(Func<Task<IconFile>> load, CancellationToken ct)
    {
        try
        {
            IconFile file = await load();
            return Results.File(file.Path, file.ContentType);
        }
        catch (Exception ex) when (ex is ArgumentException)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex) when (IsRimApiUnavailable(ex, ct))
        {
            return Results.Problem(
                detail: ex.Message,
                title: "RIMAPI is unavailable",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex) when (IsUpstreamIconFailure(ex))
        {
            return Results.Problem(
                detail: ex.Message,
                title: "RIMAPI icon response could not be used",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> JsonAsync<T>(Func<Task<T>> load, CancellationToken ct)
    {
        try
        {
            T result = await load();
            return Results.Ok(result);
        }
        catch (Exception ex) when (ex is ArgumentException)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex) when (IsRimApiUnavailable(ex, ct))
        {
            return Results.Problem(
                detail: ex.Message,
                title: "RIMAPI is unavailable",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex) when (IsUpstreamIconFailure(ex))
        {
            return Results.Problem(
                detail: ex.Message,
                title: "RIMAPI icon response could not be used",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static bool IsRimApiUnavailable(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException ||
        ex is TaskCanceledException && !ct.IsCancellationRequested;

    private static bool IsUpstreamIconFailure(Exception ex) =>
        ex is RimApiException or InvalidOperationException or FormatException or IOException;
}
