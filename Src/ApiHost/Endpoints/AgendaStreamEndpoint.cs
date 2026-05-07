using System.Text.Json;
using System.Threading.Channels;
using RimAI.Coordination;
using RimAI.Core.Advice;

namespace RimAI.Host.Endpoints;

/// <summary>
/// GET /api/advice/stream — SSE feed. On connect replays the current MayorAgenda
/// as one agenda_update event, then forwards live AdviceBus events. Sends a
/// ping every 15s when idle so proxies don't time out.
/// </summary>
public static class AgendaStreamEndpoint
{
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static IEndpointRouteBuilder MapAgendaStream(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/advice/stream", HandleAsync);
        return app;
    }

    private static async Task HandleAsync(
        HttpContext       ctx,
        AgendaStore       store,
        AdviceBus         bus,
        ILoggerFactory    loggerFactory,
        CancellationToken ct)
    {
        ILogger log = loggerFactory.CreateLogger("AgendaStream");

        ctx.Response.ContentType                = "text/event-stream";
        ctx.Response.Headers["Cache-Control"]   = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";
        await ctx.Response.Body.FlushAsync(ct);

        Channel<AgendaUpdated> channel = Channel.CreateBounded<AgendaUpdated>(
            new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest });

        void OnAgenda(AgendaUpdated e) => channel.Writer.TryWrite(e);
        bus.AgendaUpdated += OnAgenda;
        log.LogInformation("SSE client connected");

        try
        {
            if (store.Current is { } current)
                await WriteAgendaAsync(ctx, current, ct);

            while (!ct.IsCancellationRequested)
            {
                Task<bool> waitTask  = channel.Reader.WaitToReadAsync(ct).AsTask();
                Task       delayTask = Task.Delay(PingInterval, ct);
                Task       winner    = await Task.WhenAny(waitTask, delayTask);

                if (winner == waitTask && await waitTask)
                {
                    while (channel.Reader.TryRead(out AgendaUpdated? e))
                        await WriteAgendaAsync(ctx, e.Agenda, ct);
                }
                else
                {
                    await WritePingAsync(ctx, ct);
                }
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        finally
        {
            bus.AgendaUpdated -= OnAgenda;
            channel.Writer.TryComplete();
            log.LogInformation("SSE client disconnected");
        }
    }

    private static async Task WriteAgendaAsync(HttpContext ctx, MayorAgenda agenda, CancellationToken ct)
    {
        string payload = JsonSerializer.Serialize(agenda, Json);
        await ctx.Response.WriteAsync($"event: agenda_update\nid: {agenda.Version}\ndata: {payload}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }

    private static async Task WritePingAsync(HttpContext ctx, CancellationToken ct)
    {
        await ctx.Response.WriteAsync("event: ping\ndata: {}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
}
