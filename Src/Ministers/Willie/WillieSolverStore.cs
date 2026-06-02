using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed class WillieSolverStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, WillieSolverSnapshot> _latest =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<WillieInboundRequest>> _board =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, WillieSolverSnapshot>> _byRequest =
        new(StringComparer.OrdinalIgnoreCase);

    public void Record(WillieSolverSnapshot snapshot)
    {
        lock (_lock)
        {
            _latest[snapshot.Minister] = snapshot;
            if (snapshot.Request is null) return;

            if (!_byRequest.TryGetValue(snapshot.Minister, out Dictionary<string, WillieSolverSnapshot>? snapshots))
            {
                snapshots = new Dictionary<string, WillieSolverSnapshot>(StringComparer.OrdinalIgnoreCase);
                _byRequest[snapshot.Minister] = snapshots;
            }

            snapshots[RequestKey(snapshot.Request)] = snapshot;
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

            if (!_byRequest.TryGetValue(minister, out Dictionary<string, WillieSolverSnapshot>? snapshots))
                return;

            HashSet<string> currentKeys = snapshot
                .Select(row => row.RequestKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<string> staleKeys = snapshots.Keys
                .Where(key => !currentKeys.Contains(key))
                .ToList();
            foreach (string staleKey in staleKeys)
                snapshots.Remove(staleKey);
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

    public static string RequestKey(BuildingRequest request) =>
        RequestKey(
            request.TargetClass.ToString(),
            request.TargetDef,
            request.RoomClass?.ToString(),
            request.Request);

    public static string RequestKey(WillieSolverRequestSnapshot request) =>
        RequestKey(request.TargetClass, request.TargetDef, request.RoomClass, request.Request);

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

public sealed record WillieRequestBoardRow(
    WillieInboundRequest Inbound,
    WillieSolverSnapshot? Outcome);

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
