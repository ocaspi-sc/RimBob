namespace RimBob.Host;

public sealed class SseDiagnostics
{
    private readonly object _lock = new();
    private int _activeConnections;
    private long _totalConnections;
    private long _eventCount;
    private long _errorCount;
    private DateTimeOffset? _lastConnectedAt;
    private DateTimeOffset? _lastDisconnectedAt;
    private DateTimeOffset? _lastEventAt;
    private DateTimeOffset? _lastErrorAt;
    private string? _lastEventType;
    private string? _lastEventId;
    private string? _lastError;

    public void Connected()
    {
        lock (_lock)
        {
            _activeConnections++;
            _totalConnections++;
            _lastConnectedAt = DateTimeOffset.UtcNow;
        }
    }

    public void Disconnected()
    {
        lock (_lock)
        {
            if (_activeConnections > 0) _activeConnections--;
            _lastDisconnectedAt = DateTimeOffset.UtcNow;
        }
    }

    public void EventSent(string eventType, string? eventId = null)
    {
        lock (_lock)
        {
            _eventCount++;
            _lastEventAt = DateTimeOffset.UtcNow;
            _lastEventType = eventType;
            _lastEventId = eventId;
        }
    }

    public void Error(string message)
    {
        lock (_lock)
        {
            _errorCount++;
            _lastErrorAt = DateTimeOffset.UtcNow;
            _lastError = message;
        }
    }

    public SseDiagnosticsSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new SseDiagnosticsSnapshot(
                ActiveConnections: _activeConnections,
                TotalConnections: _totalConnections,
                EventCount: _eventCount,
                ErrorCount: _errorCount,
                LastConnectedAt: _lastConnectedAt,
                LastDisconnectedAt: _lastDisconnectedAt,
                LastEventAt: _lastEventAt,
                LastEventType: _lastEventType,
                LastEventId: _lastEventId,
                LastErrorAt: _lastErrorAt,
                LastError: _lastError);
        }
    }
}

public sealed record SseDiagnosticsSnapshot(
    int ActiveConnections,
    long TotalConnections,
    long EventCount,
    long ErrorCount,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastDisconnectedAt,
    DateTimeOffset? LastEventAt,
    string? LastEventType,
    string? LastEventId,
    DateTimeOffset? LastErrorAt,
    string? LastError);
