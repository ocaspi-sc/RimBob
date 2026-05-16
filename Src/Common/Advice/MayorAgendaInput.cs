using System.Text.Json.Serialization;

namespace RimBob.Core.Advice;

/// <summary>
/// Mayor's proposed agenda content — what the LLM produces. Version and
/// updated_in_game_tick are server-assigned by AgendaStore.UpdateAsync.
/// </summary>
public sealed record MayorAgendaInput(
    [property: JsonPropertyName("posture")]            MayorPosture                        Posture,
    [property: JsonPropertyName("state_of_the_union")] IReadOnlyDictionary<string, string> StateOfTheUnion,
    [property: JsonPropertyName("update_notes")]       string                              UpdateNotes,
    [property: JsonPropertyName("short_term")]         IReadOnlyList<AgendaPriority>           ShortTerm,
    [property: JsonPropertyName("long_term")]          IReadOnlyList<AgendaPriority>           LongTerm,
    [property: JsonPropertyName("cabinet_direction")]  IReadOnlyDictionary<string, string>     CabinetDirection,
    [property: JsonPropertyName("guide_citations")]    IReadOnlyList<GuideCitation>?           GuideCitations = null
);
