using RimBob.Core.Aggregates;
using RimBob.Ingestion.Dtos;

namespace RimBob.State;

public static class ThreatAggregateMapper
{
    public static ThreatBoard FromThreats(IReadOnlyList<LordDto> lords, IReadOnlyList<IncidentDto> incidents) =>
        new(MapLords(lords), MapIncidents(incidents));

    private static IReadOnlyList<HostileLord> MapLords(IReadOnlyList<LordDto> lords) =>
        lords
            .Select(lord => new HostileLord(
                Id: lord.Id,
                JobType: lord.JobType,
                FactionId: lord.FactionId,
                ThreatPoints: lord.ThreatPoints,
                PawnCount: lord.PawnIds?.Count ?? 0))
            .ToList();

    private static IReadOnlyList<IncidentRecord> MapIncidents(IReadOnlyList<IncidentDto> incidents) =>
        incidents
            .OrderBy(incident => incident.DaysSince)
            .Take(5)
            .Select(incident => new IncidentRecord(incident.Def, incident.DaysSince, incident.Label))
            .ToList();
}
