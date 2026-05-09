using System.Text.Json.Serialization;

namespace RimAI.Core.Advice;

/// <summary>
/// Compact reference to a retrieved guide passage. Carried on MayorAgenda.Citations
/// and (when relevant) referenced by id from individual short_term/long_term items
/// via AgendaItem.CiteIds. Lives in Core so the Mayor's output schema can reference
/// it without depending on the Knowledge project.
/// </summary>
public sealed record Citation(
    [property: JsonPropertyName("cite_id")]     string CiteId,
    [property: JsonPropertyName("source_path")] string SourcePath,
    [property: JsonPropertyName("heading")]     string Heading,
    [property: JsonPropertyName("snippet")]     string Snippet
);
