using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed class WillieSolverStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, WillieSolverSnapshot> _latest =
        new(StringComparer.OrdinalIgnoreCase);

    public void Record(WillieSolverSnapshot snapshot)
    {
        lock (_lock)
        {
            _latest[snapshot.Minister] = snapshot;
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
}

public sealed record WillieSolverSnapshot(
    string Minister,
    WillieSolverRequestSnapshot? Request,
    long? GameTick,
    DateTimeOffset CapturedAt,
    PlacementSolverReplayOutput Output)
{
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
                ErrorMessage: null));
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
