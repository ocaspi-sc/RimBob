using System.Text.Json;
using System.Text.Json.Nodes;

namespace RimBob.LLM;

internal static class LlmResponseParser
{
    public static T ParseOrNormalize<T>(
        string text,
        JsonSerializerOptions json,
        Func<JsonNode, T> normalize,
        Action<JsonException>? onStrictParseFailed = null,
        Func<T, bool>? isStrictValid = null)
    {
        try
        {
            T? parsed = JsonSerializer.Deserialize<T>(text, json);
            if (parsed is not null && (isStrictValid is null || isStrictValid(parsed)))
                return parsed;
        }
        catch (JsonException ex)
        {
            onStrictParseFailed?.Invoke(ex);
        }

        JsonNode root = JsonNode.Parse(text) ??
            throw new JsonException("LLM response JSON parsed to null.");
        return normalize(root);
    }

    public static T? TryDeserialize<T>(JsonNode node, JsonSerializerOptions json)
    {
        try
        {
            return node.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public static string? ReadString(JsonNode? node)
    {
        JsonValue? value = node as JsonValue;
        if (value is null) return null;
        return value.TryGetValue<string>(out string? text) ? text : value.ToJsonString();
    }

    public static string? ReadNumberAsString(JsonNode? node)
    {
        JsonValue? value = node as JsonValue;
        if (value is null) return null;
        if (value.TryGetValue<decimal>(out decimal decimalValue)) return decimalValue.ToString("0.##");
        if (value.TryGetValue<int>(out int intValue)) return intValue.ToString();
        return ReadString(node);
    }

    public static int? TryReadIntegerQuantity(JsonNode? node)
    {
        if (node is null) return null;
        if (!decimal.TryParse(ReadNumberAsString(node), out decimal value)) return null;
        return value == decimal.Truncate(value) && value <= int.MaxValue ? (int)value : null;
    }

    public static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string trimmed = value.Trim().Replace('-', '_').Replace(' ', '_');
        if (trimmed.Contains('_')) return trimmed.ToLowerInvariant();

        List<char> chars = new(trimmed.Length + 4);
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (char.IsUpper(c) && i > 0) chars.Add('_');
            chars.Add(char.ToLowerInvariant(c));
        }
        return new string(chars.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
    }

    public static string HumanizeIdentifier(string value)
    {
        string snake = ToSnakeCase(value);
        string[] words = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "LLM advice";
        return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    public static string NormalizeIdentifier(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
