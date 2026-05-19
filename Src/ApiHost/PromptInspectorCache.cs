using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RimBob.Host;

public sealed class PromptInspectorCache
{
    private readonly object _lock = new();
    private readonly Dictionary<string, PromptInspectorCacheEntry> _entries =
        new(StringComparer.Ordinal);
    private readonly TimeSpan _ttl;

    public PromptInspectorCache()
        : this(TimeSpan.FromSeconds(30))
    {
    }

    public PromptInspectorCache(TimeSpan ttl)
    {
        _ttl = ttl;
    }

    public async Task<PromptInspectorPayload> GetOrCreateAsync(
        string key,
        bool refresh,
        Func<CancellationToken, Task<PromptInspectorPayload>> create,
        CancellationToken ct)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!refresh)
        {
            lock (_lock)
            {
                if (_entries.TryGetValue(key, out PromptInspectorCacheEntry? entry) &&
                    now - entry.CapturedAt <= _ttl)
                {
                    return entry.Payload;
                }
            }
        }

        PromptInspectorPayload payload = await create(ct);
        lock (_lock)
        {
            _entries[key] = new PromptInspectorCacheEntry(payload, DateTimeOffset.UtcNow);
        }

        return payload;
    }

    public static string BuildKey(string scope, params object?[] parts)
    {
        string[] normalized = parts
            .Select(part => part switch
            {
                null => "null",
                string value => value,
                _ => part.ToString() ?? "null",
            })
            .ToArray();
        return $"{scope}:{string.Join(':', normalized)}";
    }

    public static string HashJson<T>(T value)
    {
        string json = JsonSerializer.Serialize(value);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private sealed record PromptInspectorCacheEntry(
        PromptInspectorPayload Payload,
        DateTimeOffset CapturedAt);
}

public sealed record PromptInspectorPayload(string System, string User);
