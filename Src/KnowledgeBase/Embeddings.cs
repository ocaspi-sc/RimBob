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

    private readonly Client                 _client;
    private readonly string                 _model;
    private readonly ILogger<GeminiEmbedder> _log;

    public GeminiEmbedder(string apiKey, string model, ILogger<GeminiEmbedder> log)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Gemini API key required to embed.", nameof(apiKey));
        _client = new Client(apiKey: apiKey);
        _model  = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        _log    = log;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        EmbedContentResponse response = await _client.Models.EmbedContentAsync(
            model: _model,
            contents: text,
            cancellationToken: ct);

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
}
