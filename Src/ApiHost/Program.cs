using Microsoft.Extensions.Options;
using Serilog;
using RimAI.Coordination;
using RimAI.Core.Ministers;
using RimAI.Host;
using RimAI.Host.Endpoints;
using RimAI.Ingestion;
using RimAI.LLM;
using RimAI.Ministers.Mayor;
using RimAI.State;

var builder = WebApplication.CreateBuilder(args);

// appsettings.Local.json is gitignored; safe place for the Gemini key in dev.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

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
builder.Services.AddSingleton<LlmClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<RimAiOptions>>().Value;
    // Env var wins so an explicit GEMINI_API_KEY override still works.
    string? apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey)) apiKey = opts.GeminiApiKey;
    return new LlmClient(apiKey, sp.GetRequiredService<PromptBuilder>(),
                                 sp.GetRequiredService<ILogger<LlmClient>>());
});

builder.Services.AddSingleton<ColonyState>();
builder.Services.AddSingleton<BriefingCache>();
builder.Services.AddSingleton<IngestionDispatcher>();

builder.Services.AddSingleton<AdviceBus>();
builder.Services.AddSingleton<AgendaStore>();
builder.Services.AddSingleton<MayorRules>();
builder.Services.AddSingleton<MayorStatus>();
builder.Services.AddSingleton<Mayor>();
builder.Services.AddSingleton<IMinister>(sp => sp.GetRequiredService<Mayor>());
builder.Services.AddHostedService<DayTickOrchestrator>();

var app = builder.Build();

// ── Middleware ─────────────────────────────────────────────────────────────
app.UseDefaultFiles();
app.UseStaticFiles();

// ── API endpoints ──────────────────────────────────────────────────────────
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "RimAI" }));

app.MapAgendaStream();
app.MapAgendaEndpoints();
app.MapAutonomyEndpoints();
app.MapColonyEndpoints();
app.MapStatusEndpoints();

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
                Log.Warning("Gemini API key not set — LLM calls will fail at runtime");
                Console.WriteLine("✗ Gemini API key not set (env GEMINI_API_KEY or RimAi.GeminiApiKey in appsettings.Local.json)");
            }
            else if (opts.PingLlmOnStartup)
            {
                Console.WriteLine("✓ Gemini API key present");
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
