using Microsoft.Extensions.Logging;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;

namespace RimAI.Knowledge;

public sealed class FoodRagRetriever
{
    private const int SnippetMaxChars = 320;

    private readonly KnowledgeBase _kb;
    private readonly IEmbedder? _embedder;
    private readonly bool _enabled;
    private readonly int _topK;
    private readonly ILogger<FoodRagRetriever> _log;

    public FoodRagRetriever(KnowledgeBase kb, IEmbedder? embedder, bool enabled, int topK, ILogger<FoodRagRetriever> log)
    {
        _kb = kb;
        _embedder = embedder;
        _enabled = enabled && embedder is not null;
        _topK = topK > 0 ? topK : 3;
        _log = log;
    }

    public async Task<IReadOnlyList<GuideCitation>> RetrieveAsync(FoodBriefing briefing, CancellationToken ct)
    {
        if (!_enabled || _kb.Count == 0 || _embedder is null) return [];

        string query = BuildQuery(briefing);
        float[] embedding;
        try
        {
            embedding = await _embedder.EmbedAsync(query, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Food retrieval embedding failed; proceeding without RAG this turn.");
            return [];
        }

        IReadOnlyList<Chunk> hits = _kb.Retrieve(embedding, _topK);
        List<GuideCitation> citations = new(hits.Count);
        for (int i = 0; i < hits.Count; i++)
        {
            Chunk c = hits[i];
            citations.Add(new GuideCitation($"fg{i + 1}", c.Meta.SourcePath, c.Meta.SectionHeading, Truncate(c.Text, SnippetMaxChars)));
        }
        return citations;
    }

    public static string BuildQuery(FoodBriefing b)
    {
        List<string> parts = new()
        {
            "RimWorld food farming crops cooking freezer nutrition spoilage hunting wild harvest.",
            $"Food days: {(b.EstimatedDaysOfFood is null ? "unknown" : b.EstimatedDaysOfFood.Value.ToString("F1"))}.",
            $"Meals {b.MealsCount}; raw food {b.RawFoodCount}; ready harvest {b.ReadyToHarvest}; wild harvest {b.WildHarvestCandidates}; animals {b.WildAnimalCount}."
        };
        if (b.Season.DaysToWinter is { } winter)
            parts.Add($"Days to winter: {winter}.");
        if (b.Infrastructure.Coolers == 0)
            parts.Add("No cooler/freezer signal.");
        if (b.RecentFoodIncidents.Count > 0)
            parts.Add(string.Join(' ', b.RecentFoodIncidents));
        return string.Join(' ', parts);
    }

    private static string Truncate(string text, int max)
    {
        if (text.Length <= max) return text;
        int cut = text.LastIndexOf(' ', Math.Min(max, text.Length - 1));
        if (cut < max / 2) cut = max;
        return text[..cut].TrimEnd() + "...";
    }
}
