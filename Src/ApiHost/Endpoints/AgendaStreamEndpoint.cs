using System.Text.Json;
using System.Threading.Channels;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Host;

namespace RimBob.Host.Endpoints;

/// <summary>
/// GET /api/advice/stream - SSE feed. On connect replays the current Mayor snapshot
/// and active feeder advice, then forwards live AdviceBus events.
/// </summary>
public static class AgendaStreamEndpoint
{
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static IEndpointRouteBuilder MapAgendaStream(this IEndpointRouteBuilder app)
    {
        app.ServiceProvider
            .GetRequiredService<EndpointCoverageCatalog>()
            .Register("/api/advice/stream", "available", "SSE agenda and active advice feed.");

        app.MapGet("/api/advice/stream", HandleAsync);
        return app;
    }

    private static async Task HandleAsync(
        HttpContext ctx,
        MinisterOutputStore store,
        AdviceBus bus,
        CabinetRunLogStore cabinetRuns,
        SseDiagnostics diagnostics,
        HostRuntimeIdentity hostIdentity,
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
        void OnAdviceSnapshot(AdviceSnapshot snapshot) => channel.Writer.TryWrite(SseMessage.ForAdviceSnapshot(snapshot));
        void OnCabinetRun(CabinetRunLogSnapshot snapshot) => channel.Writer.TryWrite(SseMessage.ForCabinetRun(snapshot));

        bus.AgendaUpdated += OnAgenda;
        bus.AdvicePublished += OnAdvice;
        bus.AdviceSnapshotPublished += OnAdviceSnapshot;
        cabinetRuns.RunChanged += OnCabinetRun;
        diagnostics.Connected();
        log.LogInformation("SSE client connected");

        try
        {
            await WriteHostReadyAsync(ctx, hostIdentity, diagnostics, ct);
            if (store.CurrentMayorAgenda is { } current)
                await WriteAgendaAsync(ctx, current, diagnostics, ct);
            AdviceSnapshot activeSnapshot = bus.ActiveSnapshot();
            IReadOnlyList<AdviceItem> activeAdvice = activeSnapshot.Advice;
            await WriteAdviceSnapshotAsync(ctx, activeSnapshot, diagnostics, ct);
            foreach (AdviceItem advice in activeAdvice)
                await WriteAdviceAsync(ctx, advice, diagnostics, ct);
            foreach (CabinetRunLogSnapshot cabinetRun in cabinetRuns.LatestRuns())
                await WriteCabinetRunAsync(ctx, cabinetRun, diagnostics, ct);

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
                        if (e.AdviceSnapshot is not null)
                            await WriteAdviceSnapshotAsync(ctx, e.AdviceSnapshot, diagnostics, ct);
                        if (e.Advice is not null)
                            await WriteAdviceAsync(ctx, e.Advice, diagnostics, ct);
                        if (e.CabinetRun is not null)
                            await WriteCabinetRunAsync(ctx, e.CabinetRun, diagnostics, ct);
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
            bus.AdviceSnapshotPublished -= OnAdviceSnapshot;
            cabinetRuns.RunChanged -= OnCabinetRun;
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

    private static async Task WriteHostReadyAsync(
        HttpContext ctx,
        HostRuntimeIdentity hostIdentity,
        SseDiagnostics diagnostics,
        CancellationToken ct)
    {
        string payload = JsonSerializer.Serialize(new
        {
            product = "RimBob",
            rim_bob_version = hostIdentity.RimBobVersion,
            running_version = hostIdentity.RunningVersion,
            build_number = hostIdentity.BuildNumber,
            build_datetime = hostIdentity.BuildDateTime,
            build_version = hostIdentity.BuildVersion,
            build_informational_version = hostIdentity.BuildInformationalVersion,
            build_revision = hostIdentity.BuildRevision,
            build_revision_short = hostIdentity.BuildRevisionShort,
            dashboard_asset_version = hostIdentity.DashboardAssetVersion,
            reload_token = hostIdentity.ReloadToken,
            host_started_at = hostIdentity.StartedAt,
            host_instance_id = hostIdentity.InstanceId
        }, Json);
        await ctx.Response.WriteAsync($"event: host_ready\nid: {hostIdentity.InstanceId}\ndata: {payload}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
        diagnostics.EventSent("host_ready", hostIdentity.InstanceId);
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

    private static async Task WriteAdviceSnapshotAsync(
        HttpContext ctx,
        AdviceSnapshot snapshot,
        SseDiagnostics diagnostics,
        CancellationToken ct)
    {
        string id = snapshot.Minister ?? "active";
        string payload = JsonSerializer.Serialize(new
        {
            minister = snapshot.Minister,
            advice = snapshot.Advice,
            state_summary = snapshot.StateSummary,
            state_summaries = snapshot.StateSummaries,
            chain = snapshot.Chain,
            chains = snapshot.Chains,
            flags = snapshot.Flags
        }, Json);
        await ctx.Response.WriteAsync($"event: advice_snapshot\nid: {id}\ndata: {payload}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
        diagnostics.EventSent("advice_snapshot", id);
    }

    private static async Task WriteCabinetRunAsync(
        HttpContext ctx,
        CabinetRunLogSnapshot snapshot,
        SseDiagnostics diagnostics,
        CancellationToken ct)
    {
        string payload = JsonSerializer.Serialize(snapshot, Json);
        await ctx.Response.WriteAsync($"event: cabinet_run\nid: {snapshot.RunId}\ndata: {payload}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
        diagnostics.EventSent("cabinet_run", snapshot.RunId);
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

    private sealed record SseMessage(
        MayorAgenda? Agenda,
        AdviceItem? Advice,
        AdviceSnapshot? AdviceSnapshot,
        CabinetRunLogSnapshot? CabinetRun)
    {
        public static SseMessage ForAgenda(MayorAgenda agenda) => new(agenda, null, null, null);
        public static SseMessage ForAdvice(AdviceItem advice) => new(null, advice, null, null);
        public static SseMessage ForAdviceSnapshot(AdviceSnapshot snapshot) => new(null, null, snapshot, null);
        public static SseMessage ForCabinetRun(CabinetRunLogSnapshot snapshot) => new(null, null, null, snapshot);
    }
}
