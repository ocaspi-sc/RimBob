namespace RimAI.LLM;

public sealed class RawLlmOutputStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, RawLlmOutputSnapshot> _latest =
        new(StringComparer.OrdinalIgnoreCase);

    public void Record(RawLlmOutputSnapshot snapshot)
    {
        lock (_lock)
        {
            _latest[snapshot.Minister] = snapshot;
        }
    }

    public RawLlmOutputSnapshot? Latest(string minister)
    {
        lock (_lock)
        {
            return _latest.TryGetValue(minister, out RawLlmOutputSnapshot? snapshot)
                ? snapshot
                : null;
        }
    }

    public IReadOnlyList<RawLlmOutputSnapshot> LatestAll()
    {
        lock (_lock)
        {
            return _latest.Values
                .OrderBy(snapshot => snapshot.Minister)
                .ToList();
        }
    }

    public RawLlmOutputSnapshot? LatestAny()
    {
        lock (_lock)
        {
            return _latest.Values
                .OrderByDescending(snapshot => snapshot.CapturedAt)
                .FirstOrDefault();
        }
    }
}

public sealed record RawLlmOutputSnapshot(
    string Minister,
    string Provider,
    string Model,
    int? ApiKeyIndex,
    string? ApiKeyLabel,
    DateTimeOffset CapturedAt,
    long LatencyMs,
    string Status,
    string ParseMode,
    int SystemPromptChars,
    int UserPromptChars,
    string Text);
