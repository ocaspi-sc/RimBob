using System.Text.Json.Serialization;

namespace RimBob.Core.Advice;

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<AgendaPriorityStatus>))]
public enum AgendaPriorityStatus
{
    Active,
    Completed,
    Deferred
}
