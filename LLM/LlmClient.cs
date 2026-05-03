using Google.GenAI;
using Microsoft.Extensions.Logging;

namespace RimAI.LLM;

/// <summary>
/// Google GenAI SDK wrapper (Gemini Developer API / Google AI Studio).
/// Injected per-minister by DI; calls are made only on escalation.
/// Package: Google.GenAI 1.6.1 — see RimAI.LLM.csproj.
/// </summary>
public sealed class LlmClient
{
    private readonly Func<CancellationToken, Task<string?>> _pingExecutor;
    private readonly ILogger<LlmClient> _log;

    public const string DefaultModel = "gemini-2.5-flash";

    public LlmClient(ILogger<LlmClient> log)
    {
        _log = log;
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? throw new InvalidOperationException("GEMINI_API_KEY not set");
        var client = new Client(apiKey: apiKey);
        _pingExecutor = async ct =>
        {
            var response = await client.Models.GenerateContentAsync(
                model: DefaultModel,
                contents: "Reply with a single word: pong",
                cancellationToken: ct);
            return response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        };
    }

    internal LlmClient(ILogger<LlmClient> log, Func<CancellationToken, Task<string?>> pingExecutor)
    {
        _log = log;
        _pingExecutor = pingExecutor;
    }

    /// <summary>
    /// M0 smoke test: one-shot "pong" round-trip to prove the SDK + key work end-to-end.
    /// </summary>
    public async Task<bool> PingAsync(CancellationToken ct)
    {
        try
        {
            var reply = await _pingExecutor(ct);
            if (string.IsNullOrWhiteSpace(reply))
            {
                _log.LogWarning("Gemini ping returned empty or whitespace reply");
                return false;
            }

            _log.LogInformation("Gemini ping OK: {Reply}", reply);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Gemini ping failed");
            return false;
        }
    }

    // TODO M1: Task<AdviceItem> StreamCompletionAsync(string systemPrompt, string userMessage, CancellationToken ct)
}
