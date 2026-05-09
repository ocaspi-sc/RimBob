using System.Text.Json.Serialization;

namespace RimAI.Core.Advice;

public sealed record AgendaItem(
    [property: JsonPropertyName("id")]       string                 Id,
    [property: JsonPropertyName("text")]     string                 Text,
    [property: JsonPropertyName("status")]   AgendaItemStatus       Status,
    [property: JsonPropertyName("cite_ids")] IReadOnlyList<string>? CiteIds = null
);
