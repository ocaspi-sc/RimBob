using System.Text.Json.Serialization;

namespace RimAI.Core.Advice;

[JsonConverter(typeof(JsonStringEnumConverter<FeedbackAction>))]
public enum FeedbackAction
{
    Accept,
    Dismiss,
    Modify
}
