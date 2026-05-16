using System.Text.Json.Serialization;

namespace RimBob.Core.Advice;

/// <summary>
/// Living planning document the Mayor maintains and updates once per in-game day.
/// In M1 this IS the Mayor's advice output — there is no separate daily memo.
/// See Docs/design/agenda.md for full schema and dashboard contract.
/// </summary>
public sealed record MayorAgenda(
    [property: JsonPropertyName("version")]              int                                 Version,
    [property: JsonPropertyName("updated_in_game_tick")] string                              UpdatedInGameTick,
    [property: JsonPropertyName("generated_at")]         DateTimeOffset                      GeneratedAt,
    [property: JsonPropertyName("posture")]              MayorPosture                        Posture,
    [property: JsonPropertyName("state_of_the_union")]   IReadOnlyDictionary<string, string> StateOfTheUnion,
    [property: JsonPropertyName("update_notes")]         string                              UpdateNotes,
    [property: JsonPropertyName("short_term")]           IReadOnlyList<AgendaPriority>           ShortTerm,
    [property: JsonPropertyName("long_term")]            IReadOnlyList<AgendaPriority>           LongTerm,
    [property: JsonPropertyName("cabinet_direction")]    IReadOnlyDictionary<string, string>     CabinetDirection,
    [property: JsonPropertyName("guide_citations")]      IReadOnlyList<GuideCitation>            GuideCitations
);
