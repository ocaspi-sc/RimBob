using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RimAI.Knowledge;

/// <summary>
/// On-disk cache of embeddings keyed by SHA-256 of chunk text. Re-runs skip
/// the embedding API call when the chunk content is unchanged. Append-only
/// for M2 — no GC; corpus is small.
/// </summary>
public sealed class EmbeddingCache
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly string _dir;

    public EmbeddingCache(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(_dir);
    }

    public static string HashText(string text)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public bool TryGet(string hash, out float[] embedding)
    {
        string path = PathFor(hash);
        if (!File.Exists(path))
        {
            embedding = [];
            return false;
        }
        try
        {
            string body = File.ReadAllText(path);
            float[]? cached = JsonSerializer.Deserialize<float[]>(body, Json);
            if (cached is null || cached.Length == 0)
            {
                embedding = [];
                return false;
            }
            embedding = cached;
            return true;
        }
        catch
        {
            embedding = [];
            return false;
        }
    }

    public void Put(string hash, float[] embedding)
    {
        string path = PathFor(hash);
        File.WriteAllText(path, JsonSerializer.Serialize(embedding, Json));
    }

    private string PathFor(string hash) => Path.Combine(_dir, $"{hash}.json");
}
