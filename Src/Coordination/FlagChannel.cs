using RimBob.Core.Ministers;

namespace RimBob.Coordination;

public sealed class FlagChannel
{
    private readonly object _lock = new();
    private readonly Dictionary<string, AgentFlag> _active = new(StringComparer.OrdinalIgnoreCase);

    public void Publish(AgentFlag flag)
    {
        lock (_lock)
        {
            _active[flag.Id] = flag;
            PruneExpired(DateTimeOffset.UtcNow);
        }
    }

    public IReadOnlyList<AgentFlag> Active(FlagSeverity minimum = FlagSeverity.Low)
    {
        lock (_lock)
        {
            PruneExpired(DateTimeOffset.UtcNow);
            return _active.Values
                .Where(f => f.Severity >= minimum)
                .OrderByDescending(f => f.Severity)
                .ThenBy(f => f.Domain)
                .ThenBy(f => f.Id)
                .ToList();
        }
    }

    public void Clear(string id)
    {
        lock (_lock)
        {
            _active.Remove(id);
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        List<string> expired = _active
            .Where(kv => kv.Value.ExpiresAt is not null && kv.Value.ExpiresAt <= now)
            .Select(kv => kv.Key)
            .ToList();
        foreach (string id in expired)
            _active.Remove(id);
    }
}
