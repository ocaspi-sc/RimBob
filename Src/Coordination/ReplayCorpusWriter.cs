using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RimAI.Core.Advice;
using RimAI.Core.Ministers;

namespace RimAI.Coordination;

public interface IReplayCorpusWriter
{
    Task WriteAsync(MinisterReplayRecord record, CancellationToken ct);
}

public sealed class ReplayCorpusWriter(string replayDirectory, ILogger<ReplayCorpusWriter> log) : IReplayCorpusWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task WriteAsync(MinisterReplayRecord record, CancellationToken ct)
    {
        try
        {
            await _gate.WaitAsync(ct);
            try
            {
                Directory.CreateDirectory(replayDirectory);
                string date = record.CapturedAt.UtcDateTime.ToString("yyyyMMdd");
                string minister = SanitizeMinister(record.Minister);
                string path = Path.Combine(replayDirectory, $"{minister}-{date}.jsonl");
                string line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
                await File.AppendAllTextAsync(path, line, Encoding.UTF8, ct);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Replay corpus persistence failed for minister={Minister}", record.Minister);
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

public sealed record MinisterReplayRecord(
    [property: JsonPropertyName("schema_version")]
    int SchemaVersion,
    [property: JsonPropertyName("captured_at")]
    DateTimeOffset CapturedAt,
    [property: JsonPropertyName("minister")]
    string Minister,
    [property: JsonPropertyName("trigger")]
    string Trigger,
    [property: JsonPropertyName("wakeup_payload")]
    string? WakeupPayload,
    [property: JsonPropertyName("flag")]
    AgentFlag? Flag,
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("briefing")]
    object Briefing,
    [property: JsonPropertyName("context")]
    object? Context,
    [property: JsonPropertyName("rule_trace")]
    string? RuleTrace,
    [property: JsonPropertyName("escalation_reason")]
    string? EscalationReason,
    [property: JsonPropertyName("escalation_context")]
    object? EscalationContext,
    [property: JsonPropertyName("guide_citations")]
    IReadOnlyList<GuideCitation>? GuideCitations,
    [property: JsonPropertyName("advice")]
    IReadOnlyList<AdviceItem> Advice,
    [property: JsonPropertyName("flags")]
    IReadOnlyList<AgentFlag> Flags,
    [property: JsonPropertyName("state_summary")]
    string? StateSummary,
    [property: JsonPropertyName("error")]
    ReplayErrorSummary? Error,
    [property: JsonPropertyName("llm")]
    ReplayLlmMetadata? Llm);

public sealed record ReplayErrorSummary(
    [property: JsonPropertyName("type")]
    string Type,
    [property: JsonPropertyName("message")]
    string Message);

public sealed record ReplayLlmMetadata(
    [property: JsonPropertyName("provider")]
    string? Provider,
    [property: JsonPropertyName("model")]
    string? Model,
    [property: JsonPropertyName("captured_at")]
    DateTimeOffset? CapturedAt,
    [property: JsonPropertyName("system_prompt_chars")]
    int? SystemPromptChars,
    [property: JsonPropertyName("user_prompt_chars")]
    int? UserPromptChars,
    [property: JsonPropertyName("status")]
    string? Status,
    [property: JsonPropertyName("parse_mode")]
    string? ParseMode,
    [property: JsonPropertyName("latency_ms")]
    long? LatencyMs,
    [property: JsonPropertyName("raw_output")]
    string? RawOutput);
