using System.Text.Json.Serialization;

namespace RimAI.Core.Advice;

/// <summary>
/// Living planning document the Mayor maintains and updates once per in-game day.
/// In M1 this IS the Mayor's advice output — there is no separate daily memo.
/// See Docs/design/agenda.md for full schema and dashboard contract.
/// </summary>
public sealed record MayorAgenda(
    [property: JsonPropertyName("version")]              int                                 Version,
    [property: JsonPropertyName("updated_in_game_tick")] string                              UpdatedInGameTick,
    [property: JsonPropertyName("posture")]              MayorPosture                        Posture,
    [property: JsonPropertyName("update_notes")]         string                              UpdateNotes,
    [property: JsonPropertyName("short_term")]           IReadOnlyList<AgendaItem>           ShortTerm,
    [property: JsonPropertyName("long_term")]            IReadOnlyList<AgendaItem>           LongTerm,
    [property: JsonPropertyName("minister_direction")]   IReadOnlyDictionary<string, string> MinisterDirection
);
