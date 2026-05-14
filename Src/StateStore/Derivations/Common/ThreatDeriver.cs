using RimAI.Core.Aggregates;

namespace RimAI.State.Derivations.Common;

public static class ThreatDeriver
{
    public static IReadOnlyList<HostileLord> HostileLords(ThreatBoard board) =>
        board.Lords.Where(IsHostileLord).ToList();

    public static bool HasActiveHostileThreat(ThreatBoard board) =>
        board.Lords.Any(IsHostileLord);

    public static bool IsHostileLord(HostileLord lord) =>
        lord.JobType is not null && (
            lord.JobType.Contains("Raid", StringComparison.OrdinalIgnoreCase) ||
            lord.JobType.Contains("Siege", StringComparison.OrdinalIgnoreCase) ||
            lord.JobType.Contains("Assault", StringComparison.OrdinalIgnoreCase) ||
            lord.JobType.Contains("Sapper", StringComparison.OrdinalIgnoreCase));
}
