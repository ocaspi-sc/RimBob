using System.Text.Json.Serialization;

namespace RimAI.Core.Advice;

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<AgendaPriorityStatus>))]
public enum AgendaPriorityStatus
{
    Active,
    Completed,
    Deferred
}
