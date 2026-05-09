using FluentAssertions;
using RimAI.Knowledge;

namespace RimAI.Tests.Knowledge;

public sealed class IngestTests
{
    [Fact]
    public void SplitByHeadings_BasicMarkdown_ProducesOneChunkPerSection()
    {
        const string md = """
            # Title

            Intro paragraph.

            ## First section

            First body line.

            Second body line.

            ## Second section

            Body of second.
            """;

        IReadOnlyList<Ingest.RawChunk> chunks = Ingest.SplitByHeadings(md, "test.md");

        chunks.Should().HaveCount(3);
        chunks[0].Heading.Should().Be("Title");
        chunks[0].Text.Should().Contain("Intro paragraph");
        chunks[1].Heading.Should().Be("First section");
        chunks[1].Text.Should().Contain("First body line");
        chunks[2].Heading.Should().Be("Second section");
        chunks[2].Text.Should().Contain("Body of second");
    }

    [Fact]
    public void SplitByHeadings_IgnoresLevelFourPlusHeadings()
    {
        const string md = """
            # Title

            Body.

            #### Deep heading

            More body — should stay in the Title chunk.
            """;

        IReadOnlyList<Ingest.RawChunk> chunks = Ingest.SplitByHeadings(md, "test.md");

        chunks.Should().HaveCount(1);
        chunks[0].Heading.Should().Be("Title");
        chunks[0].Text.Should().Contain("Deep heading");
        chunks[0].Text.Should().Contain("More body");
    }

    [Fact]
    public void SplitByHeadings_OversizedSection_SplitsOnParagraphBoundaries()
    {
        // Build a section longer than MaxChunkChars (3200) using paragraph blocks.
        string para = new string('p', 800) + ".";
        string md   = "## Big section\n\n" + string.Join("\n\n", Enumerable.Repeat(para, 6));

        IReadOnlyList<Ingest.RawChunk> chunks = Ingest.SplitByHeadings(md, "big.md");

        chunks.Should().HaveCountGreaterThan(1);
        foreach (Ingest.RawChunk c in chunks)
            c.Text.Length.Should().BeLessThanOrEqualTo(Ingest.MaxChunkChars);
        chunks[0].Heading.Should().StartWith("Big section");
    }

    [Fact]
    public void SplitByHeadings_EmptySection_IsSkipped()
    {
        const string md = """
            ## Empty



            ## Real

            Body.
            """;

        IReadOnlyList<Ingest.RawChunk> chunks = Ingest.SplitByHeadings(md, "test.md");

        chunks.Should().ContainSingle();
        chunks[0].Heading.Should().Be("Real");
    }

    [Fact]
    public async Task RunAsync_LoadsChunksWithCachedEmbeddings()
    {
        string tmpRoot   = Path.Combine(Path.GetTempPath(), "rimai-ingest-" + Guid.NewGuid().ToString("N"));
        string guidesDir = Path.Combine(tmpRoot, "guides");
        string cacheDir  = Path.Combine(tmpRoot, "cache");
        Directory.CreateDirectory(guidesDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(guidesDir, "a.md"),
                "# Alpha\n\nFirst section text.\n\n## Sub\n\nSecond chunk.\n");

            EmbeddingCache cache = new(cacheDir);
            FakeEmbedder   fake  = new();
            Ingest         ingest = new(fake, cache, NullLogger());

            KnowledgeBase kb = new();
            await ingest.RunAsync(guidesDir, kb, CancellationToken.None);

            kb.Count.Should().Be(2);
            fake.CallCount.Should().Be(2);

            // Re-run: cache should serve everything.
            KnowledgeBase kb2 = new();
            FakeEmbedder  fake2 = new();
            Ingest        ingest2 = new(fake2, cache, NullLogger());
            await ingest2.RunAsync(guidesDir, kb2, CancellationToken.None);

            kb2.Count.Should().Be(2);
            fake2.CallCount.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, recursive: true);
        }
    }

    private static Microsoft.Extensions.Logging.ILogger<Ingest> NullLogger()
        => Microsoft.Extensions.Logging.Abstractions.NullLogger<Ingest>.Instance;

    private sealed class FakeEmbedder : IEmbedder
    {
        public int CallCount { get; private set; }

        public Task<float[]> EmbedAsync(string text, CancellationToken ct)
        {
            CallCount++;
            // Deterministic 4-d vector seeded by text length.
            float t = text.Length;
            return Task.FromResult(new[] { t, t / 2, t / 4, t / 8 });
        }
    }
}
