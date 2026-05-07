using System.Text.Json.Serialization;

namespace RimAI.Core.Advice;

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<AgendaItemStatus>))]
public enum AgendaItemStatus
{
    Active,
    Completed,
    Deferred
}
