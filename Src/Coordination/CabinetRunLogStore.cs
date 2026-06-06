using System.Text.Json.Serialization;

namespace RimBob.Coordination;

public sealed class CabinetRunLogStore
{
    private const int MaxRuns = 12;
    private readonly object _lock = new();
    private readonly Dictionary<string, CabinetRunLogSnapshot> _runs =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _runOrder = [];

    public event Action<CabinetRunLogSnapshot>? RunChanged;

    public CabinetRunLogSnapshot StartRun(string? runId, string scope, string trigger)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string resolvedRunId = string.IsNullOrWhiteSpace(runId)
            ? Guid.NewGuid().ToString("N")
            : runId.Trim();
        CabinetRunStepSnapshot requestAccepted = new(
            Key: "request_accepted",
            Label: "Request accepted",
            Kind: "request",
            Status: "completed",
            StartedAt: now,
            CompletedAt: now,
            DurationMs: 0,
            Detail: "Host accepted the manual cabinet run request.",
            Minister: null,
            StateSource: null,
            UsedRestoredSnapshot: null,
            TracePath: null,
            RuleFired: null,
            EscalationReason: null,
            AdviceCount: null,
            FlagCount: null,
            TraceNote: null,
            ErrorType: null,
            ErrorMessage: null,
            Children: []);
        CabinetRunLogSnapshot snapshot = new(
            RunId: resolvedRunId,
            Scope: scope,
            Trigger: trigger,
            Status: "running",
            StartedAt: now,
            CompletedAt: null,
            DurationMs: null,
            Steps: [requestAccepted],
            StateSource: null,
            UsedRestoredSnapshot: null,
            ErrorType: null,
            ErrorMessage: null);

