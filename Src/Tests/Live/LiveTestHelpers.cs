using System.Text.Json;
using Xunit.Abstractions;

namespace RimAI.Tests.Live;

internal static class LiveTestHelpers
{
    public static Uri ResolveBaseUri(string environmentVariable, string fallback)
    {
        string? configured = Environment.GetEnvironmentVariable(environmentVariable);
        string value = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        return new Uri(value.EndsWith('/') ? value : $"{value}/");
    }

    public static async Task<string?> TryGetStringAsync(
        HttpClient http,
        string path,
        ITestOutputHelper output,
        string serviceName)
    {
        try
        {
            return await http.GetStringAsync(path);
        }
        catch (HttpRequestException ex)
        {
            output.WriteLine($"{serviceName} unavailable; live test not running. {ex.Message}");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            output.WriteLine($"{serviceName} unavailable; live test timed out. {ex.Message}");
            return null;
        }
    }

    public static int? SelectPlayerHomeMapId(string mapsJson)
    {
        using JsonDocument document = JsonDocument.Parse(mapsJson);
        if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind != JsonValueKind.Array)
            return null;

        int? fallback = null;
        foreach (JsonElement map in data.EnumerateArray())
        {
            int? id = TryGetInt32(map, "id");
            if (id is null)
                continue;

            fallback ??= id.Value;
            if (TryGetBoolean(map, "is_player_home") == true)
                return id.Value;
        }

        return fallback;
    }

    public static int CountDataArrayItems(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("data", out JsonElement data) &&
               data.ValueKind == JsonValueKind.Array
            ? data.GetArrayLength()
            : 0;
    }

    public static int? TryGetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out int number) => number,
            _ => null
        };
    }

    public static bool? TryGetBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
