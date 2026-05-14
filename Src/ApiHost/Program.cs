using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Formatting.Json;
using RimAI.Coordination;
using RimAI.Core.Ministers;
using RimAI.Host;
using RimAI.Host.Endpoints;
using RimAI.Ingestion;
using RimAI.Knowledge;
using RimAI.LLM;
using RimAI.Ministers.Food;
using RimAI.Ministers.Mayor;
using RimAI.State;

var builder = WebApplication.CreateBuilder(args);

// appsettings.Local.json is gitignored; safe place for the Gemini key in dev.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

string logsDir = HostLogPaths.ResolveLogsDirectory(builder.Environment.ContentRootPath);
Directory.CreateDirectory(logsDir);

// ── Logging: Serilog reads from appsettings.json ───────────────────────────
builder.Host.UseSerilog((ctx, services, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .ReadFrom.Services(services)
       .WriteTo.File(
           path: Path.Combine(logsDir, "rimai-.log"),
           rollingInterval: RollingInterval.Day,
           outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
       .WriteTo.File(
           formatter: new JsonFormatter(),
           path: Path.Combine(logsDir, "decisions-.jsonl"),
           rollingInterval: RollingInterval.Day));

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
builder.Services.AddSingleton<RawLlmOutputStore>();
builder.Services.AddSingleton<LlmClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<RimAiOptions>>().Value;
    IReadOnlyList<string> apiKeys = ResolveGeminiApiKeys(opts);
    return new LlmClient(apiKeys, sp.GetRequiredService<PromptBuilder>(),
                                  sp.GetRequiredService<ILogger<LlmClient>>(),
                                  sp.GetRequiredService<RawLlmOutputStore>());
});

builder.Services.AddSingleton<ColonyState>();
builder.Services.AddSingleton<BriefingCache>();
builder.Services.AddSingleton<IngestionDispatcher>();

builder.Services.AddSingleton<AdviceBus>();
builder.Services.AddSingleton<AgendaStore>();
builder.Services.AddSingleton<FlagChannel>();
builder.Services.AddSingleton<MinisterTraceStore>();
builder.Services.AddSingleton<IReplayCorpusWriter>(sp =>
    new ReplayCorpusWriter(
        Path.Combine(logsDir, "replay"),
        sp.GetRequiredService<ILogger<ReplayCorpusWriter>>()));
builder.Services.AddSingleton<MayorAgendaRules>();
builder.Services.AddSingleton<MayorStatus>();
builder.Services.AddSingleton<SseDiagnostics>();

// ── RAG (M2) ───────────────────────────────────────────────────────────────
// Embedder isn't in DI — null-when-unconfigured doesn't compose well with the
// generic AddSingleton<TService> constraints. Each consumer builds its own via
// the same RimAiOptions resolution path.
builder.Services.AddSingleton<KnowledgeBase>();
builder.Services.AddSingleton<EmbeddingCache>(sp =>
{
    RimAiOptions opts = sp.GetRequiredService<IOptions<RimAiOptions>>().Value;
    string cacheDir = Path.IsPathRooted(opts.Rag.CacheRoot)
        ? opts.Rag.CacheRoot
        : Path.Combine(builder.Environment.ContentRootPath, opts.Rag.CacheRoot);
    return new EmbeddingCache(cacheDir);
});
builder.Services.AddSingleton<MayorRagRetriever>(sp =>
{
    RimAiOptions opts = sp.GetRequiredService<IOptions<RimAiOptions>>().Value;
    IEmbedder? embedder = ResolveEmbedder(opts, sp);
    return new MayorRagRetriever(
        kb:       sp.GetRequiredService<KnowledgeBase>(),
        embedder: embedder,
        enabled:  opts.Rag.Enabled,
        topK:     opts.Rag.TopK,
        log:      sp.GetRequiredService<ILogger<MayorRagRetriever>>());
});
builder.Services.AddSingleton<FoodRagRetriever>(sp =>
{
    RimAiOptions opts = sp.GetRequiredService<IOptions<RimAiOptions>>().Value;
    IEmbedder? embedder = ResolveEmbedder(opts, sp);
    return new FoodRagRetriever(
        kb:       sp.GetRequiredService<KnowledgeBase>(),
        embedder: embedder,
        enabled:  opts.Rag.Enabled,
        topK:     opts.Rag.TopK,
        log:      sp.GetRequiredService<ILogger<FoodRagRetriever>>());
});
builder.Services.AddSingleton<Ingest>(sp =>
{
    RimAiOptions opts = sp.GetRequiredService<IOptions<RimAiOptions>>().Value;
    IEmbedder? embedder = ResolveEmbedder(opts, sp)
        ?? throw new InvalidOperationException("Ingest requires an embedder; check RimAi:Rag:Enabled and Gemini key.");
    return new Ingest(
        embedder: embedder,
        cache:    sp.GetRequiredService<EmbeddingCache>(),
        log:      sp.GetRequiredService<ILogger<Ingest>>());
});

