using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Formatting.Json;
using RimBob.Coordination;
using RimBob.Core.Ministers;
using RimBob.Host;
using RimBob.Host.Endpoints;
using RimBob.Ingestion;
using RimBob.Knowledge;
using RimBob.LLM;
using RimBob.Ministers.Food;
using RimBob.Ministers.Mayor;
using RimBob.State;

var builder = WebApplication.CreateBuilder(args);

// appsettings.Local.json is gitignored; safe place for the Gemini key in dev.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

RimBobOptions options = builder.Configuration
    .GetSection(RimBobOptions.SectionName)
    .Get<RimBobOptions>() ?? new RimBobOptions();

string logsDir = HostLogPaths.ResolveLogsDirectory(builder.Environment.ContentRootPath, options.LogsRoot);
string varDir = HostLogPaths.ResolveVarDirectory(builder.Environment.ContentRootPath);
Directory.CreateDirectory(logsDir);
Directory.CreateDirectory(varDir);
string agendaStorePath = Path.Combine(varDir, "agenda", "agenda-store.json");
AgendaStore agendaStore = await AgendaStore.LoadAsync(agendaStorePath);

// ── Logging: Serilog reads from appsettings.json ───────────────────────────
builder.Host.UseSerilog((ctx, services, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .ReadFrom.Services(services)
       .WriteTo.File(
           path: Path.Combine(logsDir, "rimbob-.log"),
           rollingInterval: RollingInterval.Day,
           outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
       .WriteTo.File(
           formatter: new JsonFormatter(),
           path: Path.Combine(logsDir, "decisions-.jsonl"),
           rollingInterval: RollingInterval.Day));

// ── Typed configuration ────────────────────────────────────────────────────
builder.Services
    .AddOptions<RimBobOptions>()
    .Bind(builder.Configuration.GetSection(RimBobOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Localhost-only bind (design/dashboard.md). Never 0.0.0.0.
builder.WebHost.UseUrls(options.ListenUrl);

// ── Services ───────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<RimApiClient>((sp, c) =>
{
    var opts = sp.GetRequiredService<IOptions<RimBobOptions>>().Value;
    c.BaseAddress = new Uri(opts.RimApiBaseUrl);
    c.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<IconCacheService>(sp =>
{
    RimBobOptions opts = sp.GetRequiredService<IOptions<RimBobOptions>>().Value;
    IconWarmOptions iconWarm = opts.IconWarm;
    IHostApplicationLifetime lifetime = sp.GetRequiredService<IHostApplicationLifetime>();
    string iconCacheRoot = HostLogPaths.ResolveIconCacheDirectory(
        builder.Environment.ContentRootPath,
        opts.IconCacheRoot);
    return new IconCacheService(
        iconCacheRoot,
        sp.GetRequiredService<RimApiClient>(),
        sp.GetRequiredService<ILogger<IconCacheService>>(),
        iconWarm.Attempts,
        TimeSpan.FromMilliseconds(iconWarm.RetryDelayMilliseconds),
        iconWarm.Concurrency,
        TimeSpan.FromMilliseconds(iconWarm.RequestIntervalMilliseconds),
        iconWarm.CheckpointInterval,
        lifetime.ApplicationStopping);
});
builder.Services.AddSingleton<PromptBuilder>();
builder.Services.AddSingleton<RawLlmOutputStore>();
builder.Services.AddSingleton<LlmClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<RimBobOptions>>().Value;
    IReadOnlyList<string> apiKeys = ResolveGeminiApiKeys(opts);
    return new LlmClient(apiKeys, sp.GetRequiredService<PromptBuilder>(),
                                  sp.GetRequiredService<ILogger<LlmClient>>(),
                                  sp.GetRequiredService<RawLlmOutputStore>());
});

builder.Services.AddSingleton<ColonyState>();
builder.Services.AddSingleton<BriefingCache>();
builder.Services.AddSingleton<IngestionDispatcher>();

builder.Services.AddSingleton<AdviceBus>();
builder.Services.AddSingleton(agendaStore);
builder.Services.AddSingleton<FlagChannel>();
builder.Services.AddSingleton<MinisterRegistry>();
builder.Services.AddSingleton<EndpointCoverageCatalog>();
builder.Services.AddSingleton<MinisterTraceStore>();
builder.Services.AddSingleton<AssistedApplyService>();
builder.Services.AddSingleton<IReplayCorpusWriter>(sp =>
    new ReplayCorpusWriter(
        Path.Combine(logsDir, "replay"),
        sp.GetRequiredService<ILogger<ReplayCorpusWriter>>()));
builder.Services.AddSingleton(new ReplayCorpusRawOutputReader(Path.Combine(logsDir, "replay")));
builder.Services.AddSingleton<MinisterReplayRecorder>();
builder.Services.AddSingleton<MayorAgendaRules>();
builder.Services.AddSingleton(new MayorFilePaths(
    Path.Combine(logsDir, "mayor-prompt-latest.md"),
    Path.Combine(logsDir, "mayor-response.json")));
builder.Services.AddSingleton<MayorStatus>();
builder.Services.AddSingleton<SseDiagnostics>();

// ── RAG (M2) ───────────────────────────────────────────────────────────────
// Embedder isn't in DI — null-when-unconfigured doesn't compose well with the
// generic AddSingleton<TService> constraints. Each consumer builds its own via
// the same RimBobOptions resolution path.
builder.Services.AddSingleton<KnowledgeBase>();
builder.Services.AddSingleton<EmbeddingCache>(sp =>
{
    RimBobOptions opts = sp.GetRequiredService<IOptions<RimBobOptions>>().Value;
    string cacheDir = Path.IsPathRooted(opts.Rag.CacheRoot)
        ? opts.Rag.CacheRoot
        : Path.Combine(builder.Environment.ContentRootPath, opts.Rag.CacheRoot);
    return new EmbeddingCache(cacheDir);
});
builder.Services.AddSingleton<MayorRagRetriever>(sp =>
{
    RimBobOptions opts = sp.GetRequiredService<IOptions<RimBobOptions>>().Value;
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
    RimBobOptions opts = sp.GetRequiredService<IOptions<RimBobOptions>>().Value;
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
    RimBobOptions opts = sp.GetRequiredService<IOptions<RimBobOptions>>().Value;
    IEmbedder? embedder = ResolveEmbedder(opts, sp)
        ?? throw new InvalidOperationException("Ingest requires an embedder; check RimBob:Rag:Enabled and Gemini key.");
    return new Ingest(
        embedder: embedder,
        cache:    sp.GetRequiredService<EmbeddingCache>(),
        log:      sp.GetRequiredService<ILogger<Ingest>>());
});

static IEmbedder? ResolveEmbedder(RimBobOptions opts, IServiceProvider sp)
{
    if (!opts.Rag.Enabled) return null;
    IReadOnlyList<string> apiKeys = ResolveGeminiApiKeys(opts);
    if (apiKeys.Count == 0) return null;
    return new GeminiEmbedder(apiKeys, opts.Rag.EmbeddingModel,
        sp.GetRequiredService<ILogger<GeminiEmbedder>>());
}

static IReadOnlyList<string> ResolveGeminiApiKeys(RimBobOptions opts)
{
    List<string> keys = [];
    AddGeminiKeys(keys, SplitGeminiKeyList(Environment.GetEnvironmentVariable("GEMINI_API_KEY")));
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
builder.Services.AddHostedService<AgendaBootstrapHostedService>();
builder.Services.AddHostedService<DayTickOrchestrator>();

var app = builder.Build();
app.Logger.LogInformation("Host logs directory: {LogsDirectory}", logsDir);
app.Logger.LogInformation(
    "Agenda store path: {AgendaStorePath} current_version={AgendaVersion}",
    agendaStorePath,
    agendaStore.Current?.Version);

// ── Middleware ─────────────────────────────────────────────────────────────
app.UseDefaultFiles();
app.UseStaticFiles();

// ── API endpoints ──────────────────────────────────────────────────────────
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "RimBob" }));

app.MapCabinetEndpoints();
app.MapAgendaStream();
app.MapAgendaEndpoints();
app.MapAutonomyEndpoints();
app.MapColonyEndpoints();
app.MapStatusEndpoints();
app.MapMinisterEndpoints();
app.MapIconEndpoints();
app.MapAdviceApplyEndpoints();
app.MapDevBlogEndpoints();
app.MapSystemEndpoints();

// ── Startup checks ─────────────────────────────────────────────────────────
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var opts = scope.ServiceProvider.GetRequiredService<IOptions<RimBobOptions>>().Value;
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
                Console.WriteLine("✗ Gemini API key not set (env GEMINI_API_KEY, GEMINI_API_KEYS, or RimBob.GeminiApiKeys in appsettings.Local.json)");
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
                Console.WriteLine("· RAG disabled in config (RimBob:Rag:Enabled = false)");
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

Log.Information("RimBob starting — dashboard at {Url}", options.ListenUrl);

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
