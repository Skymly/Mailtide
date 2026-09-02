using System.Text.Json;

namespace Mailtide.Core.Store;

internal static class PackedStringList
{
    public static string Encode(IReadOnlyList<string> values) =>
        JsonSerializer.Serialize(values);

    public static IReadOnlyList<string> Decode(string? encoded) =>
        string.IsNullOrWhiteSpace(encoded)
            ? []
            : JsonSerializer.Deserialize<string[]>(encoded) ?? [];
}
