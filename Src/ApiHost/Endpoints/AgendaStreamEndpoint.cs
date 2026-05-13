using System.Text.Json;
using System.Threading.Channels;
using RimAI.Coordination;
using RimAI.Core.Advice;
using RimAI.Host;

namespace RimAI.Host.Endpoints;

/// <summary>
/// GET /api/advice/stream - SSE feed. On connect replays the current MayorAgenda
/// and active feeder advice, then forwards live AdviceBus events.
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
        HttpContext ctx,
        AgendaStore store,
        AdviceBus bus,
        SseDiagnostics diagnostics,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        ILogger log = loggerFactory.CreateLogger("AgendaStream");

        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";
        await ctx.Response.Body.FlushAsync(ct);

        Channel<SseMessage> channel = Channel.CreateBounded<SseMessage>(
            new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.DropOldest });

        void OnAgenda(AgendaUpdated e) => channel.Writer.TryWrite(SseMessage.ForAgenda(e.Agenda));
        void OnAdvice(AdviceItem item) => channel.Writer.TryWrite(SseMessage.ForAdvice(item));

        bus.AgendaUpdated += OnAgenda;
        bus.AdvicePublished += OnAdvice;
        diagnostics.Connected();
        log.LogInformation("SSE client connected");

        try
        {
            if (store.Current is { } current)
                await WriteAgendaAsync(ctx, current, diagnostics, ct);
            foreach (AdviceItem advice in bus.ActiveAdvice())
                await WriteAdviceAsync(ctx, advice, diagnostics, ct);

            while (!ct.IsCancellationRequested)
            {
                Task<bool> waitTask = channel.Reader.WaitToReadAsync(ct).AsTask();
                Task delayTask = Task.Delay(PingInterval, ct);
                Task winner = await Task.WhenAny(waitTask, delayTask);

                if (winner == waitTask && await waitTask)
                {
                    while (channel.Reader.TryRead(out SseMessage? e))
                    {
                        if (e.Agenda is not null)
                            await WriteAgendaAsync(ctx, e.Agenda, diagnostics, ct);
                        if (e.Advice is not null)
                            await WriteAdviceAsync(ctx, e.Advice, diagnostics, ct);
                    }
                }
                else
                {
                    await WritePingAsync(ctx, diagnostics, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            diagnostics.Error(ex.Message);
            throw;
        }
        finally
        {
            bus.AgendaUpdated -= OnAgenda;
            bus.AdvicePublished -= OnAdvice;
            channel.Writer.TryComplete();
            diagnostics.Disconnected();
            log.LogInformation("SSE client disconnected");
        }
    }

    private static async Task WriteAgendaAsync(
        HttpContext ctx,
        MayorAgenda agenda,
        SseDiagnostics diagnostics,
        CancellationToken ct)
    {
        string payload = JsonSerializer.Serialize(agenda, Json);
        await ctx.Response.WriteAsync($"event: agenda_update\nid: {agenda.Version}\ndata: {payload}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
        diagnostics.EventSent("agenda_update", agenda.Version.ToString());
    }

    private static async Task WriteAdviceAsync(
        HttpContext ctx,
        AdviceItem advice,
        SseDiagnostics diagnostics,
        CancellationToken ct)
    {
        string payload = JsonSerializer.Serialize(advice, Json);
        await ctx.Response.WriteAsync($"event: advice\nid: {advice.Id}\ndata: {payload}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
        diagnostics.EventSent("advice", advice.Id);
    }

    private static async Task WritePingAsync(
        HttpContext ctx,
        SseDiagnostics diagnostics,
        CancellationToken ct)
    {
        await ctx.Response.WriteAsync("event: ping\ndata: {}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
        diagnostics.EventSent("ping");
    }

    private sealed record SseMessage(MayorAgenda? Agenda, AdviceItem? Advice)
    {
        public static SseMessage ForAgenda(MayorAgenda agenda) => new(agenda, null);
        public static SseMessage ForAdvice(AdviceItem advice) => new(null, advice);
    }
}
