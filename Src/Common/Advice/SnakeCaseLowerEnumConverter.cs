using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimBob.Core.Advice;

internal sealed class SnakeCaseLowerEnumConverter<T>()
    : JsonStringEnumConverter<T>(JsonNamingPolicy.SnakeCaseLower)
    where T : struct, Enum;
