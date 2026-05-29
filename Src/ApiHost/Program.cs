using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Formatting.Json;
using RimBob.Coordination;
using RimBob.Core.Ministers;
using RimBob.Core.Placement;
using RimBob.Host;
using RimBob.Host.Endpoints;
using RimBob.Ingestion;
using RimBob.Knowledge;
using RimBob.LLM;
using RimBob.Ministers.Willie;
using RimBob.Ministers.Mayor;
using RimBob.State;
using Chef = RimBob.Ministers.Food.Chef;
using FoodRules = RimBob.Ministers.Food.Rules;
using MinisterOfWillie = RimBob.Ministers.Willie.MinisterOfWillie;
using WillieRules = RimBob.Ministers.Willie.Rules;

HostProcessCrashGuard.Configure();

int exitCode = 0;

try
{
    var builder = WebApplication.CreateBuilder(args);

    // appsettings.Local.json is gitignored; safe place for the Gemini key in dev.
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

    RimBobOptions options = builder.Configuration
        .GetSection(RimBobOptions.SectionName)
        .Get<RimBobOptions>() ?? new RimBobOptions();

    string logsDir = HostLogPaths.ResolveLogsDirectory(builder.Environment.ContentRootPath, options.LogsRoot);
    string dataRoot = HostLogPaths.ResolveDataRootDirectory(builder.Environment.ContentRootPath, options.DataRoot);
    Directory.CreateDirectory(logsDir);
    Log.Logger = ConfigureSerilog(new LoggerConfiguration(), builder.Configuration, logsDir)
        .CreateBootstrapLogger();
    Directory.CreateDirectory(dataRoot);
    string ministerOutputRoot = Path.Combine(dataRoot, "ministers");
    MinisterOutputStore ministerOutputStore = await MinisterOutputStore.LoadAsync(ministerOutputRoot);
    string colonyStateSnapshotPath = Path.Combine(dataRoot, "state", "latest-colony-state.json");
    ColonyStateSnapshotStore colonySnapshotStore = await ColonyStateSnapshotStore.LoadAsync(colonyStateSnapshotPath);

    // ── Logging: Serilog reads from appsettings.json ───────────────────────────
    builder.Host.UseSerilog((ctx, services, cfg) =>
        ConfigureSerilog(cfg, ctx.Configuration, logsDir)
            .ReadFrom.Services(services));

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
    builder.Services.AddSingleton<RimApiPlacementProbe>();
    builder.Services.AddSingleton<IPlacementValidator>(sp => sp.GetRequiredService<RimApiPlacementProbe>());
    builder.Services.AddSingleton<IPathCostProbe>(sp => sp.GetRequiredService<RimApiPlacementProbe>());
    builder.Services.AddSingleton<IReadOnlyList<IPlacementGenerator>>(_ =>
        [new TemplateAnchoredGenerator(), new LargestEmptyRectangleGenerator()]);
    builder.Services.AddSingleton<IPlacementScorer, WalkablePathCostScorer>();
    builder.Services.AddSingleton<PlacementSolver>();
    builder.Services.AddSingleton<IPlacementSolver>(sp => sp.GetRequiredService<PlacementSolver>());
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
    builder.Services.AddSingleton<PromptInspectorCache>();
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
    builder.Services.AddSingleton(colonySnapshotStore);
    builder.Services.AddSingleton<BriefingCache>();
    builder.Services.AddSingleton<IngestionDispatcher>();

    builder.Services.AddSingleton(ministerOutputStore);
    builder.Services.AddSingleton<AdviceBus>(sp => new AdviceBus(sp.GetRequiredService<MinisterOutputStore>()));
    builder.Services.AddSingleton<FlagChannel>();
    builder.Services.AddSingleton<MinisterRegistry>();
    builder.Services.AddSingleton<EndpointCoverageCatalog>();
    builder.Services.AddSingleton(new HostRuntimeIdentity(builder.Environment.ContentRootPath));
    builder.Services.AddSingleton<MinisterTraceStore>();
    builder.Services.AddSingleton<AssistedApplyService>();
    builder.Services.AddTransient<RimApiRuntimeProbe>();
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
        string cacheDir = HostLogPaths.ResolveDataDirectory(
            builder.Environment.ContentRootPath,
            opts.DataRoot,
            opts.Rag.CacheRoot,
            "embeddings");
        return new EmbeddingCache(cacheDir);
    });
    builder.Services.AddSingleton<MayorRagRetriever>(sp =>
    {
        RimBobOptions opts = sp.GetRequiredService<IOptions<RimBobOptions>>().Value;
        IEmbedder? embedder = ResolveEmbedder(opts, sp);
        return new MayorRagRetriever(
            kb: sp.GetRequiredService<KnowledgeBase>(),
            embedder: embedder,
            enabled: opts.Rag.Enabled,
            topK: opts.Rag.TopK,
            log: sp.GetRequiredService<ILogger<MayorRagRetriever>>());
    });
    builder.Services.AddSingleton<FoodRagRetriever>(sp =>
    {
        RimBobOptions opts = sp.GetRequiredService<IOptions<RimBobOptions>>().Value;
        IEmbedder? embedder = ResolveEmbedder(opts, sp);
        return new FoodRagRetriever(
            kb: sp.GetRequiredService<KnowledgeBase>(),
            embedder: embedder,
            enabled: opts.Rag.Enabled,
            topK: opts.Rag.TopK,
            log: sp.GetRequiredService<ILogger<FoodRagRetriever>>());
    });
    builder.Services.AddSingleton<Ingest>(sp =>
    {
        RimBobOptions opts = sp.GetRequiredService<IOptions<RimBobOptions>>().Value;
        IEmbedder? embedder = ResolveEmbedder(opts, sp)
            ?? throw new InvalidOperationException("Ingest requires an embedder; check RimBob:Rag:Enabled and Gemini key.");
        return new Ingest(
            embedder: embedder,
            cache: sp.GetRequiredService<EmbeddingCache>(),
            log: sp.GetRequiredService<ILogger<Ingest>>());
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

    static LoggerConfiguration ConfigureSerilog(LoggerConfiguration cfg, IConfiguration configuration, string logsDir)
    {
        return cfg.ReadFrom.Configuration(configuration)
            .WriteTo.File(
                path: Path.Combine(logsDir, "rimbob-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                formatter: new JsonFormatter(),
                path: Path.Combine(logsDir, "decisions-.jsonl"),
                rollingInterval: RollingInterval.Day);
    }

    builder.Services.AddSingleton<Mayor>();
    builder.Services.AddSingleton<FoodRules>();
    builder.Services.AddSingleton<WillieRules>();
    builder.Services.AddSingleton<Chef>();
    builder.Services.AddSingleton<MinisterOfWillie>();
    builder.Services.AddSingleton<IMinister>(sp => sp.GetRequiredService<Mayor>());
    builder.Services.AddSingleton<IMinister>(sp => sp.GetRequiredService<Chef>());
    builder.Services.AddSingleton<IMinister>(sp => sp.GetRequiredService<MinisterOfWillie>());
    builder.Services.AddSingleton<CabinetCycle>();
    builder.Services.AddHostedService<ColonySnapshotRestoreHostedService>();
    builder.Services.AddHostedService<AgendaBootstrapHostedService>();
    builder.Services.AddHostedService<DayTickOrchestrator>();

    var app = builder.Build();
    app.Logger.LogInformation("Host logs directory: {LogsDirectory}", logsDir);
    app.Logger.LogInformation(
        "Minister output root: {MinisterOutputRoot} mayor_snapshot_version={Version}",
        ministerOutputRoot,
        ministerOutputStore.CurrentMayorAgenda?.Version);
    ColonySnapshotStatus colonySnapshotStatus = colonySnapshotStore.GetStatus();
    app.Logger.LogInformation(
        "Colony state snapshot path: {SnapshotPath} has_snapshot={HasSnapshot} load_error={LoadError}",
        colonyStateSnapshotPath,
        colonySnapshotStatus.HasSnapshot,
        colonySnapshotStatus.LoadError);

    // ── Middleware ─────────────────────────────────────────────────────────────
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ApplyDashboardStaticFileCacheHeaders
    });

    // ── API endpoints ──────────────────────────────────────────────────────────
    app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "RimBob" }));

    app.MapCabinetEndpoints();
    app.MapAgendaStream();
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
                        Ingest ingest = scope.ServiceProvider.GetRequiredService<Ingest>();
                        KnowledgeBase kb = scope.ServiceProvider.GetRequiredService<KnowledgeBase>();
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

    await app.RunAsync();
}
catch (Exception ex)
{
    exitCode = 1;
    Log.Fatal(ex, "Unhandled exception during startup/run");
    Console.Error.WriteLine($"FATAL: {ex.Message} (see logs)");
}
finally
{
    Log.CloseAndFlush();
}

