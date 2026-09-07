namespace Mailtide.Core;

public static class MailSignature
{
    public const string Marker = "-- ";

    public static string Apply(string? body, string? signature, bool beforeQuoted = false)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return body ?? string.Empty;
        }

        var trimmed = signature.ReplaceLineEndings("\n").TrimEnd();
        var block = Marker + "\n" + trimmed;
        var text = body ?? string.Empty;
        if (text.Contains(block, StringComparison.Ordinal))
        {
            return text;
        }

        if (string.IsNullOrEmpty(text))
        {
            return "\n\n" + block;
        }

        return beforeQuoted ? "\n\n" + block + "\n" + text : text + "\n\n" + block;
    }

    public static string Without(string? body, string? signature)
    {
        var text = body ?? string.Empty;
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrEmpty(text))
        {
            return text;
        }

        var trimmed = signature.ReplaceLineEndings("\n").TrimEnd();
        var block = Marker + "\n" + trimmed;
        var stripped = text.Replace("\n\n" + block, string.Empty, StringComparison.Ordinal)
            .Replace(block, string.Empty, StringComparison.Ordinal);
        return stripped.Trim();
    }

    public static string Replace(string? body, string? oldSignature, string? newSignature)
    {
        var stripped = Without(body, oldSignature);
        var quoted = stripped.Contains(" wrote:", StringComparison.Ordinal)
            || stripped.Contains("\n> ", StringComparison.Ordinal)
            || stripped.StartsWith("> ", StringComparison.Ordinal);
        return Apply(stripped, newSignature, beforeQuoted: quoted);
    }
}
