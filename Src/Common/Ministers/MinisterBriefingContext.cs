using System.Text.Json.Serialization;
using RimBob.Core.Advice;

namespace RimBob.Core.Ministers;

/// <summary>
/// Read-only strategic context ministers receive from the Mayor's current Agenda.
/// This is not a message and does not let ministers mutate the Agenda.
/// </summary>
public sealed record MinisterBriefingContext(
    [property: JsonPropertyName("posture")]
    MayorPosture? Posture,
    [property: JsonPropertyName("agenda_direction")]
    string? AgendaDirection,
    [property: JsonPropertyName("short_term_domains")]
    IReadOnlyList<string> ShortTermDomains
)
{
    public static MinisterBriefingContext Empty { get; } = new(null, null, []);
}
