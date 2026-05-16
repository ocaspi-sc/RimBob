using System.Text;
using Microsoft.Extensions.Logging;

namespace RimBob.Knowledge;

/// <summary>
/// Ingests guide markdown into the KnowledgeBase. Splits by H1/H2/H3 headings;
/// sections that exceed the soft size cap are split on paragraph boundaries.
/// Embeddings are cached on disk by SHA-256 of the chunk text — cold runs hit
/// the API; warm runs read from disk.
/// </summary>
public sealed class Ingest
{
    /// <summary>
    /// Soft cap on chunk size in characters. ~800 tokens at 4 chars/token.
    /// Sections longer than this are split on the nearest paragraph boundary.
    /// </summary>
    public const int MaxChunkChars = 3200;

    private readonly IEmbedder       _embedder;
    private readonly EmbeddingCache  _cache;
    private readonly ILogger<Ingest> _log;
    private readonly int             _embedDelayMs;

    public Ingest(IEmbedder embedder, EmbeddingCache cache, ILogger<Ingest> log, int embedDelayMs = 750)
    {
        _embedder     = embedder;
        _cache        = cache;
        _log          = log;
        _embedDelayMs = Math.Max(0, embedDelayMs);
    }

    /// <summary>
    /// Walks <paramref name="guidesRoot"/> for *.md files, splits each into chunks,
    /// embeds (or pulls from cache), and loads the result into <paramref name="kb"/>.
    /// </summary>
    public async Task RunAsync(string guidesRoot, KnowledgeBase kb, CancellationToken ct)
    {
        if (!Directory.Exists(guidesRoot))
        {
            _log.LogWarning("Guides root not found: {Path}. KnowledgeBase will be empty.", guidesRoot);
            kb.Load([]);
            return;
        }

        string[] files = Directory.GetFiles(guidesRoot, "*.md", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        _log.LogInformation("Ingest: scanning {Count} guide files under {Root}", files.Length, guidesRoot);

        List<Chunk> chunks   = [];
        int         cacheHit = 0, cacheMiss = 0;

        foreach (string file in files)
        {
            string relPath = Path.GetRelativePath(guidesRoot, file).Replace('\\', '/');
            string raw     = await File.ReadAllTextAsync(file, ct);
            IReadOnlyList<RawChunk> raws = SplitByHeadings(raw, relPath);

            foreach (RawChunk r in raws)
            {
                string hash = EmbeddingCache.HashText(r.Text);
                float[] embedding;
                if (_cache.TryGet(hash, out float[] cached))
                {
                    embedding = cached;
                    cacheHit++;
                }
                else
                {
                    embedding = await EmbedWithRetryAsync(r.Text, ct);
                    _cache.Put(hash, embedding);
                    cacheMiss++;
                    if (_embedDelayMs > 0) await Task.Delay(_embedDelayMs, ct);
                }

                ChunkMetadata meta = new(
                    SourcePath:     relPath,
                    SectionHeading: r.Heading,
                    StartLine:      r.StartLine,
                    EndLine:        r.EndLine);

                chunks.Add(new Chunk(
                    Id:        $"{relPath}#{r.StartLine}",
                    Text:      r.Text,
                    Meta:      meta,
                    Embedding: embedding));
            }
        }

        kb.Load(chunks);
        _log.LogInformation(
            "Ingest: loaded {Total} chunks ({Hit} cached, {Miss} embedded)",
            chunks.Count, cacheHit, cacheMiss);
    }

    private async Task<float[]> EmbedWithRetryAsync(string text, CancellationToken ct)
    {
        const int maxAttempts = 3;
        Exception? last = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try { return await _embedder.EmbedAsync(text, ct); }
            catch (Exception ex) when (attempt < maxAttempts && IsRateLimit(ex))
            {
                int backoff = 2000 * attempt;
                _log.LogWarning("Embed rate-limited (attempt {Attempt}); backing off {Ms}ms", attempt, backoff);
                await Task.Delay(backoff, ct);
                last = ex;
            }
        }
        throw last ?? new InvalidOperationException("Embed failed without exception trace.");
    }

    private static bool IsRateLimit(Exception ex)
        => ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("rate", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("429");

    /// <summary>
    /// Splits markdown text by H1/H2/H3 headings. Each heading starts a new chunk
    /// whose body runs until the next heading. Sections exceeding MaxChunkChars are
    /// split further on blank-line paragraph boundaries.
    /// </summary>
    public static IReadOnlyList<RawChunk> SplitByHeadings(string markdown, string sourcePath)
    {
        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');

        List<(string Heading, int StartLine, int EndLineExclusive)> sections = [];
        string currentHeading = "(file head)";
        int    sectionStart   = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int    level = HeadingLevel(line);
            if (level >= 1 && level <= 3)
            {
                if (i > sectionStart)
                    sections.Add((currentHeading, sectionStart, i));
                currentHeading = line.TrimStart('#').Trim();
                sectionStart   = i;
            }
        }
        sections.Add((currentHeading, sectionStart, lines.Length));

        List<RawChunk> result = [];
        foreach ((string heading, int start, int endExclusive) in sections)
        {
            // Strip the heading line itself from the body but keep it in metadata.
            int bodyStart = HeadingLevel(lines[start]) >= 1 ? start + 1 : start;
            string body = string.Join('\n', lines[bodyStart..endExclusive]).Trim();
            if (body.Length == 0) continue;

            if (body.Length <= MaxChunkChars)
            {
                result.Add(new RawChunk(heading, body, start + 1, endExclusive));
                continue;
            }

            // Section too long — split on paragraph boundaries.
            foreach (RawChunk piece in SplitOversizedSection(heading, body, start + 1))
                result.Add(piece);
        }
        return result;
    }

    private static IEnumerable<RawChunk> SplitOversizedSection(string heading, string body, int sectionStartLine)
    {
        string[] paragraphs = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        StringBuilder buf = new();
        int chunkStart = sectionStartLine;
        int curLine    = sectionStartLine;
        int part       = 1;

        foreach (string p in paragraphs)
        {
            string trimmed = p.Trim();
            int    pLines  = trimmed.Count(c => c == '\n') + 1;

            if (buf.Length > 0 && buf.Length + trimmed.Length + 2 > MaxChunkChars)
            {
                yield return new RawChunk(
                    Heading:   $"{heading} (part {part})",
                    Text:      buf.ToString().Trim(),
                    StartLine: chunkStart,
                    EndLine:   curLine);
                buf.Clear();
                chunkStart = curLine;
                part++;
            }

            if (buf.Length > 0) buf.Append("\n\n");
            buf.Append(trimmed);
            curLine += pLines + 1; // +1 for the blank line separator
        }

        if (buf.Length > 0)
        {
            yield return new RawChunk(
                Heading:   part == 1 ? heading : $"{heading} (part {part})",
                Text:      buf.ToString().Trim(),
                StartLine: chunkStart,
                EndLine:   curLine);
        }
    }

    private static int HeadingLevel(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] == '#') i++;
        if (i is >= 1 and <= 3 && i < line.Length && line[i] == ' ') return i;
        return 0;
    }

    public sealed record RawChunk(string Heading, string Text, int StartLine, int EndLine);
}
