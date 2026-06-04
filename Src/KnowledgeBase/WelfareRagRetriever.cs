using Microsoft.Extensions.Logging;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;

namespace RimBob.Knowledge;

public sealed class WelfareRagRetriever
{
    private readonly RagRetriever<WelfareSourceBriefing> _retriever;

    public WelfareRagRetriever(KnowledgeBase kb, IEmbedder? embedder, bool enabled, int topK, ILogger<WelfareRagRetriever> log)
    {
        _retriever = new RagRetriever<WelfareSourceBriefing>(
            kb,
            embedder,
            enabled,
            RetrievalProfile.Welfare.WithConfiguredTopK(topK),
            new WelfareRagQueryBuilder(),
            log);
    }

    public Task<IReadOnlyList<GuideCitation>> RetrieveAsync(WelfareSourceBriefing briefing, CancellationToken ct) =>
        _retriever.RetrieveAsync(briefing, [], ct);

    public static string BuildQuery(WelfareSourceBriefing briefing) => WelfareRagQueryBuilder.Build(briefing);
}
