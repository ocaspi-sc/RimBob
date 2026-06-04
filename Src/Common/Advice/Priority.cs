using System.Text.Json.Serialization;

namespace RimBob.Core.Advice;

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<Priority>))]
public enum Priority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}
