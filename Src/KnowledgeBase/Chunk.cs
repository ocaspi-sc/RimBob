using System.Text.Json.Serialization;

namespace RimAI.Knowledge;

public sealed record Chunk(
    [property: JsonPropertyName("id")]        string        Id,
    [property: JsonPropertyName("text")]      string        Text,
    [property: JsonPropertyName("meta")]      ChunkMetadata Meta,
    [property: JsonPropertyName("embedding")] float[]       Embedding
);
