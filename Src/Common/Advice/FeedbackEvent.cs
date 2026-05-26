using System.Text.Json.Serialization;
using RimBob.Core.Briefings;

namespace RimBob.Core.Advice;

// Schema only; wired in the deferred feedback milestone.
public sealed record FeedbackEvent(
    [property: JsonPropertyName("source")]              string         Source,
    [property: JsonPropertyName("snapshot_version")]    int?           SnapshotVersion,
    [property: JsonPropertyName("item_id")]             string?        ItemId,
    [property: JsonPropertyName("action")]              FeedbackAction Action,
    [property: JsonPropertyName("note")]                string?        Note,
    [property: JsonPropertyName("modified_text")]       string?        ModifiedText,
    [property: JsonPropertyName("issued_at")]           DateTimeOffset IssuedAt,
    [property: JsonPropertyName("issued_game_date")]    GameDate       IssuedGameDate
);
