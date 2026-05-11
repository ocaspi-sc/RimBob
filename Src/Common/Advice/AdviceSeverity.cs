using System.Text.Json.Serialization;

namespace RimAI.Core.Advice;

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<AdviceSeverity>))]
public enum AdviceSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}
