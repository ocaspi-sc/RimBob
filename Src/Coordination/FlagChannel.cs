using RimBob.Core.Ministers;

namespace RimBob.Coordination;

public sealed class FlagChannel
{
    private readonly object _lock = new();
    private readonly Dictionary<string, AgentFlag> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<PublishedAgentFlag> _published = new();
    private long _publishSequence;

    public long CurrentSequence
    {
        get
        {
            lock (_lock)
            {
                return _publishSequence;
            }
        }
    }

    public void Publish(AgentFlag flag)
    {
        lock (_lock)
        {
            _publishSequence++;
            _active[flag.Id] = flag;
            _published.Enqueue(new PublishedAgentFlag(_publishSequence, flag));
            while (_published.Count > 256)
                _published.Dequeue();

            PruneExpired(DateTimeOffset.UtcNow);
        }
    }

    public IReadOnlyList<PublishedAgentFlag> PublishedAfter(long sequence)
    {
        lock (_lock)
        {
            return _published
                .Where(entry => entry.Sequence > sequence)
                .OrderBy(entry => entry.Sequence)
                .ToList();
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

public sealed record PublishedAgentFlag(long Sequence, AgentFlag Flag);
