using System.Diagnostics;
using System.Text.Json;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Logging;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;

using GuideCitation = RimAI.Core.Advice.GuideCitation;

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
        MayorBriefing           briefing,
        MayorAgenda?            previous,
        IReadOnlyList<string>   agendaDirectives,
        IReadOnlyList<GuideCitation> guideContext,
        IReadOnlyList<AgentFlag> activeFlags,
        CancellationToken       ct);

    public delegate Task<FoodLlmResponse> FoodCallExecutor(
        FoodBriefing briefing,
        MinisterBriefingContext context,
        IReadOnlyList<GuideCitation> guideContext,
        CancellationToken ct);

    private readonly IReadOnlyList<Client>                         _clients;
    private readonly Func<CancellationToken, Task<string?>>?       _pingExecutor;
    private readonly MayorCallExecutor?                            _mayorExecutor;
    private readonly FoodCallExecutor?                             _foodExecutor;
    private readonly PromptBuilder                                 _prompts;
    private readonly ILogger<LlmClient>                            _log;
    private readonly RawLlmOutputStore?                            _rawOutputs;

    public const string DefaultModel = "gemini-2.5-flash";

    public bool IsConfigured => _clients.Count > 0 || _pingExecutor is not null;
    public int ConfiguredKeyCount => _clients.Count;

    public LlmClient(string? apiKey, PromptBuilder prompts, ILogger<LlmClient> log, RawLlmOutputStore? rawOutputs = null)
        : this(SingleKey(apiKey), prompts, log, rawOutputs)
    {
    }

    public LlmClient(IReadOnlyList<string> apiKeys, PromptBuilder prompts, ILogger<LlmClient> log, RawLlmOutputStore? rawOutputs = null)
    {
        _prompts    = prompts;
        _log        = log;
        _rawOutputs = rawOutputs;
        // Constructor must not throw — Host needs to boot for /api/health
        // even when the key is absent (e.g. CI smoke tests).
        string[] normalizedKeys = apiKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _clients = normalizedKeys.Select(key => new Client(apiKey: key)).ToArray();
        _pingExecutor = null;
        _mayorExecutor = null;
        _foodExecutor = null;
    }

    // Test-only ctor: simulates the no Gemini keys configured case.
    internal LlmClient(ILogger<LlmClient> log)
    {
        _log          = log;
        _prompts      = null!;
        _clients      = [];
        _pingExecutor = null;
        _foodExecutor = null;
        _rawOutputs   = null;
    }

    // Test-only ctor: injects a ping executor so PingAsync can be exercised without Gemini.
    internal LlmClient(ILogger<LlmClient> log, Func<CancellationToken, Task<string?>> pingExecutor)
    {
        _log          = log;
        _prompts      = null!;
        _clients      = [];
        _pingExecutor = pingExecutor;
        _mayorExecutor = null;
        _foodExecutor = null;
        _rawOutputs   = null;
    }

    // Test-only ctor: injects a Mayor agenda executor so play-cycle tests don't hit Gemini.
    internal LlmClient(ILogger<LlmClient> log, MayorCallExecutor mayorExecutor)
    {
        _log           = log;
        _prompts       = null!;
        _clients       = [];
        _pingExecutor  = null;
        _mayorExecutor = mayorExecutor;
        _foodExecutor  = null;
        _rawOutputs    = null;
    }

    internal LlmClient(ILogger<LlmClient> log, FoodCallExecutor foodExecutor)
    {
        _log          = log;
        _prompts      = null!;
        _clients      = [];
        _pingExecutor = null;
        _mayorExecutor = null;
        _foodExecutor = foodExecutor;
        _rawOutputs = null;
    }

    /// <summary>
    /// M0 smoke test: one-shot "pong" round-trip to prove the SDK + key work end-to-end.
    /// Returns false (without throwing) if no API key was configured.
    /// </summary>
    public async Task<bool> PingAsync(CancellationToken ct)
    {
        if (!IsConfigured)
        {
            _log.LogWarning("Gemini ping skipped - no Gemini API keys configured");
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
                GenerateContentResult result = await GenerateContentWithFallbackAsync(
                    "ping",
                    client => client.Models.GenerateContentAsync(
                        model: DefaultModel,
                        contents: "Reply with a single word: pong",
                        cancellationToken: ct));
                reply = result.Response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
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
        MayorBriefing           briefing,
        MayorAgenda?            previousAgenda,
        IReadOnlyList<string>   agendaDirectives,
        IReadOnlyList<GuideCitation> guideContext,
        IReadOnlyList<AgentFlag>? activeFlags,
        CancellationToken       ct)
    {
        if (_mayorExecutor is not null)
            return await _mayorExecutor(briefing, previousAgenda, agendaDirectives, guideContext, activeFlags ?? [], ct);

        if (_clients.Count == 0)
            throw new InvalidOperationException("No Gemini API keys configured - cannot call Mayor LLM.");

        string userMessage = _prompts.BuildMayorUserMessage(briefing, previousAgenda, agendaDirectives, guideContext, activeFlags);
        GenerateContentConfig config = new()
        {
            SystemInstruction = new Content { Parts = [new Part { Text = _prompts.MayorSystemPrompt }] },
            ResponseMimeType  = "application/json",
            Temperature       = 0.4f
        };

        Stopwatch sw = Stopwatch.StartNew();
        GenerateContentResult result;
        try
        {
            result = await GenerateContentWithFallbackAsync(
                "Mayor",
                client => client.Models.GenerateContentAsync(
                    model:             DefaultModel,
                    contents:          userMessage,
                    config:            config,
                    cancellationToken: ct));
            sw.Stop();
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordRawOutput(
                minister: "Mayor",
                userMessage: userMessage,
                systemPrompt: _prompts.MayorSystemPrompt,
                latencyMs: sw.ElapsedMilliseconds,
                status: "request_failed",
                parseMode: "not_applicable",
                apiKeyIndex: null,
                text: FormatRequestFailure(ex));
            throw;
        }

        string? text = result.Response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Gemini returned empty response for Mayor agenda call.");

        MayorAgendaInput? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<MayorAgendaInput>(text, ResponseJson);
        }
        catch (JsonException ex)
        {
            RecordRawOutput(
                minister: "Mayor",
                userMessage: userMessage,
                systemPrompt: _prompts.MayorSystemPrompt,
                latencyMs: sw.ElapsedMilliseconds,
                status: "parse_failed",
                parseMode: "strict_json",
                apiKeyIndex: result.ApiKeyIndex,
                text: text);
            _log.LogError(ex, "Failed to parse Mayor agenda response as JSON. Raw text:\n{Text}", text);
            throw;
        }

        if (parsed is null)
            throw new InvalidOperationException("Gemini agenda response deserialized to null.");

        string preview = parsed.UpdateNotes.Length > 200 ? parsed.UpdateNotes[..200] + "…" : parsed.UpdateNotes;
        RecordRawOutput(
            minister: "Mayor",
            userMessage: userMessage,
            systemPrompt: _prompts.MayorSystemPrompt,
            latencyMs: sw.ElapsedMilliseconds,
            status: "parsed",
            parseMode: "strict_json",
            apiKeyIndex: result.ApiKeyIndex,
            text: text);
        _log.LogInformation(
            "Mayor LLM call complete: latency={LatencyMs}ms short_term={ShortCount} long_term={LongCount} update_notes=\"{Preview}\"",
            sw.ElapsedMilliseconds, parsed.ShortTerm.Count, parsed.LongTerm.Count, preview);

        return parsed;
    }

    public async Task<FoodLlmResponse> CallFoodAsync(
        FoodBriefing briefing,
        MinisterBriefingContext context,
        IReadOnlyList<GuideCitation> guideContext,
        CancellationToken ct)
    {
        if (_foodExecutor is not null)
            return await _foodExecutor(briefing, context, guideContext, ct);

        if (_clients.Count == 0)
            throw new InvalidOperationException("No Gemini API keys configured - cannot call Food LLM.");

        string userMessage = _prompts.BuildFoodUserMessage(briefing, context, guideContext);
        GenerateContentConfig config = new()
        {
            SystemInstruction = new Content { Parts = [new Part { Text = _prompts.FoodSystemPrompt }] },
            ResponseMimeType  = "application/json",
            Temperature       = 0.3f
        };

        Stopwatch sw = Stopwatch.StartNew();
        GenerateContentResult result;
        try
        {
            result = await GenerateContentWithFallbackAsync(
                "Food",
                client => client.Models.GenerateContentAsync(
                    model:             DefaultModel,
                    contents:          userMessage,
                    config:            config,
                    cancellationToken: ct));
            sw.Stop();
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordRawOutput(
                minister: "Food",
                userMessage: userMessage,
                systemPrompt: _prompts.FoodSystemPrompt,
                latencyMs: sw.ElapsedMilliseconds,
                status: "request_failed",
                parseMode: "not_applicable",
                apiKeyIndex: null,
                text: FormatRequestFailure(ex));
            throw;
        }

        string? text = result.Response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Gemini returned empty response for Food call.");

        FoodLlmResponse parsed;
        string parseMode = "strict_json";
        bool normalized = false;
        try
        {
            FoodLlmParseResult parseResult = FoodLlmResponseParser.Parse(text, briefing, guideContext);
            parsed = parseResult.Response;
            parseMode = parseResult.ParseMode;
            normalized = parseResult.Normalized;
        }
        catch (JsonException parseEx)
        {
            RecordRawOutput(
                minister: "Food",
                userMessage: userMessage,
                systemPrompt: _prompts.FoodSystemPrompt,
                latencyMs: sw.ElapsedMilliseconds,
                status: "parse_failed",
                parseMode: parseMode,
                apiKeyIndex: result.ApiKeyIndex,
                text: text);
            _log.LogError(parseEx, "Failed to parse Food response as JSON. Raw text:\n{Text}", text);
            throw;
        }

        RecordRawOutput(
            minister: "Food",
            userMessage: userMessage,
            systemPrompt: _prompts.FoodSystemPrompt,
            latencyMs: sw.ElapsedMilliseconds,
            status: normalized ? "normalized" : "parsed",
            parseMode: parseMode,
            apiKeyIndex: result.ApiKeyIndex,
            text: text);
        _log.LogInformation(
            "Food LLM call complete: latency={LatencyMs}ms advice={AdviceCount} flags={FlagCount}",
            sw.ElapsedMilliseconds, parsed.Advice.Count, parsed.Flags.Count);
        if (normalized)
            _log.LogWarning(
                "Food response normalized from non-strict schema: advice={AdviceCount} flags={FlagCount}",
                parsed.Advice.Count, parsed.Flags.Count);

        return parsed;
    }

    private void RecordRawOutput(
        string minister,
        string userMessage,
        string systemPrompt,
        long latencyMs,
        string status,
        string parseMode,
        int? apiKeyIndex,
        string text)
    {
        _rawOutputs?.Record(new RawLlmOutputSnapshot(
            Minister: minister,
            Provider: "Gemini",
            Model: DefaultModel,
            ApiKeyIndex: apiKeyIndex,
            ApiKeyLabel: ApiKeyLabel(apiKeyIndex),
            CapturedAt: DateTimeOffset.UtcNow,
            LatencyMs: latencyMs,
            Status: status,
            ParseMode: parseMode,
            SystemPromptChars: systemPrompt.Length,
            UserPromptChars: userMessage.Length,
            Text: text));
    }

    private static string FormatRequestFailure(Exception ex) =>
        $"No model response was returned.\n{ex.GetType().Name}: {ex.Message}";

    private async Task<GenerateContentResult> GenerateContentWithFallbackAsync(
        string operation,
        Func<Client, Task<GenerateContentResponse>> call)
    {
        for (int i = 0; i < _clients.Count; i++)
        {
            try
            {
                GenerateContentResponse response = await call(_clients[i]);
                return new GenerateContentResult(response, i + 1);
            }
            catch (Exception ex) when (i + 1 < _clients.Count && IsApiKeyFallbackCandidate(ex))
            {
                _log.LogWarning(
                    ex,
                    "Gemini {Operation} failed with quota/key error on configured key {KeyIndex}; trying next configured key.",
                    operation,
                    i + 1);
            }
        }

        throw new InvalidOperationException("No Gemini API keys configured.");
    }

    private static bool IsApiKeyFallbackCandidate(Exception ex)
    {
        string text = ex.ToString();
        return text.Contains("quota", StringComparison.OrdinalIgnoreCase)
               || text.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
               || text.Contains("rate-limits", StringComparison.OrdinalIgnoreCase)
               || text.Contains("resource_exhausted", StringComparison.OrdinalIgnoreCase)
               || text.Contains("permission_denied", StringComparison.OrdinalIgnoreCase)
               || text.Contains("api key not valid", StringComparison.OrdinalIgnoreCase)
               || text.Contains("exceeded", StringComparison.OrdinalIgnoreCase)
               || text.Contains("429", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> SingleKey(string? apiKey) =>
        string.IsNullOrWhiteSpace(apiKey) ? [] : [apiKey];

    private static string? ApiKeyLabel(int? apiKeyIndex) =>
        apiKeyIndex switch
        {
            null => null,
            1 => "primary",
            _ => $"fallback_{apiKeyIndex.Value - 1}"
        };

    private sealed record GenerateContentResult(GenerateContentResponse Response, int ApiKeyIndex);
}
