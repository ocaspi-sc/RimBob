using RimBob.Core.Advice;
using RimBob.Core.Aggregates;

namespace RimBob.State.Derivations.Common;

public static class RoomClassMapper
{
    public static RoomClass? FromRoleLabel(string? roleLabel)
    {
        if (string.IsNullOrWhiteSpace(roleLabel))
            return null;

        string normalized = roleLabel.Trim();
        if (Contains(normalized, "freezer") || Contains(normalized, "cold storage")) return RoomClass.Freezer;
        if (Contains(normalized, "hospital")) return RoomClass.Hospital;
        if (Contains(normalized, "kitchen")) return RoomClass.Kitchen;
        if (Contains(normalized, "butcher")) return RoomClass.Butcher;
        if (Contains(normalized, "workshop")) return RoomClass.Workshop;
        if (Contains(normalized, "laboratory") || Contains(normalized, "research")) return RoomClass.Research;
        if (Contains(normalized, "bedroom")) return RoomClass.Bedroom;
        if (Contains(normalized, "barracks")) return RoomClass.Barracks;
        if (Contains(normalized, "prison")) return RoomClass.Prison;
        if (Contains(normalized, "recreation") || Contains(normalized, "rec room")) return RoomClass.Recreation;
        if (Contains(normalized, "dining")) return RoomClass.Dining;
        if (Contains(normalized, "storage") || Contains(normalized, "stockpile")) return RoomClass.Storage;

        return null;
    }

    public static RoomClass? FromContainedBuildings(IReadOnlyList<BuildingRecord> buildings)
    {
        if (buildings.Any(BuildingClassifier.IsHospitalBed)) return RoomClass.Hospital;
        if (buildings.Any(BuildingClassifier.IsCookingBuilding)) return RoomClass.Kitchen;
        if (buildings.Any(BuildingClassifier.IsButcherTable)) return RoomClass.Butcher;
        if (buildings.Any(BuildingClassifier.IsResearchBench)) return RoomClass.Research;
        if (buildings.Any(BuildingClassifier.IsWorkshopBench)) return RoomClass.Workshop;
        if (buildings.Any(BuildingClassifier.IsBed)) return RoomClass.Bedroom;

        // TODO: re-evaluate fallback BuildingClassifier coverage when functional_rooms rule ships.
        return null;
    }

    private static bool Contains(string text, string token) =>
        text.Contains(token, StringComparison.OrdinalIgnoreCase);
}
