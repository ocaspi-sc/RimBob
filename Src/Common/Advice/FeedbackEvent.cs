using System.Text.Json.Serialization;

namespace RimBob.Core.Advice;

// Schema only — wired in M2.
public sealed record FeedbackEvent(
    [property: JsonPropertyName("source")]              string         Source,
    [property: JsonPropertyName("agenda_version")]      int?           AgendaVersion,
    [property: JsonPropertyName("item_id")]             string?        ItemId,
    [property: JsonPropertyName("action")]              FeedbackAction Action,
    [property: JsonPropertyName("note")]                string?        Note,
    [property: JsonPropertyName("modified_text")]       string?        ModifiedText,
    [property: JsonPropertyName("issued_at")]           DateTimeOffset IssuedAt,
    [property: JsonPropertyName("issued_in_game_tick")] string         IssuedInGameTick
);
