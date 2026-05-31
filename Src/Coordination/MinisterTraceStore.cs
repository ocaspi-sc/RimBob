using RimBob.Core.Ministers;

namespace RimBob.Coordination;

public sealed class MinisterTraceStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, MinisterTraceSnapshot> _latest =
        new(StringComparer.OrdinalIgnoreCase);

    public void Begin(string minister, PlayCycleContext context)
    {
        MinisterTraceSnapshot snapshot = new(
            Minister: minister,
            Trigger: context.Trigger.ToString(),
            Status: "running",
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            Path: "not_exposed_yet",
            RuleFired: null,
            RuleDiagnostics: null,
            EscalationReason: null,
            ErrorType: null,
            ErrorMessage: null,
            AdviceCount: null,
            FlagCount: null,
            WakeupPayload: context.WakeupPayload,
            Flag: context.Flag,
            Note: "Dashboard v2 can see the wake trigger now; rule/LLM path details need minister decision logging.");

        lock (_lock)
        {
            _latest[minister] = snapshot;
        }
    }

    public void RecordPath(
        string minister,
        PlayCycleContext context,
        string path,
        string? ruleFired,
        RuleTraceDetails? ruleDiagnostics,
        string? escalationReason,
        ReplayErrorSummary? error,
        int? adviceCount,
        int? flagCount)
    {
        lock (_lock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            MinisterTraceSnapshot current =
                _latest.TryGetValue(minister, out MinisterTraceSnapshot? existing) &&
                existing.Status.Equals("running", StringComparison.OrdinalIgnoreCase)
                    ? existing
                    : new MinisterTraceSnapshot(
                        Minister: minister,
                        Trigger: context.Trigger.ToString(),
                        Status: "completed",
                        StartedAt: now,
                        CompletedAt: now,
                        Path: "not_exposed_yet",
                        RuleFired: null,
                        RuleDiagnostics: null,
                        EscalationReason: null,
                        ErrorType: null,
                        ErrorMessage: null,
                        AdviceCount: null,
                        FlagCount: null,
                        WakeupPayload: context.WakeupPayload,
                        Flag: context.Flag,
                        Note: "Trace was recorded outside the cabinet cycle runner.");

            _latest[minister] = current with
            {
                Path = path,
                RuleFired = ruleFired,
                RuleDiagnostics = ruleDiagnostics,
                EscalationReason = escalationReason,
                ErrorType = error?.Type,
                ErrorMessage = error?.Message,
                AdviceCount = adviceCount,
                FlagCount = flagCount,
                Note = BuildNote(path, ruleFired, escalationReason, error)
            };
        }
    }

    public void Complete(string minister, string? note = null)
    {
        lock (_lock)
        {
            if (!_latest.TryGetValue(minister, out MinisterTraceSnapshot? current)) return;
            _latest[minister] = current with
            {
                Status = "completed",
                CompletedAt = DateTimeOffset.UtcNow,
                Note = string.IsNullOrWhiteSpace(note)
                    ? current.Note
                    : $"{current.Note} {note}",
            };
        }
    }

    public void Fail(string minister, Exception ex)
    {
        lock (_lock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (!_latest.TryGetValue(minister, out MinisterTraceSnapshot? current))
            {
                _latest[minister] = new MinisterTraceSnapshot(
                    Minister: minister,
                    Trigger: "unknown",
                    Status: "failed",
                    StartedAt: now,
                    CompletedAt: now,
                    Path: "not_exposed_yet",
                    RuleFired: null,
                    RuleDiagnostics: null,
                    EscalationReason: null,
                    ErrorType: ex.GetType().Name,
                    ErrorMessage: ex.Message,
                    AdviceCount: null,
                    FlagCount: null,
                    WakeupPayload: null,
                    Flag: null,
                    Note: ex.Message);
                return;
            }

            _latest[minister] = current with
            {
                Status = "failed",
                CompletedAt = now,
                ErrorType = ex.GetType().Name,
                ErrorMessage = ex.Message,
                Note = ex.Message,
            };
        }
    }

    public MinisterTraceSnapshot? Latest(string minister)
    {
        lock (_lock)
        {
            return _latest.TryGetValue(minister, out MinisterTraceSnapshot? snapshot)
                ? snapshot
                : null;
        }
    }

    public IReadOnlyList<MinisterTraceSnapshot> LatestAll()
    {
        lock (_lock)
        {
            return _latest.Values
                .OrderBy(s => s.Minister)
                .ToList();
        }
    }

    private static string BuildNote(
        string path,
        string? ruleFired,
        string? escalationReason,
        ReplayErrorSummary? error) =>
        path switch
        {
            "rules" when !string.IsNullOrWhiteSpace(ruleFired) =>
                $"Rules path completed: {ruleFired}.",
            "rules" when !string.IsNullOrWhiteSpace(escalationReason) =>
                $"Rules path stopped before LLM escalation: {escalationReason}.",
            "rules" =>
                "Rules path completed.",
            "llm" when !string.IsNullOrWhiteSpace(escalationReason) =>
                $"LLM path accepted: {escalationReason}.",
            "llm" =>
                "LLM path accepted.",
            "llm_failed" when error is not null && !string.IsNullOrWhiteSpace(escalationReason) =>
                $"LLM path failed: {escalationReason}; {error.Type}: {error.Message}",
            "llm_failed" when error is not null =>
                $"LLM path failed: {error.Type}: {error.Message}",
            _ when error is not null =>
                $"{path} failed: {error.Type}: {error.Message}",
            _ =>
                $"{path} path recorded."
        };
}

public sealed record MinisterTraceSnapshot(
    string Minister,
    string Trigger,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Path,
    string? RuleFired,
    RuleTraceDetails? RuleDiagnostics,
    string? EscalationReason,
    string? ErrorType,
    string? ErrorMessage,
    int? AdviceCount,
    int? FlagCount,
    string? WakeupPayload,
    AgentFlag? Flag,
    string Note);
