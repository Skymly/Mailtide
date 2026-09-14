using System.Globalization;

namespace Mailtide.Core;

public static class MessageSearch
{
    public const string FlaggedOperator = "is:flagged";
    public const string StarredOperator = "is:starred";
    public const string UnreadOperator = "is:unread";
    public const string FailedOperator = "is:failed";
    public const string ReadOperator = "is:read";
    public const string AttachmentOperator = "has:attachment";
    public const string FromOperatorPrefix = "from:";
    public const string ToOperatorPrefix = "to:";
    public const string CcOperatorPrefix = "cc:";
    public const string BccOperatorPrefix = "bcc:";
    public const string SubjectOperatorPrefix = "subject:";
    public const string AfterOperatorPrefix = "after:";
    public const string BeforeOperatorPrefix = "before:";
    public const string OlderThanOperatorPrefix = "older_than:";
    public const string NewerThanOperatorPrefix = "newer_than:";
    public const string InOperatorPrefix = "in:";
    public const string LabelOperatorPrefix = "label:";
    public const string FilenameOperatorPrefix = "filename:";
    public const string FileOperatorPrefix = "file:";
    public const string Rfc822MessageIdOperatorPrefix = "rfc822msgid:";
    public const string MessageIdOperatorPrefix = "msgid:";
    public const string OnOperatorPrefix = "on:";
    public const string LargerOperatorPrefix = "larger:";
    public const string SmallerOperatorPrefix = "smaller:";
    public const string SizeOperatorPrefix = "size:";

    public sealed record Parsed(
        bool FlaggedOnly,
        bool UnreadOnly,
        bool ReadOnly,
        bool AttachmentOnly,
        string? FromContains,
        string? ToContains,
        string? CcContains,
        string? BccContains,
        string? SubjectContains,
        string Text,
        DateTimeOffset? After = null,
        DateTimeOffset? Before = null,
        MailboxRole? InRole = null,
        string? FilenameContains = null,
        string? FromExclude = null,
        string? ToExclude = null,
        string? CcExclude = null,
        string? BccExclude = null,
        string? SubjectExclude = null,
        string? FilenameExclude = null,
        bool WithoutAttachment = false,
        IReadOnlyList<string>? TextExcludes = null,
        bool NotFlagged = false,
        bool InAnywhere = false,
        bool FailedOnly = false,
        string? MessageIdContains = null,
        long? LargerThan = null,
        long? SmallerThan = null,
        bool FromMe = false,
        bool ToMe = false,
        string? InMailboxContains = null);

