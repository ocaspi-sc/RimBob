namespace RimBob.Coordination;

public sealed class CorpusRestoreStatusStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, CorpusRestoreMinisterStatus> _ministers =
        new(StringComparer.OrdinalIgnoreCase);

    public void MarkFlagsRestored(string minister, DateTimeOffset capturedAt, int restoredCount)
    {
        lock (_lock)
        {
            CorpusRestoreMinisterStatus current = CurrentFor(minister);
            _ministers[minister] = current with
            {
                Flags = new CorpusRestoreLaneStatus(true, capturedAt, restoredCount)
            };
        }
    }

    public void MarkAdviceRestored(string minister, DateTimeOffset capturedAt, int restoredCount)
    {
        lock (_lock)
        {
            CorpusRestoreMinisterStatus current = CurrentFor(minister);
            _ministers[minister] = current with
            {
                Advice = new CorpusRestoreLaneStatus(true, capturedAt, restoredCount)
            };
        }
    }

    public CorpusRestoreStatus Snapshot()
    {
        lock (_lock)
        {
            return new CorpusRestoreStatus(
                _ministers.Values
                    .OrderBy(status => status.Minister, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    private CorpusRestoreMinisterStatus CurrentFor(string minister) =>
        _ministers.TryGetValue(minister, out CorpusRestoreMinisterStatus? current)
            ? current
            : new CorpusRestoreMinisterStatus(
                Minister: minister,
                Flags: CorpusRestoreLaneStatus.None,
                Advice: CorpusRestoreLaneStatus.None);
}

public sealed record CorpusRestoreStatus(
    IReadOnlyList<CorpusRestoreMinisterStatus> Ministers);

public sealed record CorpusRestoreMinisterStatus(
    string Minister,
    CorpusRestoreLaneStatus Flags,
    CorpusRestoreLaneStatus Advice);

public sealed record CorpusRestoreLaneStatus(
    bool RestoredFromCorpus,
    DateTimeOffset? CapturedAt,
    int RestoredCount)
{
    public static CorpusRestoreLaneStatus None { get; } = new(false, null, 0);
}
