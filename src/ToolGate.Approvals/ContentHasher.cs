using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ToolGate.Approvals;

/// <summary>
/// Computes a deterministic SHA-256 hash of tool name and arguments for content-based approval matching.
/// </summary>
internal static class ContentHasher
{
    /// <summary>
    /// Computes a hex-encoded SHA-256 hash from the tool name and arguments.
    /// Arguments are normalized through a JSON round-trip to ensure consistent hashing
    /// regardless of whether values are CLR types or <see cref="JsonElement"/> instances
    /// (as produced by MEAI deserialization). Keys are sorted lexicographically.
    /// A null-character separator prevents tool name/argument boundary ambiguity.
    /// </summary>
    public static string Compute(string toolName, IDictionary<string, object?>? arguments)
    {
        var sb = new StringBuilder();
        sb.Append(toolName);
        sb.Append('\0');

        if (arguments is { Count: > 0 })
        {
            // Normalize: serialize to JSON, re-parse to get consistent JsonElement types,
            // then re-serialize with sorted keys. This ensures identical hashes whether
            // values are CLR types (int, string) or JsonElement instances.
            var json = JsonSerializer.Serialize(arguments);
            using var doc = JsonDocument.Parse(json);
            var sorted = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
                sorted[prop.Name] = prop.Value.Clone();
            sb.Append(JsonSerializer.Serialize(sorted));
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
