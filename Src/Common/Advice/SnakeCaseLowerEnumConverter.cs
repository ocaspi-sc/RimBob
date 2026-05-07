using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimAI.Core.Advice;

internal sealed class SnakeCaseLowerEnumConverter<T>()
    : JsonStringEnumConverter<T>(JsonNamingPolicy.SnakeCaseLower)
    where T : struct, Enum;
