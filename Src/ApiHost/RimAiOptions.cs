namespace RimAI.Host;

/// <summary>
/// Typed configuration bound from the "RimAi" section of appsettings.json
/// (and the gitignored appsettings.Local.json for local secrets).
/// </summary>
public sealed class RimAiOptions
{
    public const string SectionName = "RimAi";

    public string ListenUrl { get; init; } = "http://localhost:5000";
    public string RimApiBaseUrl { get; init; } = "http://localhost:8765/";
    public bool PingLlmOnStartup { get; init; } = true;

    /// <summary>
    /// Optional. Local-dev convenience: store the Gemini key in appsettings.Local.json
    /// (gitignored) instead of the GEMINI_API_KEY env var. Program.cs bridges this into
    /// the env var on startup so LlmClient and the rest of the system stay unchanged.
    /// Leave empty to fall back to the env var. Never put a key in appsettings.json or
    /// appsettings.Development.json — both are tracked.
    /// </summary>
    public string? GeminiApiKey { get; init; }

    public RagOptions Rag { get; init; } = new();
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
