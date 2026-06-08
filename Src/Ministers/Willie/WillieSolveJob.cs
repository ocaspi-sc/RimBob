using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.State;

namespace RimBob.Ministers.Willie;

public enum WillieSolveKind
{
    Building,
    Zone
}

public interface IWillieSolveJob
{
    string JobId { get; }
    WillieSolveKind Kind { get; }
    string Minister { get; }
    string RequestKey { get; }
    string InputFingerprint { get; }
    string? SourceMinister { get; }
    WillieBriefing Briefing { get; }
    ColonyState FrozenState { get; }
    string? DrivingAdviceId { get; }
    bool RemoveFallbackWhenApplyReady { get; }
    long? GameTick { get; }
    DateTimeOffset EnqueuedAt { get; }
}

public sealed record WillieBuildingSolveJob(
    string JobId,
    string Minister,
    string RequestKey,
    string InputFingerprint,
    string? SourceMinister,
    WillieBriefing Briefing,
    ColonyState FrozenState,
    string? DrivingAdviceId,
    bool RemoveFallbackWhenApplyReady,
    long? GameTick,
    DateTimeOffset EnqueuedAt,
    BuildingRequest Request,
    IReadOnlyList<MaterialHint> MaterialsOnHand) : IWillieSolveJob
{
    public WillieSolveKind Kind => WillieSolveKind.Building;
}

public sealed record WillieZoneSolveJob(
    string JobId,
    string Minister,
    string RequestKey,
    string InputFingerprint,
    string? SourceMinister,
    WillieBriefing Briefing,
    ColonyState FrozenState,
    string? DrivingAdviceId,
    bool RemoveFallbackWhenApplyReady,
    long? GameTick,
    DateTimeOffset EnqueuedAt,
    ZoneRequest Request) : IWillieSolveJob
{
    public WillieSolveKind Kind => WillieSolveKind.Zone;
}
