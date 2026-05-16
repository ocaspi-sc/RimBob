using System.Text.Json.Serialization;

namespace RimBob.Core.Advice;

[JsonConverter(typeof(JsonStringEnumConverter<FeedbackAction>))]
public enum FeedbackAction
{
    Accept,
    Dismiss,
    Modify
}
