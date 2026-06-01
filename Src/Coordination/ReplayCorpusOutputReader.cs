using System.Text;
using System.Text.Json;

namespace RimBob.Coordination;

public sealed class ReplayCorpusOutputReader(string replayDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<MinisterReplayRecord?> LatestAsync(
        string minister,
        Func<MinisterReplayRecord, bool>? where = null,
        CancellationToken ct = default)
    {
        FileInfo[] files = await ReplayFilesAsync(minister, ct).ConfigureAwait(false);

        foreach (FileInfo file in files)
        {
            MinisterReplayRecord? record = await LatestInFileAsync(file, minister, where, ct)
                .ConfigureAwait(false);
            if (record is not null) return record;
        }

        return null;
    }

    private Task<FileInfo[]> ReplayFilesAsync(string minister, CancellationToken ct) =>
        Task.Run(() =>
        {
            if (!Directory.Exists(replayDirectory)) return Array.Empty<FileInfo>();

            string sanitizedMinister = SanitizeMinister(minister);
            return new DirectoryInfo(replayDirectory)
                .EnumerateFiles($"{sanitizedMinister}-*.jsonl")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();
        }, ct);

    private static async Task<MinisterReplayRecord?> LatestInFileAsync(
        FileInfo file,
        string minister,
        Func<MinisterReplayRecord, bool>? where,
        CancellationToken ct)
    {
        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(file.FullName, Encoding.UTF8, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            MinisterReplayRecord? record = TryReadRecord(lines[i], minister);
            if (record is null) continue;
            if (where is not null && !where(record)) continue;

            return record;
        }

        return null;
    }

    private static MinisterReplayRecord? TryReadRecord(string line, string minister)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        try
        {
            MinisterReplayRecord? record =
                JsonSerializer.Deserialize<MinisterReplayRecord>(line, JsonOptions);
            if (record is null) return null;
            return minister.Equals(record.Minister, StringComparison.OrdinalIgnoreCase)
                ? record
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string SanitizeMinister(string minister)
    {
        string normalized = minister.Trim().ToLowerInvariant();
        StringBuilder builder = new(normalized.Length);
        foreach (char c in normalized)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                builder.Append(c);
            else if (char.IsWhiteSpace(c))
                builder.Append('-');
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }
}
