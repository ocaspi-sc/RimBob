using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;

namespace RimBob.Coordination;

/// <summary>
/// In-process pub/sub for advice events. Host bridges this onto the SSE feed.
/// Synchronous invocation; SSE handlers internally queue via a bounded channel.
/// </summary>
public sealed class AdviceBus
{
    private readonly object _lock = new();
    private readonly MinisterOutputStore? _outputStore;
    private readonly Dictionary<string, AdviceItem> _activeAdvice = new();
    private readonly Dictionary<string, string> _ministerStateSummaries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AdviceChainModel> _ministerChains =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<AgentFlag>> _ministerFlags =
        new(StringComparer.OrdinalIgnoreCase);

    public event Action<AgendaUpdated>? AgendaUpdated;
    public event Action<AdviceItem>?    AdvicePublished;
    public event Action<AdviceSnapshot>? AdviceSnapshotPublished;

    public AdviceBus(MinisterOutputStore? outputStore = null)
    {
        _outputStore = outputStore;
        if (outputStore is not null)
            Hydrate(outputStore.AdviceSnapshots());
    }

    public void Publish(AgendaUpdated e) => AgendaUpdated?.Invoke(e);
    public void Publish(AdviceItem item)
    {
        AdviceSnapshot normalizedSnapshot = AdviceSnapshotPolicy.Normalize(new AdviceSnapshot(item.Minister, [item]));
        AdviceItem normalizedItem = normalizedSnapshot.Advice.Single();
        AdviceSnapshot snapshot;
        lock (_lock)
        {
            _activeAdvice[normalizedItem.Id] = normalizedItem;
            if (normalizedSnapshot.Flags is { Count: > 0 })
                _ministerFlags[normalizedItem.Minister] = normalizedSnapshot.Flags;
            snapshot = BuildMinisterSnapshotLocked(normalizedItem.Minister);
        }
        _outputStore?.QueueAdviceSnapshot(snapshot);
        AdvicePublished?.Invoke(normalizedItem);
    }

