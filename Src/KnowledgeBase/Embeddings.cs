using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Logging;

namespace RimAI.Knowledge;

/// <summary>
/// Wraps Gemini's embedding endpoint. One call per chunk, sequential: the guide
/// corpus is small and ingestion runs once at startup.
/// </summary>
public interface IEmbedder
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct);
}

public sealed class GeminiEmbedder : IEmbedder
{
    public const string DefaultModel = "gemini-embedding-001";

    private readonly IReadOnlyList<Client>  _clients;
    private readonly string                 _model;
    private readonly ILogger<GeminiEmbedder> _log;

    public GeminiEmbedder(string apiKey, string model, ILogger<GeminiEmbedder> log)
        : this([apiKey], model, log)
    {
    }

    public GeminiEmbedder(IReadOnlyList<string> apiKeys, string model, ILogger<GeminiEmbedder> log)
    {
        string[] normalizedKeys = apiKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedKeys.Length == 0)
            throw new ArgumentException("Gemini API key required to embed.", nameof(apiKeys));
        _clients = normalizedKeys.Select(key => new Client(apiKey: key)).ToArray();
        _model  = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        _log    = log;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        EmbedContentResponse response = await EmbedContentWithFallbackAsync(text, ct);

        ContentEmbedding? first = response.Embeddings?.FirstOrDefault();
        IReadOnlyList<double>? values = first?.Values;
        if (values is null || values.Count == 0)
        {
            _log.LogError("Gemini embedding returned empty values for {Bytes}-byte input.", text.Length);
            throw new InvalidOperationException("Gemini embedding response had no values.");
        }
        float[] result = new float[values.Count];
        for (int i = 0; i < values.Count; i++) result[i] = (float)values[i];
        return result;
    }

    private async Task<EmbedContentResponse> EmbedContentWithFallbackAsync(string text, CancellationToken ct)
    {
        for (int i = 0; i < _clients.Count; i++)
        {
            try
            {
                return await _clients[i].Models.EmbedContentAsync(
                    model: _model,
                    contents: text,
                    cancellationToken: ct);
            }
            catch (Exception ex) when (i + 1 < _clients.Count && IsApiKeyFallbackCandidate(ex))
            {
                _log.LogWarning(
                    ex,
                    "Gemini embedding failed with quota/key error on configured key {KeyIndex}; trying next configured key.",
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
}