static void ApplyDashboardStaticFileCacheHeaders(StaticFileResponseContext context)
{
    string fileName = context.File.Name;
    string requestPath = context.Context.Request.Path.Value ?? string.Empty;

    if (fileName.Equals("index.html", StringComparison.OrdinalIgnoreCase))
    {
        // WHY: Vite emits hashed asset filenames; stale HTML can point browsers at deleted bundles.
        context.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        context.Context.Response.Headers["Pragma"] = "no-cache";
        context.Context.Response.Headers["Expires"] = "0";
        return;
    }

    if (requestPath.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
    {
        context.Context.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
    }
}

return exitCode;

internal static class HostProcessCrashGuard
{
    private const uint SemFailCriticalErrors = 0x0001;
    private const uint SemNoGpFaultErrorBox = 0x0002;

    internal static void Configure()
    {
        if (OperatingSystem.IsWindows())
        {
            _ = SetErrorMode(SemFailCriticalErrors | SemNoGpFaultErrorBox);
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "AppDomain unhandled exception");
                WriteFatalToConsole(ex.Message);
            }
            else
            {
                Log.Fatal("AppDomain unhandled exception: {ExceptionObject}", e.ExceptionObject);
                WriteFatalToConsole("AppDomain unhandled exception");
            }

            Log.CloseAndFlush();
            Environment.Exit(1);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    private static void WriteFatalToConsole(string message)
    {
        Console.Error.WriteLine($"FATAL: {message} (see logs)");
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint uMode);
}
