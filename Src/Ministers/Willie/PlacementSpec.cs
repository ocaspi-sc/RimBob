using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed record PlacementSpec(
    string Request,
    string Reason,
    BuildingClass TargetClass,
    string? TargetDef,
    RoomClass? RoomClass,
    CapacityNeed? CapacityNeed,
    IReadOnlyList<AdjacencyHint> Adjacency,
    PowerNeed? Power,
    TempNeed? Temperature,
    IReadOnlyList<MaterialHint> MaterialsOnHand,
    Deadline? Deadline,
    AdvicePriority? Priority,
    string? Source,
    IReadOnlyList<string> Constraints)
{
    public static PlacementSpec FromBuildingRequest(
        BuildingRequest request,
        IReadOnlyList<MaterialHint>? materialsOnHandOverride = null) =>
        new(
            Request: request.Request,
            Reason: request.Reason,
            TargetClass: request.TargetClass,
            TargetDef: request.TargetDef,
            RoomClass: request.RoomClass,
            CapacityNeed: request.CapacityNeed,
            Adjacency: request.Adjacency ?? [],
            Power: request.Power,
            Temperature: request.Temperature,
            MaterialsOnHand: materialsOnHandOverride ?? request.MaterialsOnHand ?? [],
            Deadline: request.Deadline,
            Priority: request.Priority,
            Source: request.RequestedFrom,
            // TODO: BuildingRequest has no Constraints[] field today; derive constraints from RoomClass or self-intent once Willie-owned build_intent feeds the solver.
            Constraints: []);
}
