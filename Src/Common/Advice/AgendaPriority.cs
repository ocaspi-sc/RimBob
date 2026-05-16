using System.Text.Json.Serialization;

namespace RimBob.Core.Advice;

public sealed record AgendaPriority(
    [property: JsonPropertyName("id")]       string                 Id,
    [property: JsonPropertyName("text")]     string                 Text,
    [property: JsonPropertyName("status")]   AgendaPriorityStatus   Status,
    [property: JsonPropertyName("cite_ids")] IReadOnlyList<string>? CiteIds = null
);
