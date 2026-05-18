using System.Text.Json.Serialization;
using RimBob.Core.Advice;

namespace RimBob.Core.Briefings;

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<AdviceChainStepStatus>))]
public enum AdviceChainStepStatus
{
    Trigger,
    Action,
    Have,
    Available,
    Blocked
}

public sealed record AdviceChainStep(
    [property: JsonPropertyName("key")]
    string Key,
    [property: JsonPropertyName("label")]
    string Label,
    [property: JsonPropertyName("detail")]
    string Detail,
    [property: JsonPropertyName("status")]
    AdviceChainStepStatus Status);

public sealed record AdviceChainPath(
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("steps")]
    IReadOnlyList<AdviceChainStep> Steps);

public sealed record AdviceChainModel(
    [property: JsonPropertyName("paths")]
    IReadOnlyList<AdviceChainPath> Paths)
{
    public static AdviceChainModel Empty { get; } = new([]);
}
