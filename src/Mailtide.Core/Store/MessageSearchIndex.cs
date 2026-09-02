using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core.Store;

/// <summary>
/// FTS5 index over Message subject/from/body and stripped HTML. Search does not
/// load BodyHtml into the application.
/// </summary>
internal static class MessageSearchIndex
{
    public static async Task UpsertAsync(
        MailtideDbContext db,
        Guid messageId,
        string subject,
        string fromAddress,
        string bodyText,
        string? bodyHtml,
        CancellationToken cancellationToken)
    {
        var id = messageId.ToString("D");
        await DeleteAsync(db, messageId, cancellationToken).ConfigureAwait(false);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            INSERT INTO MessageFts (MessageId, Subject, FromAddress, BodyText, HtmlText)
            VALUES (@id, @subject, @from, @body, @html)
            """;
        Add(command, "@id", id);
        Add(command, "@subject", subject);
        Add(command, "@from", fromAddress);
        Add(command, "@body", bodyText);
        Add(command, "@html", HtmlText.Strip(bodyHtml));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task DeleteAsync(
        MailtideDbContext db,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "DELETE FROM MessageFts WHERE MessageId = @id";
        Add(command, "@id", messageId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task RebuildAllAsync(MailtideDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("DELETE FROM MessageFts", cancellationToken)
            .ConfigureAwait(false);

        var rows = await db.Messages
            .AsNoTracking()
            .Select(m => new { m.Id, m.Subject, m.FromAddress, m.BodyText, m.BodyHtml })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            await UpsertAsync(
                    db,
                    row.Id,
                    row.Subject,
                    row.FromAddress,
                    row.BodyText,
                    row.BodyHtml,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public static async Task<IReadOnlyList<Guid>> SearchIdsAsync(
        MailtideDbContext db,
        string text,
        CancellationToken cancellationToken)
    {
        var match = ToMatchQuery(text);
        if (string.IsNullOrEmpty(match))
        {
            return [];
        }

        var ids = new List<Guid>();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT MessageId FROM MessageFts WHERE MessageFts MATCH @q";
        Add(command, "@q", match);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Guid.TryParse(reader.GetString(0), out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    public static string ToMatchQuery(string text)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(" AND ", tokens.Select(QuotePrefix));
    }

    private static string QuotePrefix(string token)
    {
        var escaped = token.Replace("\"", "\"\"", StringComparison.Ordinal);
        return "\"" + escaped + "\"*";
    }

    private static void Add(System.Data.Common.DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
