using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed class WillieSolverStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, WillieSolverSnapshot> _latest =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<WillieInboundRequest>> _board =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<WillieInboundZoneRequest>> _zoneBoard =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, WillieSolverSnapshot>> _byRequest =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, WillieZoneSolverSnapshot>> _zoneByRequest =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _fingerprintsByRequest =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _zoneFingerprintsByRequest =
        new(StringComparer.OrdinalIgnoreCase);

    public void RecordBuildingOutcome(WillieSolverSnapshot snapshot, string? inputFingerprint = null)
    {
        lock (_lock)
        {
            _latest[snapshot.Minister] = snapshot;
            if (snapshot.Request is null) return;

            string requestKey = RequestKey(snapshot.Request);
            OutcomesFor(_byRequest, snapshot.Minister)[requestKey] = snapshot;
            RecordFingerprint(_fingerprintsByRequest, snapshot.Minister, requestKey, inputFingerprint);
        }
    }

    public void RecordInbound(string minister, IReadOnlyList<WillieInboundRequest> board)
    {
        lock (_lock)
        {
            IReadOnlyList<WillieInboundRequest> snapshot = board
                .GroupBy(row => row.RequestKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            _board[minister] = snapshot;
            PruneDroppedBuildingLifecycleRows(minister, snapshot.Select(row => row.RequestKey).ToHashSet(StringComparer.OrdinalIgnoreCase));
        }
    }

    public void RecordZoneInbound(string minister, IReadOnlyList<WillieInboundZoneRequest> board)
    {
        lock (_lock)
        {
            IReadOnlyList<WillieInboundZoneRequest> snapshot = board
                .GroupBy(row => row.RequestKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            _zoneBoard[minister] = snapshot;
            PruneDroppedZoneLifecycleRows(minister, snapshot.Select(row => row.RequestKey).ToHashSet(StringComparer.OrdinalIgnoreCase));
        }
    }

    public void RecordZoneOutcome(WillieZoneSolverSnapshot snapshot, string? inputFingerprint = null)
    {
        lock (_lock)
        {
            string requestKey = RequestKey(snapshot.Request);
            OutcomesFor(_zoneByRequest, snapshot.Minister)[requestKey] = snapshot;
            RecordFingerprint(_zoneFingerprintsByRequest, snapshot.Minister, requestKey, inputFingerprint);
        }
    }

    public WillieSolverSnapshot? TryGetFreshBuildingOutcome(
        string minister,
        BuildingRequest request,
        string inputFingerprint)
    {
        lock (_lock)
        {
            string requestKey = RequestKey(request);
            return HasFreshFingerprint(_fingerprintsByRequest, minister, requestKey, inputFingerprint) &&
                _byRequest.TryGetValue(minister, out Dictionary<string, WillieSolverSnapshot>? snapshots) &&
                snapshots.TryGetValue(requestKey, out WillieSolverSnapshot? snapshot) &&
                IsReusableTerminal(snapshot.Status)
                    ? snapshot
                    : null;
        }
    }

    public WillieZoneSolverSnapshot? TryGetFreshZoneOutcome(
        string minister,
        ZoneRequest request,
        string inputFingerprint)
    {
        lock (_lock)
        {
            string requestKey = RequestKey(request);
            return HasFreshFingerprint(_zoneFingerprintsByRequest, minister, requestKey, inputFingerprint) &&
                _zoneByRequest.TryGetValue(minister, out Dictionary<string, WillieZoneSolverSnapshot>? snapshots) &&
                snapshots.TryGetValue(requestKey, out WillieZoneSolverSnapshot? snapshot) &&
                IsReusableTerminal(snapshot.Status)
                    ? snapshot
                    : null;
        }
    }

    public void RecordQueued(IWillieSolveJob job) =>
        RecordLifecycle(job, "queued", "Solve job is queued.");

    public void RecordRunning(IWillieSolveJob job) =>
        RecordLifecycle(job, "running", "Solve job is running.");

    public void RecordStale(IWillieSolveJob job, string message)
    {
        lock (_lock)
        {
            bool current = job.Kind switch
            {
                WillieSolveKind.Building => HasFreshFingerprint(_fingerprintsByRequest, job.Minister, job.RequestKey, job.InputFingerprint),
                WillieSolveKind.Zone => HasFreshFingerprint(_zoneFingerprintsByRequest, job.Minister, job.RequestKey, job.InputFingerprint),
                _ => false
            };
            if (!current) return;
        }

        RecordLifecycle(job, "stale", message);
    }

    public bool CanPatchAdvice(IWillieSolveJob job)
    {
        lock (_lock)
        {
            return job.Kind switch
            {
                WillieSolveKind.Building =>
                    BoardContains(_board, job.Minister, job.RequestKey) &&
                    HasFreshFingerprint(_fingerprintsByRequest, job.Minister, job.RequestKey, job.InputFingerprint) &&
                    _byRequest.TryGetValue(job.Minister, out Dictionary<string, WillieSolverSnapshot>? snapshots) &&
                    snapshots.TryGetValue(job.RequestKey, out WillieSolverSnapshot? snapshot) &&
                    IsPatchTerminal(snapshot.Status),
                WillieSolveKind.Zone =>
                    BoardContains(_zoneBoard, job.Minister, job.RequestKey) &&
                    HasFreshFingerprint(_zoneFingerprintsByRequest, job.Minister, job.RequestKey, job.InputFingerprint) &&
                    _zoneByRequest.TryGetValue(job.Minister, out Dictionary<string, WillieZoneSolverSnapshot>? snapshots) &&
                    snapshots.TryGetValue(job.RequestKey, out WillieZoneSolverSnapshot? snapshot) &&
                    IsPatchTerminal(snapshot.Status),
                _ => false
            };
        }
    }

    public WillieSolverSnapshot? Latest(string minister)
    {
        lock (_lock)
        {
            return _latest.TryGetValue(minister, out WillieSolverSnapshot? snapshot)
                ? snapshot
                : null;
        }
    }

    public IReadOnlyList<WillieRequestBoardRow> RequestBoard(string minister)
    {
        lock (_lock)
        {
            if (!_board.TryGetValue(minister, out IReadOnlyList<WillieInboundRequest>? board))
                return [];

            _byRequest.TryGetValue(minister, out Dictionary<string, WillieSolverSnapshot>? snapshots);
            return board
                .Select(row =>
                {
                    WillieSolverSnapshot? outcome = snapshots is not null &&
                        snapshots.TryGetValue(row.RequestKey, out WillieSolverSnapshot? stored)
                            ? stored
                            : null;
                    return new WillieRequestBoardRow(row, outcome);
                })
                .ToList();
        }
    }

    public IReadOnlyList<WillieZoneRequestBoardRow> ZoneRequestBoard(string minister)
    {
        lock (_lock)
        {
            _zoneByRequest.TryGetValue(minister, out Dictionary<string, WillieZoneSolverSnapshot>? snapshots);
            return _zoneBoard.TryGetValue(minister, out IReadOnlyList<WillieInboundZoneRequest>? board)
                ? board.Select(row =>
                {
                    WillieZoneSolverSnapshot? outcome = snapshots is not null &&
                        snapshots.TryGetValue(row.RequestKey, out WillieZoneSolverSnapshot? stored)
                            ? stored
                            : null;
                    return new WillieZoneRequestBoardRow(row, outcome);
                }).ToList()
                : [];
        }
    }

    public static string RequestKey(BuildingRequest request) =>
        RequestKey(
            request.TargetClass.ToString(),
            request.TargetDef,
            request.RoomClass?.ToString(),
            request.Request);

    public static string RequestKey(WillieSolverRequestSnapshot request) =>
        RequestKey(request.TargetClass, request.TargetDef, request.RoomClass, request.Request);

    public static string RequestKey(ZoneRequest request) =>
        string.Join(
            "|",
            NormalizeKeyPart(request.ZoneClass.ToString()),
            NormalizeKeyPart(request.PlantDef),
            request.TileCount?.ToString() ?? "-",
            NormalizeKeyList(request.AllowedItemDefs),
            NormalizeKeyList(request.AllowedItemCategories),
            NormalizeKeyPart(request.Request));

    public static string RequestKey(WillieZoneRequestSnapshot request) =>
        string.Join(
            "|",
            NormalizeKeyPart(request.ZoneClass),
            NormalizeKeyPart(request.PlantDef),
            request.TileCount?.ToString() ?? "-",
            NormalizeKeyList(request.AllowedItemDefs),
            NormalizeKeyList(request.AllowedItemCategories),
            NormalizeKeyPart(request.Request));

    public static string RequestKey(
        string targetClass,
        string? targetDef,
        string? roomClass,
        string request) =>
        string.Join(
            "|",
            NormalizeKeyPart(targetClass),
            NormalizeKeyPart(targetDef),
            NormalizeKeyPart(roomClass),
            NormalizeKeyPart(request));

    private static string NormalizeKeyPart(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Trim().ToLowerInvariant();

    private static string NormalizeKeyList(IReadOnlyList<string>? values) =>
        values is null || values.Count == 0
            ? "-"
            : string.Join(
                ",",
                values
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim().ToLowerInvariant())
                    .Order(StringComparer.Ordinal));

    private static Dictionary<string, TSnapshot> OutcomesFor<TSnapshot>(
        Dictionary<string, Dictionary<string, TSnapshot>> outcomes,
        string minister)
    {
        if (!outcomes.TryGetValue(minister, out Dictionary<string, TSnapshot>? snapshots))
        {
            snapshots = new Dictionary<string, TSnapshot>(StringComparer.OrdinalIgnoreCase);
            outcomes[minister] = snapshots;
        }

        return snapshots;
    }

    private static void RecordFingerprint(
        Dictionary<string, Dictionary<string, string>> fingerprintsByMinister,
        string minister,
        string requestKey,
        string? inputFingerprint)
    {
        if (string.IsNullOrWhiteSpace(inputFingerprint))
        {
            RemoveFingerprint(fingerprintsByMinister, minister, requestKey);
            return;
        }

        Dictionary<string, string> fingerprints = OutcomesFor(fingerprintsByMinister, minister);
        fingerprints[requestKey] = inputFingerprint;
    }

    private static void RemoveFingerprint(
        Dictionary<string, Dictionary<string, string>> fingerprintsByMinister,
        string minister,
        string requestKey)
    {
        if (fingerprintsByMinister.TryGetValue(minister, out Dictionary<string, string>? fingerprints))
            fingerprints.Remove(requestKey);
    }

    private static bool HasFreshFingerprint(
        Dictionary<string, Dictionary<string, string>> fingerprintsByMinister,
        string minister,
        string requestKey,
        string inputFingerprint) =>
        fingerprintsByMinister.TryGetValue(minister, out Dictionary<string, string>? fingerprints) &&
        fingerprints.TryGetValue(requestKey, out string? stored) &&
        string.Equals(stored, inputFingerprint, StringComparison.Ordinal);

    private void RecordLifecycle(IWillieSolveJob job, string status, string message)
    {
        lock (_lock)
        {
            PlacementSolverReplayOutput output = PlacementSolverReplayOutput.FromLifecycle(status, message);
            switch (job)
            {
                case WillieBuildingSolveJob buildingJob:
                    RecordBuildingOutcomeLocked(new WillieSolverSnapshot(
                        Minister: buildingJob.Minister,
                        Request: WillieSolverRequestSnapshot.FromRequest(buildingJob.Request, buildingJob.SourceMinister),
                        GameTick: buildingJob.GameTick,
                        CapturedAt: DateTimeOffset.UtcNow,
                        Output: output,
                        Options: []), buildingJob.InputFingerprint);
                    break;
                case WillieZoneSolveJob zoneJob:
                    RecordZoneOutcomeLocked(new WillieZoneSolverSnapshot(
                        Minister: zoneJob.Minister,
                        Request: WillieZoneRequestSnapshot.FromRequest(zoneJob.Request, zoneJob.SourceMinister),
                        GameTick: zoneJob.GameTick,
                        CapturedAt: DateTimeOffset.UtcNow,
                        Output: output,
                        Options: []), zoneJob.InputFingerprint);
                    break;
            }
        }
    }

    private void RecordBuildingOutcomeLocked(WillieSolverSnapshot snapshot, string? inputFingerprint)
    {
        _latest[snapshot.Minister] = snapshot;
        if (snapshot.Request is null) return;

        string requestKey = RequestKey(snapshot.Request);
        OutcomesFor(_byRequest, snapshot.Minister)[requestKey] = snapshot;
        RecordFingerprint(_fingerprintsByRequest, snapshot.Minister, requestKey, inputFingerprint);
    }

    private void RecordZoneOutcomeLocked(WillieZoneSolverSnapshot snapshot, string? inputFingerprint)
    {
        string requestKey = RequestKey(snapshot.Request);
        OutcomesFor(_zoneByRequest, snapshot.Minister)[requestKey] = snapshot;
        RecordFingerprint(_zoneFingerprintsByRequest, snapshot.Minister, requestKey, inputFingerprint);
    }

    private void PruneDroppedBuildingLifecycleRows(string minister, HashSet<string> liveRequestKeys)
    {
        if (!_byRequest.TryGetValue(minister, out Dictionary<string, WillieSolverSnapshot>? snapshots))
            return;

        foreach (string requestKey in snapshots
            .Where(pair => !liveRequestKeys.Contains(pair.Key) && IsLifecycleStatus(pair.Value.Status))
            .Select(pair => pair.Key)
            .ToList())
        {
            snapshots.Remove(requestKey);
            RemoveFingerprint(_fingerprintsByRequest, minister, requestKey);
        }
    }

    private void PruneDroppedZoneLifecycleRows(string minister, HashSet<string> liveRequestKeys)
    {
        if (!_zoneByRequest.TryGetValue(minister, out Dictionary<string, WillieZoneSolverSnapshot>? snapshots))
            return;

        foreach (string requestKey in snapshots
            .Where(pair => !liveRequestKeys.Contains(pair.Key) && IsLifecycleStatus(pair.Value.Status))
            .Select(pair => pair.Key)
            .ToList())
        {
            snapshots.Remove(requestKey);
            RemoveFingerprint(_zoneFingerprintsByRequest, minister, requestKey);
        }
    }

    private static bool BoardContains(
        Dictionary<string, IReadOnlyList<WillieInboundRequest>> boards,
        string minister,
        string requestKey) =>
        boards.TryGetValue(minister, out IReadOnlyList<WillieInboundRequest>? board) &&
        board.Any(row => string.Equals(row.RequestKey, requestKey, StringComparison.OrdinalIgnoreCase));

    private static bool BoardContains(
        Dictionary<string, IReadOnlyList<WillieInboundZoneRequest>> boards,
        string minister,
        string requestKey) =>
        boards.TryGetValue(minister, out IReadOnlyList<WillieInboundZoneRequest>? board) &&
        board.Any(row => string.Equals(row.RequestKey, requestKey, StringComparison.OrdinalIgnoreCase));

    private static bool IsReusableTerminal(string status) =>
        status is "options" or "no_fit";

    private static bool IsPatchTerminal(string status) =>
        status is "options" or "no_fit" or "error";

    private static bool IsLifecycleStatus(string status) =>
        status is "queued" or "running" or "stale";
}

public sealed record WillieSolverSnapshot(
    string Minister,
    WillieSolverRequestSnapshot? Request,
    long? GameTick,
    DateTimeOffset CapturedAt,
    PlacementSolverReplayOutput Output,
    IReadOnlyList<AdviceOption> Options)
{
    public WillieSolverSnapshot(
        string Minister,
        WillieSolverRequestSnapshot? Request,
        long? GameTick,
        DateTimeOffset CapturedAt,
        PlacementSolverReplayOutput Output)
        : this(Minister, Request, GameTick, CapturedAt, Output, [])
    {
    }

    public string Status => Output.Status;
    public string? NoFit => Output.NoFit;
    public string? Draftable => Output.Draftable;
    public string? PlacementValid => Output.PlacementValid;
    public string? MaterialsReady => Output.MaterialsReady;
    public string? ApplyReady => Output.ApplyReady;
    public PlacementTrace? Trace => Output.Trace;
    public string? ErrorType => Output.ErrorType;
    public string? ErrorMessage => Output.ErrorMessage;

    public static WillieSolverSnapshot NotSeen(string minister) =>
        new(
            Minister: minister,
            Request: null,
            GameTick: null,
            CapturedAt: DateTimeOffset.UtcNow,
            Output: new PlacementSolverReplayOutput(
                Status: "not_seen_yet",
                NoFit: null,
                Draftable: null,
                PlacementValid: null,
                MaterialsReady: null,
                ApplyReady: null,
                Trace: null,
                ErrorType: null,
                ErrorMessage: null),
            Options: []);
}

public sealed record WillieInboundRequest(
    BuildingRequest Request,
    string? SourceMinister,
    string RequestKey);

public sealed record WillieInboundZoneRequest(
    ZoneRequest Request,
    string? SourceMinister,
    string RequestKey);

public sealed record WillieRequestBoardRow(
    WillieInboundRequest Inbound,
    WillieSolverSnapshot? Outcome);

public sealed record WillieZoneRequestBoardRow(
    WillieInboundZoneRequest Inbound,
    WillieZoneSolverSnapshot? Outcome);

public sealed record WillieZoneSolverSnapshot(
    string Minister,
    WillieZoneRequestSnapshot Request,
    long? GameTick,
    DateTimeOffset CapturedAt,
    PlacementSolverReplayOutput Output,
    IReadOnlyList<AdviceOption> Options)
{
    public string Status => Output.Status;

    public string? NoFit => Output.NoFit;
}

public sealed record WillieZoneRequestSnapshot(
    string Request,
    string Reason,
    string ZoneClass,
    string? PlantDef,
    int? TileCount,
    IReadOnlyList<string>? AllowedItemDefs,
    IReadOnlyList<string>? AllowedItemCategories,
    string? RequestedFrom,
    string? SourceMinister,
    string? Priority)
{
    public static WillieZoneRequestSnapshot FromRequest(
        ZoneRequest request,
        string? sourceMinister) =>
        new(
            Request: request.Request,
            Reason: request.Reason,
            ZoneClass: request.ZoneClass.ToString(),
            PlantDef: request.PlantDef,
            TileCount: request.TileCount,
            AllowedItemDefs: request.AllowedItemDefs,
            AllowedItemCategories: request.AllowedItemCategories,
            RequestedFrom: request.RequestedFrom,
            SourceMinister: sourceMinister,
            Priority: request.Priority?.ToString());
}

public sealed record WillieSolverRequestSnapshot(
    string Request,
    string Reason,
    string TargetClass,
    string? TargetDef,
    string? RoomClass,
    string? RequestedFrom,
    string? SourceMinister,
    string? Priority)
{
    public static WillieSolverRequestSnapshot FromRequest(
        BuildingRequest request,
        string? sourceMinister) =>
        new(
            Request: request.Request,
            Reason: request.Reason,
            TargetClass: request.TargetClass.ToString(),
            TargetDef: request.TargetDef,
            RoomClass: request.RoomClass?.ToString(),
            RequestedFrom: request.RequestedFrom,
            SourceMinister: sourceMinister,
            Priority: request.Priority?.ToString());
}
