using Microsoft.Extensions.Logging;
using RimBob.Core.Advice;

namespace RimBob.Knowledge;

public interface IRagQueryBuilder<in TBriefing>
{
    string BuildQuery(TBriefing briefing, IReadOnlyList<string> directives);
}

public sealed class RagRetriever<TBriefing>
{
    private readonly KnowledgeBase _kb;
    private readonly IEmbedder? _embedder;
    private readonly bool _enabled;
    private readonly RetrievalProfile _profile;
    private readonly IRagQueryBuilder<TBriefing> _queryBuilder;
    private readonly ILogger _log;

    public RagRetriever(
        KnowledgeBase kb,
        IEmbedder? embedder,
        bool enabled,
        RetrievalProfile profile,
        IRagQueryBuilder<TBriefing> queryBuilder,
        ILogger log)
    {
        _kb = kb;
        _embedder = embedder;
        _enabled = enabled && embedder is not null;
        _profile = profile;
        _queryBuilder = queryBuilder;
        _log = log;
    }

    public bool Enabled => _enabled;

    public async Task<IReadOnlyList<GuideCitation>> RetrieveAsync(
        TBriefing briefing,
        IReadOnlyList<string> directives,
        CancellationToken ct)
    {
        if (!_enabled || _kb.Count == 0 || _embedder is null) return [];

        string query = _queryBuilder.BuildQuery(briefing, directives);
        float[] queryEmbedding;
        try
        {
            queryEmbedding = await _embedder.EmbedAsync(query, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "{Minister} retrieval embedding failed; proceeding without RAG this turn.", _profile.Name);
            return [];
        }

        IReadOnlyList<Chunk> hits = _kb.Retrieve(queryEmbedding, _profile.TopK);
        List<GuideCitation> citations = new(hits.Count);
        for (int i = 0; i < hits.Count; i++)
        {
            Chunk chunk = hits[i];
            citations.Add(new GuideCitation(
                CiteId: $"{_profile.CitationPrefix}{i + 1}",
                SourcePath: chunk.Meta.SourcePath,
                Heading: chunk.Meta.SectionHeading,
                Snippet: Truncate(chunk.Text, _profile.SnippetMaxChars, _profile.SnippetSuffix)));
        }

        _log.LogInformation(
            "{Minister} RAG: retrieved {Count} chunks (topK={TopK}, query={QueryChars} chars)",
            _profile.Name, citations.Count, _profile.TopK, query.Length);

        return citations;
    }

    private static string Truncate(string text, int max, string suffix)
    {
        if (text.Length <= max) return text;
        int cut = text.LastIndexOf(' ', Math.Min(max, text.Length - 1));
        if (cut < max / 2) cut = max;
        return text[..cut].TrimEnd() + suffix;
    }
}
