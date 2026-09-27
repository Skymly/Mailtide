using System.Globalization;
using Mailtide.Core;

namespace Mailtide.UI;

public static class MailShellFormatting
{
    public const int ConversationSnippetLength = 160;

    public const int MaxRecentSearches = 10;

    public const int ThreadListPageSize = 10;

    public const double ThreadDragThreshold = 8;

    public static bool ShouldStartDrag(double deltaX, double deltaY, double threshold = ThreadDragThreshold) =>
        (deltaX * deltaX) + (deltaY * deltaY) >= threshold * threshold;

    public static bool CanPageScroll(double offset, double extent, double viewport, bool down)
    {
        if (viewport <= 0)
        {
            return false;
        }

        var max = Math.Max(0, extent - viewport);
        return down ? offset + 1 < max : offset > 1;
    }

    public static bool ShouldDeferPagingToHtml(bool htmlVisible, bool htmlFocused, bool canPageOuter) =>
        htmlVisible && htmlFocused && !canPageOuter;

    public static bool ShouldDeferPagingToAttachmentStrip(bool stripVisible, bool stripFocused) =>
        stripVisible && stripFocused;

    public static double PageScrollOffset(double offset, double extent, double viewport, bool down)
    {
        var max = Math.Max(0, extent - viewport);
        var clamped = Math.Clamp(offset, 0, max);
        if (viewport <= 0 || !CanPageScroll(clamped, extent, viewport, down))
        {
            return clamped;
        }

        var step = Math.Max(1, viewport * 0.9);
        return down
            ? Math.Min(max, clamped + step)
            : Math.Max(0, clamped - step);
    }

    public static int ComposeTabTarget(int fieldCount, int currentIndex, bool forward)
    {
        if (fieldCount <= 0 || currentIndex < 0 || currentIndex >= fieldCount)
        {
            return -1;
        }

        var next = currentIndex + (forward ? 1 : -1);
        return next < 0 || next >= fieldCount ? -1 : next;
    }

    public static ConversationCard? AdjacentConversationCard(
        IReadOnlyList<ConversationCard> cards,
        Guid? selectedMessageId,
        bool forward)
    {
        ArgumentNullException.ThrowIfNull(cards);
        if (cards.Count == 0)
        {
            return null;
        }

        var index = -1;
        if (selectedMessageId is { } id)
        {
            for (var i = 0; i < cards.Count; i++)
            {
                if (cards[i].Message.Id == id)
                {
                    index = i;
                    break;
                }
            }
        }

        if (index < 0)
        {
            return forward ? cards[0] : cards[^1];
        }

        var next = index + (forward ? 1 : -1);
        return next < 0 || next >= cards.Count ? null : cards[next];
    }

    public static ThreadRow? AdjacentThreadRow(
        IReadOnlyList<ThreadRow> rows,
        ThreadRow? current,
        bool forward)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var threads = rows.Where(row => row.ShowAsThread).ToList();
        if (threads.Count == 0)
        {
            return null;
        }

        var index = -1;
        if (current is not null)
        {
            for (var i = 0; i < threads.Count; i++)
            {
                if (threads[i].Id == current.Id)
                {
                    index = i;
                    break;
                }
            }
        }

        if (index < 0)
        {
            return forward ? threads[0] : threads[^1];
        }

