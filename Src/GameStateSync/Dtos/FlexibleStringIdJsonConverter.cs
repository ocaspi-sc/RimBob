using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimBob.Ingestion.Dtos;

/// <summary>
/// RIMAPI id fields are not fully consistent across endpoints: some use JSON
/// numbers and others use strings. State aggregates keep ids as strings.
/// </summary>
public sealed class FlexibleStringIdJsonConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.Number when reader.TryGetInt64(out long value) => value.ToString(CultureInfo.InvariantCulture),
            JsonTokenType.Number => reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
            _ => throw new JsonException($"Expected string or number id, got {reader.TokenType}.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
