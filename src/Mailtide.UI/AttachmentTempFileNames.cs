namespace Mailtide.UI;

/// <summary>
/// Shared filename sanitization for Host open-attachment temp files.
/// </summary>
public static class AttachmentTempFileNames
{
    public static string BuildUniqueFileName(string fileName, string contentType)
    {
        var leaf = Path.GetFileName(fileName.Replace('\\', '/'));
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            leaf = leaf.Replace(c, '_');
        }

        leaf = leaf.Trim();
        if (string.IsNullOrWhiteSpace(leaf) || leaf is "." or "..")
        {
            leaf = "attachment" + ExtensionFor(contentType, originalName: fileName);
        }
        else if (string.IsNullOrEmpty(Path.GetExtension(leaf)))
        {
            leaf += ExtensionFor(contentType, originalName: fileName);
        }

        return $"{Guid.NewGuid():N}-{leaf}";
    }

    private static string ExtensionFor(string contentType, string originalName)
    {
        var fromName = Path.GetExtension(Path.GetFileName(originalName.Replace('\\', '/')));
        if (!string.IsNullOrEmpty(fromName) && fromName.Length <= 16)
        {
            return fromName;
        }

        return contentType.Trim().ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "text/plain" => ".txt",
            "text/html" => ".html",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "application/zip" => ".zip",
            _ => string.Empty,
        };
    }
}
