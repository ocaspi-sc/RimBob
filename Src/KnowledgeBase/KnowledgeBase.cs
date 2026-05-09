namespace RimAI.Knowledge;

/// <summary>
/// In-process cosine-similarity store for guide chunks. Small corpus (~50 chunks);
/// no vector DB. Threadsafe-by-construction: chunks are loaded once at startup and
/// the list is replaced atomically. Reads are lock-free against the snapshot.
/// </summary>
public sealed class KnowledgeBase
{
    private IReadOnlyList<Chunk> _chunks = [];

    public int Count => _chunks.Count;

    public KnowledgeBase() { }

    public KnowledgeBase(IReadOnlyList<Chunk> chunks)
    {
        _chunks = chunks;
    }

    public void Load(IReadOnlyList<Chunk> chunks)
    {
        _chunks = chunks;
    }

    public IReadOnlyList<Chunk> Retrieve(float[] queryEmbedding, int topK)
    {
        if (queryEmbedding.Length == 0 || _chunks.Count == 0 || topK <= 0)
            return [];

        return _chunks
            .Select(c => (Chunk: c, Score: CosineSimilarity(c.Embedding, queryEmbedding)))
            .OrderByDescending(t => t.Score)
            .Take(topK)
            .Select(t => t.Chunk)
            .ToList();
    }

    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException(
                $"Embedding dimensionality mismatch: {a.Length} vs {b.Length}.");

        double dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot  += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        if (magA == 0 || magB == 0) return 0f;
        return (float)(dot / (Math.Sqrt(magA) * Math.Sqrt(magB)));
    }
}
