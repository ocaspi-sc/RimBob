using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;

namespace RimBob.Coordination;

/// <summary>
/// Latest-only durable output snapshots for all ministers.
/// Mayor keeps its structured MayorAgenda; feeders keep their AdviceSnapshot.
/// </summary>
public sealed class MinisterOutputStore
{
    private const int SnapshotSchemaVersion = 2;
    private const string MayorMinister = "mayor";
    private const string MayorOutputKind = "mayor_agenda";
    private const string AdviceOutputKind = "advice_snapshot";

    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _lock = new();
    private readonly SemaphoreSlim _mayorMutationGate = new(1, 1);
    private readonly SemaphoreSlim _adviceFlushGate = new(1, 1);
    private readonly Dictionary<string, AdviceSnapshot> _adviceSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PendingAdviceSnapshot> _pendingAdviceSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MinisterOutputSnapshotStatus> _snapshotStatuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _rootPath;
    private int _adviceFlushQueued;
    private MayorAgenda? _currentMayorAgenda;

    public MinisterOutputStore() : this(null, null, [], [])
    {
    }

    private MinisterOutputStore(
        string? rootPath,
        MayorAgenda? currentMayorAgenda,
        IReadOnlyList<AdviceSnapshot> adviceSnapshots,
        IReadOnlyList<MinisterOutputSnapshotStatus> snapshotStatuses)
    {
        _rootPath = rootPath;
        _currentMayorAgenda = currentMayorAgenda;

        foreach (AdviceSnapshot snapshot in adviceSnapshots)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.Minister))
            {
                AdviceSnapshot normalized = AdviceSnapshotPolicy.Normalize(snapshot);
                _adviceSnapshots[normalized.Minister!] = normalized;
            }
        }

        foreach (MinisterOutputSnapshotStatus status in snapshotStatuses)
            _snapshotStatuses[status.Minister] = status;
    }

    public MayorAgenda? CurrentMayorAgenda
    {
        get { lock (_lock) return _currentMayorAgenda; }
    }

    public bool HasAnyOutput
    {
        get
        {
            lock (_lock)
                return _currentMayorAgenda is not null || _adviceSnapshots.Count > 0;
        }
    }

    public static async Task<MinisterOutputStore> LoadAsync(string rootPath, CancellationToken ct = default)
    {
        if (!Directory.Exists(rootPath))
            return new MinisterOutputStore(rootPath, null, [], []);

        MayorAgenda? mayorAgenda = null;
        List<AdviceSnapshot> adviceSnapshots = [];
        List<MinisterOutputSnapshotStatus> statuses = [];

        foreach (string path in Directory.EnumerateFiles(rootPath, "*.json"))
        {
            await using FileStream stream = File.OpenRead(path);
            MinisterOutputEnvelope? envelope =
                await JsonSerializer.DeserializeAsync<MinisterOutputEnvelope>(stream, SnapshotJson, ct);
            if (envelope is null)
                throw new InvalidOperationException($"Minister output snapshot is empty: {path}");

            if (envelope.SchemaVersion != SnapshotSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported minister output snapshot schema {envelope.SchemaVersion} in {path}.");
            }

            string minister = NormalizeMinisterName(envelope.Minister);
            switch (envelope.OutputKind)
            {
                case MayorOutputKind:
                    if (!string.Equals(minister, MayorMinister, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Mayor output snapshot was stored for '{envelope.Minister}' in {path}.");

                    mayorAgenda = envelope.Payload.Deserialize<MayorAgenda>(SnapshotJson)
                                  ?? throw new InvalidOperationException($"Mayor output payload is empty: {path}");
                    statuses.Add(StatusFor(envelope, path, "available", null));
                    break;

                case AdviceOutputKind:
                    AdviceSnapshot adviceSnapshot = envelope.Payload.Deserialize<AdviceSnapshot>(SnapshotJson)
                                                    ?? throw new InvalidOperationException($"Advice output payload is empty: {path}");
                    if (string.IsNullOrWhiteSpace(adviceSnapshot.Minister))
                        throw new InvalidOperationException($"Advice output snapshot has no minister: {path}");

                    adviceSnapshots.Add(AdviceSnapshotPolicy.Normalize(adviceSnapshot));
                    statuses.Add(StatusFor(envelope, path, "available", null));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported minister output kind '{envelope.OutputKind}' in {path}.");
            }
        }

        return new MinisterOutputStore(rootPath, mayorAgenda, adviceSnapshots, statuses);
    }

    public IReadOnlyList<AdviceSnapshot> AdviceSnapshots()
    {
        lock (_lock)
            return _adviceSnapshots.Values.ToArray();
    }

    public AdviceSnapshot? GetAdviceSnapshot(string minister)
    {
        lock (_lock)
        {
            string normalized = NormalizeMinisterName(minister);
            return _adviceSnapshots.TryGetValue(normalized, out AdviceSnapshot? snapshot)
                ? snapshot
                : null;
        }
    }

    public async Task<MayorAgenda> UpdateMayorAsync(
        MayorAgendaInput proposed,
        GameDate updatedGameDate,
        CancellationToken ct = default)
    {
        await _mayorMutationGate.WaitAsync(ct);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            MayorAgenda next;
            lock (_lock)
            {
                int version = (_currentMayorAgenda?.Version ?? 0) + 1;
                next = new MayorAgenda(
                    Version: version,
                    UpdatedGameDate: updatedGameDate,
                    GeneratedAt: now,
                    Posture: proposed.Posture,
                    StateOfTheUnion: proposed.StateOfTheUnion,
                    UpdateNotes: proposed.UpdateNotes,
                    ShortTerm: proposed.ShortTerm,
                    LongTerm: proposed.LongTerm,
                    CabinetDirection: proposed.CabinetDirection,
                    GuideCitations: proposed.GuideCitations ?? []);
            }

            await PersistSnapshotAsync(
                MayorMinister,
                MayorOutputKind,
                next.Version,
                now,
                next,
                ct);

            lock (_lock)
            {
                _currentMayorAgenda = next;
                _snapshotStatuses[MayorMinister] = new MinisterOutputSnapshotStatus(
                    MayorMinister,
                    MayorOutputKind,
                    SnapshotPath(MayorMinister),
                    next.Version,
                    now,
                    "available",
                    null);
            }

            return next;
        }
        finally
        {
            _mayorMutationGate.Release();
        }
    }

    public void QueueAdviceSnapshot(AdviceSnapshot snapshot)
    {
        AdviceSnapshot normalized = AdviceSnapshotPolicy.Normalize(snapshot);
        foreach (AdviceSnapshot ministerSnapshot in SplitAdviceSnapshot(normalized))
            QueueSingleAdviceSnapshot(ministerSnapshot);
    }

    public async Task FlushPendingAdviceAsync(CancellationToken ct = default)
    {
        await _adviceFlushGate.WaitAsync(ct);
        try
        {
            while (true)
            {
                PendingAdviceSnapshot[] batch;
                lock (_lock)
                {
                    if (_pendingAdviceSnapshots.Count == 0)
                    {
                        _adviceFlushQueued = 0;
                        return;
                    }

                    batch = _pendingAdviceSnapshots.Values.ToArray();
                    _pendingAdviceSnapshots.Clear();
                }

                foreach (PendingAdviceSnapshot pending in batch)
                {
                    try
                    {
                        await PersistSnapshotAsync(
                            pending.Snapshot.Minister!,
                            AdviceOutputKind,
                            pending.Generation,
                            pending.PersistedAt,
                            pending.Snapshot,
                            ct);
                        MarkAdviceSnapshotStatus(pending, "available", null);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        MarkAdviceSnapshotStatus(pending, "flush_failed", ex.Message);
                    }
                }
            }
        }
        finally
        {
            _adviceFlushGate.Release();
        }
    }

    public MinisterOutputStoreStatus GetStatus()
    {
        lock (_lock)
        {
            return new MinisterOutputStoreStatus(
                RootPath: _rootPath,
                Exists: _rootPath is not null && Directory.Exists(_rootPath),
                Snapshots: _snapshotStatuses.Values
                    .OrderBy(status => status.Minister, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    private void QueueSingleAdviceSnapshot(AdviceSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Minister))
            return;

        string minister = NormalizeMinisterName(snapshot.Minister);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int generation;
        lock (_lock)
        {
            generation = (_snapshotStatuses.TryGetValue(minister, out MinisterOutputSnapshotStatus? previous)
                ? previous.Generation ?? 0
                : 0) + 1;
            AdviceSnapshot normalized = snapshot with { Minister = minister };
            PendingAdviceSnapshot pending = new(normalized, generation, now);
            _adviceSnapshots[minister] = normalized;
            _pendingAdviceSnapshots[minister] = pending;
            _snapshotStatuses[minister] = new MinisterOutputSnapshotStatus(
                minister,
                AdviceOutputKind,
                SnapshotPath(minister),
                generation,
                now,
                "pending_flush",
                null);

            if (_adviceFlushQueued == 0)
            {
                _adviceFlushQueued = 1;
                _ = Task.Run(() => FlushPendingAdviceAsync(CancellationToken.None));
            }
        }
    }

    private void MarkAdviceSnapshotStatus(PendingAdviceSnapshot pending, string state, string? error)
    {
        string minister = NormalizeMinisterName(pending.Snapshot.Minister!);
        lock (_lock)
        {
            _snapshotStatuses[minister] = new MinisterOutputSnapshotStatus(
                minister,
                AdviceOutputKind,
                SnapshotPath(minister),
                pending.Generation,
                pending.PersistedAt,
                state,
                error);
        }
    }

    private async Task PersistSnapshotAsync<T>(
        string minister,
        string outputKind,
        int generation,
        DateTimeOffset persistedAt,
        T payload,
        CancellationToken ct)
    {
        string? path = SnapshotPath(minister);
        if (path is null)
            return;

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        MinisterOutputFile<T> file = new(
            SnapshotSchemaVersion,
            NormalizeMinisterName(minister),
            outputKind,
            persistedAt,
            generation,
            payload);
        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        string json = JsonSerializer.Serialize(file, SnapshotJson);

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

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            throw;
        }
    }

    private string? SnapshotPath(string minister)
    {
        if (_rootPath is null)
            return null;

        return Path.Combine(_rootPath, $"{NormalizeMinisterName(minister)}.json");
    }

    private static IReadOnlyList<AdviceSnapshot> SplitAdviceSnapshot(AdviceSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Minister))
            return [snapshot];

        HashSet<string> ministers = new(StringComparer.OrdinalIgnoreCase);
        foreach (AdviceItem item in snapshot.Advice)
            ministers.Add(item.Minister);
        if (snapshot.StateSummaries is not null)
            foreach (string minister in snapshot.StateSummaries.Keys)
                ministers.Add(minister);
        if (snapshot.Chains is not null)
            foreach (string minister in snapshot.Chains.Keys)
                ministers.Add(minister);
        if (snapshot.Flags is not null)
            foreach (AgentFlag flag in snapshot.Flags)
                ministers.Add(flag.SourceMinister);

        return ministers
            .Select(minister => new AdviceSnapshot(
                Minister: NormalizeMinisterName(minister),
                Advice: snapshot.Advice
                    .Where(item => item.Minister.Equals(minister, StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
                StateSummary: snapshot.StateSummaries is not null &&
                              snapshot.StateSummaries.TryGetValue(minister, out string? summary)
                    ? summary
                    : null,
                StateSummaries: null,
                Chain: snapshot.Chains is not null &&
                       snapshot.Chains.TryGetValue(minister, out AdviceChainModel? chain)
                    ? chain
                    : null,
                Chains: null,
                Flags: snapshot.Flags?
                    .Where(flag => flag.SourceMinister.Equals(minister, StringComparison.OrdinalIgnoreCase))
                    .ToArray()))
            .ToArray();
    }

    private static MinisterOutputSnapshotStatus StatusFor(
        MinisterOutputEnvelope envelope,
        string path,
        string state,
        string? error) =>
        new(
            NormalizeMinisterName(envelope.Minister),
            envelope.OutputKind,
            path,
            envelope.Generation,
            envelope.PersistedAt,
            state,
            error);

    private static string NormalizeMinisterName(string minister)
    {
        string normalized = MinisterRegistry.NormalizeKey(minister);
        return normalized is "food" or "chef" ? "chef" : normalized;
    }

    private sealed record PendingAdviceSnapshot(
        AdviceSnapshot Snapshot,
        int Generation,
        DateTimeOffset PersistedAt);

    private sealed record MinisterOutputEnvelope(
        [property: JsonPropertyName("schema_version")]
        int SchemaVersion,
        [property: JsonPropertyName("minister")]
        string Minister,
        [property: JsonPropertyName("output_kind")]
        string OutputKind,
        [property: JsonPropertyName("persisted_at")]
        DateTimeOffset PersistedAt,
        [property: JsonPropertyName("generation")]
        int Generation,
        [property: JsonPropertyName("payload")]
        JsonElement Payload);

    private sealed record MinisterOutputFile<T>(
        [property: JsonPropertyName("schema_version")]
        int SchemaVersion,
        [property: JsonPropertyName("minister")]
        string Minister,
        [property: JsonPropertyName("output_kind")]
        string OutputKind,
        [property: JsonPropertyName("persisted_at")]
        DateTimeOffset PersistedAt,
        [property: JsonPropertyName("generation")]
        int Generation,
        [property: JsonPropertyName("payload")]
        T Payload);
}

public sealed record MinisterOutputStoreStatus(
    [property: JsonPropertyName("root_path")]
    string? RootPath,
    [property: JsonPropertyName("exists")]
    bool Exists,
    [property: JsonPropertyName("snapshots")]
    IReadOnlyList<MinisterOutputSnapshotStatus> Snapshots);

public sealed record MinisterOutputSnapshotStatus(
    [property: JsonPropertyName("minister")]
    string Minister,
    [property: JsonPropertyName("output_kind")]
    string OutputKind,
    [property: JsonPropertyName("path")]
    string? Path,
    [property: JsonPropertyName("generation")]
    int? Generation,
    [property: JsonPropertyName("persisted_at")]
    DateTimeOffset? PersistedAt,
    [property: JsonPropertyName("state")]
    string State,
    [property: JsonPropertyName("last_error")]
    string? LastError);
