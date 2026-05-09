using FluentAssertions;
using RimAI.Knowledge;

namespace RimAI.Tests.Knowledge;

public sealed class KnowledgeBaseTests
{
    [Fact]
    public void Cosine_OfIdenticalVectors_IsOne()
    {
        float[] v = [0.1f, 0.4f, 0.9f];
        KnowledgeBase.CosineSimilarity(v, v).Should().BeApproximately(1f, 1e-6f);
    }

    [Fact]
    public void Cosine_OfOrthogonal_IsZero()
    {
        float[] a = [1, 0, 0];
        float[] b = [0, 1, 0];
        KnowledgeBase.CosineSimilarity(a, b).Should().BeApproximately(0f, 1e-6f);
    }

    [Fact]
    public void Cosine_OfOpposite_IsMinusOne()
    {
        float[] a = [0.5f, 0.5f, 0f];
        float[] b = [-0.5f, -0.5f, 0f];
        KnowledgeBase.CosineSimilarity(a, b).Should().BeApproximately(-1f, 1e-6f);
    }

    [Fact]
    public void Retrieve_ReturnsTopKByCosineDescending()
    {
        ChunkMetadata meta = new("guides/test.md", "S", 1, 2);
        Chunk near    = new("near",    "near vector text",    meta, [0.9f, 0.1f, 0f]);
        Chunk middle  = new("middle",  "middling text",       meta, [0.5f, 0.5f, 0f]);
        Chunk far     = new("far",     "orthogonal text",     meta, [0f,   1f,   0f]);

        KnowledgeBase kb = new([near, middle, far]);

        IReadOnlyList<Chunk> top2 = kb.Retrieve([1f, 0f, 0f], topK: 2);

        top2.Should().HaveCount(2);
        top2[0].Id.Should().Be("near");
        top2[1].Id.Should().Be("middle");
    }

    [Fact]
    public void Retrieve_OnEmptyStore_ReturnsEmpty()
    {
        KnowledgeBase kb = new();
        kb.Retrieve([1f, 0f], topK: 5).Should().BeEmpty();
    }

    [Fact]
    public void Retrieve_TopKLargerThanCorpus_ReturnsAll()
    {
        ChunkMetadata meta = new("g.md", "h", 1, 1);
        Chunk a = new("a", "a", meta, [1, 0]);
        Chunk b = new("b", "b", meta, [0, 1]);
        KnowledgeBase kb = new([a, b]);

        kb.Retrieve([1, 1], topK: 10).Should().HaveCount(2);
    }
}
