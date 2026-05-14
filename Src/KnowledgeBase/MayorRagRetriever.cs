using Microsoft.Extensions.Logging;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;

namespace RimAI.Knowledge;

/// <summary>
/// Builds a retrieval query for the Mayor from the daily briefing + agenda directives,
/// embeds it once, and pulls top-K Chunks from the KnowledgeBase. Returns GuideCitation
/// records ready to be slotted into the LLM prompt and the agenda output.
/// Disabled retrievers (Enabled=false) short-circuit to an empty list — used when
/// RimAi:Rag:Enabled is false in config.
/// </summary>
public sealed class MayorRagRetriever
{
    private readonly RagRetriever<MayorBriefing> _retriever;

    public MayorRagRetriever(
        KnowledgeBase           kb,
        IEmbedder?              embedder,
        bool                    enabled,
        int                     topK,
        ILogger<MayorRagRetriever> log)
    {
        _retriever = new RagRetriever<MayorBriefing>(
            kb,
            embedder,
            enabled,
            RetrievalProfile.Mayor.WithConfiguredTopK(topK),
            new MayorRagQueryBuilder(),
            log);
    }

    public bool Enabled => _retriever.Enabled;

    public Task<IReadOnlyList<GuideCitation>> RetrieveAsync(
        MayorBriefing         briefing,
        IReadOnlyList<string> agendaDirectives,
        CancellationToken     ct) =>
        _retriever.RetrieveAsync(briefing, agendaDirectives, ct);

    public static string BuildQuery(MayorBriefing b, IReadOnlyList<string> agendaDirectives) =>
        MayorRagQueryBuilder.Build(b, agendaDirectives);
}
