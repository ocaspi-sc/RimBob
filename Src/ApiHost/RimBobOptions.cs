namespace RimBob.Host;

/// <summary>
/// Typed configuration bound from the "RimBob" section of appsettings.json
/// (and the gitignored appsettings.Local.json for local secrets).
/// </summary>
public sealed class RimBobOptions
{
    public const string SectionName = "RimBob";

    public string ListenUrl { get; init; } = "http://localhost:5000";
    public string RimApiBaseUrl { get; init; } = "http://localhost:8765/";
    public bool PingLlmOnStartup { get; init; } = true;
    public string? LogsRoot { get; init; }
    public string? IconCacheRoot { get; init; }

    /// <summary>
    /// Optional ordered Gemini keys for quota/key failures. Local dev can place
    /// these in appsettings.Local.json; env var GEMINI_API_KEYS can provide a
    /// semicolon/comma/newline separated list. Env var GEMINI_API_KEY can provide
    /// one key. Never put keys in appsettings.json or appsettings.Development.json
    /// - both are tracked.
    /// </summary>
    public string[] GeminiApiKeys { get; init; } = [];

    public RagOptions Rag { get; init; } = new();

    public IconWarmOptions IconWarm { get; init; } = new();
}

/// <summary>
/// RAG / KnowledgeBase configuration. M2 ships in-process cosine retrieval over
/// markdown guides under <see cref="GuidesRoot"/>.
/// </summary>
public sealed class RagOptions
{
    public bool   Enabled        { get; init; } = true;
    public int    TopK           { get; init; } = 3;
    public string EmbeddingModel { get; init; } = "gemini-embedding-001";

    /// <summary>Path (relative to ContentRoot) where guide markdown lives.</summary>
    public string GuidesRoot { get; init; } = "Docs/guides";

    /// <summary>Path (relative to ContentRoot) for the on-disk embedding cache.</summary>
    public string CacheRoot { get; init; } = "var/embeddings";
}