        Store(snapshot);
        Publish(snapshot);
        return snapshot;
    }

    public CabinetRunLogSnapshot StartStep(
        string runId,
        string key,
        string label,
        string kind,
        string? detail = null,
        string? minister = null) =>
        UpdateStep(
            runId,
            key,
            label,
            kind,
            status: "running",
            completedAt: null,
            durationMs: null,
            detail: detail,
            minister: minister,
            stateSource: null,
            usedRestoredSnapshot: null,
            trace: null,
            errorType: null,
            errorMessage: null);

    public CabinetRunLogSnapshot StartOrResumeStep(
        string runId,
        string key,
        string label,
        string kind,
        string? detail = null,
        string? minister = null)
    {
        CabinetRunLogSnapshot snapshot = Mutate(runId, current =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            CabinetRunStepSnapshot? existing = current.Steps
                .FirstOrDefault(step => step.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            CabinetRunStepSnapshot step = new(
                Key: key,
                Label: label,
                Kind: kind,
                Status: "running",
                StartedAt: existing?.StartedAt ?? now,
                CompletedAt: null,
                DurationMs: null,
                Detail: detail ?? existing?.Detail,
                Minister: minister ?? existing?.Minister,
                StateSource: existing?.StateSource,
                UsedRestoredSnapshot: existing?.UsedRestoredSnapshot,
                TracePath: existing?.TracePath,
                RuleFired: existing?.RuleFired,
                EscalationReason: existing?.EscalationReason,
                AdviceCount: existing?.AdviceCount,
                FlagCount: existing?.FlagCount,
                TraceNote: existing?.TraceNote,
                ErrorType: null,
                ErrorMessage: null,
                Children: existing?.Children ?? []);

            return current with
            {
                Steps = AppendOrReplace(current.Steps, step, preserveExistingIndex: true),
            };
        });
        Publish(snapshot);
        return snapshot;
    }

    public CabinetRunLogSnapshot CompleteStep(
        string runId,
        string key,
        string label,
        string kind,
        string? detail = null,
        string? minister = null,
        string? stateSource = null,
        bool? usedRestoredSnapshot = null,
        MinisterTraceSnapshot? trace = null)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        return UpdateStep(
            runId,
            key,
            label,
            kind,
            status: "completed",
            completedAt: completedAt,
            durationMs: null,
            detail: detail,
            minister: minister,
            stateSource: stateSource,
            usedRestoredSnapshot: usedRestoredSnapshot,
            trace: trace,
            errorType: null,
            errorMessage: null);
    }

    public CabinetRunLogSnapshot SkipStep(
        string runId,
        string key,
        string label,
        string kind,
        string detail,
        string? minister = null)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        return UpdateStep(
            runId,
            key,
            label,
            kind,
            status: "skipped",
            completedAt: completedAt,
            durationMs: 0,
            detail: detail,
            minister: minister,
            stateSource: null,
            usedRestoredSnapshot: null,
            trace: null,
            errorType: null,
            errorMessage: null);
    }

    public CabinetRunLogSnapshot FailStep(
        string runId,
        string key,
        string label,
        string kind,
        Exception ex,
        string? detail = null,
        string? minister = null,
        string? stateSource = null,
        bool? usedRestoredSnapshot = null,
        MinisterTraceSnapshot? trace = null)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        return UpdateStep(
            runId,
            key,
            label,
            kind,
            status: "failed",
            completedAt: completedAt,
            durationMs: null,
            detail: detail ?? ex.Message,
            minister: minister,
            stateSource: stateSource,
            usedRestoredSnapshot: usedRestoredSnapshot,
            trace: trace,
            errorType: ex.GetType().Name,
            errorMessage: ex.Message);
    }

    public CabinetRunLogSnapshot AddChildStep(
        string runId,
        string parentKey,
        CabinetRunStepSnapshot child)
    {
        CabinetRunLogSnapshot snapshot = Mutate(runId, current =>
        {
            CabinetRunStepSnapshot parent = current.Steps
                .FirstOrDefault(step => step.Key.Equals(parentKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Cabinet run log parent step '{parentKey}' has not been started.");
            IReadOnlyList<CabinetRunStepSnapshot> children = AppendOrReplace(parent.Children, child);
            CabinetRunStepSnapshot updatedParent = RecomputeParentFromChildren(parent, children);

            return current with
            {
                Steps = AppendOrReplace(current.Steps, updatedParent, preserveExistingIndex: true),
                StateSource = child.StateSource ?? current.StateSource,
                UsedRestoredSnapshot = child.UsedRestoredSnapshot ?? current.UsedRestoredSnapshot,
            };
        });
        Publish(snapshot);
        return snapshot;
    }

    public CabinetRunLogSnapshot CompleteRun(
        string runId,
        string stateSource,
        bool usedRestoredSnapshot)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        CabinetRunLogSnapshot snapshot = Mutate(runId, current =>
        {
            CabinetRunStepSnapshot completeStep = new(
                Key: "cabinet_complete",
                Label: "Cabinet complete",
                Kind: "complete",
                Status: "completed",
                StartedAt: completedAt,
                CompletedAt: completedAt,
                DurationMs: 0,
                Detail: "Manual cabinet run completed.",
                Minister: null,
                StateSource: stateSource,
                UsedRestoredSnapshot: usedRestoredSnapshot,
                TracePath: null,
                RuleFired: null,
                EscalationReason: null,
                AdviceCount: null,
                FlagCount: null,
                TraceNote: null,
                ErrorType: null,
                ErrorMessage: null,
                Children: []);
            return current with
            {
                Status = "completed",
                CompletedAt = completedAt,
                DurationMs = DurationMs(current.StartedAt, completedAt),
                StateSource = stateSource,
                UsedRestoredSnapshot = usedRestoredSnapshot,
                Steps = AppendOrReplace(current.Steps, completeStep),
                ErrorType = null,
                ErrorMessage = null,
            };
        });
        Publish(snapshot);
        return snapshot;
    }

    public CabinetRunLogSnapshot FailRun(
        string runId,
        Exception ex,
        string? stateSource = null,
        bool? usedRestoredSnapshot = null)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        CabinetRunLogSnapshot snapshot = Mutate(runId, current =>
        {
            CabinetRunStepSnapshot failedStep = new(
                Key: "cabinet_failed",
                Label: "Cabinet failed",
                Kind: "complete",
                Status: "failed",
                StartedAt: completedAt,
                CompletedAt: completedAt,
                DurationMs: 0,
                Detail: ex.Message,
                Minister: null,
                StateSource: stateSource ?? current.StateSource,
                UsedRestoredSnapshot: usedRestoredSnapshot ?? current.UsedRestoredSnapshot,
                TracePath: null,
                RuleFired: null,
                EscalationReason: null,
                AdviceCount: null,
                FlagCount: null,
                TraceNote: null,
                ErrorType: ex.GetType().Name,
                ErrorMessage: ex.Message,
                Children: []);
            return current with
            {
                Status = "failed",
                CompletedAt = completedAt,
                DurationMs = DurationMs(current.StartedAt, completedAt),
                StateSource = stateSource ?? current.StateSource,
                UsedRestoredSnapshot = usedRestoredSnapshot ?? current.UsedRestoredSnapshot,
                ErrorType = ex.GetType().Name,
                ErrorMessage = ex.Message,
                Steps = AppendOrReplace(current.Steps, failedStep),
            };
        });
        Publish(snapshot);
        return snapshot;
    }

    public CabinetRunLogSnapshot? Latest(string runId)
    {
        lock (_lock)
        {
            return _runs.TryGetValue(runId, out CabinetRunLogSnapshot? snapshot)
                ? snapshot
                : null;
        }
    }

    public IReadOnlyList<CabinetRunLogSnapshot> LatestRuns()
    {
        lock (_lock)
        {
            return _runOrder
                .Select(runId => _runs[runId])
                .ToArray();
        }
    }

    private CabinetRunLogSnapshot UpdateStep(
        string runId,
        string key,
        string label,
        string kind,
        string status,
        DateTimeOffset? completedAt,
        long? durationMs,
        string? detail,
        string? minister,
        string? stateSource,
        bool? usedRestoredSnapshot,
        MinisterTraceSnapshot? trace,
        string? errorType,
        string? errorMessage)
    {
        CabinetRunLogSnapshot snapshot = Mutate(runId, current =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            CabinetRunStepSnapshot? existing = current.Steps
                .FirstOrDefault(step => step.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            DateTimeOffset startedAt = existing?.StartedAt ?? now;
            DateTimeOffset? resolvedCompletedAt = completedAt;
            long? resolvedDurationMs = durationMs ?? (resolvedCompletedAt is null
                ? null
                : DurationMs(startedAt, resolvedCompletedAt.Value));
            CabinetRunStepSnapshot step = new(
                Key: key,
                Label: label,
                Kind: kind,
                Status: status,
                StartedAt: startedAt,
                CompletedAt: resolvedCompletedAt,
                DurationMs: resolvedDurationMs,
                Detail: detail ?? existing?.Detail,
                Minister: minister ?? existing?.Minister,
                StateSource: stateSource ?? existing?.StateSource,
                UsedRestoredSnapshot: usedRestoredSnapshot ?? existing?.UsedRestoredSnapshot,
                TracePath: trace?.Path ?? existing?.TracePath,
                RuleFired: trace?.RuleFired ?? existing?.RuleFired,
                EscalationReason: trace?.EscalationReason ?? existing?.EscalationReason,
                AdviceCount: trace?.AdviceCount ?? existing?.AdviceCount,
                FlagCount: trace?.FlagCount ?? existing?.FlagCount,
                TraceNote: trace?.Note ?? existing?.TraceNote,
                ErrorType: errorType ?? trace?.ErrorType ?? existing?.ErrorType,
                ErrorMessage: errorMessage ?? trace?.ErrorMessage ?? existing?.ErrorMessage,
                Children: existing?.Children ?? []);

            return current with
            {
                Steps = AppendOrReplace(current.Steps, step),
                StateSource = stateSource ?? current.StateSource,
                UsedRestoredSnapshot = usedRestoredSnapshot ?? current.UsedRestoredSnapshot,
            };
        });
        Publish(snapshot);
        return snapshot;
    }

    private CabinetRunLogSnapshot Mutate(
        string runId,
        Func<CabinetRunLogSnapshot, CabinetRunLogSnapshot> mutate)
    {
        lock (_lock)
        {
            if (!_runs.TryGetValue(runId, out CabinetRunLogSnapshot? current))
                throw new InvalidOperationException($"Cabinet run log '{runId}' has not been started.");

            CabinetRunLogSnapshot next = mutate(current);
            _runs[runId] = next;
            return next;
        }
    }

    private void Store(CabinetRunLogSnapshot snapshot)
    {
        lock (_lock)
        {
            if (!_runs.ContainsKey(snapshot.RunId))
                _runOrder.Add(snapshot.RunId);

            _runs[snapshot.RunId] = snapshot;

            while (_runOrder.Count > MaxRuns)
            {
                string evicted = _runOrder[0];
                _runOrder.RemoveAt(0);
                _runs.Remove(evicted);
            }
        }
    }

    private void Publish(CabinetRunLogSnapshot snapshot) =>
        RunChanged?.Invoke(snapshot);

    private static CabinetRunStepSnapshot RecomputeParentFromChildren(
        CabinetRunStepSnapshot parent,
        IReadOnlyList<CabinetRunStepSnapshot> children)
    {
        CabinetRunStepSnapshot? failedChild = children
            .FirstOrDefault(child => StatusIs(child, "failed"));
        string status = failedChild is not null
            ? "failed"
            : children.Any(child => StatusIs(child, "running"))
                ? "running"
                : "completed";
        DateTimeOffset? completedAt = status.Equals("running", StringComparison.OrdinalIgnoreCase)
            ? null
            : children
                .Where(child => child.CompletedAt.HasValue)
                .Select(child => child.CompletedAt!.Value)
                .DefaultIfEmpty(parent.CompletedAt ?? DateTimeOffset.UtcNow)
                .Max();
        long? durationMs = completedAt is null
            ? null
            : DurationMs(parent.StartedAt, completedAt.Value);

        return parent with
        {
            Status = status,
            CompletedAt = completedAt,
            DurationMs = durationMs,
            Detail = ChildAggregateDetail(children),
            ErrorType = failedChild?.ErrorType,
            ErrorMessage = failedChild?.ErrorMessage,
            Children = children,
        };
    }

    private static string ChildAggregateDetail(IReadOnlyList<CabinetRunStepSnapshot> children)
    {
        string unit = children.All(child => child.Kind.Equals("solver", StringComparison.OrdinalIgnoreCase))
            ? "solver run"
            : "substep";
        int completed = children.Count(child => StatusIs(child, "completed"));
        int failed = children.Count(child => StatusIs(child, "failed"));
        int running = children.Count(child => StatusIs(child, "running"));
        List<string> parts = [];
        if (completed > 0 && (failed > 0 || running > 0))
            parts.Add($"{completed} completed");
        if (failed > 0)
            parts.Add($"{failed} failed");
        if (running > 0)
            parts.Add($"{running} running");

        string total = $"{children.Count} {Pluralize(unit, children.Count)}";
        return parts.Count == 0 ? total : $"{total} - {string.Join(", ", parts)}";
    }

    private static string Pluralize(string singular, int count) =>
        count == 1 ? singular : $"{singular}s";

    private static bool StatusIs(CabinetRunStepSnapshot step, string status) =>
        step.Status.Equals(status, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<CabinetRunStepSnapshot> AppendOrReplace(
        IReadOnlyList<CabinetRunStepSnapshot> steps,
        CabinetRunStepSnapshot step,
        bool preserveExistingIndex = false)
    {
        List<CabinetRunStepSnapshot> next = steps.ToList();
        int existingIndex = next.FindIndex(existing =>
            existing.Key.Equals(step.Key, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0 && preserveExistingIndex)
        {
            next[existingIndex] = step;
            return next;
        }

        if (existingIndex >= 0)
            next.RemoveAt(existingIndex);

        next.Add(step);
        return next;
    }

    private static long DurationMs(DateTimeOffset startedAt, DateTimeOffset completedAt) =>
        Math.Max(0, (long)Math.Round((completedAt - startedAt).TotalMilliseconds));
}

public sealed record CabinetRunLogSnapshot(
    [property: JsonPropertyName("run_id")]
    string RunId,
    [property: JsonPropertyName("scope")]
    string Scope,
    [property: JsonPropertyName("trigger")]
    string Trigger,
    [property: JsonPropertyName("status")]
    string Status,
    [property: JsonPropertyName("started_at")]
    DateTimeOffset StartedAt,
    [property: JsonPropertyName("completed_at")]
    DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("duration_ms")]
    long? DurationMs,
    [property: JsonPropertyName("steps")]
    IReadOnlyList<CabinetRunStepSnapshot> Steps,
    [property: JsonPropertyName("state_source")]
    string? StateSource,
    [property: JsonPropertyName("used_restored_snapshot")]
    bool? UsedRestoredSnapshot,
    [property: JsonPropertyName("error_type")]
    string? ErrorType,
    [property: JsonPropertyName("error_message")]
    string? ErrorMessage);

