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
    private readonly Func<CancellationToken, Task<string?>>? _pingExecutor;
    private readonly ILogger<LlmClient> _log;

    public const string DefaultModel = "gemini-2.5-flash";

    public bool IsConfigured => _pingExecutor is not null;

    public LlmClient(ILogger<LlmClient> log)
    {
        _log = log;
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            // Constructor must not throw — Host needs to boot for /api/health
            // even when the key is absent (e.g. CI smoke tests).
            _pingExecutor = null;
            return;
        }

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
    /// Returns false (without throwing) if no API key was configured.
    /// </summary>
    public async Task<bool> PingAsync(CancellationToken ct)
    {
        if (_pingExecutor is null)
        {
            _log.LogWarning("Gemini ping skipped — GEMINI_API_KEY not set");
            return false;
        }

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
