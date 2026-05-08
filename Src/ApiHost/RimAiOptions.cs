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
}
