using Microsoft.Extensions.Options;
using Serilog;
using RimAI.Host;
using RimAI.Ingestion;
using RimAI.LLM;
using RimAI.State;

var builder = WebApplication.CreateBuilder(args);

// ── Logging: Serilog reads from appsettings.json ───────────────────────────
builder.Host.UseSerilog((ctx, services, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .ReadFrom.Services(services));

// ── Typed configuration ────────────────────────────────────────────────────
builder.Services
    .AddOptions<RimAiOptions>()
    .Bind(builder.Configuration.GetSection(RimAiOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var options = builder.Configuration
    .GetSection(RimAiOptions.SectionName)
    .Get<RimAiOptions>() ?? new RimAiOptions();

// Localhost-only bind (design/dashboard.md). Never 0.0.0.0.
builder.WebHost.UseUrls(options.ListenUrl);

// ── Services ───────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<RimApiClient>((sp, c) =>
{
    var opts = sp.GetRequiredService<IOptions<RimAiOptions>>().Value;
    c.BaseAddress = new Uri(opts.RimApiBaseUrl);
    c.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<PromptBuilder>();
builder.Services.AddSingleton<LlmClient>();

builder.Services.AddSingleton<ColonyState>();
builder.Services.AddSingleton<BriefingCache>();
builder.Services.AddScoped<IngestionDispatcher>();

var app = builder.Build();

// ── Middleware ─────────────────────────────────────────────────────────────
app.UseDefaultFiles();
app.UseStaticFiles();

// ── API endpoints ──────────────────────────────────────────────────────────
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "RimAI" }));

// SSE feed — empty in M0; ministers push AdviceItems here from M1 onward
app.MapGet("/api/advice/stream", async (HttpContext ctx, CancellationToken ct) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers["Cache-Control"] = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";
    await ctx.Response.Body.FlushAsync(ct);
    try { await Task.Delay(Timeout.Infinite, ct); }
    catch (OperationCanceledException) { }
});

// ── Startup checks ─────────────────────────────────────────────────────────
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var opts = scope.ServiceProvider.GetRequiredService<IOptions<RimAiOptions>>().Value;
            var client = scope.ServiceProvider.GetRequiredService<RimApiClient>();

            Log.Information("Connecting to RIMAPI at {BaseUrl}", opts.RimApiBaseUrl);
            var (ok, message) = await client.HandshakeAsync();

            if (ok)
            {
                Log.Information("RIMAPI handshake OK — first pawn: {PawnName}", message);
                Console.WriteLine($"\n✓ RIMAPI handshake OK — first pawn: {message}");
            }
            else
            {
                Log.Warning("RIMAPI handshake failed: {Reason}", message);
                Console.WriteLine($"\n✗ {message}");
                Console.WriteLine("  Start RimWorld with the RIMAPI mod loaded and a colony open.");
            }

            var llm = scope.ServiceProvider.GetRequiredService<LlmClient>();
            if (!llm.IsConfigured)
            {
                Log.Warning("GEMINI_API_KEY not set — LLM calls will fail at runtime");
                Console.WriteLine("✗ GEMINI_API_KEY env var not set");
            }
            else if (opts.PingLlmOnStartup)
            {
                Console.WriteLine("✓ GEMINI_API_KEY present");
                using var pingCts =
                    CancellationTokenSource.CreateLinkedTokenSource(app.Lifetime.ApplicationStopping);
                pingCts.CancelAfter(TimeSpan.FromSeconds(5));
                var pingOk = await llm.PingAsync(pingCts.Token);
                Console.WriteLine(pingOk ? "✓ Gemini ping OK" : "✗ Gemini ping failed (see logs)");
            }

            Console.WriteLine($"\nDashboard: {opts.ListenUrl}\n");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unhandled exception in startup background checks");
            Console.WriteLine($"\n✗ Startup checks failed — see logs: {ex.Message}\n");
        }
    });
});

Log.Information("RimAI starting — dashboard at {Url}", options.ListenUrl);

try
{
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception at startup");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
