namespace RimBob.Coordination;

/// <summary>
/// Tracks the Mayor's current run state so the dashboard can show a live timer
/// and surface the last error without scraping logs.
/// </summary>
public sealed class MayorStatus
{
    private readonly object _lock = new();
    private bool _running;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _completedAt;
    private DateTimeOffset? _lastLlmSuccessAt;
    private string? _lastError;

    public bool IsRunning            { get { lock (_lock) return _running; } }
    public DateTimeOffset? StartedAt { get { lock (_lock) return _startedAt; } }
    public DateTimeOffset? CompletedAt { get { lock (_lock) return _completedAt; } }
    public DateTimeOffset? LastLlmSuccessAt { get { lock (_lock) return _lastLlmSuccessAt; } }
    public string? LastError         { get { lock (_lock) return _lastError; } }

    public void Begin()
    {
        lock (_lock)
        {
            _running   = true;
            _startedAt = DateTimeOffset.UtcNow;
            _lastError = null;
        }
    }

    public void End(string? error = null)
    {
        lock (_lock)
        {
            _running      = false;
            _completedAt  = DateTimeOffset.UtcNow;
            _lastError    = error;
        }
    }

    public void MarkLlmSuccess()
    {
        lock (_lock) _lastLlmSuccessAt = DateTimeOffset.UtcNow;
    }
}
