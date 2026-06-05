using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed class RoomTemplateSet
{
    private readonly IReadOnlyDictionary<RoomClass, IRoomTemplate> templatesByRoomClass;
    private readonly IReadOnlyDictionary<BuildingClass, IRoomTemplate> templatesByBuildingClass;

    public RoomTemplateSet(IReadOnlyList<IRoomTemplate> templates)
    {
        templatesByRoomClass = templates
            .Where(template => !template.TargetClass.HasValue)
            .GroupBy(template => template.RoomClass)
            .ToDictionary(
                group => group.Key,
                group => group.First());

        templatesByBuildingClass = templates
            .Where(template => template.TargetClass.HasValue)
            .ToDictionary(template => template.TargetClass!.Value);
    }

    public static RoomTemplateSet Default { get; } = new(
    [
        new FreezerTemplate(),
        new KitchenTemplate(),
        new HospitalTemplate(),
        new BedroomTemplate(),
        new BarracksTemplate(),
        new HeaterTemplate(),
        new CoolerTemplate(),
        new RecreationTemplate(),
        new DiningTemplate(),
        new WorkshopTemplate(),
        new StorageTemplate()
    ]);

    public static RoomTemplateSet FreezerOnly { get; } = new([new FreezerTemplate()]);

    public IRoomTemplate? ForSpec(PlacementSpec spec)
    {
        if (templatesByBuildingClass.TryGetValue(spec.TargetClass, out IRoomTemplate? specificTemplate))
            return specificTemplate;

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
            BuildingClass.Heater or BuildingClass.Cooler => RoomClass.Barracks,
            BuildingClass.ProductionBench => RoomClass.Workshop,
            BuildingClass.Stockpile or BuildingClass.Shelf => RoomClass.Storage,
            BuildingClass.Recreation => RoomClass.Recreation,
            BuildingClass.Table => RoomClass.Dining,
            _ => null
        };
}
