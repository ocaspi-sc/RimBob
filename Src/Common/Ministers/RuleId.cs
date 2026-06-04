using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimBob.Core.Ministers;

[JsonConverter(typeof(RuleIdJsonConverter))]
public readonly record struct RuleId(string Value)
{
    public override string ToString() => Value;

    public static implicit operator RuleId(string value) => new(value);

    public static implicit operator string(RuleId value) => value.Value;
}

public sealed class RuleIdJsonConverter : JsonConverter<RuleId>
{
    public override RuleId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? value = reader.GetString();
        return new RuleId(value ?? "");
    }

    public override void Write(Utf8JsonWriter writer, RuleId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
