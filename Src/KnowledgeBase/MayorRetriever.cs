using Microsoft.Extensions.Logging;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;

namespace RimAI.Knowledge;

/// <summary>
/// Builds a retrieval query for the Mayor from the daily briefing + agenda directives,
/// embeds it once, and pulls top-K Chunks from the KnowledgeBase. Returns Citation
/// records ready to be slotted into the LLM prompt and the agenda output.
/// Disabled retrievers (Enabled=false) short-circuit to an empty list — used when
/// RimAi:Rag:Enabled is false in config.
/// </summary>
public sealed class MayorRetriever
{
    private const int SnippetMaxChars = 320;

    private readonly KnowledgeBase           _kb;
    private readonly IEmbedder?              _embedder;
    private readonly int                     _topK;
    private readonly bool                    _enabled;
    private readonly ILogger<MayorRetriever> _log;

    public MayorRetriever(
        KnowledgeBase           kb,
        IEmbedder?              embedder,
        bool                    enabled,
        int                     topK,
        ILogger<MayorRetriever> log)
    {
        _kb       = kb;
        _embedder = embedder;
        _enabled  = enabled && embedder is not null;
        _topK     = topK > 0 ? topK : 3;
        _log      = log;
    }

    public bool Enabled => _enabled;

    public async Task<IReadOnlyList<Citation>> RetrieveAsync(
        MayorBriefing         briefing,
        IReadOnlyList<string> agendaDirectives,
        CancellationToken     ct)
    {
        if (!_enabled || _kb.Count == 0 || _embedder is null) return [];

        string query = BuildQuery(briefing, agendaDirectives);
        float[] queryEmbedding;
        try
        {
            queryEmbedding = await _embedder.EmbedAsync(query, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Mayor retrieval embedding failed; proceeding without RAG this turn.");
            return [];
        }

        IReadOnlyList<Chunk> hits = _kb.Retrieve(queryEmbedding, _topK);
        List<Citation> citations = new(hits.Count);
        for (int i = 0; i < hits.Count; i++)
        {
            Chunk c = hits[i];
            citations.Add(new Citation(
                CiteId:     $"g{i + 1}",
                SourcePath: c.Meta.SourcePath,
                Heading:    c.Meta.SectionHeading,
                Snippet:    Truncate(c.Text, SnippetMaxChars)));
        }

        _log.LogInformation(
            "Mayor RAG: retrieved {Count} chunks (topK={TopK}, query={QueryChars} chars)",
            citations.Count, _topK, query.Length);

        return citations;
    }

    public static string BuildQuery(MayorBriefing b, IReadOnlyList<string> agendaDirectives)
    {
        List<string> parts = new(8);

        if (b.Date.Quadrum is not null && b.Date.Day is not null)
            parts.Add($"Y{b.Date.Year ?? 0} {b.Date.Quadrum} day {b.Date.Day}.");
        if (b.Season.CurrentSeason is not null)
            parts.Add($"Season: {b.Season.CurrentSeason}.");
        if (b.Season.DaysToWinter is { } dtw)
            parts.Add($"Days to winter: {dtw}.");

        parts.Add($"Colonists: {b.Colonists.Count}; mood {b.Mood.AverageMood:F2}; downed {b.Medical.Downed}.");
        if (b.Food.EstimatedDaysOfFood is { } days)
            parts.Add($"Food: {days:F1} days; stockpile {b.Food.EstimatedFoodUnitsInStockpile}.");
        if (b.Threat.ActiveRaid)
            parts.Add($"Active raid: {b.Threat.HostileLordCount} hostile groups, {b.Threat.TotalThreatPoints:F0} threat points.");
        parts.Add($"Wealth: {b.Wealth.Colony:F0}.");
        parts.Add($"Weather: {b.Weather.Def}, {b.Weather.TemperatureC:F1}°C.");
        if (b.Research.CurrentProject is not null)
            parts.Add($"Research: {b.Research.CurrentProject}.");

        foreach (string directive in agendaDirectives)
            parts.Add(directive);

        parts.Add("Which RimWorld guide passages give the most relevant strategic advice?");
        return string.Join(' ', parts);
    }

    private static string Truncate(string text, int max)
    {
        if (text.Length <= max) return text;
        int cut = text.LastIndexOf(' ', Math.Min(max, text.Length - 1));
        if (cut < max / 2) cut = max;
        return text[..cut].TrimEnd() + "…";
    }
}
