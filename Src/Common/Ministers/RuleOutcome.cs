using System.Text.Json.Serialization;
using RimBob.Core.Advice;

namespace RimBob.Core.Ministers;

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<RuleOutcome>))]
public enum RuleOutcome
{
    Selected,
    NotMatched,
    Escalated
}
