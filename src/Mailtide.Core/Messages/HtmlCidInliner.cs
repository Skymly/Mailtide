namespace Mailtide.Core;

public static class HtmlCidInliner
{
    public static string Inline(string html, IEnumerable<(string ContentId, string ContentType, byte[] Content)> parts)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(parts);

        var result = html;
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part.ContentId) || part.Content.Length == 0)
            {
                continue;
            }

            var id = part.ContentId.Trim().Trim('<', '>');
            if (id.Length == 0)
            {
                continue;
            }

            var data = "data:" + part.ContentType + ";base64," + Convert.ToBase64String(part.Content);
            result = result.Replace("cid:" + id, data, StringComparison.OrdinalIgnoreCase);
            result = result.Replace("CID:" + id, data, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}
