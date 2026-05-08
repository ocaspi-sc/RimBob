using System.Diagnostics;
using System.Text.Json;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Logging;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;

namespace RimAI.LLM;

/// <summary>
/// Google GenAI SDK wrapper (Gemini Developer API / Google AI Studio).
/// Injected per-minister by DI; calls are made only on escalation.
/// Package: Google.GenAI 1.6.1 — see RimAI.LLM.csproj.
/// </summary>
public sealed class LlmClient
{
    private static readonly JsonSerializerOptions ResponseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public delegate Task<MayorAgendaInput> MayorCallExecutor(
        MayorBriefing briefing, MayorAgenda? previous,
        IReadOnlyList<string> lensPrefills, CancellationToken ct);

    private readonly Client?                                       _client;
    private readonly Func<CancellationToken, Task<string?>>?       _pingExecutor;
    private readonly MayorCallExecutor?                            _mayorExecutor;
    private readonly PromptBuilder                                 _prompts;
    private readonly ILogger<LlmClient>                            _log;

    public const string DefaultModel = "gemini-2.5-flash";

    public bool IsConfigured => _client is not null || _pingExecutor is not null;

    public LlmClient(string? apiKey, PromptBuilder prompts, ILogger<LlmClient> log)
    {
        _prompts = prompts;
        _log     = log;
        // Constructor must not throw — Host needs to boot for /api/health
        // even when the key is absent (e.g. CI smoke tests).
        _client = string.IsNullOrWhiteSpace(apiKey) ? null : new Client(apiKey: apiKey);
    }

    // Test-only ctor: simulates the "no GEMINI_API_KEY set" case.
    internal LlmClient(ILogger<LlmClient> log)
    {
        _log          = log;
        _prompts      = null!;
        _client       = null;
        _pingExecutor = null;
    }

    // Test-only ctor: injects a ping executor so PingAsync can be exercised without Gemini.
    internal LlmClient(ILogger<LlmClient> log, Func<CancellationToken, Task<string?>> pingExecutor)
    {
        _log          = log;
        _prompts      = null!;
        _client       = null;
        _pingExecutor = pingExecutor;
    }

    // Test-only ctor: injects a Mayor agenda executor so play-cycle tests don't hit Gemini.
    internal LlmClient(ILogger<LlmClient> log, MayorCallExecutor mayorExecutor)
    {
        _log           = log;
        _prompts       = null!;
        _client        = null;
        _mayorExecutor = mayorExecutor;
    }

    /// <summary>
    /// M0 smoke test: one-shot "pong" round-trip to prove the SDK + key work end-to-end.
    /// Returns false (without throwing) if no API key was configured.
    /// </summary>
    public async Task<bool> PingAsync(CancellationToken ct)
    {
        if (!IsConfigured)
        {
            _log.LogWarning("Gemini ping skipped — GEMINI_API_KEY not set");
            return false;
        }

        try
        {
            string? reply;
            if (_pingExecutor is not null)
            {
                reply = await _pingExecutor(ct);
            }
            else
            {
                Google.GenAI.Types.GenerateContentResponse response = await _client!.Models.GenerateContentAsync(
                    model: DefaultModel,
                    contents: "Reply with a single word: pong",
                    cancellationToken: ct);
                reply = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            }

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

    /// <summary>
    /// Calls Gemini to produce a new MayorAgendaInput from the current briefing and
    /// (optionally) the previous agenda. Forces JSON output via responseMimeType.
    /// Throws if the API key is missing or the response can't be parsed.
    /// </summary>
    public async Task<MayorAgendaInput> CallMayorAsync(
        MayorBriefing         briefing,
        MayorAgenda?          previousAgenda,
        IReadOnlyList<string> lensPrefills,
        CancellationToken     ct)
    {
        if (_mayorExecutor is not null)
            return await _mayorExecutor(briefing, previousAgenda, lensPrefills, ct);

        if (_client is null)
            throw new InvalidOperationException("GEMINI_API_KEY not set — cannot call Mayor LLM.");

        string userMessage = _prompts.BuildMayorUserMessage(briefing, previousAgenda, lensPrefills);
        GenerateContentConfig config = new()
        {
            SystemInstruction = new Content { Parts = [new Part { Text = _prompts.MayorSystemPrompt }] },
            ResponseMimeType  = "application/json",
            Temperature       = 0.4f
        };

        Stopwatch sw = Stopwatch.StartNew();
        Google.GenAI.Types.GenerateContentResponse response = await _client.Models.GenerateContentAsync(
            model:             DefaultModel,
            contents:          userMessage,
            config:            config,
            cancellationToken: ct);
        sw.Stop();

        string? text = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Gemini returned empty response for Mayor agenda call.");

        MayorAgendaInput? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<MayorAgendaInput>(text, ResponseJson);
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Failed to parse Mayor agenda response as JSON. Raw text:\n{Text}", text);
            throw;
        }

        if (parsed is null)
            throw new InvalidOperationException("Gemini agenda response deserialized to null.");

        string preview = parsed.UpdateNotes.Length > 200 ? parsed.UpdateNotes[..200] + "…" : parsed.UpdateNotes;
        _log.LogInformation(
            "Mayor LLM call complete: latency={LatencyMs}ms short_term={ShortCount} long_term={LongCount} update_notes=\"{Preview}\"",
            sw.ElapsedMilliseconds, parsed.ShortTerm.Count, parsed.LongTerm.Count, preview);

        return parsed;
    }
}
