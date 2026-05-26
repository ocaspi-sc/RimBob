using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RimBob.State.Parsing;

namespace RimBob.State;

public sealed class ColonyStateSnapshotStore
{
    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private readonly object _lock = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly string? _snapshotPath;
    private readonly ILogger<ColonyStateSnapshotStore> _log;

    private ColonyStateSnapshot? _latest;
    private DateTimeOffset? _lastSaveAt;
    private string? _lastSaveError;
    private string? _loadError;

    public ColonyStateSnapshotStore() : this(null, NullLogger<ColonyStateSnapshotStore>.Instance)
    {
    }

    public ColonyStateSnapshotStore(string? path, ILogger<ColonyStateSnapshotStore> log)
    {
        _snapshotPath = path;
        _log = log;
    }

    private ColonyStateSnapshotStore(
        string? path,
        ILogger<ColonyStateSnapshotStore> log,
        ColonyStateSnapshot? latest,
        string? loadError)
        : this(path, log)
    {
        _latest = latest;
        _loadError = loadError;
    }

    public ColonyStateSnapshot? Latest
    {
        get
        {
            lock (_lock)
                return _latest;
        }
    }

    public string? LoadError
    {
        get
        {
            lock (_lock)
                return _loadError;
        }
    }

    public static async Task<ColonyStateSnapshotStore> LoadAsync(
        string path,
        ILogger<ColonyStateSnapshotStore>? log = null,
        CancellationToken ct = default)
    {
        ILogger<ColonyStateSnapshotStore> logger = log ?? NullLogger<ColonyStateSnapshotStore>.Instance;
        if (!File.Exists(path))
            return new ColonyStateSnapshotStore(path, logger, null, null);

        try
        {
            string json = await File.ReadAllTextAsync(path, ct);
            int? schemaVersion = ReadSchemaVersion(json);
            if (schemaVersion is not null && schemaVersion != ColonyStateSnapshot.CurrentSchemaVersion)
            {
                DeleteUnsupportedSnapshot(path, logger);
                string error =
                    $"Unsupported colony state snapshot schema {schemaVersion} in {path}; deleted stale snapshot.";
                logger.LogWarning("{SnapshotLoadError}", error);
                return new ColonyStateSnapshotStore(path, logger, null, error);
            }

            ColonyStateSnapshot? snapshot = JsonSerializer.Deserialize<ColonyStateSnapshot>(json, SnapshotJson);
            if (snapshot is null)
            {
                string error = $"Colony state snapshot is empty: {path}";
                logger.LogWarning("{SnapshotLoadError}", error);
                return new ColonyStateSnapshotStore(path, logger, null, error);
            }

            if (snapshot.SchemaVersion != ColonyStateSnapshot.CurrentSchemaVersion)
            {
                DeleteUnsupportedSnapshot(path, logger);
                string error =
                    $"Unsupported colony state snapshot schema {snapshot.SchemaVersion} in {path}; deleted stale snapshot.";
                logger.LogWarning("{SnapshotLoadError}", error);
                return new ColonyStateSnapshotStore(path, logger, null, error);
            }

            return new ColonyStateSnapshotStore(path, logger, snapshot, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            string error = $"Could not load colony state snapshot {path}: {ex.Message}";
            logger.LogWarning(ex, "Could not load colony state snapshot {SnapshotPath}", path);
            return new ColonyStateSnapshotStore(path, logger, null, error);
        }
    }

    private static int? ReadSchemaVersion(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("schema_version", out JsonElement schema))
                return null;

            return schema.ValueKind == JsonValueKind.Number && schema.TryGetInt32(out int version)
                ? version
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void DeleteUnsupportedSnapshot(
        string path,
        ILogger<ColonyStateSnapshotStore> logger)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not delete unsupported colony state snapshot {SnapshotPath}", path);
        }
    }

    public async Task SaveAsync(ColonyState state, CancellationToken ct = default)
    {
        ColonyStateSnapshot snapshot = CreateSnapshot(state);

        await _saveGate.WaitAsync(ct);
        try
        {
            if (_snapshotPath is not null)
                await PersistSnapshotAsync(snapshot, ct);

            lock (_lock)
            {
                _latest = snapshot;
                _lastSaveAt = DateTimeOffset.UtcNow;
                _lastSaveError = null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_lock)
                _lastSaveError = ex.Message;

            throw;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public void RestoreInto(ColonyState state)
    {
        ColonyStateSnapshot? snapshot = Latest;
        if (snapshot is null)
            return;

        state.Map.Update(snapshot.Map);
        state.Economy.Update(snapshot.Economy);
        state.Colonists.Update(snapshot.Colonists);
        state.Rooms.Update(snapshot.Rooms);
        state.Stockpiles.Update(snapshot.Stockpiles);
        state.Buildings.Update(snapshot.Buildings);
        state.WorkTables.Update(snapshot.WorkTables);
        state.Power.Update(snapshot.Power);
        state.Threats.Update(snapshot.Threats);
        state.Weather.Update(snapshot.Weather);
        state.Farm.Update(snapshot.Farm);
        state.Plants.Update(snapshot.Plants);
        state.Things.Update(snapshot.Things);
        state.ThingDefs.Update(snapshot.ThingDefs);
        state.AnimalDefs.Update(snapshot.AnimalDefs);
        state.Terrain.Update(snapshot.Terrain);
        state.StoredResources.Update(snapshot.StoredResources);
        state.Animals.Update(snapshot.Animals);
        state.Resources.Update(snapshot.Resources);
        state.Research.Update(snapshot.Research);
        state.LastRefreshSource = ColonyStateOrigin.Snapshot;
        state.LastLiveRefreshAt = null;
    }

    public ColonySnapshotStatus GetStatus()
    {
        lock (_lock)
        {
            TimeSpan? age = _latest is null
                ? null
                : DateTimeOffset.UtcNow - _latest.CapturedAt;

            if (age.HasValue && age.Value.Ticks < 0)
                age = TimeSpan.Zero;

            return new ColonySnapshotStatus(
                Path: _snapshotPath,
                HasSnapshot: _latest is not null,
                SnapshotId: _latest?.SnapshotId,
                CapturedAt: _latest?.CapturedAt,
                Age: age,
                GameTick: _latest?.GameTick,
                GameDate: _latest?.GameDate,
                MapId: _latest?.MapId,
                Source: _latest?.Source,
                SchemaVersion: _latest?.SchemaVersion,
                LastSaveAt: _lastSaveAt,
                LastSaveError: _lastSaveError,
                LoadError: _loadError);
        }
    }

    private async Task PersistSnapshotAsync(ColonyStateSnapshot snapshot, CancellationToken ct)
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

    private static ColonyStateSnapshot CreateSnapshot(ColonyState state)
    {
        DateTimeOffset capturedAt = state.LastLiveRefreshAt ?? DateTimeOffset.UtcNow;

        return new ColonyStateSnapshot
        {
            SchemaVersion = ColonyStateSnapshot.CurrentSchemaVersion,
            SnapshotId = Guid.NewGuid().ToString("N"),
            CapturedAt = capturedAt,
            Source = "live",
            MapId = state.Map.Value.Id,
            GameTick = state.Economy.Value.Tick,
            GameDate = RimDateParser.Parse(state.Economy.Value.DateTimeRaw, state.Economy.Value.Tick),
            AggregateVersions = AggregateVersions(state),
            Map = state.Map.Value,
            Economy = state.Economy.Value,
            Colonists = state.Colonists.Value,
            Rooms = state.Rooms.Value,
            Stockpiles = state.Stockpiles.Value,
            Buildings = state.Buildings.Value,
            WorkTables = state.WorkTables.Value,
            Power = state.Power.Value,
            Threats = state.Threats.Value,
            Weather = state.Weather.Value,
            Farm = state.Farm.Value,
            Plants = state.Plants.Value,
            Things = state.Things.Value,
            ThingDefs = state.ThingDefs.Value,
            AnimalDefs = state.AnimalDefs.Value,
            Terrain = state.Terrain.Value,
            StoredResources = state.StoredResources.Value,
            Animals = state.Animals.Value,
            Resources = state.Resources.Value,
            Research = state.Research.Value
        };
    }

    private static IReadOnlyDictionary<string, long> AggregateVersions(ColonyState state) =>
        new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["Map"] = state.Map.Version,
            ["Economy"] = state.Economy.Version,
            ["Colonists"] = state.Colonists.Version,
            ["Rooms"] = state.Rooms.Version,
            ["Stockpiles"] = state.Stockpiles.Version,
            ["Buildings"] = state.Buildings.Version,
            ["WorkTables"] = state.WorkTables.Version,
            ["Power"] = state.Power.Version,
            ["Threats"] = state.Threats.Version,
            ["Weather"] = state.Weather.Version,
            ["Farm"] = state.Farm.Version,
            ["Plants"] = state.Plants.Version,
            ["Things"] = state.Things.Version,
            ["ThingDefs"] = state.ThingDefs.Version,
            ["AnimalDefs"] = state.AnimalDefs.Version,
            ["Terrain"] = state.Terrain.Version,
            ["StoredResources"] = state.StoredResources.Version,
            ["Animals"] = state.Animals.Version,
            ["Resources"] = state.Resources.Version,
            ["Research"] = state.Research.Version
        };
}
