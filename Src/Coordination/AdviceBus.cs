using RimBob.Core.Advice;
using RimBob.Core.Briefings;

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
    private readonly Dictionary<string, AdviceChainModel> _ministerChains =
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
        string? stateSummary = null,
        AdviceChainModel? chain = null)
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

            if (chain is null)
                _ministerChains.Remove(minister);
            else
                _ministerChains[minister] = chain;

            currentMinisterAdvice = SortAdvice(_activeAdvice.Values
                .Where(item => string.Equals(item.Minister, minister, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        AdviceSnapshotPublished?.Invoke(new AdviceSnapshot(minister, currentMinisterAdvice, NormalizeStateSummary(stateSummary), Chain: chain));
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

    public bool RemoveAppliedAction(string adviceId, int actionIndex)
    {
        AdviceItem? updatedAdvice = null;
        AdviceSnapshot snapshot;
        lock (_lock)
        {
            PruneExpired(DateTimeOffset.UtcNow);
            if (!_activeAdvice.TryGetValue(adviceId, out AdviceItem? advice))
                return false;

            if (actionIndex < 0 || actionIndex >= advice.Actions.Count)
                return false;

            List<AdviceAction> actions = advice.Actions.ToList();
            actions.RemoveAt(actionIndex);
            if (actions.Count == 0)
            {
                _activeAdvice.Remove(adviceId);
            }
            else
            {
                updatedAdvice = advice with { Actions = actions };
                _activeAdvice[adviceId] = updatedAdvice;
            }

            Dictionary<string, string> summaries = new(_ministerStateSummaries, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, AdviceChainModel> chains = new(_ministerChains, StringComparer.OrdinalIgnoreCase);
            snapshot = new AdviceSnapshot(
                Minister: null,
                Advice: SortAdvice(_activeAdvice.Values).ToList(),
                StateSummary: null,
                StateSummaries: summaries,
                Chain: null,
                Chains: chains);
        }

        AdviceSnapshotPublished?.Invoke(snapshot);
        if (updatedAdvice is not null)
            AdvicePublished?.Invoke(updatedAdvice);
        return true;
    }

    public AdviceSnapshot ActiveSnapshot()
    {
        lock (_lock)
        {
            PruneExpired(DateTimeOffset.UtcNow);
            Dictionary<string, string> summaries = new(_ministerStateSummaries, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, AdviceChainModel> chains = new(_ministerChains, StringComparer.OrdinalIgnoreCase);
            return new AdviceSnapshot(
                Minister: null,
                Advice: SortAdvice(_activeAdvice.Values).ToList(),
                StateSummary: null,
                StateSummaries: summaries,
                Chain: null,
                Chains: chains);
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
    IReadOnlyDictionary<string, string>? StateSummaries = null,
    AdviceChainModel? Chain = null,
    IReadOnlyDictionary<string, AdviceChainModel>? Chains = null);
