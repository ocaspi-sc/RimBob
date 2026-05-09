using System.Text.Json.Serialization;

namespace RimAI.Knowledge;

public sealed record ChunkMetadata(
    [property: JsonPropertyName("source_path")]     string SourcePath,
    [property: JsonPropertyName("section_heading")] string SectionHeading,
    [property: JsonPropertyName("start_line")]      int    StartLine,
    [property: JsonPropertyName("end_line")]        int    EndLine
);
