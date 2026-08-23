namespace Mailtide.Core;

public static class MessagePreview
{
    public const int MaxLength = 80;

    public static string FromBodyText(string? bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
        {
            return string.Empty;
        }

        var line = bodyText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[0];
        var collapsed = string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length <= MaxLength)
        {
            return collapsed;
        }

        return collapsed[..MaxLength].TrimEnd() + "…";
    }
}
