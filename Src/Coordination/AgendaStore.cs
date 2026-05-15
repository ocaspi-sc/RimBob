using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RimAI.Core.Advice;

namespace RimAI.Coordination;

/// <summary>
/// Holds the current MayorAgenda plus a ring buffer of the last 30 versions.
/// </summary>
public sealed class AgendaStore
{
    private const int HistoryCap = 30;
    private const int SnapshotSchemaVersion = 1;
    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        WriteIndented = true
    };

    private readonly object _lock = new();
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly LinkedList<MayorAgenda> _history = new();
    private readonly string? _snapshotPath;
    private MayorAgenda? _current;

    public AgendaStore() : this(null, null, [])
    {
    }

    private AgendaStore(string? snapshotPath, MayorAgenda? current, IReadOnlyList<MayorAgenda> history)
    {
        _snapshotPath = snapshotPath;
        _current = current;

        foreach (MayorAgenda agenda in history.Take(HistoryCap))
            _history.AddLast(agenda);
    }

    public MayorAgenda? Current
    {
        get { lock (_lock) return _current; }
    }

    public IReadOnlyList<MayorAgenda> History
    {
        get { lock (_lock) return _history.ToArray(); }
    }

    public static async Task<AgendaStore> LoadAsync(string snapshotPath, CancellationToken ct = default)
    {
        if (!File.Exists(snapshotPath))
            return new AgendaStore(snapshotPath, null, []);

        await using FileStream stream = File.OpenRead(snapshotPath);
        AgendaStoreSnapshot? snapshot = await JsonSerializer.DeserializeAsync<AgendaStoreSnapshot>(stream, SnapshotJson, ct);
        if (snapshot is null)
            throw new InvalidOperationException($"Agenda store snapshot is empty: {snapshotPath}");

        if (snapshot.SchemaVersion != SnapshotSchemaVersion)
            throw new InvalidOperationException(
                $"Unsupported agenda store snapshot schema {snapshot.SchemaVersion} in {snapshotPath}.");

        return new AgendaStore(snapshotPath, snapshot.Current, NormalizeHistory(snapshot.History));
    }

    /// <summary>
    /// Stamps the proposed agenda with a monotonic Version (previous + 1, starting at 1)
    /// and the supplied tick, then promotes it to Current and pushes the previous Current
    /// onto the head of History (newest first), capped at 30.
    /// </summary>
    public async Task<MayorAgenda> UpdateAsync(
        MayorAgendaInput proposed,
        string updatedInGameTick,
        CancellationToken ct = default)
    {
        await _mutationGate.WaitAsync(ct);
        try
        {
            MayorAgenda next;
            IReadOnlyList<MayorAgenda> nextHistory;

            lock (_lock)
            {
                int version = (_current?.Version ?? 0) + 1;
                next = new MayorAgenda(
                    Version:           version,
                    UpdatedInGameTick: updatedInGameTick,
                    GeneratedAt:       DateTimeOffset.UtcNow,
                    Posture:           proposed.Posture,
                    StateOfTheUnion:   proposed.StateOfTheUnion,
                    UpdateNotes:       proposed.UpdateNotes,
                    ShortTerm:         proposed.ShortTerm,
                    LongTerm:          proposed.LongTerm,
                    CabinetDirection:  proposed.CabinetDirection,
                    GuideCitations:    proposed.GuideCitations ?? []
                );

                nextHistory = BuildNextHistory(_current, _history);
            }

            await PersistSnapshotAsync(new AgendaStoreSnapshot(SnapshotSchemaVersion, next, nextHistory), ct);

            lock (_lock)
            {
                _current = next;
                _history.Clear();
                foreach (MayorAgenda agenda in nextHistory)
                    _history.AddLast(agenda);
            }

            return next;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task PersistSnapshotAsync(AgendaStoreSnapshot snapshot, CancellationToken ct)
    {
        if (_snapshotPath is null)
            return;

        string? directory = Path.GetDirectoryName(_snapshotPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string tempPath = $"{_snapshotPath}.{Guid.NewGuid():N}.tmp";
        string json = JsonSerializer.Serialize(snapshot, SnapshotJson);
        try
        {
            {
                await using FileStream stream = new(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 16 * 1024,
                    FileOptions.WriteThrough);
                await using StreamWriter writer = new(stream, Encoding.UTF8);
                await writer.WriteAsync(json.AsMemory(), ct);
                await writer.FlushAsync(ct);
            }

            File.Move(tempPath, _snapshotPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            throw;
        }
    }

    private static IReadOnlyList<MayorAgenda> BuildNextHistory(
        MayorAgenda? current,
        IEnumerable<MayorAgenda> existingHistory)
    {
        List<MayorAgenda> history = new(HistoryCap);
        if (current is not null)
            history.Add(current);

        foreach (MayorAgenda agenda in existingHistory)
        {
            if (history.Count >= HistoryCap)
                break;

            history.Add(agenda);
        }

        return history;
    }

    private static IReadOnlyList<MayorAgenda> NormalizeHistory(IReadOnlyList<MayorAgenda>? history)
    {
        if (history is null)
            return [];

        return history.Take(HistoryCap).ToArray();
    }

    private sealed record AgendaStoreSnapshot(
        [property: JsonPropertyName("schema_version")]
        int SchemaVersion,
        [property: JsonPropertyName("current")]
        MayorAgenda? Current,
        [property: JsonPropertyName("history")]
        IReadOnlyList<MayorAgenda>? History);
}