static IEmbedder? ResolveEmbedder(RimAiOptions opts, IServiceProvider sp)
{
    if (!opts.Rag.Enabled) return null;
    IReadOnlyList<string> apiKeys = ResolveGeminiApiKeys(opts);
    if (apiKeys.Count == 0) return null;
    return new GeminiEmbedder(apiKeys, opts.Rag.EmbeddingModel,
        sp.GetRequiredService<ILogger<GeminiEmbedder>>());
}

static IReadOnlyList<string> ResolveGeminiApiKeys(RimAiOptions opts)
{
    List<string> keys = [];
    AddGeminiKeys(keys, SplitGeminiKeyList(Environment.GetEnvironmentVariable("GEMINI_API_KEYS")));
    AddGeminiKeys(keys, opts.GeminiApiKeys);
    return keys
        .Where(key => !string.IsNullOrWhiteSpace(key))
        .Select(key => key.Trim())
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}

static void AddGeminiKeys(List<string> keys, IEnumerable<string>? values)
{
    if (values is null) return;
    foreach (string key in values)
    {
        if (!string.IsNullOrWhiteSpace(key)) keys.Add(key);
    }
}

static IReadOnlyList<string> SplitGeminiKeyList(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return [];
    char[] separators = [';', ',', '\r', '\n'];
    return raw.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

builder.Services.AddSingleton<Mayor>();
builder.Services.AddSingleton<Rules>();
builder.Services.AddSingleton<MinisterOfFood>();
builder.Services.AddSingleton<IMinister>(sp => sp.GetRequiredService<Mayor>());
builder.Services.AddSingleton<IMinister>(sp => sp.GetRequiredService<MinisterOfFood>());
builder.Services.AddSingleton<CabinetCycle>();
builder.Services.AddHostedService<DayTickOrchestrator>();

var app = builder.Build();
app.Logger.LogInformation("Host logs directory: {LogsDirectory}", logsDir);

// ── Middleware ─────────────────────────────────────────────────────────────
app.UseDefaultFiles();
app.UseStaticFiles();

// ── API endpoints ──────────────────────────────────────────────────────────
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "RimAI" }));

app.MapCabinetEndpoints();
app.MapAgendaStream();
app.MapAgendaEndpoints();
app.MapAutonomyEndpoints();
app.MapColonyEndpoints();
app.MapStatusEndpoints();
app.MapMinisterEndpoints();
app.MapSystemEndpoints();

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
                Console.WriteLine("✗ Gemini API key not set (env GEMINI_API_KEYS or RimAi.GeminiApiKeys in appsettings.Local.json)");
            }
            else if (opts.PingLlmOnStartup)
            {
                Console.WriteLine($"✓ Gemini API key(s) present: {llm.ConfiguredKeyCount}");
                using var pingCts =
                    CancellationTokenSource.CreateLinkedTokenSource(app.Lifetime.ApplicationStopping);
                pingCts.CancelAfter(TimeSpan.FromSeconds(5));
                var pingOk = await llm.PingAsync(pingCts.Token);
                Console.WriteLine(pingOk ? "✓ Gemini ping OK" : "✗ Gemini ping failed (see logs)");
            }

            // ── RAG ingestion (M2) ───────────────────────────────────────
            if (opts.Rag.Enabled)
            {
                IEmbedder? embedder = ResolveEmbedder(opts, scope.ServiceProvider);
                if (embedder is null)
                {
                    Log.Warning("RAG enabled but no Gemini key configured — KnowledgeBase will stay empty.");
                    Console.WriteLine("✗ RAG enabled but Gemini key missing — KnowledgeBase empty");
                }
                else
                {
                    string guidesRoot = Path.IsPathRooted(opts.Rag.GuidesRoot)
                        ? opts.Rag.GuidesRoot
                        : Path.Combine(app.Environment.ContentRootPath, opts.Rag.GuidesRoot);
                    Ingest        ingest = scope.ServiceProvider.GetRequiredService<Ingest>();
                    KnowledgeBase kb     = scope.ServiceProvider.GetRequiredService<KnowledgeBase>();
                    using CancellationTokenSource ingestCts =
                        CancellationTokenSource.CreateLinkedTokenSource(app.Lifetime.ApplicationStopping);
                    ingestCts.CancelAfter(TimeSpan.FromMinutes(2));
                    try
                    {
                        await ingest.RunAsync(guidesRoot, kb, ingestCts.Token);
                        Console.WriteLine($"✓ KnowledgeBase ready: {kb.Count} chunks from {opts.Rag.GuidesRoot}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Guide ingestion failed; Mayor will run without RAG this session.");
                        Console.WriteLine($"✗ KnowledgeBase ingest failed — {ex.Message}");
                    }
                }
            }
            else
            {
                Console.WriteLine("· RAG disabled in config (RimAi:Rag:Enabled = false)");
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
