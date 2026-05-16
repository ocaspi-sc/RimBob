using RimBob.Core.Aggregates;

namespace RimBob.State.Derivations.Common;

public static class BuildingClassifier
{
    public static bool IsBed(BuildingRecord building) =>
        Contains(building, "Bed") && !Contains(building, "Hospital");

    public static bool IsHospitalBed(BuildingRecord building) =>
        Contains(building, "Hospital");

    public static bool IsBattery(BuildingRecord building) =>
        Contains(building, "Battery");

    public static bool IsTurret(BuildingRecord building) =>
        Contains(building, "Turret");

    public static bool IsGenerator(BuildingRecord building) =>
        Contains(building, "Generator") ||
        Contains(building, "SolarPanel") ||
        Contains(building, "WindTurbine");

    public static bool IsCooler(BuildingRecord building) =>
        Contains(building, "Cooler");

    public static bool IsHeater(BuildingRecord building) =>
        Contains(building, "Heater");

    public static bool IsCookingBuilding(BuildingRecord building) =>
        Contains(building, "Stove") ||
        Contains(building, "Campfire");

    public static bool IsButcherTable(BuildingRecord building) =>
        Contains(building, "Butcher");

    private static bool Contains(BuildingRecord building, string token) =>
        building.Def.Contains(token, StringComparison.OrdinalIgnoreCase);
}
