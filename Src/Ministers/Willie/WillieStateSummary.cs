using System.Globalization;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;

namespace RimBob.Ministers.Willie;

public static class WillieStateSummary
{
    public static string Build(WillieBriefing briefing)
    {
        string power = FormatPower(briefing.PowerStability);
        string queue = FormatQueue(briefing.StalledBuilds, briefing.MaterialBottleneck.MissingMaterials);
        string rooms = $"Rooms: {briefing.BaseLayout.RoomCount} rooms, {briefing.AnchorInventory.Anchors.Count} classified anchors, {briefing.StoragePlacement.StockpileZones} stockpile zones.";
        string coverage = $"Coverage: {LiveLabel(briefing.DataCoverage.HasLiveState)}, rooms {YesNo(briefing.DataCoverage.HasRooms)}, buildings {YesNo(briefing.DataCoverage.HasBuildings)}, backlog {YesNo(briefing.DataCoverage.HasConstructionBacklog)}.";

        return string.Join(Environment.NewLine, [power, queue, rooms, coverage]);
    }

    private static string FormatPower(WilliePowerStabilitySummary power)
    {
        string battery = power.CapacityWd > 0
            ? $"{power.StoredWd.ToString("0", CultureInfo.InvariantCulture)}/{power.CapacityWd.ToString("0", CultureInfo.InvariantCulture)} Wd"
            : "no battery capacity";
        return $"Power: {FormatSigned(power.NetW)} W net, {power.GeneratorCount} generators, {power.BatteryCount} batteries, {battery}.";
    }

    private static string FormatQueue(
        WillieStalledBuildsSummary stalled,
        IReadOnlyList<MaterialCount> missingMaterials)
    {
        string missing = missingMaterials.Count == 0
            ? "no missing materials"
            : string.Join(", ", missingMaterials.Take(3).Select(material => $"{material.Count} {material.DefName}"));
        return $"Build queue: {stalled.PendingBuildCount} pending, {stalled.BlockedCount} blocked, {missing}.";
    }

    private static string FormatSigned(float value) =>
        value >= 0
            ? $"+{value.ToString("0", CultureInfo.InvariantCulture)}"
            : value.ToString("0", CultureInfo.InvariantCulture);

    private static string LiveLabel(bool hasLiveState) => hasLiveState ? "live" : "restored/missing live state";

    private static string YesNo(bool value) => value ? "yes" : "no";
}
