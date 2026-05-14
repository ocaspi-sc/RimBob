using Microsoft.Extensions.Logging;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;

namespace RimAI.Knowledge;

public sealed class FoodRagRetriever
{
    private readonly RagRetriever<FoodBriefing> _retriever;

    public FoodRagRetriever(KnowledgeBase kb, IEmbedder? embedder, bool enabled, int topK, ILogger<FoodRagRetriever> log)
    {
        _retriever = new RagRetriever<FoodBriefing>(
            kb,
            embedder,
            enabled,
            RetrievalProfile.Food.WithConfiguredTopK(topK),
            new FoodRagQueryBuilder(),
            log);
    }

    public Task<IReadOnlyList<GuideCitation>> RetrieveAsync(FoodBriefing briefing, CancellationToken ct) =>
        _retriever.RetrieveAsync(briefing, [], ct);

    public static string BuildQuery(FoodBriefing b) => FoodRagQueryBuilder.Build(b);
}
