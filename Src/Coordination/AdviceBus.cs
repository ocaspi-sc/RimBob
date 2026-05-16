using RimBob.Core.Advice;

namespace RimBob.Coordination;

/// <summary>
/// In-process pub/sub for advice events. Host bridges this onto the SSE feed.
/// Synchronous invocation; SSE handlers internally queue via a bounded channel.
/// </summary>
public sealed class AdviceBus
{
    private readonly object _lock = new();
    private readonly Dictionary<string, AdviceItem> _activeAdvice = new();
    private readonly Dictionary<string, string> _ministerStateSummaries =
        new(StringComparer.OrdinalIgnoreCase);

    public event Action<AgendaUpdated>? AgendaUpdated;
    public event Action<AdviceItem>?    AdvicePublished;
    public event Action<AdviceSnapshot>? AdviceSnapshotPublished;

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

    public void ReplaceMinisterAdvice(
        string minister,
        IReadOnlyList<AdviceItem> advice,
        string? stateSummary = null)
    {
        if (string.IsNullOrWhiteSpace(minister))
            throw new ArgumentException("Minister name is required.", nameof(minister));

        foreach (AdviceItem item in advice)
        {
            if (!string.Equals(item.Minister, minister, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"Advice item '{item.Id}' belongs to '{item.Minister}', not '{minister}'.",
                    nameof(advice));
        }

        IReadOnlyList<AdviceItem> currentMinisterAdvice;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        lock (_lock)
        {
            PruneExpired(now);

            List<string> existingIds = _activeAdvice
                .Where(kv => string.Equals(kv.Value.Minister, minister, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();
            foreach (string id in existingIds)
                _activeAdvice.Remove(id);

            foreach (AdviceItem item in advice)
            {
                if (item.ExpiresAt > now)
                    _activeAdvice[item.Id] = item;
            }

            if (string.IsNullOrWhiteSpace(stateSummary))
                _ministerStateSummaries.Remove(minister);
            else
                _ministerStateSummaries[minister] = stateSummary.Trim();

            currentMinisterAdvice = SortAdvice(_activeAdvice.Values
                .Where(item => string.Equals(item.Minister, minister, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        AdviceSnapshotPublished?.Invoke(new AdviceSnapshot(minister, currentMinisterAdvice, NormalizeStateSummary(stateSummary)));
        foreach (AdviceItem item in currentMinisterAdvice)
            AdvicePublished?.Invoke(item);
    }

    public IReadOnlyList<AdviceItem> ActiveAdvice()
    {
        lock (_lock)
        {
            PruneExpired(DateTimeOffset.UtcNow);
            return SortAdvice(_activeAdvice.Values).ToList();
        }
    }

    public bool TryGetActiveAdvice(string id, out AdviceItem? advice)
    {
        lock (_lock)
        {
            PruneExpired(DateTimeOffset.UtcNow);
            return _activeAdvice.TryGetValue(id, out advice);
        }
    }

    public AdviceSnapshot ActiveSnapshot()
    {
        lock (_lock)
        {
            PruneExpired(DateTimeOffset.UtcNow);
            Dictionary<string, string> summaries = new(_ministerStateSummaries, StringComparer.OrdinalIgnoreCase);
            return new AdviceSnapshot(
                Minister: null,
                Advice: SortAdvice(_activeAdvice.Values).ToList(),
                StateSummary: null,
                StateSummaries: summaries);
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

    private static IOrderedEnumerable<AdviceItem> SortAdvice(IEnumerable<AdviceItem> advice) =>
        advice
            .OrderByDescending(a => a.Priority)
            .ThenByDescending(a => a.IssuedAt);

    private static string? NormalizeStateSummary(string? stateSummary) =>
        string.IsNullOrWhiteSpace(stateSummary) ? null : stateSummary.Trim();
}

public sealed record AgendaUpdated(MayorAgenda Agenda);
public sealed record AdviceSnapshot(
    string? Minister,
    IReadOnlyList<AdviceItem> Advice,
    string? StateSummary = null,
    IReadOnlyDictionary<string, string>? StateSummaries = null);