    public static bool Matches(
        bool isFlagged,
        bool isRead,
        string subject,
        string fromAddress,
        string? bodyText,
        string? bodyHtml,
        string query,
        string? toAddresses = null,
        string? ccAddresses = null,
        string? bccAddresses = null,
        bool hasAttachment = false,
        IEnumerable<string>? attachmentNames = null,
        string? internetMessageId = null,
        long sizeBytes = 0,
        IEnumerable<string>? selfAddresses = null)
    {
        var clauses = OrClauses(query);
        if (clauses.Count > 1)
        {
            return clauses.Any(clause => Matches(
                isFlagged,
                isRead,
                subject,
                fromAddress,
                bodyText,
                bodyHtml,
                clause,
                toAddresses,
                ccAddresses,
                bccAddresses,
                hasAttachment,
                attachmentNames,
                internetMessageId,
                sizeBytes,
                selfAddresses));
        }

        var parsed = Parse(query);
        if (parsed.FlaggedOnly && !isFlagged)
        {
            return false;
        }

        if (parsed.NotFlagged && isFlagged)
        {
            return false;
        }

        if (parsed.UnreadOnly && isRead)
        {
            return false;
        }

        if (parsed.ReadOnly && !isRead)
        {
            return false;
        }

        if (parsed.AttachmentOnly && !hasAttachment)
        {
            return false;
        }

        if (parsed.WithoutAttachment && hasAttachment)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.FromExclude)
            && ContainsIgnoreCase(fromAddress, parsed.FromExclude))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.ToExclude)
            && ContainsIgnoreCase(toAddresses, parsed.ToExclude))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.CcExclude)
            && ContainsIgnoreCase(ccAddresses, parsed.CcExclude))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.BccExclude)
            && ContainsIgnoreCase(bccAddresses, parsed.BccExclude))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.SubjectExclude)
            && ContainsIgnoreCase(subject, parsed.SubjectExclude))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.FilenameExclude)
            && ContainsAnyIgnoreCase(attachmentNames, parsed.FilenameExclude))
        {
            return false;
        }

        if (parsed.TextExcludes is { Count: > 0 } excludes)
        {
            foreach (var exclude in excludes)
            {
                if (ContainsIgnoreCase(subject, exclude)
                    || ContainsIgnoreCase(fromAddress, exclude)
                    || ContainsIgnoreCase(bodyText, exclude)
                    || ContainsIgnoreCase(bodyHtml, exclude)
                    || ContainsAnyIgnoreCase(attachmentNames, exclude))
                {
                    return false;
                }
            }
        }

        if (!string.IsNullOrEmpty(parsed.FilenameContains)
            && !ContainsAnyIgnoreCase(attachmentNames, parsed.FilenameContains))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.FromContains)
            && !ContainsIgnoreCase(fromAddress, parsed.FromContains))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.ToContains)
            && !ContainsIgnoreCase(toAddresses, parsed.ToContains))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.CcContains)
            && !ContainsIgnoreCase(ccAddresses, parsed.CcContains))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.BccContains)
            && !ContainsIgnoreCase(bccAddresses, parsed.BccContains))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.SubjectContains)
            && !ContainsIgnoreCase(subject, parsed.SubjectContains))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.MessageIdContains)
            && !MatchesMessageId(internetMessageId, parsed.MessageIdContains))
        {
            return false;
        }

        if (parsed.FromMe && !ContainsSelf(fromAddress, selfAddresses))
        {
            return false;
        }

        if (parsed.ToMe
            && !ContainsSelf(toAddresses, selfAddresses)
            && !ContainsSelf(ccAddresses, selfAddresses)
            && !ContainsSelf(bccAddresses, selfAddresses))
        {
            return false;
        }

        if (parsed.LargerThan is { } larger && sizeBytes <= larger)
        {
            return false;
        }

        if (parsed.SmallerThan is { } smaller && sizeBytes >= smaller)
        {
            return false;
        }

        if (string.IsNullOrEmpty(parsed.Text))
        {
            return true;
        }

        return ContainsIgnoreCase(subject, parsed.Text)
            || ContainsIgnoreCase(fromAddress, parsed.Text)
            || ContainsIgnoreCase(bodyText, parsed.Text)
            || ContainsIgnoreCase(bodyHtml, parsed.Text)
            || ContainsAnyIgnoreCase(attachmentNames, parsed.Text);
    }

    public static bool IsChipFilterOnly(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var parsed = Parse(query);
        if (!string.IsNullOrEmpty(parsed.Text)
            || parsed.FromContains is not null
            || parsed.ToContains is not null
            || parsed.CcContains is not null
            || parsed.BccContains is not null
            || parsed.SubjectContains is not null
            || parsed.After is not null
            || parsed.Before is not null
            || parsed.InRole is not null
            || parsed.FilenameContains is not null
            || parsed.FromExclude is not null
            || parsed.ToExclude is not null
            || parsed.CcExclude is not null
            || parsed.BccExclude is not null
            || parsed.SubjectExclude is not null
            || parsed.FilenameExclude is not null
            || parsed.InAnywhere
            || parsed.MessageIdContains is not null
            || parsed.LargerThan is not null
            || parsed.SmallerThan is not null
            || parsed.FromMe
            || parsed.ToMe
            || parsed.InMailboxContains is not null
            || parsed.TextExcludes is { Count: > 0 })
        {
            return false;
        }

        return parsed.UnreadOnly
            || parsed.ReadOnly
            || parsed.FlaggedOnly
            || parsed.NotFlagged
            || parsed.AttachmentOnly
            || parsed.WithoutAttachment
            || parsed.FailedOnly;
    }

    public static string? KeepChipFilter(string? query, bool outbox)
    {
        if (!IsChipFilterOnly(query))
        {
            return null;
        }

        var parsed = Parse(query!);
        var mailChip = parsed.UnreadOnly
            || parsed.ReadOnly
            || parsed.FlaggedOnly
            || parsed.NotFlagged
            || parsed.AttachmentOnly
            || parsed.WithoutAttachment;
        if (outbox)
        {
            return parsed.FailedOnly ? query!.Trim() : null;
        }

        return mailChip ? query!.Trim() : null;
    }

    public static string ToggleUnread(string? query) => ToggleOperator(query, UnreadOperator);

    public static string ToggleFailed(string? query) => ToggleOperator(query, FailedOperator);

    public static string ToggleFlagged(string? query) => ToggleOperator(query, FlaggedOperator);

    public static string ToggleAttachment(string? query)
    {
        var tokens = Tokenize(query ?? string.Empty).ToList();
        var removed = tokens.RemoveAll(token =>
            token.Equals(AttachmentOperator, StringComparison.OrdinalIgnoreCase)
            || token.Equals("has:attach", StringComparison.OrdinalIgnoreCase)
            || token.Equals("has:attachments", StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            tokens.Add(AttachmentOperator);
        }

        return JoinTokens(tokens);
    }

    public static string ToggleOperator(string? query, string op)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(op);
        var tokens = Tokenize(query ?? string.Empty).ToList();
        var removed = tokens.RemoveAll(token => token.Equals(op, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            tokens.Add(op);
        }

        return JoinTokens(tokens);
    }

    public static IReadOnlyList<string> Tokenize(string query)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < query.Length)
        {
            while (i < query.Length && char.IsWhiteSpace(query[i]))
            {
                i++;
            }

            if (i >= query.Length)
            {
                break;
            }

            if (query[i] == '-' && i + 1 < query.Length && query[i + 1] == '"')
            {
                i += 2;
                var start = i;
                while (i < query.Length && query[i] != '"')
                {
                    i++;
                }

                var quoted = query[start..i];
                if (quoted.Length > 0)
                {
                    tokens.Add("-" + quoted);
                }

                if (i < query.Length)
                {
                    i++;
                }

                continue;
            }

            if (query[i] == '"')
            {
                i++;
                var start = i;
                while (i < query.Length && query[i] != '"')
                {
                    i++;
                }

                var quoted = query[start..i];
                if (quoted.Length > 0)
                {
                    tokens.Add(quoted);
                }

                if (i < query.Length)
                {
                    i++;
                }

                continue;
            }

            var tokenStart = i;
            while (i < query.Length && !char.IsWhiteSpace(query[i]) && query[i] != '"')
            {
                i++;
            }

            if (i < query.Length
                && query[i] == '"'
                && i > tokenStart
                && query[i - 1] == ':')
            {
                var prefix = query[tokenStart..i];
                i++;
                var valueStart = i;
                while (i < query.Length && query[i] != '"')
                {
                    i++;
                }

                var value = query[valueStart..i];
                if (value.Length > 0)
                {
                    tokens.Add(prefix + value);
                }

                if (i < query.Length)
                {
                    i++;
                }

                continue;
            }

            var token = query[tokenStart..i];
            if (token.Length > 0)
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    public static IReadOnlyList<string> OrClauses(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [query ?? string.Empty];
        }

        var clauses = new List<string>();
        var current = new List<string>();
        foreach (var token in Tokenize(query))
        {
            if (token.Equals("OR", StringComparison.Ordinal))
            {
                if (current.Count > 0)
                {
                    clauses.Add(JoinTokens(current));
                    current.Clear();
                }

                continue;
            }

            current.Add(token);
        }

        if (current.Count > 0)
        {
            clauses.Add(JoinTokens(current));
        }

        return clauses.Count == 0 ? [query] : clauses;
    }

    public static Parsed Parse(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new Parsed(false, false, false, false, null, null, null, null, null, string.Empty);
        }

        var flaggedOnly = false;
        var unreadOnly = false;
        var failedOnly = false;
        var readOnly = false;
        var attachmentOnly = false;
        string? fromContains = null;
        string? toContains = null;
        string? ccContains = null;
        string? bccContains = null;
        string? subjectContains = null;
        string? filenameContains = null;
        string? fromExclude = null;
        string? toExclude = null;
        string? ccExclude = null;
        string? bccExclude = null;
        string? subjectExclude = null;
        string? filenameExclude = null;
        var withoutAttachment = false;
        var notFlagged = false;
        var textExcludes = new List<string>();
        DateTimeOffset? afterUtc = null;
        DateTimeOffset? beforeUtc = null;
        MailboxRole? inRole = null;
        var inAnywhere = false;
        string? inMailboxContains = null;
        string? messageIdContains = null;
        var fromMe = false;
        var toMe = false;
        long? largerThan = null;
        long? smallerThan = null;
        var textParts = new List<string>();
        foreach (var rawToken in Tokenize(query))
        {
            var negated = rawToken.StartsWith('-') && rawToken.Length > 1;
            var token = negated ? rawToken[1..] : rawToken;
            if (token.Equals(FlaggedOperator, StringComparison.OrdinalIgnoreCase)
                || token.Equals(StarredOperator, StringComparison.OrdinalIgnoreCase))
            {
                if (negated)
                {
                    notFlagged = true;
                }
                else
                {
                    flaggedOnly = true;
                }

                continue;
            }

            if (token.Equals(UnreadOperator, StringComparison.OrdinalIgnoreCase))
            {
                if (negated)
                {
                    readOnly = true;
                }
                else
                {
                    unreadOnly = true;
                }

                continue;
            }

            if (token.Equals(FailedOperator, StringComparison.OrdinalIgnoreCase))
            {
                if (!negated)
                {
                    failedOnly = true;
                }

                continue;
            }

            if (token.Equals(ReadOperator, StringComparison.OrdinalIgnoreCase))
            {
                if (negated)
                {
                    unreadOnly = true;
                }
                else
                {
                    readOnly = true;
                }

                continue;
            }

            if (token.Equals(AttachmentOperator, StringComparison.OrdinalIgnoreCase)
                || token.Equals("has:attach", StringComparison.OrdinalIgnoreCase)
                || token.Equals("has:attachments", StringComparison.OrdinalIgnoreCase))
            {
                if (negated)
                {
                    withoutAttachment = true;
                }
                else
                {
                    attachmentOnly = true;
                }

                continue;
            }

            if (TryPrefix(token, FromOperatorPrefix, out var from))
            {
                if (IsMeToken(from))
                {
                    if (!negated)
                    {
                        fromMe = true;
                    }

                    continue;
                }

                if (negated)
                {
                    fromExclude = from;
                }
                else
                {
                    fromContains = from;
                }

                continue;
            }

            if (TryPrefix(token, ToOperatorPrefix, out var to))
            {
                if (IsMeToken(to))
                {
                    if (!negated)
                    {
                        toMe = true;
                    }

                    continue;
                }

                if (negated)
                {
                    toExclude = to;
                }
                else
                {
                    toContains = to;
                }

                continue;
            }

            if (TryPrefix(token, CcOperatorPrefix, out var cc))
            {
                if (IsMeToken(cc))
                {
                    if (!negated)
                    {
                        toMe = true;
                    }

                    continue;
                }

                if (negated)
                {
                    ccExclude = cc;
                }
                else
                {
                    ccContains = cc;
                }

                continue;
            }

            if (TryPrefix(token, BccOperatorPrefix, out var bcc))
            {
                if (IsMeToken(bcc))
                {
                    if (!negated)
                    {
                        toMe = true;
                    }

                    continue;
                }

                if (negated)
                {
                    bccExclude = bcc;
                }
                else
                {
                    bccContains = bcc;
                }

                continue;
            }

            if (TryPrefix(token, SubjectOperatorPrefix, out var subject))
            {
                if (negated)
                {
                    subjectExclude = subject;
                }
                else
                {
                    subjectContains = subject;
                }

                continue;
            }

            if (TryPrefix(token, AfterOperatorPrefix, out var afterText)
                && TryParseDate(afterText, out var after))
            {
                if (!negated)
                {
                    afterUtc = after;
                }

                continue;
            }

            if (TryPrefix(token, BeforeOperatorPrefix, out var beforeText)
                && TryParseDate(beforeText, out var before))
            {
                if (!negated)
                {
                    beforeUtc = before;
                }

                continue;
            }

            if (TryPrefix(token, OlderThanOperatorPrefix, out var olderText)
                && TryParseRelativeAge(olderText, out var older))
            {
                if (!negated)
                {
                    beforeUtc = DateTimeOffset.UtcNow - older;
                }

                continue;
            }

            if (TryPrefix(token, NewerThanOperatorPrefix, out var newerText)
                && TryParseRelativeAge(newerText, out var newer))
            {
                if (!negated)
                {
                    afterUtc = DateTimeOffset.UtcNow - newer;
                }

                continue;
            }

            if (TryPrefix(token, OnOperatorPrefix, out var onText)
                && TryParseDateRange(onText, out var onStart, out var onEnd))
            {
                if (!negated)
                {
                    afterUtc = onStart;
                    beforeUtc = onEnd;
                }

                continue;
            }

            if (TryPrefix(token, Rfc822MessageIdOperatorPrefix, out var messageId)
                || TryPrefix(token, MessageIdOperatorPrefix, out messageId))
            {
                if (!negated)
                {
                    messageIdContains = messageId;
                }

                continue;
            }

            if ((TryPrefix(token, LargerOperatorPrefix, out var largerText)
                    || TryPrefix(token, SizeOperatorPrefix, out largerText))
                && TryParseByteSize(largerText, out var larger))
            {
                if (!negated)
                {
                    largerThan = larger;
                }

                continue;
            }

            if (TryPrefix(token, SmallerOperatorPrefix, out var smallerText)
                && TryParseByteSize(smallerText, out var smaller))
            {
                if (!negated)
                {
                    smallerThan = smaller;
                }

                continue;
            }

            if (TryPrefix(token, InOperatorPrefix, out var inText)
                || TryPrefix(token, LabelOperatorPrefix, out inText))
            {
                if (IsAnywhereMailbox(inText))
                {
                    if (!negated)
                    {
                        inAnywhere = true;
                    }

                    continue;
                }

                if (TryParseMailboxRole(inText, out var role))
                {
                    if (!negated)
                    {
                        inRole = role;
                    }

                    continue;
                }

                if (!negated)
                {
                    inMailboxContains = inText;
                }

                continue;
            }

            if (TryPrefix(token, "is:", out var isValue)
                && TryParseMailboxRole(isValue, out var isRole))
            {
                if (!negated)
                {
                    inRole = isRole;
                }

                continue;
            }

            if (TryPrefix(token, FilenameOperatorPrefix, out var filename)
                || TryPrefix(token, FileOperatorPrefix, out filename))
            {
                if (negated)
                {
                    filenameExclude = filename;
                }
                else
                {
                    filenameContains = filename;
                }

                continue;
            }

            if (negated)
            {
                textExcludes.Add(token);
                continue;
            }

            textParts.Add(token);
        }

        return new Parsed(
            flaggedOnly,
            unreadOnly,
            readOnly,
            attachmentOnly,
            fromContains,
            toContains,
            ccContains,
            bccContains,
            subjectContains,
            string.Join(' ', textParts),
            afterUtc,
            beforeUtc,
            inRole,
            filenameContains,
            fromExclude,
            toExclude,
            ccExclude,
            bccExclude,
            subjectExclude,
            filenameExclude,
            withoutAttachment,
            textExcludes,
            notFlagged,
            inAnywhere,
            failedOnly,
            messageIdContains,
            largerThan,
            smallerThan,
            fromMe,
            toMe,
            inMailboxContains);
    }

    private static string JoinTokens(IEnumerable<string> tokens) =>
        string.Join(' ', tokens.Select(QuoteToken));

    private static string QuoteToken(string token)
    {
        if (token.IndexOfAny([' ', '	']) < 0)
        {
            return token;
        }

        var colon = token.IndexOf(':');
        if (colon > 0 && colon < token.Length - 1)
        {
            return token[..(colon + 1)] + '"' + token[(colon + 1)..] + '"';
        }

        return '"' + token + '"';
    }

    public static bool MailboxMatchesIn(string? name, string? path, string? inMailbox)
    {
        if (string.IsNullOrWhiteSpace(inMailbox))
        {
            return false;
        }

        var needle = NormalizeInMailbox(inMailbox);
        if (needle.Length == 0)
        {
            return false;
        }

        if (MatchesInNeedle(name, needle) || MatchesInNeedle(path, needle))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = NormalizeInMailbox(path);
        var slash = normalizedPath.LastIndexOf('/');
        return slash >= 0
            && slash < normalizedPath.Length - 1
            && normalizedPath[(slash + 1)..].Equals(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeInMailbox(string value) =>
        value.Trim().Replace('\\', '/').Replace('.', '/').Trim('/');

    private static bool MatchesInNeedle(string? value, string needle)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals(needle, StringComparison.OrdinalIgnoreCase)
            || NormalizeInMailbox(value).Equals(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryPrefix(string token, string prefix, out string value)
    {
        if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && token.Length > prefix.Length)
        {
            value = token[prefix.Length..];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsAnywhereMailbox(string value) =>
        value.Equals("anywhere", StringComparison.OrdinalIgnoreCase)
        || value.Equals("all", StringComparison.OrdinalIgnoreCase)
        || value.Equals("any", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseMailboxRole(string value, out MailboxRole role)
    {
        if (value.Equals("inbox", StringComparison.OrdinalIgnoreCase))
        {
            role = MailboxRole.Inbox;
            return true;
        }

        if (value.Equals("sent", StringComparison.OrdinalIgnoreCase))
        {
            role = MailboxRole.Sent;
            return true;
        }

        if (value.Equals("drafts", StringComparison.OrdinalIgnoreCase))
        {
            role = MailboxRole.Drafts;
            return true;
        }

        if (value.Equals("trash", StringComparison.OrdinalIgnoreCase)
            || value.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || value.Equals("deleted", StringComparison.OrdinalIgnoreCase))
        {
            role = MailboxRole.Trash;
            return true;
        }

        if (value.Equals("junk", StringComparison.OrdinalIgnoreCase)
            || value.Equals("spam", StringComparison.OrdinalIgnoreCase))
        {
            role = MailboxRole.Junk;
            return true;
        }

        if (value.Equals("draft", StringComparison.OrdinalIgnoreCase))
        {
            role = MailboxRole.Drafts;
            return true;
        }

        if (value.Equals("archive", StringComparison.OrdinalIgnoreCase)
            || value.Equals("archives", StringComparison.OrdinalIgnoreCase))
        {
            role = MailboxRole.Archive;
            return true;
        }

        role = default;
        return false;
    }

    public static bool TryParseByteSize(string value, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var raw = value.Trim();
        var multiplier = 1L;
        if (raw.Length >= 2)
        {
            var suffix2 = raw[^2..].ToLowerInvariant();
            if (suffix2 is "kb" or "mb" or "gb")
            {
                multiplier = suffix2[0] switch
                {
                    'k' => 1024L,
                    'm' => 1024L * 1024,
                    'g' => 1024L * 1024 * 1024,
                    _ => 1L,
                };
                raw = raw[..^2];
            }
        }

        if (multiplier == 1 && raw.Length >= 1)
        {
            var unit = char.ToLowerInvariant(raw[^1]);
            if (unit is 'k' or 'm' or 'g' or 'b')
            {
                multiplier = unit switch
                {
                    'k' => 1024L,
                    'm' => 1024L * 1024,
                    'g' => 1024L * 1024 * 1024,
                    _ => 1L,
                };
                raw = raw[..^1];
            }
        }

        if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
            || amount < 0)
        {
            return false;
        }

        try
        {
            bytes = checked(amount * multiplier);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static bool TryParseRelativeAge(string value, out TimeSpan age)
    {
        age = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length < 2)
        {
            return false;
        }

        var unit = char.ToLowerInvariant(value[^1]);
        if (unit is not ('d' or 'w' or 'm' or 'y'))
        {
            return false;
        }

        if (!int.TryParse(value[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
            || amount < 0)
        {
            return false;
        }

        age = unit switch
        {
            'd' => TimeSpan.FromDays(amount),
            'w' => TimeSpan.FromDays(amount * 7.0),
            'm' => TimeSpan.FromDays(amount * 30.0),
            'y' => TimeSpan.FromDays(amount * 365.0),
            _ => default,
        };
        return true;
    }

    private static DateTimeOffset LocalDayStart(int offsetDays)
    {
        var now = DateTimeOffset.Now;
        var start = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        return start.AddDays(offsetDays);
    }

    private static DateTimeOffset LocalWeekStart(int offsetDays)
    {
        var start = LocalDayStart(0);
        var mondayOffset = ((int)start.DayOfWeek + 6) % 7;
        return start.AddDays(-mondayOffset + offsetDays);
    }

    private static DateTimeOffset LocalMonthStart(int offsetMonths)
    {
        var now = DateTimeOffset.Now;
        var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
        return start.AddMonths(offsetMonths);
    }

    private static DateTimeOffset LocalYearStart(int offsetYears)
    {
        var now = DateTimeOffset.Now;
        var start = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, now.Offset);
        return start.AddYears(offsetYears);
    }

    private static DateTimeOffset DateRangeEnd(string token, DateTimeOffset start)
    {
        if (token.Equals("thisweek", StringComparison.OrdinalIgnoreCase)
            || token.Equals("lastweek", StringComparison.OrdinalIgnoreCase))
        {
            return start.AddDays(7);
        }

        if (token.Equals("thismonth", StringComparison.OrdinalIgnoreCase)
            || token.Equals("lastmonth", StringComparison.OrdinalIgnoreCase))
        {
            return start.AddMonths(1);
        }

        if (token.Equals("thisyear", StringComparison.OrdinalIgnoreCase)
            || token.Equals("lastyear", StringComparison.OrdinalIgnoreCase))
        {
            return start.AddYears(1);
        }

        return start.AddDays(1);
    }

    private static bool TryParseDateRange(string value, out DateTimeOffset start, out DateTimeOffset end)
    {
        start = default;
        end = default;
        if (TryParseDate(value, out start))
        {
            var trimmed = value.Trim();
            end = DateRangeEnd(trimmed, start);
            return true;
        }

        return false;
    }

    private static bool TryParseDate(string value, out DateTimeOffset parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            parsed = LocalDayStart(0);
            return true;
        }

        if (trimmed.Equals("yesterday", StringComparison.OrdinalIgnoreCase))
        {
            parsed = LocalDayStart(-1);
            return true;
        }

        if (trimmed.Equals("tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            parsed = LocalDayStart(1);
            return true;
        }

        if (trimmed.Equals("thisweek", StringComparison.OrdinalIgnoreCase))
        {
            parsed = LocalWeekStart(0);
            return true;
        }

        if (trimmed.Equals("lastweek", StringComparison.OrdinalIgnoreCase))
        {
            parsed = LocalWeekStart(-7);
            return true;
        }

        if (trimmed.Equals("thismonth", StringComparison.OrdinalIgnoreCase))
        {
            parsed = LocalMonthStart(0);
            return true;
        }

        if (trimmed.Equals("lastmonth", StringComparison.OrdinalIgnoreCase))
        {
            parsed = LocalMonthStart(-1);
            return true;
        }

        if (trimmed.Equals("thisyear", StringComparison.OrdinalIgnoreCase))
        {
            parsed = LocalYearStart(0);
            return true;
        }

        if (trimmed.Equals("lastyear", StringComparison.OrdinalIgnoreCase))
        {
            parsed = LocalYearStart(-1);
            return true;
        }

        return DateTimeOffset.TryParse(
            trimmed,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out parsed);
    }

    public static bool ContainsSelf(string? haystack, IEnumerable<string>? selfAddresses)
    {
        if (string.IsNullOrEmpty(haystack) || selfAddresses is null)
        {
            return false;
        }

        foreach (var address in selfAddresses)
        {
            if (!string.IsNullOrWhiteSpace(address) && ContainsIgnoreCase(haystack, address))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMeToken(string value) =>
        value.Equals("me", StringComparison.OrdinalIgnoreCase);

    public static bool MatchesMessageId(string? stored, string? query)
    {
        var needle = NormalizeMessageId(query);
        return needle.Length > 0
            && NormalizeMessageId(stored).Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeMessageId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '<' && trimmed[^1] == '>')
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed.Trim();
    }

    private static bool ContainsIgnoreCase(string? value, string query) =>
        !string.IsNullOrEmpty(value)
        && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAnyIgnoreCase(IEnumerable<string>? values, string query)
    {
        if (values is null || string.IsNullOrEmpty(query))
        {
            return false;
        }

        foreach (var value in values)
        {
            if (ContainsIgnoreCase(value, query))
            {
                return true;
            }
        }

        return false;
    }
}
