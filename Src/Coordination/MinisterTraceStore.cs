using RimAI.Core.Ministers;

namespace RimAI.Coordination;

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
            EscalationReason: null,
            WakeupPayload: context.WakeupPayload,
            Flag: context.Flag,
            Note: "Dashboard v2 can see the wake trigger now; rule/LLM path details need minister decision logging.");

        lock (_lock)
        {
            _latest[minister] = snapshot;
        }
    }

    public void Complete(string minister)
    {
        lock (_lock)
        {
            if (!_latest.TryGetValue(minister, out MinisterTraceSnapshot? current)) return;
            _latest[minister] = current with
            {
                Status = "completed",
                CompletedAt = DateTimeOffset.UtcNow,
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
                    EscalationReason: null,
                    WakeupPayload: null,
                    Flag: null,
                    Note: ex.Message);
                return;
            }

            _latest[minister] = current with
            {
                Status = "failed",
                CompletedAt = now,
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
}

public sealed record MinisterTraceSnapshot(
    string Minister,
    string Trigger,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Path,
    string? RuleFired,
    string? EscalationReason,
    string? WakeupPayload,
    AgentFlag? Flag,
    string Note);
