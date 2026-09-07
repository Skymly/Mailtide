namespace Mailtide.Core;

public static class AttachmentContentType
{
    public const string OctetStream = "application/octet-stream";

    public static string FromFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var ext = Path.GetExtension(fileName);
        if (ext.Length == 0)
        {
            return OctetStream;
        }

        return Types.TryGetValue(ext, out var type) ? type : OctetStream;
    }

    public static string Resolve(string fileName, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType)
            && !contentType.Equals(OctetStream, StringComparison.OrdinalIgnoreCase))
        {
            return contentType;
        }

        return FromFileName(fileName);
    }

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".json"] = "application/json",
        [".pdf"] = "application/pdf",
        [".zip"] = "application/zip",
        [".eml"] = "message/rfc822",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".mp3"] = "audio/mpeg",
        [".mp4"] = "video/mp4",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    };
}
