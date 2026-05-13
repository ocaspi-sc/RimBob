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

    private readonly Client?                                       _client;
    private readonly Func<CancellationToken, Task<string?>>?       _pingExecutor;
    private readonly MayorCallExecutor?                            _mayorExecutor;
    private readonly FoodCallExecutor?                             _foodExecutor;
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
        _pingExecutor = null;
        _mayorExecutor = null;
        _foodExecutor = null;
    }

    // Test-only ctor: simulates the "no GEMINI_API_KEY set" case.
    internal LlmClient(ILogger<LlmClient> log)
    {
        _log          = log;
        _prompts      = null!;
        _client       = null;
        _pingExecutor = null;
        _foodExecutor = null;
    }

    // Test-only ctor: injects a ping executor so PingAsync can be exercised without Gemini.
    internal LlmClient(ILogger<LlmClient> log, Func<CancellationToken, Task<string?>> pingExecutor)
    {
        _log          = log;
        _prompts      = null!;
        _client       = null;
        _pingExecutor = pingExecutor;
        _mayorExecutor = null;
        _foodExecutor = null;
    }

    // Test-only ctor: injects a Mayor agenda executor so play-cycle tests don't hit Gemini.
    internal LlmClient(ILogger<LlmClient> log, MayorCallExecutor mayorExecutor)
    {
        _log           = log;
        _prompts       = null!;
        _client        = null;
        _pingExecutor  = null;
        _mayorExecutor = mayorExecutor;
        _foodExecutor  = null;
    }

    internal LlmClient(ILogger<LlmClient> log, FoodCallExecutor foodExecutor)
    {
        _log          = log;
        _prompts      = null!;
        _client       = null;
        _pingExecutor = null;
        _mayorExecutor = null;
        _foodExecutor = foodExecutor;
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
        MayorBriefing           briefing,
        MayorAgenda?            previousAgenda,
        IReadOnlyList<string>   agendaDirectives,
        IReadOnlyList<GuideCitation> guideContext,
        IReadOnlyList<AgentFlag>? activeFlags,
        CancellationToken       ct)
    {
        if (_mayorExecutor is not null)
            return await _mayorExecutor(briefing, previousAgenda, agendaDirectives, guideContext, activeFlags ?? [], ct);

        if (_client is null)
            throw new InvalidOperationException("GEMINI_API_KEY not set — cannot call Mayor LLM.");

        string userMessage = _prompts.BuildMayorUserMessage(briefing, previousAgenda, agendaDirectives, guideContext, activeFlags);
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

    public async Task<FoodLlmResponse> CallFoodAsync(
        FoodBriefing briefing,
        MinisterBriefingContext context,
        IReadOnlyList<GuideCitation> guideContext,
        CancellationToken ct)
    {
        if (_foodExecutor is not null)
            return await _foodExecutor(briefing, context, guideContext, ct);

        if (_client is null)
            throw new InvalidOperationException("GEMINI_API_KEY not set - cannot call Food LLM.");

        string userMessage = _prompts.BuildFoodUserMessage(briefing, context, guideContext);
        GenerateContentConfig config = new()
        {
            SystemInstruction = new Content { Parts = [new Part { Text = _prompts.FoodSystemPrompt }] },
            ResponseMimeType  = "application/json",
            Temperature       = 0.3f
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
            throw new InvalidOperationException("Gemini returned empty response for Food call.");

        bool normalized = false;
        FoodLlmResponse parsed;
        try
        {
            LlmAdviceNormalizationContext normalizeContext = new(
                Minister: "Food",
                Domain: "food",
                BriefingVersion: briefing.BriefingVersion,
                GameTick: briefing.GameTick,
                Date: briefing.Date,
                DefaultAdviceType: nameof(FoodAdviceType.FoodSecurity),
                DefaultRationale: "Food LLM escalation selected this recommendation.",
                GuideContext: guideContext);

            parsed = LlmResponseParser.ParseOrNormalize(
                text,
                ResponseJson,
                root =>
                {
                    NormalizedAdviceResponse normalizedResponse =
                        LlmAdviceResponseNormalizer.Normalize(root, normalizeContext, ResponseJson);
                    return new FoodLlmResponse(
                        normalizedResponse.Advice,
                        normalizedResponse.Flags,
                        normalizedResponse.Notes);
                },
                ex =>
                {
                    normalized = true;
                    _log.LogWarning(ex, "Food response did not match strict schema; attempting tolerant normalization.");
                });
        }
        catch (JsonException parseEx)
        {
            _log.LogError(parseEx, "Failed to parse Food response as JSON. Raw text:\n{Text}", text);
            throw;
        }

        _log.LogInformation(
            "Food LLM call complete: latency={LatencyMs}ms advice={AdviceCount} flags={FlagCount}",
            sw.ElapsedMilliseconds, parsed.Advice.Count, parsed.Flags.Count);
        if (normalized)
            _log.LogWarning(
                "Food response normalized from non-strict schema: advice={AdviceCount} flags={FlagCount}",
                parsed.Advice.Count, parsed.Flags.Count);

        return parsed;
    }

}
