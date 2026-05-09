namespace RimAI.Knowledge;

/// <summary>
/// Per-minister retrieval configuration. M2 ships only the Mayor profile; feeders
/// (M3+) will get their own. Plain POCO — wire through DI when the time comes.
/// </summary>
public sealed record RetrievalProfile(
    string Name,
    int    TopK
)
{
    public static readonly RetrievalProfile Mayor = new(Name: "Mayor", TopK: 3);
}