    public void ReplaceMinisterAdvice(
        string minister,
        IReadOnlyList<AdviceItem> advice,
        string? stateSummary = null,
        AdviceChainModel? chain = null,
        IReadOnlyList<AgentFlag>? flags = null)
    {
        if (string.IsNullOrWhiteSpace(minister))
            throw new ArgumentException("Minister name is required.", nameof(minister));

        AdviceSnapshot incoming = AdviceSnapshotPolicy.Normalize(new AdviceSnapshot(
            minister,
            advice,
            NormalizeStateSummary(stateSummary),
            Chain: chain,
            Flags: flags));

        foreach (AdviceItem item in incoming.Advice)
        {
            if (!string.Equals(item.Minister, minister, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"Advice item '{item.Id}' belongs to '{item.Minister}', not '{minister}'.",
                    nameof(advice));
        }

        IReadOnlyList<AdviceItem> currentMinisterAdvice;

        lock (_lock)
        {
            List<string> existingIds = _activeAdvice
                .Where(kv => string.Equals(kv.Value.Minister, minister, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();
            foreach (string id in existingIds)
                _activeAdvice.Remove(id);

            foreach (AdviceItem item in incoming.Advice)
                _activeAdvice[item.Id] = item;

            if (string.IsNullOrWhiteSpace(incoming.StateSummary))
                _ministerStateSummaries.Remove(minister);
            else
                _ministerStateSummaries[minister] = incoming.StateSummary.Trim();

            if (incoming.Chain is null)
                _ministerChains.Remove(minister);
            else
                _ministerChains[minister] = incoming.Chain;

            if (incoming.Flags is null || incoming.Flags.Count == 0)
                _ministerFlags.Remove(minister);
            else
                _ministerFlags[minister] = incoming.Flags;

            currentMinisterAdvice = SortAdvice(_activeAdvice.Values
                .Where(item => string.Equals(item.Minister, minister, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        AdviceSnapshot snapshot = new(
            minister,
            currentMinisterAdvice,
            incoming.StateSummary,
            Chain: incoming.Chain,
            Flags: incoming.Flags);
        _outputStore?.QueueAdviceSnapshot(snapshot);
        AdviceSnapshotPublished?.Invoke(snapshot);
        foreach (AdviceItem item in currentMinisterAdvice)
            AdvicePublished?.Invoke(item);
    }

    public IReadOnlyList<AdviceItem> ActiveAdvice()
    {
        lock (_lock)
        {
            return SortAdvice(_activeAdvice.Values).ToList();
        }
    }

    public bool TryGetActiveAdvice(string id, out AdviceItem? advice)
    {
        lock (_lock)
        {
            return _activeAdvice.TryGetValue(id, out advice);
        }
    }

    public bool RemoveAppliedAction(string adviceId, int actionIndex)
    {
        AdviceItem? updatedAdvice = null;
        AdviceSnapshot snapshot;
        AdviceSnapshot? ministerSnapshot;
        lock (_lock)
        {
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
            IReadOnlyList<AgentFlag> activeFlags = _ministerFlags.Values.SelectMany(current => current).ToList();
            ministerSnapshot = BuildMinisterSnapshotLocked(advice.Minister);
            snapshot = new AdviceSnapshot(
                Minister: null,
                Advice: SortAdvice(_activeAdvice.Values).ToList(),
                StateSummary: null,
                StateSummaries: summaries,
                Chain: null,
                Chains: chains,
                Flags: activeFlags);
        }

        if (ministerSnapshot is not null)
            _outputStore?.QueueAdviceSnapshot(ministerSnapshot);
        AdviceSnapshotPublished?.Invoke(snapshot);
        if (updatedAdvice is not null)
            AdvicePublished?.Invoke(updatedAdvice);
        return true;
    }

    public void Hydrate(IReadOnlyList<AdviceSnapshot> snapshots)
    {
        lock (_lock)
        {
            _activeAdvice.Clear();
            _ministerStateSummaries.Clear();
            _ministerChains.Clear();
            _ministerFlags.Clear();

            foreach (AdviceSnapshot originalSnapshot in snapshots)
            {
                AdviceSnapshot snapshot = AdviceSnapshotPolicy.Normalize(originalSnapshot);
                if (string.IsNullOrWhiteSpace(snapshot.Minister))
                    continue;

                string minister = snapshot.Minister;
                foreach (AdviceItem item in snapshot.Advice)
                    _activeAdvice[item.Id] = item;

                if (!string.IsNullOrWhiteSpace(snapshot.StateSummary))
                    _ministerStateSummaries[minister] = snapshot.StateSummary.Trim();

                if (snapshot.Chain is not null)
                    _ministerChains[minister] = snapshot.Chain;

                if (snapshot.Flags is { Count: > 0 })
                    _ministerFlags[minister] = snapshot.Flags;
            }
        }
    }

    public AdviceSnapshot ActiveSnapshot()
    {
        lock (_lock)
        {
            Dictionary<string, string> summaries = new(_ministerStateSummaries, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, AdviceChainModel> chains = new(_ministerChains, StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<AgentFlag> activeFlags = _ministerFlags.Values.SelectMany(current => current).ToList();
            return new AdviceSnapshot(
                Minister: null,
                Advice: SortAdvice(_activeAdvice.Values).ToList(),
                StateSummary: null,
                StateSummaries: summaries,
                Chain: null,
                Chains: chains,
                Flags: activeFlags);
        }
    }

    private AdviceSnapshot BuildMinisterSnapshotLocked(string minister)
    {
        string? stateSummary = _ministerStateSummaries.TryGetValue(minister, out string? summary)
            ? summary
            : null;
        AdviceChainModel? chain = _ministerChains.TryGetValue(minister, out AdviceChainModel? currentChain)
            ? currentChain
            : null;
        IReadOnlyList<AgentFlag>? flags = _ministerFlags.TryGetValue(minister, out IReadOnlyList<AgentFlag>? currentFlags)
            ? currentFlags
            : null;
        return new AdviceSnapshot(
            Minister: minister,
            Advice: SortAdvice(_activeAdvice.Values
                .Where(item => string.Equals(item.Minister, minister, StringComparison.OrdinalIgnoreCase)))
                .ToList(),
            StateSummary: stateSummary,
            StateSummaries: null,
            Chain: chain,
            Chains: null,
            Flags: flags);
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
    IReadOnlyDictionary<string, AdviceChainModel>? Chains = null,
    IReadOnlyList<AgentFlag>? Flags = null);