public sealed record CabinetRunStepSnapshot(
    [property: JsonPropertyName("key")]
    string Key,
    [property: JsonPropertyName("label")]
    string Label,
    [property: JsonPropertyName("kind")]
    string Kind,
    [property: JsonPropertyName("status")]
    string Status,
    [property: JsonPropertyName("started_at")]
    DateTimeOffset StartedAt,
    [property: JsonPropertyName("completed_at")]
    DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("duration_ms")]
    long? DurationMs,
    [property: JsonPropertyName("detail")]
    string? Detail,
    [property: JsonPropertyName("minister")]
    string? Minister,
    [property: JsonPropertyName("state_source")]
    string? StateSource,
    [property: JsonPropertyName("used_restored_snapshot")]
    bool? UsedRestoredSnapshot,
    [property: JsonPropertyName("trace_path")]
    string? TracePath,
    [property: JsonPropertyName("rule_fired")]
    string? RuleFired,
    [property: JsonPropertyName("escalation_reason")]
    string? EscalationReason,
    [property: JsonPropertyName("advice_count")]
    int? AdviceCount,
    [property: JsonPropertyName("flag_count")]
    int? FlagCount,
    [property: JsonPropertyName("trace_note")]
    string? TraceNote,
    [property: JsonPropertyName("error_type")]
    string? ErrorType,
    [property: JsonPropertyName("error_message")]
    string? ErrorMessage,
    // WHY: the run dialog only uses one nested level today, but reusing the step
    // record keeps the SSE snapshot shape uniform.
    [property: JsonPropertyName("children")]
    IReadOnlyList<CabinetRunStepSnapshot> Children);
