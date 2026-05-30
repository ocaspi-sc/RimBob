using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed class RoomTemplateSet
{
    private readonly IReadOnlyDictionary<RoomClass, IRoomTemplate> templatesByRoomClass;

    public RoomTemplateSet(IReadOnlyList<IRoomTemplate> templates)
    {
        templatesByRoomClass = templates
            .GroupBy(template => template.RoomClass)
            .ToDictionary(
                group => group.Key,
                group => group.First());
    }

    public static RoomTemplateSet Default { get; } = new(
    [
        new FreezerTemplate(),
        new KitchenTemplate(),
        new HospitalTemplate(),
        new BedroomTemplate(),
        new WorkshopTemplate(),
        new StorageTemplate()
    ]);

    public static RoomTemplateSet FreezerOnly { get; } = new([new FreezerTemplate()]);

    public IRoomTemplate? ForSpec(PlacementSpec spec)
    {
        RoomClass? roomClass = spec.RoomClass ?? RoomClassForTarget(spec.TargetClass);
        return roomClass is not null &&
            templatesByRoomClass.TryGetValue(roomClass.Value, out IRoomTemplate? template)
                ? template
                : null;
    }

    private static RoomClass? RoomClassForTarget(BuildingClass targetClass) =>
        targetClass switch
        {
            BuildingClass.Freezer => RoomClass.Freezer,
            BuildingClass.Bed => RoomClass.Bedroom,
            BuildingClass.ProductionBench => RoomClass.Workshop,
            BuildingClass.Stockpile or BuildingClass.Shelf => RoomClass.Storage,
            _ => null
        };
}
