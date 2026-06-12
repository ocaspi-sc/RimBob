using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

internal static class WilliePlacementSolveText
{
    public static string ReadinessWire(PlacementReadiness readiness) =>
        readiness.ToString().ToLowerInvariant();

    public static string NoteFor(PlacementResult result, BuildingRequest request)
    {
        if (result.Options.Count > 0)
        {
            return $"Placement solver: {result.Options.Count} validated option{Plural(result.Options.Count)}; materials_ready={ReadinessWire(result.MaterialsReady)}, apply_ready={ReadinessWire(result.ApplyReady)}.";
        }

        string reason = result.NoFit is null
            ? $"no validated {RequestRoomLabel(request)} option was emitted"
            : NoFitNote(result.NoFit.Value, request);
        return $"Placement solver no-fit: {reason}.";
    }

    public static string NoteFor(PlacementResult result, ZoneRequest request)
    {
        if (result.Options.Count > 0)
        {
            return $"Zone solver: {result.Options.Count} inspected option{Plural(result.Options.Count)}; apply_ready={ReadinessWire(result.ApplyReady)} via {ApplyKindFor(request)} validation.";
        }

        string reason = result.NoFit is null
            ? $"no validated {ZoneLabel(request)} option was emitted"
            : NoFitNote(result.NoFit.Value, request);
        return $"Zone solver no-fit: {reason}.";
    }

    public static string? AdviceBodyNoteFor(PlacementResult result, BuildingRequest request)
    {
        if (result.Options.Count > 0) return null;

        string reason = result.NoFit is null
            ? $"no validated {RequestRoomLabel(request)} option was emitted"
            : NoFitNote(result.NoFit.Value, request);
        return $"Placement solver could not suggest layout options because {reason}.";
    }

    public static string? AdviceBodyNoteFor(PlacementResult result, ZoneRequest request)
    {
        if (result.Options.Count > 0)
            return $"Zone solver found {result.Options.Count} coordinate option{Plural(result.Options.Count)} below; Apply validates live terrain and occupancy before creating the zone.";

        string reason = result.NoFit is null
            ? $"no validated {ZoneLabel(request)} option was emitted"
            : NoFitNote(result.NoFit.Value, request);
        return $"Zone solver could not suggest zone options because {reason}.";
    }

    public static string CachedNoteFor(PlacementResult result, BuildingRequest request)
    {
        if (result.Options.Count > 0)
        {
            return $"Placement solver cache hit: reused {result.Options.Count} validated option{Plural(result.Options.Count)}; materials_ready={ReadinessWire(result.MaterialsReady)}, apply_ready={ReadinessWire(result.ApplyReady)}.";
        }

        string reason = result.NoFit is null
            ? $"no validated {RequestRoomLabel(request)} option was emitted"
            : NoFitNote(result.NoFit.Value, request);
        return $"Placement solver cache hit: reused no-fit result: {reason}.";
    }

    public static string CachedNoteFor(PlacementResult result, ZoneRequest request)
    {
        if (result.Options.Count > 0)
        {
            return $"Zone solver cache hit: reused {result.Options.Count} inspected option{Plural(result.Options.Count)}; apply_ready={ReadinessWire(result.ApplyReady)} via {ApplyKindFor(request)} validation.";
        }

        string reason = result.NoFit is null
            ? $"no validated {ZoneLabel(request)} option was emitted"
            : NoFitNote(result.NoFit.Value, request);
        return $"Zone solver cache hit: reused no-fit result: {reason}.";
    }

    private static string NoFitNote(NoFitReason reason, BuildingRequest request) => reason switch
    {
        NoFitReason.NoAnchors => $"no {RequestAnchorLabel(request)} anchor is available in the Willie briefing",
        NoFitReason.NoDrafts => $"no {RequestRoomLabel(request)} drafts were generated",
        NoFitReason.HardGateRejected => $"all {RequestRoomLabel(request)} drafts failed shared hard gates",
        NoFitReason.NoReachablePath => $"no walkable route to a {RequestAnchorLabel(request)} anchor",
        NoFitReason.ValidationRejected => $"no buildable footprint near the {RequestAnchorLabel(request)} passed fork validation",
        _ => $"no validated {RequestRoomLabel(request)} option was emitted"
    };

    private static string NoFitNote(NoFitReason reason, ZoneRequest request) => reason switch
    {
        NoFitReason.UnsupportedZoneClass => $"{request.ZoneClass} is not supported by the zone solver",
        NoFitReason.NoTerrainGrid => "cell-level terrain is unavailable in the current state snapshot",
        NoFitReason.NoGrowableCells => $"no {ZoneTerrainLabel(request)} terrain cells were found inside the Home/buildable bounds",
        NoFitReason.AllZoneCellsBlocked => $"all {ZoneTerrainLabel(request)} cells were blocked by existing zones or occupancy",
        NoFitReason.NoZoneRectangle => $"no compact unoccupied {ZoneTerrainLabel(request)} rectangle matched the requested tile count",
        _ => $"no validated {ZoneLabel(request)} option was emitted"
    };

    private static string ApplyKindFor(ZoneRequest request) =>
        request.ZoneClass == ZoneClass.Stockpile ? "create_stockpile_zone" : "create_growing_zone";

    private static string ZoneLabel(ZoneRequest request) =>
        request.ZoneClass == ZoneClass.Stockpile ? "stockpile-zone" : "growing-zone";

    private static string ZoneTerrainLabel(ZoneRequest request) =>
        request.ZoneClass == ZoneClass.Stockpile ? "stockpile-capable" : "growable";

    private static string RequestRoomLabel(BuildingRequest request) =>
        request.RoomClass is not null
            ? FormatEnum(request.RoomClass.Value.ToString())
            : FormatEnum(request.TargetClass.ToString());

    private static string RequestAnchorLabel(BuildingRequest request)
    {
        string? target = request.Adjacency?
            .FirstOrDefault(adjacency => adjacency.Relation == AdjacencyRelation.Near)
            ?.Target;
        if (string.IsNullOrWhiteSpace(target) && Rules.IsFreezingBuildRequest(request))
            return "kitchen";

        return string.IsNullOrWhiteSpace(target)
            ? "requested"
            : target.Replace('_', ' ').ToLowerInvariant();
    }

    private static string FormatEnum(string value)
    {
        List<char> chars = new(value.Length + 4);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsUpper(c) && i > 0) chars.Add(' ');
            chars.Add(char.ToLowerInvariant(c));
        }

        return new string(chars.ToArray());
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
