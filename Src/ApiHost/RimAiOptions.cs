namespace RimAI.Host;

/// <summary>
/// Typed configuration bound from the "RimAi" section of appsettings.json.
/// Secrets (GEMINI_API_KEY) stay in env vars — never bind them here.
/// </summary>
public sealed class RimAiOptions
{
    public const string SectionName = "RimAi";

    public string ListenUrl { get; init; } = "http://localhost:5000";
    public string RimApiBaseUrl { get; init; } = "http://localhost:8765/";
    public bool PingLlmOnStartup { get; init; } = true;
}
