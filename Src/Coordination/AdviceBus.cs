using RimAI.Core.Advice;

namespace RimAI.Coordination;

/// <summary>
/// In-process pub/sub for advice events. Host bridges this onto the SSE feed.
/// Synchronous invocation; SSE handlers internally queue via a bounded channel.
/// </summary>
public sealed class AdviceBus
{
    private readonly object _lock = new();
    private readonly Dictionary<string, AdviceItem> _activeAdvice = new();

    public event Action<AgendaUpdated>? AgendaUpdated;
    public event Action<AdviceItem>?    AdvicePublished;

    public void Publish(AgendaUpdated e) => AgendaUpdated?.Invoke(e);
    public void Publish(AdviceItem item)
    {
        lock (_lock)
        {
            _activeAdvice[item.Id] = item;
            PruneExpired(DateTimeOffset.UtcNow);
        }
        AdvicePublished?.Invoke(item);
    }

    public IReadOnlyList<AdviceItem> ActiveAdvice()
    {
        lock (_lock)
        {
            PruneExpired(DateTimeOffset.UtcNow);
            return _activeAdvice.Values
                .OrderByDescending(a => a.Severity)
                .ThenByDescending(a => a.IssuedAt)
                .ToList();
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        List<string> expired = _activeAdvice
            .Where(kv => kv.Value.ExpiresAt <= now)
            .Select(kv => kv.Key)
            .ToList();
        foreach (string id in expired)
            _activeAdvice.Remove(id);
    }
}

public sealed record AgendaUpdated(MayorAgenda Agenda);
