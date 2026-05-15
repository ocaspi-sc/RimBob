using RimAI.Core.Advice;
using RimAI.Core.Ministers;
using RimAI.LLM;

namespace RimAI.Coordination;

public sealed class MinisterReplayRecorder(
    IReplayCorpusWriter? writer,
    RawLlmOutputStore? rawOutputs = null)
{
    public Task RecordAsync(MinisterReplayEntry entry, CancellationToken ct)
    {
        if (writer is null) return Task.CompletedTask;

        ReplayLlmMetadata? llm = entry.Llm
            ?? (entry.LlmAttemptStarted.HasValue
                ? LatestLlmMetadata(entry.Minister, entry.LlmAttemptStarted.Value)
                : null);

        MinisterReplayRecord record = new(
            SchemaVersion: 2,
            CapturedAt: DateTimeOffset.UtcNow,
            Minister: entry.Minister,
            Trigger: entry.Cycle.Trigger.ToString(),
            WakeupPayload: entry.Cycle.WakeupPayload,
            Flag: entry.Cycle.Flag,
            Path: entry.Path,
            Briefing: entry.Briefing,
            Context: entry.Context,
            RuleTrace: entry.RuleTrace,
            EscalationReason: entry.EscalationReason,
            EscalationContext: entry.EscalationContext,
            GuideCitations: entry.GuideCitations,
            Advice: entry.Advice ?? [],
            Flags: entry.Flags ?? [],
            StateSummary: entry.StateSummary,
            Error: entry.Error,
            Llm: llm,
            OutputKind: entry.OutputKind,
            Output: entry.Output);

        return writer.WriteAsync(record, ct);
    }

    public ReplayLlmMetadata? LatestLlmMetadata(string minister, DateTimeOffset since)
    {
        RawLlmOutputSnapshot? snapshot = rawOutputs?.Latest(minister);
        if (snapshot is null) return null;
        if (snapshot.CapturedAt < since) return null;

        return new ReplayLlmMetadata(
            Provider: snapshot.Provider,
            Model: snapshot.Model,
            CapturedAt: snapshot.CapturedAt,
            SystemPromptChars: snapshot.SystemPromptChars,
            UserPromptChars: snapshot.UserPromptChars,
            Status: snapshot.Status,
            ParseMode: snapshot.ParseMode,
            LatencyMs: snapshot.LatencyMs,
            RawOutput: snapshot.Text,
            ApiKeyIndex: snapshot.ApiKeyIndex,
            ApiKeyLabel: snapshot.ApiKeyLabel);
    }
}

public sealed record MinisterReplayEntry(
    string Minister,
    PlayCycleContext Cycle,
    string Path,
    object Briefing,
    object? Context = null,
    string? RuleTrace = null,
    string? EscalationReason = null,
    object? EscalationContext = null,
    IReadOnlyList<GuideCitation>? GuideCitations = null,
    IReadOnlyList<AdviceItem>? Advice = null,
    IReadOnlyList<AgentFlag>? Flags = null,
    string? StateSummary = null,
    ReplayErrorSummary? Error = null,
    DateTimeOffset? LlmAttemptStarted = null,
    ReplayLlmMetadata? Llm = null,
    string? OutputKind = null,
    object? Output = null);
