using RimAI.Core.Advice;

namespace RimAI.Coordination;

/// <summary>
/// Holds the current MayorAgenda plus a ring buffer of the last 30 versions.
/// In-memory only for M1; re-derives from blank on Host restart.
/// </summary>
public sealed class AgendaStore
{
    private const int HistoryCap = 30;

    private readonly object _lock = new();
    private readonly LinkedList<MayorAgenda> _history = new();
    private MayorAgenda? _current;

    public MayorAgenda? Current
    {
        get { lock (_lock) return _current; }
    }

    public IReadOnlyList<MayorAgenda> History
    {
        get { lock (_lock) return _history.ToArray(); }
    }

    /// <summary>
    /// Stamps the proposed agenda with a monotonic Version (previous + 1, starting at 1)
    /// and the supplied tick, then promotes it to Current and pushes the previous Current
    /// onto the head of History (newest first), capped at 30.
    /// </summary>
    public MayorAgenda Update(MayorAgendaInput proposed, string updatedInGameTick)
    {
        lock (_lock)
        {
            var version = (_current?.Version ?? 0) + 1;
            var next = new MayorAgenda(
                Version:           version,
                UpdatedInGameTick: updatedInGameTick,
                Posture:           proposed.Posture,
                StateOfTheUnion:   proposed.StateOfTheUnion,
                UpdateNotes:       proposed.UpdateNotes,
                ShortTerm:         proposed.ShortTerm,
                LongTerm:          proposed.LongTerm,
                MinisterDirection: proposed.MinisterDirection
            );

            if (_current is not null)
            {
                _history.AddFirst(_current);
                while (_history.Count > HistoryCap) _history.RemoveLast();
            }
            _current = next;
            return next;
        }
    }
}

/// <summary>
/// Mayor's proposed agenda content. Version + tick are assigned by the store.
/// </summary>
public sealed record MayorAgendaInput(
    MayorPosture                        Posture,
    string                              StateOfTheUnion,
    string                              UpdateNotes,
    IReadOnlyList<AgendaItem>           ShortTerm,
    IReadOnlyList<AgendaItem>           LongTerm,
    IReadOnlyDictionary<string, string> MinisterDirection
);
