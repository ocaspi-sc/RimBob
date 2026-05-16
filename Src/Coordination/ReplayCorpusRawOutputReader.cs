using System.Text;
using System.Text.Json;
using RimAI.LLM;

namespace RimAI.Coordination;

public sealed class ReplayCorpusRawOutputReader(string replayDirectory)
{
    public RawLlmOutputSnapshot? Latest(string minister)
    {
        if (!Directory.Exists(replayDirectory)) return null;

        string sanitizedMinister = SanitizeMinister(minister);
        FileInfo[] files = new DirectoryInfo(replayDirectory)
            .EnumerateFiles($"{sanitizedMinister}-*.jsonl")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(20)
            .ToArray();

        foreach (FileInfo file in files)
        {
            RawLlmOutputSnapshot? snapshot = LatestInFile(file, minister);
            if (snapshot is not null) return snapshot;
        }

        return null;
    }

    private static RawLlmOutputSnapshot? LatestInFile(FileInfo file, string minister)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file.FullName, Encoding.UTF8);
        }
        catch
        {
            return null;
        }

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            RawLlmOutputSnapshot? snapshot = TryReadSnapshot(lines[i], minister);
            if (snapshot is not null) return snapshot;
        }

        return null;
    }

    private static RawLlmOutputSnapshot? TryReadSnapshot(string line, string minister)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            string? recordMinister = ReadString(root, "minister");
            if (!minister.Equals(recordMinister, StringComparison.OrdinalIgnoreCase)) return null;
            if (!root.TryGetProperty("llm", out JsonElement llm) || llm.ValueKind != JsonValueKind.Object) return null;

            string? rawOutput = ReadString(llm, "raw_output");
            if (string.IsNullOrWhiteSpace(rawOutput)) return null;

            DateTimeOffset capturedAt =
                ReadDateTimeOffset(llm, "captured_at") ??
                ReadDateTimeOffset(root, "captured_at") ??
                DateTimeOffset.MinValue;

            return new RawLlmOutputSnapshot(
                Minister: recordMinister ?? minister,
                Provider: ReadString(llm, "provider") ?? "Replay",
                Model: ReadString(llm, "model") ?? "unknown",
                ApiKeyIndex: ReadInt(llm, "api_key_index"),
                ApiKeyLabel: ReadString(llm, "api_key_label"),
                CapturedAt: capturedAt,
                LatencyMs: ReadLong(llm, "latency_ms") ?? 0,
                Status: ReadString(llm, "status") ?? "replay_captured",
                ParseMode: ReadString(llm, "parse_mode") ?? "replay",
                SystemPromptChars: ReadInt(llm, "system_prompt_chars") ?? 0,
                UserPromptChars: ReadInt(llm, "user_prompt_chars") ?? 0,
                Text: rawOutput);
        }
        catch (JsonException)
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

    private static string? ReadString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? ReadInt(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out int number)
            ? number
            : null;
    }

    private static long? ReadLong(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt64(out long number)
            ? number
            : null;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) &&
               value.ValueKind == JsonValueKind.String &&
               value.TryGetDateTimeOffset(out DateTimeOffset date)
            ? date
            : null;
    }
}
