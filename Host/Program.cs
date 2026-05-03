using Serilog;
using RimAI.Ingestion;
using RimAI.LLM;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/rimai-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // ── Bind localhost-only (design/dashboard.md) ──────────────────────────────
    builder.WebHost.UseUrls("http://localhost:5000");

    // ── Services ───────────────────────────────────────────────────────────────
    var rimApiBase = Environment.GetEnvironmentVariable("RIMAPI_BASE_URL")
                     ?? "http://localhost:8765/";

    builder.Services.AddHttpClient<RimApiClient>(c =>
    {
        c.BaseAddress = new Uri(rimApiBase);
        c.Timeout = TimeSpan.FromSeconds(10);
    });
    builder.Services.AddSingleton<LlmClient>();

    var app = builder.Build();

    // ── Middleware ─────────────────────────────────────────────────────────────
    app.UseDefaultFiles();   // serves index.html from wwwroot/ for "/"
    app.UseStaticFiles();    // dashboard Vite build output

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

    // ── M0 startup checks ──────────────────────────────────────────────────────
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<RimApiClient>();

                Log.Information("Connecting to RIMAPI at {BaseUrl}", rimApiBase);
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

                var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
                if (string.IsNullOrEmpty(apiKey))
                {
                    Log.Warning("GEMINI_API_KEY not set — LLM calls will fail at runtime");
                    Console.WriteLine("✗ GEMINI_API_KEY env var not set");
                }
                else
                {
                    Log.Information("GEMINI_API_KEY present (length={Len})", apiKey.Length);
                    Console.WriteLine("✓ GEMINI_API_KEY present");

                    using var pingCts =
                        CancellationTokenSource.CreateLinkedTokenSource(app.Lifetime.ApplicationStopping);
                    pingCts.CancelAfter(TimeSpan.FromSeconds(5));
                    var llm = scope.ServiceProvider.GetRequiredService<LlmClient>();
                    var pingOk = await llm.PingAsync(pingCts.Token);
                    Console.WriteLine(pingOk ? "✓ Gemini ping OK" : "✗ Gemini ping failed (see logs)");
                }

                Console.WriteLine($"\nDashboard: http://localhost:5000\n");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unhandled exception in startup background checks");
                Console.WriteLine($"\n✗ Startup checks failed — see logs: {ex.Message}\n");
            }
        });
    });

    Log.Information("RimAI starting — dashboard at http://localhost:5000");
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
