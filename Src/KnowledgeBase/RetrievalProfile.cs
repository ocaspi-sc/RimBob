namespace RimAI.Knowledge;

/// <summary>
/// Per-minister retrieval configuration.
/// </summary>
public sealed record RetrievalProfile(
    string Name,
    string CitationPrefix,
    int TopK,
    int SnippetMaxChars,
    string SnippetSuffix = "..."
)
{
    public static readonly RetrievalProfile Mayor = new(
        Name: "Mayor",
        CitationPrefix: "g",
        TopK: 3,
        SnippetMaxChars: 320,
        SnippetSuffix: "\u2026");

    public static readonly RetrievalProfile Food = new(
        Name: "Food",
        CitationPrefix: "fg",
        TopK: 3,
        SnippetMaxChars: 320);

    public RetrievalProfile WithConfiguredTopK(int topK) =>
        this with { TopK = topK > 0 ? topK : TopK };
}
