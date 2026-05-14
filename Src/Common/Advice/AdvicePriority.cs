using System.Text.Json.Serialization;

namespace RimAI.Core.Advice;

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<AdvicePriority>))]
public enum AdvicePriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}