        var next = index + (forward ? 1 : -1);
        return next < 0 || next >= threads.Count ? null : threads[next];
    }

    public static ThreadRow? PagedThreadRow(
        IReadOnlyList<ThreadRow> rows,
        ThreadRow? current,
        bool forward,
        int pageSize = ThreadListPageSize)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (pageSize <= 0)
        {
            return AdjacentThreadRow(rows, current, forward);
        }

        var threads = rows.Where(row => row.ShowAsThread).ToList();
        if (threads.Count == 0)
        {
            return null;
        }

        var index = -1;
        if (current is not null)
        {
            for (var i = 0; i < threads.Count; i++)
            {
                if (threads[i].Id == current.Id)
                {
                    index = i;
                    break;
                }
            }
        }

        if (index < 0)
        {
            return AdjacentThreadRow(rows, current: null, forward);
        }

        var next = index + (forward ? pageSize : -pageSize);
        next = Math.Clamp(next, 0, threads.Count - 1);
        return next == index ? null : threads[next];
    }

    public static ThreadRow? RowMatchingPrefix(
        IReadOnlyList<ThreadRow> rows,
        string prefix,
        ThreadRow? startAfter = null,
        ThreadListSort sort = ThreadListSort.Newest)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (string.IsNullOrEmpty(prefix) || rows.Count == 0)
        {
            return null;
        }

        var start = 0;
        if (startAfter is not null)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (ReferenceEquals(rows[i], startAfter)
                    || (startAfter.Id != Guid.Empty && rows[i].Id == startAfter.Id && !rows[i].IsGroupHeader))
                {
                    start = i + 1;
                    break;
                }
            }
        }

        for (var n = 0; n < rows.Count; n++)
        {
            var row = rows[(start + n) % rows.Count];
            if (row.ShowAsThread && RowPrefixHaystack(row, sort).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        return null;
    }

    private static string RowPrefixHaystack(ThreadRow row, ThreadListSort sort) =>
        sort == ThreadListSort.Subject ? row.Subject : row.Sender;

    public static TypeaheadAdvance AdvanceTypeahead(
        IReadOnlyList<ThreadRow> rows,
        string? previousPrefix,
        char ch,
        ThreadRow? current,
        ThreadListSort sort = ThreadListSort.Newest)
    {
        previousPrefix ??= string.Empty;
        var grown = previousPrefix + ch;
        var grownMatch = RowMatchingPrefix(rows, grown, sort: sort);
        if (grownMatch is not null)
        {
            return new TypeaheadAdvance(grown, grownMatch);
        }

        if (previousPrefix.Length == 1
            && char.ToLowerInvariant(previousPrefix[0]) == char.ToLowerInvariant(ch))
        {
            var cycled = RowMatchingPrefix(rows, previousPrefix, current, sort);
            if (cycled is not null)
            {
                return new TypeaheadAdvance(previousPrefix, cycled);
            }
        }

        var reset = ch.ToString();
        return new TypeaheadAdvance(reset, RowMatchingPrefix(rows, reset, sort: sort));
    }

    public static string NavPersistenceKey(ShellNavItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind switch
        {
            ShellNavKind.UnifiedInbox => "unified",
            ShellNavKind.Account when item.AccountId is { } accountId =>
                "account:" + accountId.ToString("D"),
            ShellNavKind.Mailbox when item.MailboxId is { } mailboxId =>
                "mailbox:" + mailboxId.ToString("D"),
            ShellNavKind.Outbox when item.AccountId is { } outboxAccountId =>
                "outbox:" + outboxAccountId.ToString("D"),
            ShellNavKind.Favorites => "favorites",
            _ => string.Empty,
        };
    }

    public static bool IsCurrentListRow(ThreadRow row, Guid? selectedThreadId, Guid? selectedMessageId)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Thread is { } thread)
        {
            return selectedThreadId == thread.Latest.Id
                || thread.Messages.Any(message =>
                    message.Id == selectedThreadId || message.Id == selectedMessageId);
        }

        return row.SearchHit is { } hit && hit.Id == selectedMessageId;
    }

    public static bool IsCurrentNav(
        ShellNavItem item,
        bool showingUnifiedInbox,
        bool showingOutbox,
        Guid? selectedAccountId,
        Guid? selectedMailboxId,
        Guid? accountInboxId)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind switch
        {
            ShellNavKind.Favorites => true,
            ShellNavKind.UnifiedInbox => showingUnifiedInbox,
            ShellNavKind.Outbox => showingOutbox && item.AccountId == selectedAccountId,
            ShellNavKind.Mailbox =>
                item.MailboxId is { } mailboxId && mailboxId == selectedMailboxId,
            ShellNavKind.Account =>
                item.AccountId == selectedAccountId
                && !showingUnifiedInbox
                && !showingOutbox
                && selectedMailboxId is { } current
                && accountInboxId == current,
            _ => false,
        };
    }

    public static ShellNavItem? FindSelectedNav(
        IReadOnlyList<ShellNavItem> items,
        bool showingUnifiedInbox,
        bool showingOutbox,
        Guid? selectedAccountId,
        Guid? selectedMailboxId,
        ShellNavItem? previous = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (previous is { IsFavoritePin: true, MailboxId: { } pinId }
            && !showingUnifiedInbox
            && !showingOutbox
            && pinId == selectedMailboxId)
        {
            var pin = FindFavoritePin(items, pinId);
            if (pin is not null)
            {
                return pin;
            }
        }

        return FindSelectedNavCore(
                items,
                showingUnifiedInbox,
                showingOutbox,
                selectedAccountId,
                selectedMailboxId)
            ?? items.FirstOrDefault(item => item.Kind != ShellNavKind.Favorites)
            ?? items.FirstOrDefault();
    }

    private static ShellNavItem? FindFavoritePin(IReadOnlyList<ShellNavItem> items, Guid mailboxId)
    {
        foreach (var item in FlattenNav(items))
        {
            if (item.IsFavoritePin && item.MailboxId == mailboxId)
            {
                return item;
            }
        }

        return null;
    }

    private static ShellNavItem? FindSelectedNavCore(
        IReadOnlyList<ShellNavItem> items,
        bool showingUnifiedInbox,
        bool showingOutbox,
        Guid? selectedAccountId,
        Guid? selectedMailboxId)
    {
        foreach (var item in items)
        {
            if (item.IsFavoritePin)
            {
                continue;
            }

            if (showingUnifiedInbox && item.Kind == ShellNavKind.UnifiedInbox)
            {
                return item;
            }

            if (showingOutbox
                && item.Kind == ShellNavKind.Outbox
                && item.AccountId == selectedAccountId)
            {
                return item;
            }

            if (item.Kind == ShellNavKind.Mailbox && item.MailboxId == selectedMailboxId)
            {
                return item;
            }

            if (item.Kind == ShellNavKind.Account
                && item.AccountId == selectedAccountId
                && selectedMailboxId is null
                && !showingOutbox)
            {
                return item;
            }

            var nested = FindSelectedNavCore(
                item.Children,
                showingUnifiedInbox,
                showingOutbox,
                selectedAccountId,
                selectedMailboxId);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    public static IReadOnlyList<ShellNavItem> FlattenNav(IReadOnlyList<ShellNavItem> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var items = new List<ShellNavItem>();
        AppendNav(roots, items);
        return items;
    }

    public static IReadOnlyList<ShellNavItem> NavPath(
        IReadOnlyList<ShellNavItem> roots,
        ShellNavItem target)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(target);
        var path = new List<ShellNavItem>();
        return TryNavPath(roots, target, path) ? path : Array.Empty<ShellNavItem>();
    }

    private static bool TryNavPath(
        IReadOnlyList<ShellNavItem> items,
        ShellNavItem target,
        List<ShellNavItem> path)
    {
        foreach (var item in items)
        {
            path.Add(item);
            if (ReferenceEquals(item, target))
            {
                return true;
            }

            if (item.Children.Count > 0 && TryNavPath(item.Children, target, path))
            {
                return true;
            }

            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    public static bool CanGoToNav(ShellNavItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return !item.IsFavoritePin
            && item.Kind is ShellNavKind.UnifiedInbox or ShellNavKind.Mailbox or ShellNavKind.Outbox;
    }

    public static ShellNavItem? AdjacentNavItem(
        IReadOnlyList<ShellNavItem> items,
        ShellNavItem? current,
        bool forward)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return null;
        }

        var index = -1;
        if (current is not null)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], current) || NavSame(items[i], current))
                {
                    index = i;
                    break;
                }
            }
        }

        if (index < 0)
        {
            return FirstGoToNav(items, forward);
        }

        var step = forward ? 1 : -1;
        for (var i = index + step; i >= 0 && i < items.Count; i += step)
        {
            if (CanGoToNav(items[i]))
            {
                return items[i];
            }
        }

        return null;
    }

    private static ShellNavItem? FirstGoToNav(IReadOnlyList<ShellNavItem> items, bool forward)
    {
        if (forward)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (CanGoToNav(items[i]))
                {
                    return items[i];
                }
            }
        }
        else
        {
            for (var i = items.Count - 1; i >= 0; i--)
            {
                if (CanGoToNav(items[i]))
                {
                    return items[i];
                }
            }
        }

        return null;
    }

    public static bool HasOwnUnread(ShellNavItem item, IReadOnlyDictionary<Guid, int> unreadByMailbox)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(unreadByMailbox);
        return !item.IsFavoritePin
            && item.Kind == ShellNavKind.Mailbox
            && item.MailboxId is { } mailboxId
            && unreadByMailbox.TryGetValue(mailboxId, out var unread)
            && unread > 0;
    }

    public static ShellNavItem? AdjacentUnreadMailbox(
        IReadOnlyList<ShellNavItem> items,
        ShellNavItem? current,
        Guid? currentMailboxId,
        IReadOnlyDictionary<Guid, int> unreadByMailbox,
        bool forward)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(unreadByMailbox);
        if (items.Count == 0)
        {
            return null;
        }

        var index = -1;
        if (current is not null)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], current) || NavSame(items[i], current))
                {
                    index = i;
                    break;
                }
            }
        }

        var step = forward ? 1 : -1;
        var start = index < 0
            ? (forward ? 0 : items.Count - 1)
            : index + step;
        for (var i = start; i >= 0 && i < items.Count; i += step)
        {
            var item = items[i];
            if (item.MailboxId is { } mailboxId && mailboxId == currentMailboxId)
            {
                continue;
            }

            if (HasOwnUnread(item, unreadByMailbox))
            {
                return item;
            }
        }

        return null;
    }

    private static void AppendNav(IReadOnlyList<ShellNavItem> source, List<ShellNavItem> target)
    {
        foreach (var item in source)
        {
            target.Add(item);
            if (item.Children.Count > 0)
            {
                AppendNav(item.Children, target);
            }
        }
    }

    public static ShellNavItem? NavMatchingPrefix(
        IReadOnlyList<ShellNavItem> items,
        string prefix,
        ShellNavItem? startAfter = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (string.IsNullOrEmpty(prefix) || items.Count == 0)
        {
            return null;
        }

        var start = 0;
        if (startAfter is not null)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], startAfter)
                    || NavSame(items[i], startAfter))
                {
                    start = i + 1;
                    break;
                }
            }
        }

        for (var n = 0; n < items.Count; n++)
        {
            var item = items[(start + n) % items.Count];
            if (item.Title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    public static NavTypeaheadAdvance AdvanceNavTypeahead(
        IReadOnlyList<ShellNavItem> items,
        string? previousPrefix,
        char ch,
        ShellNavItem? current)
    {
        previousPrefix ??= string.Empty;
        var grown = previousPrefix + ch;
        var grownMatch = NavMatchingPrefix(items, grown);
        if (grownMatch is not null)
        {
            return new NavTypeaheadAdvance(grown, grownMatch);
        }

        if (previousPrefix.Length == 1
            && char.ToLowerInvariant(previousPrefix[0]) == char.ToLowerInvariant(ch))
        {
            var cycled = NavMatchingPrefix(items, previousPrefix, current);
            if (cycled is not null)
            {
                return new NavTypeaheadAdvance(previousPrefix, cycled);
            }
        }

        var reset = ch.ToString();
        return new NavTypeaheadAdvance(reset, NavMatchingPrefix(items, reset));
    }

    public static IReadOnlyList<MailboxInfo> FilterMailboxes(
        IReadOnlyList<MailboxInfo> mailboxes,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(mailboxes);
        if (string.IsNullOrWhiteSpace(query))
        {
            return mailboxes;
        }

        return mailboxes
            .Where(mailbox =>
                mailbox.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || mailbox.Path.Contains(query, StringComparison.OrdinalIgnoreCase)
                || MailboxMoveLabel(mailbox).Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static string MailboxPickerLabel(MailboxInfo mailbox, string? accountName)
    {
        ArgumentNullException.ThrowIfNull(mailbox);
        var label = MailboxMoveLabel(mailbox);
        return string.IsNullOrWhiteSpace(accountName) ? label : accountName + " / " + label;
    }

    public static IReadOnlyList<(Guid Id, string Label)> RecentMoveTargets(
        IReadOnlyList<MailboxInfo> destinations,
        IReadOnlyList<Guid> recentIds,
        Guid? currentMailboxId,
        int max = 5)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(recentIds);
        if (max <= 0)
        {
            return [];
        }

        var byId = destinations.ToDictionary(item => item.Id);
        var items = new List<(Guid Id, string Label)>();
        foreach (var id in recentIds)
        {
            if (id == currentMailboxId || !byId.TryGetValue(id, out var mailbox))
            {
                continue;
            }

            items.Add((id, MailboxMoveLabel(mailbox)));
            if (items.Count >= max)
            {
                break;
            }
        }

        return items;
    }

    public static IReadOnlyList<(Guid Id, string Label)> FilterPickerItems(
        IReadOnlyList<(Guid Id, string Label)> items,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (string.IsNullOrWhiteSpace(query))
        {
            return items;
        }

        return items
            .Where(item => item.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static bool NavSame(ShellNavItem left, ShellNavItem right) =>
        left.Kind == right.Kind
        && left.AccountId == right.AccountId
        && left.MailboxId == right.MailboxId;

    public static string Sender(string fromAddress)
    {
        var trimmed = fromAddress.Trim();
        var angles = trimmed.IndexOf('<');
        if (angles >= 0)
        {
            var name = trimmed[..angles].Trim().Trim('"');
            if (name.Length > 0)
            {
                return name;
            }

            var close = trimmed.IndexOf('>');
            if (close > angles + 1)
            {
                return trimmed[(angles + 1)..close].Trim();
            }
        }

        return trimmed;
    }

    public static string ListPrimary(
        string fromAddress,
        IReadOnlyList<string>? toAddresses,
        MailboxRole? role)
    {
        if (role is MailboxRole.Sent or MailboxRole.Drafts)
        {
            var names = new List<string>();
            foreach (var address in toAddresses ?? Array.Empty<string>())
            {
                var name = Sender(address);
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }

            return names.Count == 0 ? "(no recipient)" : string.Join(", ", names);
        }

        return Sender(fromAddress);
    }

    public static string Subject(string? subject) =>
        string.IsNullOrWhiteSpace(subject) ? "(no subject)" : subject;

    public static string ListRightLabel(ThreadListSort sort, string dateLabel, long sizeBytes) =>
        sort == ThreadListSort.Size && sizeBytes > 0 ? SizeLabel(sizeBytes) : dateLabel;

    public static string SizeLabel(long bytes)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        if (bytes < 1024L * 1024)
        {
            return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
        }

        return (bytes / (1024.0 * 1024 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " GB";
    }

    public static string AsQuote(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text.ReplaceLineEndings("\n").TrimEnd().Split('\n');
        return string.Join(
            Environment.NewLine,
            lines.Select(line => line.Length == 0 ? ">" : "> " + line));
    }

    public static string? ThreadSenderAddress(ThreadRow? row) =>
        row?.Thread?.Latest.FromAddress
        ?? row?.SearchHit?.FromAddress
        ?? row?.FromAddress;

    public static string FromSearchQuery(string? fromAddress)
    {
        if (MailAddresses.TryGetMailbox(fromAddress, out var address, out _))
        {
            return "from:" + address;
        }

        var trimmed = (fromAddress ?? string.Empty).Trim();
        return trimmed.Length == 0 ? string.Empty : "from:" + trimmed;
    }

    public static string ToSearchQuery(string? toAddress)
    {
        if (MailAddresses.TryGetMailbox(toAddress, out var address, out _))
        {
            return "to:" + address;
        }

        var trimmed = (toAddress ?? string.Empty).Trim();
        return trimmed.Length == 0 ? string.Empty : "to:" + trimmed;
    }

    public static string ToSearchQuery(IReadOnlyList<string>? toAddresses)
    {
        if (toAddresses is null || toAddresses.Count == 0)
        {
            return string.Empty;
        }

        return ToSearchQuery(toAddresses[0]);
    }

    public static string FormatMessageId(string? raw)
    {
        var id = MessageSearch.NormalizeMessageId(raw);
        return id.Length == 0 ? string.Empty : "<" + id + ">";
    }

    public static string DateTip(DateTimeOffset sortDate, string dateLabel, long sizeBytes)
    {
        var tip = sortDate == default ? dateLabel : sortDate.ToLocalTime().ToString("f");
        return sizeBytes > 0 ? tip + " · " + SizeLabel(sizeBytes) : tip;
    }

    public static string Headers(MessageInfo message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var lines = new List<string>
        {
            "From: " + message.FromAddress,
            "To: " + string.Join(", ", message.ToAddresses),
        };
        if (message.CcAddresses.Count > 0)
        {
            lines.Add("Cc: " + string.Join(", ", message.CcAddresses));
        }

        if (message.BccAddresses.Count > 0)
        {
            lines.Add("Bcc: " + string.Join(", ", message.BccAddresses));
        }

        if (message.ReplyToAddresses.Count > 0)
        {
            lines.Add("Reply-To: " + string.Join(", ", message.ReplyToAddresses));
        }

        lines.Add("Subject: " + Subject(message.Subject));
        lines.Add("Date: " + message.ReceivedAt.ToLocalTime().ToString("f"));
        var messageId = FormatMessageId(message.InternetMessageId);
        if (messageId.Length > 0)
        {
            lines.Add("Message-ID: " + messageId);
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string WithCount(string title, int count)
    {
        ArgumentNullException.ThrowIfNull(title);
        return count > 0 ? title + " (" + count + ")" : title;
    }

    public static string? ChipFilterTitle(string? query)
    {
        if (!MessageSearch.IsChipFilterOnly(query))
        {
            return null;
        }

        return ChipPartsTitle(MessageSearch.Parse(query!));
    }

    private static string? ChipPartsTitle(MessageSearch.Parsed parsed)
    {
        var parts = new List<string>();
        if (parsed.FailedOnly)
        {
            parts.Add("Failed");
        }

        if (parsed.UnreadOnly)
        {
            parts.Add("Unread");
        }
        else if (parsed.ReadOnly)
        {
            parts.Add("Read");
        }

        if (parsed.FlaggedOnly)
        {
            parts.Add("Flagged");
        }
        else if (parsed.NotFlagged)
        {
            parts.Add("Unflagged");
        }

        if (parsed.AttachmentOnly)
        {
            parts.Add("Files");
        }
        else if (parsed.WithoutAttachment)
        {
            parts.Add("No files");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    public static string ChipFilterEmptyCopy(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return title switch
        {
            "Unread" => "No unread messages",
            "Flagged" => "No flagged messages",
            "Files" => "No messages with files",
            "Failed" => "No failed messages",
            "Read" => "No read messages",
            _ => "No " + title.ToLowerInvariant() + " messages",
        };
    }

    public static string EmptyListCopy(
        bool showingOutbox,
        bool showingUnifiedInbox,
        string? query,
        MailboxRole? role = null)
    {
        if (showingOutbox)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return "Outbox is empty";
            }

            var outboxParsed = MessageSearch.Parse(query);
            if (outboxParsed.FailedOnly
                && string.IsNullOrEmpty(outboxParsed.Text)
                && string.IsNullOrEmpty(outboxParsed.ToContains)
                && string.IsNullOrEmpty(outboxParsed.SubjectContains))
            {
                return "No failed messages";
            }

            return "No matching messages";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            if (showingUnifiedInbox)
            {
                return "No messages in Unified Inbox";
            }

            return RoleEmptyCopy(role);
        }

        if (ChipFilterTitle(query) is { } chipTitle)
        {
            return ChipFilterEmptyCopy(chipTitle);
        }

        var parsed = MessageSearch.Parse(query);
        var chipParts = ChipPartsTitle(parsed);
        if (IsRoleOrChipScopeOnly(parsed))
        {
            if (chipParts is { } scopedChips)
            {
                return ChipFilterEmptyCopy(scopedChips);
            }

            if (parsed.InRole is { } inRole)
            {
                return RoleEmptyCopy(inRole);
            }
        }

        if (IsDateScopeOnly(parsed))
        {
            return "No messages in that date range";
        }

        if (IsFilenameScopeOnly(parsed))
        {
            return "No messages with that file";
        }

        var onlySize = IsSizeOrMeScope(parsed);
        if (onlySize && parsed.LargerThan is not null && parsed.SmallerThan is null)
        {
            return "No messages that large";
        }

        if (onlySize && parsed.SmallerThan is not null && parsed.LargerThan is null)
        {
            return "No messages that small";
        }

        if (onlySize && parsed.FromMe && !parsed.ToMe)
        {
            return "No messages from you";
        }

        if (onlySize && parsed.ToMe && !parsed.FromMe)
        {
            return "No messages to you";
        }

        return "No matching messages";
    }

    public static string RoleEmptyCopy(MailboxRole? role) =>
        role switch
        {
            MailboxRole.Sent => "No sent messages",
            MailboxRole.Drafts => "No drafts",
            MailboxRole.Trash => "No messages in Trash",
            MailboxRole.Junk => "No junk messages",
            MailboxRole.Archive => "No archived messages",
            _ => "No messages",
        };

    private static bool IsRoleOrChipScopeOnly(MessageSearch.Parsed parsed) =>
        HasNoFreeTextOrPeople(parsed)
        && parsed.After is null
        && parsed.Before is null
        && parsed.FilenameContains is null
        && parsed.LargerThan is null
        && parsed.SmallerThan is null
        && !parsed.FromMe
        && !parsed.ToMe
        && (parsed.InRole is not null || ChipPartsTitle(parsed) is not null);

    private static bool IsDateScopeOnly(MessageSearch.Parsed parsed) =>
        HasNoFreeTextOrPeople(parsed)
        && ChipPartsTitle(parsed) is null
        && parsed.InRole is null
        && parsed.FilenameContains is null
        && parsed.LargerThan is null
        && parsed.SmallerThan is null
        && !parsed.FromMe
        && !parsed.ToMe
        && (parsed.After is not null || parsed.Before is not null);

    private static bool IsFilenameScopeOnly(MessageSearch.Parsed parsed) =>
        HasNoFreeTextOrPeople(parsed)
        && ChipPartsTitle(parsed) is null
        && parsed.InRole is null
        && parsed.After is null
        && parsed.Before is null
        && parsed.LargerThan is null
        && parsed.SmallerThan is null
        && !parsed.FromMe
        && !parsed.ToMe
        && parsed.FilenameContains is not null;

    private static bool IsSizeOrMeScope(MessageSearch.Parsed parsed) =>
        HasNoFreeTextOrPeople(parsed)
        && ChipPartsTitle(parsed) is null
        && parsed.After is null
        && parsed.Before is null
        && parsed.FilenameContains is null;

    private static bool HasNoFreeTextOrPeople(MessageSearch.Parsed parsed) =>
        string.IsNullOrEmpty(parsed.Text)
        && parsed.FromContains is null
        && parsed.ToContains is null
        && parsed.CcContains is null
        && parsed.BccContains is null
        && parsed.SubjectContains is null
        && parsed.FromExclude is null
        && parsed.ToExclude is null
        && parsed.CcExclude is null
        && parsed.BccExclude is null
        && parsed.SubjectExclude is null
        && parsed.FilenameExclude is null
        && parsed.TextExcludes is not { Count: > 0 }
        && parsed.MessageIdContains is null
        && !parsed.InAnywhere
        && parsed.InMailboxContains is null;

    public static string WindowTitle(string? listHeader)
    {
        if (string.IsNullOrWhiteSpace(listHeader))
        {
            return "Mailtide";
        }

        var title = listHeader.Trim();
        var unread = 0;
        var paren = title.LastIndexOf(" (", StringComparison.Ordinal);
        if (paren > 0 && title.EndsWith(")", StringComparison.Ordinal))
        {
            var inner = title[(paren + 2)..^1];
            if (int.TryParse(inner, out var n) && n > 0)
            {
                unread = n;
            }

            title = title[..paren];
        }

        return unread > 0
            ? "(" + unread + ") " + title + " — Mailtide"
            : title + " — Mailtide";
    }

    public static string ComposeWindowTitle(string? subject)
    {
        var label = string.IsNullOrWhiteSpace(subject) ? "Draft" : subject.Trim();
        return label + " — Mailtide";
    }

    public static string DateLabel(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        var day = local.Date;
        if (day == today || day == today.AddDays(-1))
        {
            return local.ToString("t");
        }

        if (day > today.AddDays(-7))
        {
            return local.ToString("ddd");
        }

        if (local.Year == DateTimeOffset.Now.Year)
        {
            return local.ToString("MMM d");
        }

        return local.ToString("yyyy-MM-dd");
    }

    public static string DateGroupTitle(DateTimeOffset value)
    {
        var local = value.ToLocalTime().Date;
        var today = DateTimeOffset.Now.Date;
        if (local == today)
        {
            return "Today";
        }

        if (local == today.AddDays(-1))
        {
            return "Yesterday";
        }

        if (local > today.AddDays(-7))
        {
            return "This week";
        }

        if (local.Year == today.Year)
        {
            return value.ToLocalTime().ToString("MMMM");
        }

        return local.ToString("yyyy");
    }

    public static IReadOnlyList<ThreadRow> WithDateGroups(
        IReadOnlyList<ThreadRow> rows,
        bool newestFirst = true)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var ordered = newestFirst
            ? rows.OrderByDescending(row => row.SortDate).ThenBy(row => row.Id).ToList()
            : rows.OrderBy(row => row.SortDate).ThenBy(row => row.Id).ToList();
        var result = new List<ThreadRow>();
        string? previous = null;
        foreach (var row in ordered)
        {
            var title = DateGroupTitle(row.SortDate);
            if (title != previous)
            {
                result.Add(new ThreadRow
                {
                    Id = Guid.Empty,
                    Sender = string.Empty,
                    Subject = title,
                    Preview = string.Empty,
                    DateLabel = string.Empty,
                    SortDate = row.SortDate,
                    IsGroupHeader = true,
                    GroupTitle = title,
                });
                previous = title;
            }

            result.Add(row);
        }

        return result;
    }

    public static IReadOnlyList<ThreadRow> SortThreadRows(
        IReadOnlyList<ThreadRow> rows,
        ThreadListSort sort)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (sort is ThreadListSort.Newest or ThreadListSort.Oldest)
        {
            return WithDateGroups(rows, newestFirst: sort != ThreadListSort.Oldest);
        }

        var threads = rows.Where(row => !row.IsGroupHeader);
        if (sort == ThreadListSort.From)
        {
            return threads
                .OrderBy(row => row.Sender, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(row => row.SortDate)
                .ThenBy(row => row.Id)
                .ToList();
        }

        if (sort == ThreadListSort.Size)
        {
            return threads
                .OrderByDescending(row => row.SizeBytes)
                .ThenByDescending(row => row.SortDate)
                .ThenBy(row => row.Id)
                .ToList();
        }

        return threads
            .OrderBy(row => row.Subject, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(row => row.SortDate)
            .ThenBy(row => row.Id)
            .ToList();
    }

    public static IReadOnlyList<ThreadListSort> ListSortChoices { get; } =
    [
        ThreadListSort.Newest,
        ThreadListSort.Oldest,
        ThreadListSort.From,
        ThreadListSort.Subject,
        ThreadListSort.Size,
    ];

    public static string ListSortLabel(ThreadListSort sort) =>
        sort switch
        {
            ThreadListSort.Oldest => "Oldest",
            ThreadListSort.From => "From",
            ThreadListSort.Subject => "Subject",
            ThreadListSort.Size => "Size",
            _ => "Newest",
        };

    public static ThreadListSort? ParseListSortLabel(string? header) =>
        header switch
        {
            "Newest" => ThreadListSort.Newest,
            "Oldest" => ThreadListSort.Oldest,
            "From" => ThreadListSort.From,
            "Subject" => ThreadListSort.Subject,
            "Size" => ThreadListSort.Size,
            _ => null,
        };

    public static bool CanSelectThreadRow(ThreadRow? row) =>
        row is { IsGroupHeader: false };

    public static bool ThreadWasExpanded(
        IEnumerable<Guid> threadMessageIds,
        IReadOnlyCollection<Guid> expandedIds)
    {
        ArgumentNullException.ThrowIfNull(threadMessageIds);
        ArgumentNullException.ThrowIfNull(expandedIds);
        var set = expandedIds as HashSet<Guid> ?? expandedIds.ToHashSet();
        return threadMessageIds.Any(set.Contains);
    }

    public static ThreadListSort NextListSort(ThreadListSort sort) =>
        sort switch
        {
            ThreadListSort.Newest => ThreadListSort.Oldest,
            ThreadListSort.Oldest => ThreadListSort.From,
            ThreadListSort.From => ThreadListSort.Subject,
            ThreadListSort.Subject => ThreadListSort.Size,
            _ => ThreadListSort.Newest,
        };

    public static ThreadListSort ParseListSort(string? raw) =>
        raw switch
        {
            "0" => ThreadListSort.Oldest,
            "from" => ThreadListSort.From,
            "subject" => ThreadListSort.Subject,
            "size" => ThreadListSort.Size,
            _ => ThreadListSort.Newest,
        };

    public static string EncodeListSort(ThreadListSort sort) =>
        sort switch
        {
            ThreadListSort.Oldest => "0",
            ThreadListSort.From => "from",
            ThreadListSort.Subject => "subject",
            ThreadListSort.Size => "size",
            _ => "1",
        };

    public static ComposePasteKind ClassifyComposePaste(
        bool headerField,
        bool hasFiles,
        bool hasText,
        bool hasBitmap)
    {
        if (headerField)
        {
            return ComposePasteKind.Text;
        }

        if (hasFiles)
        {
            return ComposePasteKind.AttachFiles;
        }

        if (hasText)
        {
            return ComposePasteKind.Text;
        }

        return hasBitmap ? ComposePasteKind.AttachImage : ComposePasteKind.Text;
    }

    public static ThreadRow? RowAfterCrossingHeader(
        IReadOnlyList<ThreadRow> rows,
        ThreadRow header,
        ThreadRow? previous)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(header);
        if (!header.IsGroupHeader)
        {
            return previous;
        }

        var index = -1;
        var previousIndex = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (ReferenceEquals(rows[i], header))
            {
                index = i;
            }

            if (previous is not null && ReferenceEquals(rows[i], previous))
            {
                previousIndex = i;
            }
        }

        if (index < 0)
        {
            return previous;
        }

        if (index == 0)
        {
            return FirstNonHeader(rows, 0, 1) ?? previous;
        }

        if (index == rows.Count - 1)
        {
            return FirstNonHeader(rows, rows.Count - 1, -1) ?? previous;
        }

        if (previousIndex >= 0 && index > previousIndex)
        {
            return FirstNonHeader(rows, index + 1, 1) ?? previous;
        }

        if (previousIndex >= 0 && index < previousIndex)
        {
            return FirstNonHeader(rows, index - 1, -1) ?? previous;
        }

        return previous;
    }

    public static bool NeedsEmptySubjectConfirm(string? subject) =>
        string.IsNullOrWhiteSpace(subject);

    public static bool NeedsRecipient(string? to, string? cc = null, string? bcc = null) =>
        MailAddresses.Parse(to).Count == 0
        && MailAddresses.Parse(cc).Count == 0
        && MailAddresses.Parse(bcc).Count == 0;

    public static IReadOnlyList<string> InvalidRecipients(string? to, string? cc = null, string? bcc = null) =>
        MailAddresses.Invalid(
            [.. MailAddresses.Parse(to), .. MailAddresses.Parse(cc), .. MailAddresses.Parse(bcc)]);

    public static string RecipientError(string? to, string? cc = null, string? bcc = null)
    {
        if (NeedsRecipient(to, cc, bcc))
        {
            return "Add at least one recipient before sending.";
        }

        var invalid = InvalidRecipients(to, cc, bcc);
        if (invalid.Count == 0)
        {
            return string.Empty;
        }

        return invalid.Count == 1
            ? "This address looks invalid: " + invalid[0]
            : "These addresses look invalid: " + string.Join(", ", invalid);
    }

    public static bool NeedsEmptyBodyConfirm(
        string? body,
        string? signature,
        int attachmentCount,
        string? html = null) =>
        attachmentCount <= 0
        && string.IsNullOrWhiteSpace(html)
        && string.IsNullOrWhiteSpace(MailSignature.Without(body, signature));

    public static bool ShouldUnpinFavorite(
        bool isFavorite,
        bool ontoFavoritesHeader,
        Guid? beforeMailboxId) =>
        isFavorite && !ontoFavoritesHeader && beforeMailboxId is null;

    public static int? FavoriteDestinationIndex(
        IReadOnlyList<Guid> ids,
        Guid movingId,
        bool ontoFavoritesHeader,
        Guid? beforeMailboxId)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var current = IndexOfId(ids, movingId);
        if (current < 0)
        {
            if (ontoFavoritesHeader)
            {
                return 0;
            }

            if (beforeMailboxId is null)
            {
                return null;
            }

            var pinAt = IndexOfId(ids, beforeMailboxId.Value);
            return pinAt < 0 ? null : pinAt;
        }

        if (ontoFavoritesHeader)
        {
            return current == 0 ? null : 0;
        }

        if (beforeMailboxId is null || beforeMailboxId == movingId)
        {
            return null;
        }

        var before = IndexOfId(ids, beforeMailboxId.Value);
        if (before < 0)
        {
            return null;
        }

        var dest = current < before ? before - 1 : before;
        return dest == current ? null : dest;
    }

    private static int IndexOfId(IReadOnlyList<Guid> ids, Guid id)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id)
            {
                return i;
            }
        }

        return -1;
    }

    public static ThreadNavDropKind ThreadNavDrop(ThreadRow row, ShellNavItem target)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(target);
        if (!row.ShowAsThread || row.OutboxItem is not null)
        {
            return ThreadNavDropKind.None;
        }

        if (target.Kind != ShellNavKind.Mailbox
            || target.MailboxId is null
            || target.AccountId is null)
        {
            return ThreadNavDropKind.None;
        }

        var accountId = row.Thread?.Latest.AccountId ?? row.SearchHit?.AccountId;
        var mailboxId = row.Thread?.Latest.MailboxId ?? row.SearchHit?.MailboxId;
        if (accountId != target.AccountId || mailboxId == target.MailboxId)
        {
            return ThreadNavDropKind.None;
        }

        return target.Role switch
        {
            MailboxRole.Trash => ThreadNavDropKind.Trash,
            MailboxRole.Junk => ThreadNavDropKind.Junk,
            _ => ThreadNavDropKind.Move,
        };
    }

    public static bool ThreadNavDropCopies(ThreadNavDropKind kind, bool copyModifier) =>
        copyModifier && kind == ThreadNavDropKind.Move;

    private static ThreadRow? FirstNonHeader(IReadOnlyList<ThreadRow> rows, int start, int step)
    {
        for (var i = start; i >= 0 && i < rows.Count; i += step)
        {
            if (!rows[i].IsGroupHeader)
            {
                return rows[i];
            }
        }

        return null;
    }

    public static string Recipients(string prefix, IReadOnlyList<string> addresses) =>
        addresses.Count == 0 ? string.Empty : prefix + string.Join(", ", addresses);

    public static bool OutboxItemMatches(OutboxItemInfo item, string? query)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var clauses = MessageSearch.OrClauses(query);
        if (clauses.Count > 1)
        {
            return clauses.Any(clause => OutboxItemMatches(item, clause));
        }

        var parsed = MessageSearch.Parse(query);
        if (parsed.FailedOnly && item.State != OutboxItemState.Failed)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.ToContains)
            && !ContainsAny(item.ToAddresses, parsed.ToContains))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.SubjectContains)
            && !ContainsIgnoreCase(item.Subject, parsed.SubjectContains))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.ToExclude)
            && ContainsAny(item.ToAddresses, parsed.ToExclude))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.SubjectExclude)
            && ContainsIgnoreCase(item.Subject, parsed.SubjectExclude))
        {
            return false;
        }

        if (parsed.TextExcludes is { Count: > 0 } excludes)
        {
            foreach (var exclude in excludes)
            {
                if (ContainsIgnoreCase(item.Subject, exclude)
                    || ContainsAny(item.ToAddresses, exclude)
                    || ContainsIgnoreCase(item.ErrorMessage, exclude)
                    || ContainsIgnoreCase(item.State.ToString(), exclude))
                {
                    return false;
                }
            }
        }

        if (string.IsNullOrEmpty(parsed.Text))
        {
            return true;
        }

        return ContainsIgnoreCase(item.Subject, parsed.Text)
            || ContainsIgnoreCase(item.State.ToString(), parsed.Text)
            || ContainsIgnoreCase(item.ErrorMessage, parsed.Text)
            || ContainsAny(item.ToAddresses, parsed.Text);
    }

    private static bool ContainsIgnoreCase(string? value, string needle) =>
        !string.IsNullOrEmpty(value)
        && value.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(IEnumerable<string> values, string needle)
    {
        foreach (var value in values)
        {
            if (ContainsIgnoreCase(value, needle))
            {
                return true;
            }
        }

        return false;
    }

    public static char MailboxPathDelimiter(IEnumerable<MailboxInfo> mailboxes)
    {
        ArgumentNullException.ThrowIfNull(mailboxes);
        var paths = mailboxes.Select(mailbox => mailbox.Path ?? string.Empty).ToList();
        if (paths.Any(path => path.Contains('/')))
        {
            return '/';
        }

        if (paths.Any(path => path.Contains('.')))
        {
            return '.';
        }

        return '/';
    }

    public static string NormalizeMailboxPath(string? path, char delimiter = '/')
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = delimiter == '/' ? path.Replace("\\", "/") : path;
        return normalized.Trim().TrimEnd(delimiter);
    }

    public static string? MailboxParentPath(string? path, char delimiter = '/')
    {
        var normalized = NormalizeMailboxPath(path, delimiter);
        var split = normalized.LastIndexOf(delimiter);
        return split > 0 ? normalized[..split] : null;
    }

    public static string MailboxNavTitle(MailboxInfo mailbox, char delimiter = '/')
    {
        ArgumentNullException.ThrowIfNull(mailbox);
        var normalized = NormalizeMailboxPath(mailbox.Path, delimiter);
        var split = normalized.LastIndexOf(delimiter);
        if (split >= 0 && split < normalized.Length - 1)
        {
            return normalized[(split + 1)..];
        }

        return mailbox.Name;
    }

    public static string MailboxMoveLabel(MailboxInfo mailbox)
    {
        ArgumentNullException.ThrowIfNull(mailbox);
        var delimiter = MailboxPathDelimiter([mailbox]);
        var normalized = NormalizeMailboxPath(mailbox.Path, delimiter);
        if (normalized.IndexOf(delimiter) > 0)
        {
            return normalized.Replace(delimiter.ToString(), " / ");
        }

        return mailbox.Name;
    }

    public static IReadOnlyList<ShellNavItem> MailboxNavTree(IEnumerable<MailboxInfo> mailboxes)
    {
        ArgumentNullException.ThrowIfNull(mailboxes);
        var list = mailboxes.ToList();
        var delimiter = MailboxPathDelimiter(list);
        var byPath = new Dictionary<string, MailboxInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var mailbox in list)
        {
            var key = NormalizeMailboxPath(mailbox.Path, delimiter);
            if (key.Length > 0)
            {
                byPath.TryAdd(key, mailbox);
            }
        }

        var children = list.ToDictionary(mailbox => mailbox.Id, _ => new List<MailboxInfo>());
        var roots = new List<MailboxInfo>();
        foreach (var mailbox in list)
        {
            var parentPath = MailboxParentPath(mailbox.Path, delimiter);
            if (parentPath is not null
                && byPath.TryGetValue(parentPath, out var parent)
                && parent.Id != mailbox.Id)
            {
                children[parent.Id].Add(mailbox);
            }
            else
            {
                roots.Add(mailbox);
            }
        }

        int RolledUnread(MailboxInfo mailbox) =>
            mailbox.UnreadCount + children[mailbox.Id].Sum(RolledUnread);

        ShellNavItem ToItem(MailboxInfo mailbox) => new()
        {
            Kind = ShellNavKind.Mailbox,
            Title = MailboxNavTitle(mailbox, delimiter),
            AccountId = mailbox.AccountId,
            MailboxId = mailbox.Id,
            Role = mailbox.Role,
            UnreadCount = RolledUnread(mailbox),
            Children = children[mailbox.Id].Select(ToItem).ToList(),
        };

        return roots.Select(ToItem).ToList();
    }

    public static string? ConversationPositionLabel(IReadOnlyList<ConversationCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        if (cards.Count <= 1)
        {
            return null;
        }

        for (var i = 0; i < cards.Count; i++)
        {
            if (cards[i].IsSelected)
            {
                return (i + 1) + " of " + cards.Count;
            }
        }

        return null;
    }

    public static MessageInfo? ConversationSubjectMessage(IReadOnlyList<ConversationCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        return cards
            .Select(card => card.Message)
            .OrderByDescending(message => message.ReceivedAt)
            .ThenByDescending(message => message.Id)
            .FirstOrDefault();
    }

    public static IReadOnlyList<MessageInfo> OrderConversationMessages(
        IEnumerable<MessageInfo> messages,
        bool newestFirst)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return newestFirst
            ? messages.OrderByDescending(item => item.ReceivedAt).ThenByDescending(item => item.Id).ToList()
            : messages.OrderBy(item => item.ReceivedAt).ThenBy(item => item.Id).ToList();
    }

    public static ConversationSplit SplitConversation(IReadOnlyList<ConversationCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var selectedIndex = -1;
        for (var i = 0; i < cards.Count; i++)
        {
            if (cards[i].IsSelected)
            {
                selectedIndex = i;
                break;
            }
        }

        if (selectedIndex < 0)
        {
            return new ConversationSplit(cards, null, []);
        }

        return new ConversationSplit(
            cards.Take(selectedIndex).ToList(),
            cards[selectedIndex],
            cards.Skip(selectedIndex + 1).ToList());
    }

    public static QuotedSplit SplitQuoted(string? body) =>
        MessageQuote.Split(body);

    public static string ConversationSnippet(string? bodyText, string? needle = null) =>
        MessagePreview.Snippet(bodyText, ConversationSnippetLength, needle);

    public static string UniqueFileName(string? fileName, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        var raw = string.IsNullOrWhiteSpace(fileName) ? "attachment" : fileName.Trim();
        var name = Path.GetFileName(raw.Replace('\\', '/'));
        if (name.Length == 0 || name is "." or "..")
        {
            name = "attachment";
        }

        if (!exists(name))
        {
            return name;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        if (string.IsNullOrEmpty(stem))
        {
            stem = "attachment";
        }

        for (var i = 1; i < 1000; i++)
        {
            var candidate = stem + " (" + i + ")" + ext;
            if (!exists(candidate))
            {
                return candidate;
            }
        }

        return stem + "-" + Guid.NewGuid().ToString("N") + ext;
    }

    public static string? SearchHighlightNeedle(string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || MessageSearch.IsChipFilterOnly(query))
        {
            return null;
        }

        var text = MessageSearch.Parse(query).Text;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public static IReadOnlyList<ConversationFindHit> FindInConversation(
        IReadOnlyList<ConversationCard> cards,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(cards);
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var needle = query.Trim();
        var hits = new List<ConversationFindHit>();
        for (var i = 0; i < cards.Count; i++)
        {
            AddFindHits(hits, i, cards[i].Sender, needle, inBody: false);
            AddFindHits(hits, i, cards[i].Message.Subject, needle, inBody: false);
            AddFindHits(hits, i, cards[i].BodyText, needle, inBody: true);
            var htmlText = HtmlText.Strip(cards[i].BodyHtml);
            if (!string.IsNullOrEmpty(htmlText)
                && !string.Equals(htmlText, cards[i].BodyText, StringComparison.Ordinal))
            {
                AddFindHits(hits, i, htmlText, needle, inBody: true);
            }
        }

        return hits;
    }

    public static IReadOnlyList<string> PushRecentSearch(IReadOnlyList<string> existing, string? query)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0 || MessageSearch.IsChipFilterOnly(q))
        {
            return existing;
        }

        var next = existing
            .Where(item => !item.Equals(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
        next.Insert(0, q);
        if (next.Count > MaxRecentSearches)
        {
            next.RemoveRange(MaxRecentSearches, next.Count - MaxRecentSearches);
        }

        return next;
    }

    public static IReadOnlyList<string> RemoveRecentSearch(IReadOnlyList<string> existing, string? query)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0)
        {
            return existing;
        }

        return existing
            .Where(item => !item.Equals(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static IReadOnlyList<string> MatchingRecentSearches(IReadOnlyList<string> recent, string? typed)
    {
        ArgumentNullException.ThrowIfNull(recent);
        if (string.IsNullOrWhiteSpace(typed))
        {
            return recent;
        }

        var needle = typed.Trim();
        return recent
            .Where(item => item.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static string EncodeRecentSearches(IEnumerable<string> items) =>
        string.Join("\n", items.Select(item => item.Replace("\r", " ").Replace("\n", " ").Trim()).Where(item => item.Length > 0));

    public static List<string> DecodeRecentSearches(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return [];
        }

        return raw.Split("\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .Take(MaxRecentSearches)
            .ToList();
    }

    public static int CurrentBodyFindOccurrence(IReadOnlyList<ConversationFindHit> hits, int index)
    {
        ArgumentNullException.ThrowIfNull(hits);
        if (index < 0 || index >= hits.Count || !hits[index].InBody)
        {
            return -1;
        }

        var card = hits[index].CardIndex;
        var occurrence = 0;
        for (var i = 0; i < index; i++)
        {
            if (hits[i].CardIndex == card && hits[i].InBody)
            {
                occurrence++;
            }
        }

        return occurrence;
    }

    public static int NextFindIndex(int count, int current, bool forward)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (current < 0 || current >= count)
        {
            return forward ? 0 : count - 1;
        }

        return forward ? (current + 1) % count : (current - 1 + count) % count;
    }

    private static void AddFindHits(
        List<ConversationFindHit> hits,
        int cardIndex,
        string? hay,
        string needle,
        bool inBody)
    {
        if (string.IsNullOrEmpty(hay))
        {
            return;
        }

        var start = 0;
        while (start < hay.Length)
        {
            var found = hay.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return;
            }

            hits.Add(new ConversationFindHit(cardIndex, found, needle.Length, inBody));
            start = found + Math.Max(needle.Length, 1);
        }
    }

    public static bool? ObjectMenuOutboxOverride(string? header, bool showingOutbox)
    {
        if (header is "Retry" or "Discard")
        {
            return showingOutbox;
        }

        if (header is "Reply" or "Reply All" or "Forward" or "Forward as Attachment" or "Edit as New" or "Save as .eml"
            or "Copy subject" or "Copy address" or "Copy headers" or "Copy Message-ID" or "Show original" or "Search from sender" or "Search to recipient" or "Move" or "Copy" or "Archive"
            or "Find in conversation" or "Delete")
        {
            return !showingOutbox;
        }

        return null;
    }

    public static bool? ObjectMenuConversationOverride(string? header, bool canExpand, bool expanded)
    {
        if (header is "Expand all")
        {
            return canExpand && !expanded;
        }

        if (header is "Collapse all")
        {
            return canExpand && expanded;
        }

        return null;
    }

    public static NavFocusedDeleteAction NavFocusedDelete(Guid? mailboxId, MailboxRole? role)
    {
        if (mailboxId is null)
        {
            return NavFocusedDeleteAction.None;
        }

        return role switch
        {
            MailboxRole.Trash => NavFocusedDeleteAction.EmptyTrash,
            MailboxRole.Junk => NavFocusedDeleteAction.EmptyJunk,
            MailboxRole.Inbox or MailboxRole.Sent or MailboxRole.Drafts or MailboxRole.Archive => NavFocusedDeleteAction.None,
            _ => NavFocusedDeleteAction.DeleteMailbox,
        };
    }
}

public enum NavFocusedDeleteAction
{
    None = 0,
    EmptyTrash = 1,
    EmptyJunk = 2,
    DeleteMailbox = 3,
}

public sealed record ConversationSplit(
    IReadOnlyList<ConversationCard> Before,
    ConversationCard? Selected,
    IReadOnlyList<ConversationCard> After);

public enum ThreadListSort
{
    Newest = 0,
    Oldest = 1,
    From = 2,
    Subject = 3,
    Size = 4,
}

public enum ComposePasteKind
{
    Text = 0,
    AttachFiles = 1,
    AttachImage = 2,
}

public enum ThreadNavDropKind
{
    None = 0,
    Move = 1,
    Trash = 2,
    Junk = 3,
}

public sealed record ConversationFindHit(int CardIndex, int Offset, int Length, bool InBody);

public sealed record TypeaheadAdvance(string Prefix, ThreadRow? Row);

public sealed record NavTypeaheadAdvance(string Prefix, ShellNavItem? Item);

public sealed class ConversationCard
{
    public required MessageInfo Message { get; init; }

    public required string Sender { get; init; }

    public required string DateLabel { get; init; }

    public string ToLine { get; init; } = string.Empty;

    public string CcLine { get; init; } = string.Empty;

    public string BccLine { get; init; } = string.Empty;

    public string ReplyToLine { get; init; } = string.Empty;

    public bool ShowToLine => IsSelected && !string.IsNullOrEmpty(ToLine);

    public bool ShowCcLine => IsSelected && !string.IsNullOrEmpty(CcLine);

    public bool ShowBccLine => IsSelected && !string.IsNullOrEmpty(BccLine);

    public bool ShowReplyToLine => IsSelected && !string.IsNullOrEmpty(ReplyToLine);

    public bool IsSelected { get; init; }

    public bool ConversationExpanded { get; init; }

    public bool ShowBodyText =>
        !IsSelected
        && !ConversationExpanded
        && !BodyUnavailable
        && !string.IsNullOrWhiteSpace(BodyText);

    public bool ShowExpandedBody =>
        !IsSelected
        && ConversationExpanded
        && !BodyUnavailable
        && !string.IsNullOrWhiteSpace(BodyText);

    public bool ShowUnavailable => BodyUnavailable && !IsSelected;

    public bool ShowRemoteImagesCaption => IsSelected && HasRemoteImages;

    public IReadOnlyList<AttachmentInfo> Attachments { get; init; } = [];

    public bool ShowAttachmentChips => IsSelected && Attachments.Count > 0;

    public bool ShowAttachmentClip => !IsSelected && Attachments.Count > 0;

    public string AttachmentTip =>
        string.Join(", ", Attachments.Select(attachment => attachment.ListLabel));

    public bool ShowActions => IsSelected;

    public bool ShowFlag => IsSelected && !Message.IsFlagged;

    public bool ShowUnflag => IsSelected && Message.IsFlagged;

    public bool ShowMarkUnread => IsSelected && Message.IsRead;

    public bool ShowMarkRead => IsSelected && !Message.IsRead;

    public bool HasRemoteImages { get; init; }

    public bool BodyUnavailable { get; init; }

    public string BodyText { get; init; } = string.Empty;

    public string? SearchNeedle { get; init; }

    public string Snippet =>
        MailShellFormatting.ConversationSnippet(
            MailShellFormatting.SplitQuoted(BodyText).Visible,
            SearchNeedle);

    public string ExpandedBody =>
        MailShellFormatting.SplitQuoted(BodyText).Visible;

    public string? BodyHtml { get; init; }

    public string Initial =>
        string.IsNullOrEmpty(Sender) ? "?" : Sender[..1].ToUpperInvariant();
}
