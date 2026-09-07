using Mailtide.Core;
using Mailtide.UI;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class MailShellFormattingTests
{
    [TestMethod]
    public void OutboxItemMatches_filters_subject_state_and_error()
    {
        var queued = new OutboxItemInfo(
            Guid.NewGuid(),
            Guid.NewGuid(),
            OutboxItemState.Queued,
            "Invoice 99",
            null,
            DateTimeOffset.UtcNow)
        {
            ToAddresses = ["pat@example.com"],
        };
        var failed = queued with
        {
            State = OutboxItemState.Failed,
            Subject = "Hello",
            ErrorMessage = "SMTP rejected",
        };

        Assert.IsTrue(MailShellFormatting.OutboxItemMatches(queued, "invoice"));
        Assert.IsFalse(MailShellFormatting.OutboxItemMatches(queued, "smtp"));
        Assert.IsTrue(MailShellFormatting.OutboxItemMatches(queued, "pat@"));
        Assert.IsTrue(MailShellFormatting.OutboxItemMatches(failed, "rejected"));
        Assert.IsTrue(MailShellFormatting.OutboxItemMatches(failed, "Failed"));
        Assert.IsTrue(MailShellFormatting.OutboxItemMatches(queued, " "));
        Assert.IsTrue(MailShellFormatting.OutboxItemMatches(queued, "to:pat"));
        Assert.IsFalse(MailShellFormatting.OutboxItemMatches(queued, "to:bob"));
        Assert.IsTrue(MailShellFormatting.OutboxItemMatches(queued, "subject:Invoice"));
        Assert.IsFalse(MailShellFormatting.OutboxItemMatches(queued, "-to:pat"));
        Assert.IsFalse(MailShellFormatting.OutboxItemMatches(queued, "-invoice"));
        Assert.IsTrue(MailShellFormatting.OutboxItemMatches(queued, "invoice OR receipt"));
        Assert.IsTrue(MailShellFormatting.OutboxItemMatches(queued, "to:bob OR to:pat"));
        Assert.IsFalse(MailShellFormatting.OutboxItemMatches(queued, "receipt OR to:bob"));
        Assert.IsTrue(MailShellFormatting.OutboxItemMatches(failed, "is:failed"));
        Assert.IsFalse(MailShellFormatting.OutboxItemMatches(queued, "is:failed"));
        Assert.AreEqual("No failed messages", MailShellFormatting.EmptyListCopy(true, false, "is:failed"));
        Assert.AreEqual("Outbox is empty", MailShellFormatting.EmptyListCopy(true, false, ""));
    }

    [TestMethod]
    public void ConversationCard_unselected_html_shows_snippet_not_the_webview_slot()
    {
        var older = Card(selected: false, bodyText: "older html", bodyHtml: "<p>older html</p>");
        var expanded = Card(selected: true, bodyText: "later html", bodyHtml: "<p>later html</p>", remote: true);

        Assert.IsTrue(older.ShowBodyText);
        Assert.AreEqual("older html", older.Snippet);
        Assert.IsFalse(older.ShowToLine);
        Assert.IsFalse(older.ShowCcLine);
        Assert.IsFalse(older.ShowRemoteImagesCaption);
        Assert.IsFalse(older.ShowActions);
        Assert.IsFalse(older.ShowFlag);
        Assert.IsFalse(expanded.ShowBodyText);
        Assert.IsTrue(expanded.ShowActions);
        Assert.IsTrue(expanded.ShowFlag);
        Assert.IsFalse(expanded.ShowUnflag);
        Assert.IsTrue(Card(selected: true, flagged: true).ShowUnflag);
        Assert.IsFalse(Card(selected: true, flagged: true).ShowFlag);
        Assert.IsTrue(expanded.ShowToLine);
        Assert.IsTrue(expanded.ShowCcLine);
        Assert.IsTrue(expanded.ShowRemoteImagesCaption);
        Assert.IsFalse(Card(selected: true, bodyText: "plain").ShowBodyText);
        Assert.IsTrue(Card(unavailable: true).ShowUnavailable);
        Assert.IsFalse(Card(selected: true, unavailable: true).ShowUnavailable);
    }

    [TestMethod]
    public void WithCount_appends_positive_counts_only()
    {
        Assert.AreEqual("Inbox", MailShellFormatting.WithCount("Inbox", 0));
        Assert.AreEqual("Inbox (3)", MailShellFormatting.WithCount("Inbox", 3));
        Assert.AreEqual("Outbox (1)", MailShellFormatting.WithCount("Outbox", 1));
    }

    [TestMethod]
    public void DateLabel_uses_time_weekday_then_date()
    {
        var now = DateTimeOffset.Now;
        Assert.AreEqual(now.ToLocalTime().ToString("t"), MailShellFormatting.DateLabel(now));
        var yesterday = now.AddDays(-1);
        Assert.AreEqual(yesterday.ToLocalTime().ToString("t"), MailShellFormatting.DateLabel(yesterday));
        var recent = now.AddDays(-3);
        Assert.AreEqual(recent.ToLocalTime().ToString("ddd"), MailShellFormatting.DateLabel(recent));
        var earlierThisYear = new DateTimeOffset(now.Year, 1, 15, 9, 0, 0, now.Offset);
        if (earlierThisYear.ToLocalTime().Date < DateTimeOffset.Now.Date.AddDays(-7))
        {
            Assert.AreEqual(earlierThisYear.ToLocalTime().ToString("MMM d"), MailShellFormatting.DateLabel(earlierThisYear));
        }

        var older = now.AddYears(-1);
        Assert.AreEqual(older.ToLocalTime().ToString("yyyy-MM-dd"), MailShellFormatting.DateLabel(older));
    }

    [TestMethod]
    public void DateGroupTitle_buckets_today_and_yesterday()
    {
        var now = DateTimeOffset.Now;
        Assert.AreEqual("Today", MailShellFormatting.DateGroupTitle(now));
        Assert.AreEqual("Yesterday", MailShellFormatting.DateGroupTitle(now.AddDays(-1)));
    }

    [TestMethod]
    public void Outbox_badge_turns_failed_when_any_item_failed()
    {
        var queued = new ShellNavItem { Kind = ShellNavKind.Outbox, Title = "Outbox", OutboxCount = 2 };
        Assert.IsTrue(queued.HasOutboxBadge);
        Assert.IsFalse(queued.HasOutboxFailed);
        var failed = new ShellNavItem
        {
            Kind = ShellNavKind.Outbox,
            Title = "Outbox",
            OutboxCount = 2,
            OutboxFailedCount = 1,
        };
        Assert.IsFalse(failed.HasOutboxBadge);
        Assert.IsTrue(failed.HasOutboxFailed);
    }

    [TestMethod]
    public void NavFocusedDelete_empties_Trash_and_Junk_and_spares_role_Mailboxes()
    {
        var id = Guid.NewGuid();
        Assert.AreEqual(NavFocusedDeleteAction.None, MailShellFormatting.NavFocusedDelete(null, MailboxRole.Inbox));
        Assert.AreEqual(NavFocusedDeleteAction.None, MailShellFormatting.NavFocusedDelete(id, MailboxRole.Inbox));
        Assert.AreEqual(NavFocusedDeleteAction.None, MailShellFormatting.NavFocusedDelete(id, MailboxRole.Sent));
        Assert.AreEqual(NavFocusedDeleteAction.None, MailShellFormatting.NavFocusedDelete(id, MailboxRole.Drafts));
        Assert.AreEqual(NavFocusedDeleteAction.None, MailShellFormatting.NavFocusedDelete(id, MailboxRole.Archive));
        Assert.AreEqual(NavFocusedDeleteAction.EmptyTrash, MailShellFormatting.NavFocusedDelete(id, MailboxRole.Trash));
        Assert.AreEqual(NavFocusedDeleteAction.EmptyJunk, MailShellFormatting.NavFocusedDelete(id, MailboxRole.Junk));
        Assert.AreEqual(NavFocusedDeleteAction.DeleteMailbox, MailShellFormatting.NavFocusedDelete(id, null));
    }

    [TestMethod]
    public void PageScroll_pages_then_stops_at_the_ends()
    {
        Assert.IsTrue(MailShellFormatting.CanPageScroll(0, 1000, 200, down: true));
        Assert.IsFalse(MailShellFormatting.CanPageScroll(800, 1000, 200, down: true));
        Assert.IsFalse(MailShellFormatting.CanPageScroll(0, 1000, 200, down: false));
        Assert.IsTrue(MailShellFormatting.CanPageScroll(180, 1000, 200, down: false));
        Assert.IsFalse(MailShellFormatting.CanPageScroll(0, 100, 200, down: true));
        Assert.IsTrue(MailShellFormatting.ShouldDeferPagingToHtml(htmlVisible: true, htmlFocused: true, canPageOuter: false));
        Assert.IsFalse(MailShellFormatting.ShouldDeferPagingToHtml(htmlVisible: true, htmlFocused: true, canPageOuter: true));
        Assert.IsFalse(MailShellFormatting.ShouldDeferPagingToHtml(htmlVisible: true, htmlFocused: false, canPageOuter: false));
        Assert.IsFalse(MailShellFormatting.ShouldDeferPagingToHtml(htmlVisible: false, htmlFocused: true, canPageOuter: false));
        Assert.IsTrue(MailShellFormatting.ShouldDeferPagingToAttachmentStrip(stripVisible: true, stripFocused: true));
        Assert.IsFalse(MailShellFormatting.ShouldDeferPagingToAttachmentStrip(stripVisible: true, stripFocused: false));
        Assert.IsFalse(MailShellFormatting.ShouldDeferPagingToAttachmentStrip(stripVisible: false, stripFocused: true));
        Assert.AreEqual(180, MailShellFormatting.PageScrollOffset(0, 1000, 200, down: true), 0.001);
        Assert.AreEqual(800, MailShellFormatting.PageScrollOffset(800, 1000, 200, down: true), 0.001);
        Assert.AreEqual(0, MailShellFormatting.PageScrollOffset(180, 1000, 200, down: false), 0.001);
        Assert.AreEqual(0, MailShellFormatting.PageScrollOffset(0, 1000, 200, down: false), 0.001);
    }

    [TestMethod]
    public void ConversationCard_expand_all_shows_full_body_on_collapsed_cards()
    {
        var collapsed = Card(selected: false, bodyText: "Hello\n> quoted");
        Assert.IsTrue(collapsed.ShowBodyText);
        Assert.IsFalse(collapsed.ShowExpandedBody);
        var expanded = Card(selected: false, bodyText: "Hello\n> quoted", conversationExpanded: true);
        Assert.IsFalse(expanded.ShowBodyText);
        Assert.IsTrue(expanded.ShowExpandedBody);
        Assert.AreEqual("Hello", expanded.ExpandedBody.Trim());
        var selected = Card(selected: true, bodyText: "Hello", conversationExpanded: true);
        Assert.IsFalse(selected.ShowBodyText);
        Assert.IsFalse(selected.ShowExpandedBody);
        Assert.IsTrue(selected.ShowMarkUnread);
        Assert.IsFalse(selected.ShowMarkRead);
        var unread = Card(selected: true, isRead: false);
        Assert.IsFalse(unread.ShowMarkUnread);
        Assert.IsTrue(unread.ShowMarkRead);
        Assert.IsFalse(Card(selected: false).ShowMarkUnread);
        var clipped = Card(
            selected: false,
            attachments:
            [
                new AttachmentInfo(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "notes.txt", "text/plain"),
                new AttachmentInfo(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "photo.png", "image/png"),
            ]);
        Assert.IsTrue(clipped.ShowAttachmentClip);
        Assert.AreEqual("notes.txt, photo.png", clipped.AttachmentTip);
    }

    [TestMethod]
    public void ComposeTabTarget_moves_through_header_fields()
    {
        Assert.AreEqual(1, MailShellFormatting.ComposeTabTarget(4, 0, forward: true));
        Assert.AreEqual(3, MailShellFormatting.ComposeTabTarget(4, 2, forward: true));
        Assert.AreEqual(-1, MailShellFormatting.ComposeTabTarget(4, 3, forward: true));
        Assert.AreEqual(2, MailShellFormatting.ComposeTabTarget(4, 3, forward: false));
        Assert.AreEqual(-1, MailShellFormatting.ComposeTabTarget(4, 0, forward: false));
        Assert.AreEqual(-1, MailShellFormatting.ComposeTabTarget(0, 0, forward: true));
    }

    [TestMethod]
    public void AdjacentConversationCard_moves_without_wrapping()
    {
        var older = Card();
        var newer = Card();
        ConversationCard[] cards = [older, newer];
        Assert.AreSame(newer, MailShellFormatting.AdjacentConversationCard(cards, older.Message.Id, true));
        Assert.IsNull(MailShellFormatting.AdjacentConversationCard(cards, newer.Message.Id, true));
        Assert.AreSame(older, MailShellFormatting.AdjacentConversationCard(cards, newer.Message.Id, false));
        Assert.IsNull(MailShellFormatting.AdjacentConversationCard(cards, older.Message.Id, false));
        Assert.AreSame(older, MailShellFormatting.AdjacentConversationCard(cards, null, true));
        Assert.AreSame(newer, MailShellFormatting.AdjacentConversationCard(cards, null, false));
    }

    [TestMethod]
    public void AdjacentThreadRow_moves_without_wrapping_or_headers()
    {
        var a = new ThreadRow { Id = Guid.NewGuid(), Sender = "A", Subject = "s", Preview = "", DateLabel = "t" };
        var header = new ThreadRow { Id = Guid.Empty, Sender = "", Subject = "Today", Preview = "", DateLabel = "", IsGroupHeader = true, GroupTitle = "Today" };
        var b = new ThreadRow { Id = Guid.NewGuid(), Sender = "B", Subject = "s", Preview = "", DateLabel = "t" };
        ThreadRow[] rows = [header, a, b];
        Assert.AreSame(b, MailShellFormatting.AdjacentThreadRow(rows, a, true));
        Assert.IsNull(MailShellFormatting.AdjacentThreadRow(rows, b, true));
        Assert.AreSame(a, MailShellFormatting.AdjacentThreadRow(rows, b, false));
        Assert.IsNull(MailShellFormatting.AdjacentThreadRow(rows, a, false));
        Assert.AreSame(a, MailShellFormatting.AdjacentThreadRow(rows, null, true));
        Assert.AreSame(b, MailShellFormatting.AdjacentThreadRow(rows, null, false));
    }

    [TestMethod]
    public void PagedThreadRow_skips_headers_and_clamps_without_wrapping()
    {
        var header = new ThreadRow { Id = Guid.Empty, Sender = "", Subject = "Today", Preview = "", DateLabel = "", IsGroupHeader = true, GroupTitle = "Today" };
        var rows = new List<ThreadRow> { header };
        ThreadRow? current = null;
        for (var i = 0; i < 6; i++)
        {
            var row = new ThreadRow { Id = Guid.NewGuid(), Sender = "S" + i, Subject = "s", Preview = "", DateLabel = "t" };
            rows.Add(row);
            if (i == 0)
            {
                current = row;
            }
        }

        var paged = MailShellFormatting.PagedThreadRow(rows, current, true, pageSize: 3);
        Assert.AreEqual("S3", paged?.Sender);
        Assert.AreEqual("S5", MailShellFormatting.PagedThreadRow(rows, paged, true, pageSize: 3)?.Sender);
        Assert.IsNull(MailShellFormatting.PagedThreadRow(rows, rows[^1], true, pageSize: 3));
        Assert.AreEqual("S2", MailShellFormatting.PagedThreadRow(rows, rows[^1], false, pageSize: 3)?.Sender);
        Assert.IsNull(MailShellFormatting.PagedThreadRow(rows, current, false, pageSize: 3));
        Assert.AreSame(current, MailShellFormatting.PagedThreadRow(rows, null, true, pageSize: 3));
        Assert.AreSame(rows[^1], MailShellFormatting.PagedThreadRow(rows, null, false, pageSize: 3));
    }

    [TestMethod]
    public void AdjacentNavItem_skips_account_headers_and_favorite_pins()
    {
        var favorites = new ShellNavItem { Kind = ShellNavKind.Favorites, Title = "Favorites" };
        var pin = new ShellNavItem
        {
            Kind = ShellNavKind.Mailbox,
            Title = "INBOX",
            MailboxId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            IsFavoritePin = true,
        };
        var unified = new ShellNavItem { Kind = ShellNavKind.UnifiedInbox, Title = "Unified Inbox" };
        var inbox = new ShellNavItem
        {
            Kind = ShellNavKind.Mailbox,
            Title = "INBOX",
            MailboxId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
        };
        var sent = new ShellNavItem
        {
            Kind = ShellNavKind.Mailbox,
            Title = "Sent",
            MailboxId = Guid.NewGuid(),
            AccountId = inbox.AccountId,
        };
        var account = new ShellNavItem
        {
            Kind = ShellNavKind.Account,
            Title = "Personal",
            AccountId = inbox.AccountId,
            Children = [inbox, sent],
        };
        var outbox = new ShellNavItem
        {
            Kind = ShellNavKind.Outbox,
            Title = "Outbox",
            AccountId = inbox.AccountId,
        };
        var flat = MailShellFormatting.FlattenNav([favorites, pin, unified, account, outbox]);
        Assert.IsFalse(MailShellFormatting.CanGoToNav(favorites));
        Assert.IsFalse(MailShellFormatting.CanGoToNav(pin));
        Assert.IsFalse(MailShellFormatting.CanGoToNav(account));
        Assert.AreSame(unified, MailShellFormatting.AdjacentNavItem(flat, pin, true));
        Assert.AreSame(inbox, MailShellFormatting.AdjacentNavItem(flat, unified, true));
        Assert.AreSame(inbox, MailShellFormatting.AdjacentNavItem(flat, account, true));
        Assert.AreSame(sent, MailShellFormatting.AdjacentNavItem(flat, inbox, true));
        Assert.AreSame(outbox, MailShellFormatting.AdjacentNavItem(flat, sent, true));
        Assert.IsNull(MailShellFormatting.AdjacentNavItem(flat, outbox, true));
        Assert.AreSame(sent, MailShellFormatting.AdjacentNavItem(flat, outbox, false));
        Assert.AreSame(unified, MailShellFormatting.AdjacentNavItem(flat, null, true));
        Assert.AreSame(outbox, MailShellFormatting.AdjacentNavItem(flat, null, false));
    }

    [TestMethod]
    public void AdjacentUnreadMailbox_skips_pins_and_read_folders()
    {
        var pin = new ShellNavItem
        {
            Kind = ShellNavKind.Mailbox,
            Title = "INBOX",
            MailboxId = Guid.NewGuid(),
            IsFavoritePin = true,
        };
        var inbox = new ShellNavItem
        {
            Kind = ShellNavKind.Mailbox,
            Title = "INBOX",
            MailboxId = Guid.NewGuid(),
        };
        var sent = new ShellNavItem
        {
            Kind = ShellNavKind.Mailbox,
            Title = "Sent",
            MailboxId = Guid.NewGuid(),
        };
        var archive = new ShellNavItem
        {
            Kind = ShellNavKind.Mailbox,
            Title = "Archive",
            MailboxId = Guid.NewGuid(),
        };
        var unread = new Dictionary<Guid, int>
        {
            [inbox.MailboxId!.Value] = 0,
            [sent.MailboxId!.Value] = 0,
            [archive.MailboxId!.Value] = 2,
            [pin.MailboxId!.Value] = 5,
        };
        ShellNavItem[] flat = [pin, inbox, sent, archive];
        Assert.IsFalse(MailShellFormatting.HasOwnUnread(pin, unread));
        Assert.IsFalse(MailShellFormatting.HasOwnUnread(inbox, unread));
        Assert.AreSame(archive, MailShellFormatting.AdjacentUnreadMailbox(flat, inbox, inbox.MailboxId, unread, true));
        Assert.AreSame(archive, MailShellFormatting.AdjacentUnreadMailbox(flat, pin, inbox.MailboxId, unread, true));
        Assert.IsNull(MailShellFormatting.AdjacentUnreadMailbox(flat, archive, archive.MailboxId, unread, true));
        Assert.AreSame(archive, MailShellFormatting.AdjacentUnreadMailbox(flat, null, null, unread, true));
        unread[inbox.MailboxId!.Value] = 1;
        Assert.AreSame(inbox, MailShellFormatting.AdjacentUnreadMailbox(flat, archive, archive.MailboxId, unread, false));
    }

    [TestMethod]
    public void PushRecentSearch_dedupes_and_caps_at_ten()
    {
        var once = MailShellFormatting.PushRecentSearch([], "  from:bob  ");
        Assert.AreEqual("from:bob", once.Single());
        var again = MailShellFormatting.PushRecentSearch(once, "FROM:BOB");
        Assert.AreEqual("FROM:BOB", again.Single());
        IReadOnlyList<string> filled = [];
        for (var i = 0; i < 12; i++)
        {
            filled = MailShellFormatting.PushRecentSearch(filled, "q" + i);
        }

        Assert.HasCount(10, filled);
        Assert.AreEqual("q11", filled[0]);
        Assert.AreEqual("q2", filled[^1]);
        CollectionAssert.AreEqual(new[] { "keep" }, MailShellFormatting.PushRecentSearch(["keep"], "  ").ToArray());
        CollectionAssert.AreEqual(new[] { "keep" }, MailShellFormatting.PushRecentSearch(["keep"], "is:unread").ToArray());
        CollectionAssert.AreEqual(new[] { "keep" }, MailShellFormatting.PushRecentSearch(["keep"], "is:flagged has:attachment").ToArray());
        CollectionAssert.AreEqual(new[] { "from:bob is:unread", "keep" }, MailShellFormatting.PushRecentSearch(["keep"], "from:bob is:unread").ToArray());
        CollectionAssert.AreEqual(new[] { "from:bob" }, MailShellFormatting.MatchingRecentSearches(["from:bob", "invoice"], "BOB").ToArray());
        CollectionAssert.AreEqual(new[] { "invoice" }, MailShellFormatting.RemoveRecentSearch(["from:bob", "invoice"], "FROM:BOB").ToArray());
        CollectionAssert.AreEqual(new[] { "keep" }, MailShellFormatting.RemoveRecentSearch(["keep"], "missing").ToArray());
        Assert.AreEqual("a\nb", MailShellFormatting.EncodeRecentSearches(["a", "b"]));
        CollectionAssert.AreEqual(new[] { "a", "b" }, MailShellFormatting.DecodeRecentSearches("a\nb"));
    }

    [TestMethod]
    public void SortThreadRows_from_and_subject_skip_date_headers()
    {
        var carol = new ThreadRow
        {
            Id = Guid.NewGuid(),
            Sender = "Carol",
            Subject = "Zebra",
            Preview = string.Empty,
            DateLabel = "t",
            SortDate = DateTimeOffset.Now,
            SizeBytes = 100,
        };
        var bob = new ThreadRow
        {
            Id = Guid.NewGuid(),
            Sender = "Bob",
            Subject = "Apple",
            Preview = string.Empty,
            DateLabel = "o",
            SortDate = DateTimeOffset.Now.AddYears(-1),
            SizeBytes = 5000,
        };
        var fromSort = MailShellFormatting.SortThreadRows([carol, bob], ThreadListSort.From);
        Assert.IsFalse(fromSort.Any(row => row.IsGroupHeader));
        Assert.AreEqual("Bob", fromSort[0].Sender);
        Assert.AreEqual("Carol", fromSort[1].Sender);

        var subjectSort = MailShellFormatting.SortThreadRows([carol, bob], ThreadListSort.Subject);
        Assert.AreEqual("Apple", subjectSort[0].Subject);
        Assert.AreEqual("Zebra", subjectSort[1].Subject);

        var newest = MailShellFormatting.SortThreadRows([bob, carol], ThreadListSort.Newest);
        Assert.IsTrue(newest[0].IsGroupHeader);
        Assert.AreSame(carol, newest[1]);

        Assert.AreEqual(ThreadListSort.Oldest, MailShellFormatting.NextListSort(ThreadListSort.Newest));
        Assert.AreEqual(ThreadListSort.From, MailShellFormatting.NextListSort(ThreadListSort.Oldest));
        Assert.AreEqual(ThreadListSort.Subject, MailShellFormatting.NextListSort(ThreadListSort.From));
        Assert.AreEqual(ThreadListSort.Size, MailShellFormatting.NextListSort(ThreadListSort.Subject));
        Assert.AreEqual(ThreadListSort.Newest, MailShellFormatting.NextListSort(ThreadListSort.Size));
        CollectionAssert.AreEqual(
            new[]
            {
                ThreadListSort.Newest,
                ThreadListSort.Oldest,
                ThreadListSort.From,
                ThreadListSort.Subject,
                ThreadListSort.Size,
            },
            MailShellFormatting.ListSortChoices.ToArray());
        Assert.AreEqual(ThreadListSort.Size, MailShellFormatting.ParseListSortLabel("Size"));
        Assert.AreEqual(ThreadListSort.Newest, MailShellFormatting.ParseListSortLabel("Newest"));
        Assert.IsNull(MailShellFormatting.ParseListSortLabel("Unread"));
        var keep = Guid.NewGuid();
        var skip = Guid.NewGuid();
        Assert.IsTrue(MailShellFormatting.ThreadWasExpanded([keep, skip], new HashSet<Guid> { keep }));
        Assert.IsFalse(MailShellFormatting.ThreadWasExpanded([skip], new HashSet<Guid> { keep }));
        Assert.IsFalse(MailShellFormatting.ThreadWasExpanded([], new HashSet<Guid> { keep }));
        Assert.IsTrue(MailShellFormatting.CanSelectThreadRow(Thread(Guid.NewGuid(), Guid.NewGuid())));
        Assert.IsFalse(MailShellFormatting.CanSelectThreadRow(new ThreadRow
        {
            Id = Guid.Empty,
            Sender = "",
            Subject = "",
            Preview = "",
            DateLabel = "",
            IsGroupHeader = true,
            GroupTitle = "Today",
        }));
        Assert.AreEqual("From", MailShellFormatting.ListSortLabel(ThreadListSort.From));
        Assert.AreEqual("Size", MailShellFormatting.ListSortLabel(ThreadListSort.Size));
        Assert.AreEqual(ThreadListSort.From, MailShellFormatting.ParseListSort("from"));
        Assert.AreEqual(ThreadListSort.Size, MailShellFormatting.ParseListSort("size"));
        Assert.AreEqual("subject", MailShellFormatting.EncodeListSort(ThreadListSort.Subject));
        Assert.AreEqual("size", MailShellFormatting.EncodeListSort(ThreadListSort.Size));

        var sizeSort = MailShellFormatting.SortThreadRows([carol, bob], ThreadListSort.Size);
        Assert.IsFalse(sizeSort.Any(row => row.IsGroupHeader));
        Assert.AreEqual("Bob", sizeSort[0].Sender);
        Assert.AreEqual("Carol", sizeSort[1].Sender);
    }

    [TestMethod]
    public void WithDateGroups_inserts_a_header_when_the_bucket_changes()
    {
        var today = new ThreadRow
        {
            Id = Guid.NewGuid(),
            Sender = "A",
            Subject = "s",
            Preview = string.Empty,
            DateLabel = "t",
            SortDate = DateTimeOffset.Now,
        };
        var older = new ThreadRow
        {
            Id = Guid.NewGuid(),
            Sender = "B",
            Subject = "s",
            Preview = string.Empty,
            DateLabel = "o",
            SortDate = DateTimeOffset.Now.AddYears(-1),
        };
        var grouped = MailShellFormatting.WithDateGroups([older, today]);
        Assert.IsTrue(grouped[0].IsGroupHeader);
        Assert.AreEqual("Today", grouped[0].GroupTitle);
        Assert.AreSame(today, grouped[1]);
        Assert.IsTrue(grouped[2].IsGroupHeader);
        Assert.AreNotEqual(grouped[0].GroupTitle, grouped[2].GroupTitle);
        Assert.AreSame(older, grouped[3]);

        var oldestFirst = MailShellFormatting.WithDateGroups([today, older], newestFirst: false);
        Assert.AreSame(older, oldestFirst.First(row => !row.IsGroupHeader));
        Assert.AreSame(today, oldestFirst.Last(row => !row.IsGroupHeader));
    }

    [TestMethod]
    public void RowAfterCrossingHeader_skips_headers_when_moving_from_an_adjacent_row()
    {
        var today = new ThreadRow
        {
            Id = Guid.NewGuid(),
            Sender = "A",
            Subject = "s",
            Preview = string.Empty,
            DateLabel = "t",
            SortDate = DateTimeOffset.Now,
        };
        var older = new ThreadRow
        {
            Id = Guid.NewGuid(),
            Sender = "B",
            Subject = "s",
            Preview = string.Empty,
            DateLabel = "o",
            SortDate = DateTimeOffset.Now.AddYears(-1),
        };
        var grouped = MailShellFormatting.WithDateGroups([today, older]);
        var yesterdayHeader = grouped[2];
        Assert.IsTrue(yesterdayHeader.IsGroupHeader);
        Assert.AreSame(older, MailShellFormatting.RowAfterCrossingHeader(grouped, yesterdayHeader, today));
        Assert.AreSame(today, MailShellFormatting.RowAfterCrossingHeader(grouped, yesterdayHeader, older));
        Assert.AreSame(today, MailShellFormatting.RowAfterCrossingHeader(grouped, grouped[0], older));
    }

    [TestMethod]
    public void RowAfterCrossingHeader_skips_headers_when_page_moving_across_groups()
    {
        var today = new ThreadRow
        {
            Id = Guid.NewGuid(),
            Sender = "A",
            Subject = "s",
            Preview = string.Empty,
            DateLabel = "t",
            SortDate = DateTimeOffset.Now,
        };
        var yesterday = new ThreadRow
        {
            Id = Guid.NewGuid(),
            Sender = "B",
            Subject = "s",
            Preview = string.Empty,
            DateLabel = "y",
            SortDate = DateTimeOffset.Now.AddDays(-1),
        };
        var older = new ThreadRow
        {
            Id = Guid.NewGuid(),
            Sender = "C",
            Subject = "s",
            Preview = string.Empty,
            DateLabel = "o",
            SortDate = DateTimeOffset.Now.AddYears(-1),
        };
        var grouped = MailShellFormatting.WithDateGroups([today, yesterday, older]);
        Assert.AreSame(older, MailShellFormatting.RowAfterCrossingHeader(grouped, grouped[4], today));
        Assert.AreSame(today, MailShellFormatting.RowAfterCrossingHeader(grouped, grouped[2], older));
        Assert.AreSame(today, MailShellFormatting.RowAfterCrossingHeader(grouped, grouped[0], older));
        var trailingHeader = new ThreadRow
        {
            Id = Guid.Empty,
            Sender = string.Empty,
            Subject = "End",
            Preview = string.Empty,
            DateLabel = string.Empty,
            SortDate = older.SortDate,
            IsGroupHeader = true,
            GroupTitle = "End",
        };
        var withEnd = grouped.Append(trailingHeader).ToList();
        Assert.AreSame(older, MailShellFormatting.RowAfterCrossingHeader(withEnd, trailingHeader, today));
    }

    [TestMethod]
    public void ListPrimary_shows_recipients_in_Sent_and_Drafts()
    {
        Assert.AreEqual("bob@example.com", MailShellFormatting.Sender("<bob@example.com>"));
        Assert.AreEqual("Bob", MailShellFormatting.Sender("Bob <bob@example.com>"));
        Assert.AreEqual("me@example.com", MailShellFormatting.ListPrimary("me@example.com", ["Pat <pat@example.com>"], MailboxRole.Inbox));
        Assert.AreEqual(
            "Pat, sam@example.com",
            MailShellFormatting.ListPrimary(
                "me@example.com",
                ["Pat <pat@example.com>", "sam@example.com"],
                MailboxRole.Sent));
        Assert.AreEqual("(no recipient)", MailShellFormatting.ListPrimary("me@example.com", [], MailboxRole.Drafts));
        Assert.AreEqual("me@example.com", MailShellFormatting.ListPrimary("me@example.com", ["pat@example.com"], MailboxRole.Trash));
    }

    [TestMethod]
    public void NeedsEmptyBodyConfirm_treats_signature_only_as_empty()
    {
        Assert.IsTrue(MailShellFormatting.NeedsEmptySubjectConfirm(" "));
        Assert.IsFalse(MailShellFormatting.NeedsEmptySubjectConfirm("Hello"));
        Assert.IsTrue(MailShellFormatting.NeedsEmptyBodyConfirm(null, null, 0));
        Assert.IsTrue(MailShellFormatting.NeedsEmptyBodyConfirm(MailSignature.Apply(string.Empty, "Pat"), "Pat", 0));
        Assert.IsFalse(MailShellFormatting.NeedsEmptyBodyConfirm("Hello", "Pat", 0));
        Assert.IsFalse(MailShellFormatting.NeedsEmptyBodyConfirm(string.Empty, null, 1));
        Assert.IsFalse(MailShellFormatting.NeedsEmptyBodyConfirm(string.Empty, null, 0, "<p>hi</p>"));
        Assert.IsTrue(MailShellFormatting.NeedsRecipient(" ", "", null));
        Assert.IsFalse(MailShellFormatting.NeedsRecipient("", cc: "carol@example.com"));
        Assert.IsFalse(MailShellFormatting.NeedsRecipient("", bcc: "hidden@example.com"));
        Assert.AreEqual("Add at least one recipient before sending.", MailShellFormatting.RecipientError(""));
        Assert.AreEqual(
            "This address looks invalid: not-an-email",
            MailShellFormatting.RecipientError("not-an-email"));
        Assert.AreEqual(
            "These addresses look invalid: bad, also-bad",
            MailShellFormatting.RecipientError("bad, bob@example.com", cc: "also-bad"));
        Assert.AreEqual(string.Empty, MailShellFormatting.RecipientError("Alice <alice@example.com>"));
        Assert.AreEqual(ComposePasteKind.Text, MailShellFormatting.ClassifyComposePaste(true, true, true, true));
        Assert.AreEqual(ComposePasteKind.AttachFiles, MailShellFormatting.ClassifyComposePaste(false, true, true, false));
        Assert.AreEqual(ComposePasteKind.Text, MailShellFormatting.ClassifyComposePaste(false, false, true, true));
        Assert.AreEqual(ComposePasteKind.AttachImage, MailShellFormatting.ClassifyComposePaste(false, false, false, true));
        Assert.AreEqual(ComposePasteKind.Text, MailShellFormatting.ClassifyComposePaste(false, false, false, false));
    }

    [TestMethod]
    public void ConversationSnippet_collapses_whitespace_and_truncates()
    {
        Assert.AreEqual("hello world", MailShellFormatting.ConversationSnippet("hello\n\n world"));
        var longBody = new string('a', MailShellFormatting.ConversationSnippetLength + 8);
        var snippet = MailShellFormatting.ConversationSnippet(longBody);
        Assert.AreEqual(MailShellFormatting.ConversationSnippetLength + 1, snippet.Length);
        Assert.IsTrue(snippet.EndsWith('…'));
        var buried = new string('a', MailShellFormatting.ConversationSnippetLength) + " secret-token " + new string('b', MailShellFormatting.ConversationSnippetLength);
        var around = MailShellFormatting.ConversationSnippet(buried, "secret-token");
        StringAssert.Contains(around, "secret-token");
        Assert.IsTrue(around.StartsWith('…'));
        Assert.IsTrue(around.EndsWith('…'));
        Assert.AreEqual("hello world", MailShellFormatting.ConversationSnippet("hello\n\n world", "missing"));
        Assert.IsFalse(Card(bodyText: buried).Snippet.Contains("secret-token", StringComparison.Ordinal));
        StringAssert.Contains(Card(bodyText: buried, searchNeedle: "secret-token").Snippet, "secret-token");
    }

    [TestMethod]
    public void MailboxNavTree_nests_child_paths_under_existing_parents()
    {
        var account = Guid.NewGuid();
        var inbox = new MailboxInfo(Guid.NewGuid(), account, "INBOX", "INBOX", MailboxRole.Inbox);
        var work = new MailboxInfo(Guid.NewGuid(), account, "INBOX/Work", "INBOX/Work", null);
        var receipts = new MailboxInfo(Guid.NewGuid(), account, "Receipts", "INBOX/Work/Receipts", null);
        var sent = new MailboxInfo(Guid.NewGuid(), account, "Sent", "Sent", MailboxRole.Sent);

        var tree = MailShellFormatting.MailboxNavTree([inbox, work, receipts, sent]);
        Assert.HasCount(2, tree);
        Assert.AreEqual("INBOX", tree[0].Title);
        Assert.HasCount(1, tree[0].Children);
        Assert.AreEqual("Work", tree[0].Children[0].Title);
        Assert.AreEqual(work.Id, tree[0].Children[0].MailboxId);
        Assert.AreEqual("Receipts", tree[0].Children[0].Children.Single().Title);
        Assert.AreEqual("Sent", tree[1].Title);
        Assert.IsEmpty(tree[1].Children);
    }

    [TestMethod]
    public void MailboxNavTree_rolls_unread_up_to_parents()
    {
        var account = Guid.NewGuid();
        var inbox = new MailboxInfo(Guid.NewGuid(), account, "INBOX", "INBOX", MailboxRole.Inbox)
        {
            UnreadCount = 1,
        };
        var work = new MailboxInfo(Guid.NewGuid(), account, "Work", "INBOX/Work", null)
        {
            UnreadCount = 2,
        };
        var receipts = new MailboxInfo(Guid.NewGuid(), account, "Receipts", "INBOX/Work/Receipts", null)
        {
            UnreadCount = 4,
        };
        var tree = MailShellFormatting.MailboxNavTree([inbox, work, receipts]);
        Assert.AreEqual(7, tree.Single().UnreadCount);
        Assert.AreEqual(6, tree.Single().Children.Single().UnreadCount);
        Assert.AreEqual(4, tree.Single().Children.Single().Children.Single().UnreadCount);
    }

    [TestMethod]
    public void FavoriteDestinationIndex_inserts_before_target_or_at_header()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        Guid[] ids = [a, b, c];
        Assert.AreEqual(0, MailShellFormatting.FavoriteDestinationIndex(ids, c, true, null));
        Assert.IsNull(MailShellFormatting.FavoriteDestinationIndex(ids, a, true, null));
        Assert.AreEqual(1, MailShellFormatting.FavoriteDestinationIndex(ids, a, false, c));
        Assert.AreEqual(1, MailShellFormatting.FavoriteDestinationIndex(ids, c, false, b));
        Assert.IsNull(MailShellFormatting.FavoriteDestinationIndex(ids, b, false, b));
        Assert.IsNull(MailShellFormatting.FavoriteDestinationIndex(ids, a, false, Guid.NewGuid()));
        var fresh = Guid.NewGuid();
        Assert.AreEqual(0, MailShellFormatting.FavoriteDestinationIndex(ids, fresh, true, null));
        Assert.AreEqual(1, MailShellFormatting.FavoriteDestinationIndex(ids, fresh, false, b));
        Assert.IsNull(MailShellFormatting.FavoriteDestinationIndex(ids, fresh, false, null));
        Assert.IsNull(MailShellFormatting.FavoriteDestinationIndex(ids, fresh, false, Guid.NewGuid()));
        Assert.IsTrue(MailShellFormatting.ShouldUnpinFavorite(true, false, null));
        Assert.IsFalse(MailShellFormatting.ShouldUnpinFavorite(false, false, null));
        Assert.IsFalse(MailShellFormatting.ShouldUnpinFavorite(true, true, null));
        Assert.IsFalse(MailShellFormatting.ShouldUnpinFavorite(true, false, b));
    }

    [TestMethod]
    public void RecentMoveTargets_skips_current_and_keeps_recent_order()
    {
        var account = Guid.NewGuid();
        var inbox = new MailboxInfo(Guid.NewGuid(), account, "INBOX", "INBOX", MailboxRole.Inbox);
        var work = new MailboxInfo(Guid.NewGuid(), account, "Work", "INBOX/Work", null);
        var later = new MailboxInfo(Guid.NewGuid(), account, "Later", "Later", null);
        var missing = Guid.NewGuid();
        var recents = MailShellFormatting.RecentMoveTargets(
            [inbox, work, later],
            [later.Id, missing, work.Id, inbox.Id],
            inbox.Id);
        Assert.HasCount(2, recents);
        Assert.AreEqual(later.Id, recents[0].Id);
        Assert.AreEqual("Later", recents[0].Label);
        Assert.AreEqual(work.Id, recents[1].Id);
        Assert.AreEqual("INBOX / Work", recents[1].Label);
        Assert.IsEmpty(MailShellFormatting.RecentMoveTargets([work], [], work.Id));
        Assert.IsEmpty(MailShellFormatting.RecentMoveTargets([work], [work.Id], work.Id));
        Assert.HasCount(1, MailShellFormatting.RecentMoveTargets(
            [inbox, work, later],
            [later.Id, work.Id, inbox.Id],
            currentMailboxId: null,
            max: 1));
    }

    [TestMethod]
    public void MailboxMoveLabel_shows_nested_path()
    {
        var account = Guid.NewGuid();
        var work = new MailboxInfo(Guid.NewGuid(), account, "Work", "INBOX/Work", null);
        var dotted = new MailboxInfo(Guid.NewGuid(), account, "Work", "INBOX.Work", null);
        var inbox = new MailboxInfo(Guid.NewGuid(), account, "INBOX", "INBOX", MailboxRole.Inbox);
        Assert.AreEqual("INBOX / Work", MailShellFormatting.MailboxMoveLabel(work));
        Assert.AreEqual("INBOX / Work", MailShellFormatting.MailboxMoveLabel(dotted));
        Assert.AreEqual("INBOX", MailShellFormatting.MailboxMoveLabel(inbox));
    }

    [TestMethod]
    public void MailboxNavTree_nests_dot_delimited_IMAP_paths()
    {
        var account = Guid.NewGuid();
        var inbox = new MailboxInfo(Guid.NewGuid(), account, "INBOX", "INBOX", MailboxRole.Inbox);
        var work = new MailboxInfo(Guid.NewGuid(), account, "Work", "INBOX.Work", null);
        var tree = MailShellFormatting.MailboxNavTree([inbox, work]);
        Assert.AreEqual("INBOX", tree.Single().Title);
        Assert.AreEqual("Work", tree.Single().Children.Single().Title);
    }

    [TestMethod]
    public void MailboxNavTree_keeps_orphan_child_at_root()
    {
        var work = new MailboxInfo(Guid.NewGuid(), Guid.NewGuid(), "Work", "INBOX/Work", null);
        var tree = MailShellFormatting.MailboxNavTree([work]);
        Assert.HasCount(1, tree);
        Assert.AreEqual("Work", tree[0].Title);
        Assert.IsEmpty(tree[0].Children);
    }

    [TestMethod]
    public void UniqueFileName_avoids_collisions_and_path_segments()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "notes.txt" };
        Assert.AreEqual("notes (1).txt", MailShellFormatting.UniqueFileName("notes.txt", used.Contains));
        Assert.AreEqual("photo.png", MailShellFormatting.UniqueFileName("photo.png", used.Contains));
        Assert.AreEqual("secret.txt", MailShellFormatting.UniqueFileName(@"C:\Windows\secret.txt", used.Contains));
        Assert.AreEqual("attachment", MailShellFormatting.UniqueFileName("..", used.Contains));
    }

    [TestMethod]
    public void OrderConversationMessages_oldest_or_newest_first()
    {
        var older = new MessageInfo(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "1",
            "Old",
            "a@b.com",
            new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
            true);
        var newer = new MessageInfo(
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            older.AccountId,
            older.MailboxId,
            "2",
            "New",
            "a@b.com",
            new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero),
            true);
        var oldest = MailShellFormatting.OrderConversationMessages([newer, older], newestFirst: false);
        Assert.AreEqual(older.Id, oldest[0].Id);
        Assert.AreEqual(newer.Id, oldest[1].Id);
        var newest = MailShellFormatting.OrderConversationMessages([older, newer], newestFirst: true);
        Assert.AreEqual(newer.Id, newest[0].Id);
        Assert.AreEqual(older.Id, newest[1].Id);
    }

    [TestMethod]
    public void ConversationPositionLabel_and_subject_use_selection_and_latest()
    {
        var older = Card(selected: false, bodyText: "older", subject: "Root", receivedAt: new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var later = Card(selected: true, bodyText: "later", subject: "Re: Root", receivedAt: new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero));
        Assert.IsNull(MailShellFormatting.ConversationPositionLabel([later]));
        Assert.AreEqual("2 of 2", MailShellFormatting.ConversationPositionLabel([older, later]));
        Assert.AreEqual("1 of 2", MailShellFormatting.ConversationPositionLabel([later, older]));
        Assert.AreEqual("Re: Root", MailShellFormatting.ConversationSubjectMessage([later, older])!.Subject);
        Assert.IsNull(MailShellFormatting.ConversationSubjectMessage([]));
    }

    [TestMethod]
    public void SplitConversation_places_html_slot_under_the_selected_Message()
    {
        var older = Card(selected: false, bodyText: "older");
        var middle = Card(selected: true, bodyHtml: "<p>middle</p>");
        var later = Card(selected: false, bodyText: "later");

        var split = MailShellFormatting.SplitConversation([older, middle, later]);

        CollectionAssert.AreEqual(new[] { older }, split.Before.ToArray());
        Assert.AreSame(middle, split.Selected);
        CollectionAssert.AreEqual(new[] { later }, split.After.ToArray());

        var none = MailShellFormatting.SplitConversation([older, later]);
        CollectionAssert.AreEqual(new[] { older, later }, none.Before.ToArray());
        Assert.IsNull(none.Selected);
        Assert.IsEmpty(none.After);
    }

    [TestMethod]
    public void ThreadNavDrop_moves_same_account_Mailbox_and_maps_Trash_Junk()
    {
        var account = Guid.NewGuid();
        var inboxId = Guid.NewGuid();
        var sentId = Guid.NewGuid();
        var trashId = Guid.NewGuid();
        var junkId = Guid.NewGuid();
        var otherAccount = Guid.NewGuid();
        var row = Thread(account, inboxId);
        Assert.AreEqual(
            ThreadNavDropKind.Move,
            MailShellFormatting.ThreadNavDrop(row, Nav(ShellNavKind.Mailbox, account, sentId, MailboxRole.Sent)));
        Assert.AreEqual(
            ThreadNavDropKind.Trash,
            MailShellFormatting.ThreadNavDrop(row, Nav(ShellNavKind.Mailbox, account, trashId, MailboxRole.Trash)));
        Assert.AreEqual(
            ThreadNavDropKind.Junk,
            MailShellFormatting.ThreadNavDrop(row, Nav(ShellNavKind.Mailbox, account, junkId, MailboxRole.Junk)));
        Assert.AreEqual(
            ThreadNavDropKind.None,
            MailShellFormatting.ThreadNavDrop(row, Nav(ShellNavKind.Mailbox, account, inboxId, MailboxRole.Inbox)));
        Assert.AreEqual(
            ThreadNavDropKind.None,
            MailShellFormatting.ThreadNavDrop(row, Nav(ShellNavKind.Mailbox, otherAccount, sentId, MailboxRole.Sent)));
        Assert.AreEqual(
            ThreadNavDropKind.None,
            MailShellFormatting.ThreadNavDrop(row, Nav(ShellNavKind.UnifiedInbox, null, null, null)));
        Assert.AreEqual(
            ThreadNavDropKind.None,
            MailShellFormatting.ThreadNavDrop(row, Nav(ShellNavKind.Outbox, account, null, null)));
        Assert.IsTrue(MailShellFormatting.ThreadNavDropCopies(ThreadNavDropKind.Move, true));
        Assert.IsFalse(MailShellFormatting.ThreadNavDropCopies(ThreadNavDropKind.Move, false));
        Assert.IsFalse(MailShellFormatting.ThreadNavDropCopies(ThreadNavDropKind.Trash, true));
        Assert.IsFalse(MailShellFormatting.ThreadNavDropCopies(ThreadNavDropKind.Junk, true));
        Assert.IsFalse(MailShellFormatting.ThreadNavDropCopies(ThreadNavDropKind.None, true));
        Assert.AreEqual(
            ThreadNavDropKind.None,
            MailShellFormatting.ThreadNavDrop(
                new ThreadRow
                {
                    Id = Guid.NewGuid(),
                    Sender = "Queued",
                    Subject = "s",
                    Preview = string.Empty,
                    DateLabel = "t",
                    OutboxItem = new OutboxItemInfo(
                        Guid.NewGuid(),
                        account,
                        OutboxItemState.Queued,
                        "s",
                        null,
                        DateTimeOffset.UtcNow),
                },
                Nav(ShellNavKind.Mailbox, account, sentId, MailboxRole.Sent)));
    }

    [TestMethod]
    public void RowMatchingPrefix_skips_headers_and_matches_sender()
    {
        var header = new ThreadRow
        {
            Id = Guid.Empty,
            Sender = "Ann",
            Subject = "h",
            Preview = string.Empty,
            DateLabel = string.Empty,
            IsGroupHeader = true,
        };
        var bob = new ThreadRow
        {
            Id = Guid.NewGuid(),
            Sender = "Bob",
            Subject = "s",
            Preview = string.Empty,
            DateLabel = "t",
        };
        var ann = new ThreadRow
        {
            Id = Guid.NewGuid(),
            Sender = "Ann",
            Subject = "s",
            Preview = string.Empty,
            DateLabel = "t",
        };
        Assert.AreSame(ann, MailShellFormatting.RowMatchingPrefix([header, bob, ann], "a"));
        Assert.AreSame(bob, MailShellFormatting.RowMatchingPrefix([header, bob, ann], "bo"));
        Assert.IsNull(MailShellFormatting.RowMatchingPrefix([header, bob], "z"));
        var invoice = new ThreadRow
        {
            Id = Guid.NewGuid(),
            Sender = "Carol",
            Subject = "Invoice",
            Preview = string.Empty,
            DateLabel = "t",
        };
        Assert.AreSame(
            invoice,
            MailShellFormatting.RowMatchingPrefix([header, bob, invoice], "in", sort: ThreadListSort.Subject));
        Assert.IsNull(MailShellFormatting.RowMatchingPrefix([header, bob, invoice], "in"));

        var beth = new ThreadRow
        {
            Id = Guid.NewGuid(),
            Sender = "Beth",
            Subject = "s",
            Preview = string.Empty,
            DateLabel = "t",
        };
        var rows = new ThreadRow[] { header, bob, beth, ann };
        var firstB = MailShellFormatting.AdvanceTypeahead(rows, "", 'b', null);
        Assert.AreEqual("b", firstB.Prefix);
        Assert.AreSame(bob, firstB.Row);
        var nextB = MailShellFormatting.AdvanceTypeahead(rows, "b", 'b', bob);
        Assert.AreEqual("b", nextB.Prefix);
        Assert.AreSame(beth, nextB.Row);
        var grown = MailShellFormatting.AdvanceTypeahead(rows, "b", 'o', bob);
        Assert.AreEqual("bo", grown.Prefix);
        Assert.AreSame(bob, grown.Row);
    }

    [TestMethod]
    public void AdvanceNavTypeahead_grows_and_cycles_Mailbox_titles()
    {
        var unified = new ShellNavItem { Kind = ShellNavKind.UnifiedInbox, Title = "Unified Inbox" };
        var inbox = new ShellNavItem { Kind = ShellNavKind.Mailbox, Title = "INBOX", MailboxId = Guid.NewGuid() };
        var archive = new ShellNavItem { Kind = ShellNavKind.Mailbox, Title = "Archive", MailboxId = Guid.NewGuid() };
        var account = new ShellNavItem
        {
            Kind = ShellNavKind.Account,
            Title = "Personal",
            AccountId = Guid.NewGuid(),
            Children = [inbox, archive],
        };
        var flat = MailShellFormatting.FlattenNav([unified, account]);
        Assert.HasCount(4, flat);

        var firstI = MailShellFormatting.AdvanceNavTypeahead(flat, "", 'i', null);
        Assert.AreEqual("i", firstI.Prefix);
        Assert.AreSame(inbox, firstI.Item);
        var grown = MailShellFormatting.AdvanceNavTypeahead(flat, "i", 'n', inbox);
        Assert.AreEqual("in", grown.Prefix);
        Assert.AreSame(inbox, grown.Item);
        var archived = MailShellFormatting.AdvanceNavTypeahead(flat, "", 'a', null);
        Assert.AreSame(archive, archived.Item);

        var path = MailShellFormatting.NavPath([unified, account], archive);
        Assert.HasCount(2, path);
        Assert.AreSame(account, path[0]);
        Assert.AreSame(archive, path[1]);
        Assert.IsEmpty(MailShellFormatting.NavPath([unified, account], new ShellNavItem { Kind = ShellNavKind.Mailbox, Title = "Nope" }));
    }

    [TestMethod]
    public void FilterMailboxes_and_picker_items_match_path_or_label()
    {
        var inbox = new Mailtide.Core.MailboxInfo(Guid.NewGuid(), Guid.NewGuid(), "INBOX", "INBOX", Mailtide.Core.MailboxRole.Inbox);
        var work = new Mailtide.Core.MailboxInfo(Guid.NewGuid(), inbox.AccountId, "Work", "INBOX/Work", null);
        var filtered = MailShellFormatting.FilterMailboxes([inbox, work], "work");
        Assert.AreEqual("Work", filtered.Single().Name);
        Assert.AreEqual("Personal / INBOX / Work", MailShellFormatting.MailboxPickerLabel(work, "Personal"));

        var items = new (Guid Id, string Label)[]
        {
            (inbox.Id, "INBOX"),
            (work.Id, "INBOX / Work"),
        };
        Assert.AreEqual(work.Id, MailShellFormatting.FilterPickerItems(items, "work").Single().Id);
        Assert.HasCount(2, MailShellFormatting.FilterPickerItems(items, " "));
    }

    [TestMethod]
    public void ShouldStartDrag_requires_threshold_distance()
    {
        Assert.IsFalse(MailShellFormatting.ShouldStartDrag(3, 4));
        Assert.IsTrue(MailShellFormatting.ShouldStartDrag(0, MailShellFormatting.ThreadDragThreshold));
        Assert.IsTrue(MailShellFormatting.ShouldStartDrag(6, 6));
    }

    [TestMethod]
    public void SplitQuoted_hides_reply_history_and_keeps_new_text()
    {
        var body = "Thanks" + "\n\n" + "On 2026-01-01 10:00 UTC, Bob wrote:" + "\n\n" + "> old";
        var split = MailShellFormatting.SplitQuoted(body);
        Assert.AreEqual("Thanks", split.Visible);
        Assert.IsTrue(split.HasQuoted);
        StringAssert.Contains(split.Quoted, "wrote:");
        Assert.AreEqual("Thanks", MailShellFormatting.ConversationSnippet(split.Visible));

        var gt = MailShellFormatting.SplitQuoted("Hello" + "\n" + "> quoted");
        Assert.AreEqual("Hello", gt.Visible);
        Assert.AreEqual("> quoted", gt.Quoted);

        var onlyQuote = MailShellFormatting.SplitQuoted("> all quoted");
        Assert.AreEqual("> all quoted", onlyQuote.Visible);
        Assert.IsFalse(onlyQuote.HasQuoted);

        var none = MailShellFormatting.SplitQuoted("just a body");
        Assert.AreEqual("just a body", none.Visible);
        Assert.IsFalse(none.HasQuoted);
    }

    [TestMethod]
    public void FindInConversation_wraps_case_insensitive_hits()
    {
        var first = Card(bodyText: "Hello world");
        var second = Card(bodyText: "Say hello again");
        var hits = MailShellFormatting.FindInConversation([first, second], "HELLO");
        Assert.HasCount(2, hits);
        Assert.AreEqual(0, hits[0].CardIndex);
        Assert.AreEqual(0, hits[0].Offset);
        Assert.AreEqual(5, hits[0].Length);
        Assert.IsTrue(hits[0].InBody);
        Assert.AreEqual(1, hits[1].CardIndex);
        Assert.AreEqual(4, hits[1].Offset);
        Assert.IsTrue(hits[1].InBody);
        var titled = Card(bodyText: "body", subject: "Invoice 99");
        var subjectHits = MailShellFormatting.FindInConversation([titled], "invoice");
        Assert.HasCount(1, subjectHits);
        Assert.IsFalse(subjectHits[0].InBody);
        Assert.AreEqual(0, MailShellFormatting.NextFindIndex(2, -1, true));
        Assert.AreEqual(1, MailShellFormatting.NextFindIndex(2, 0, true));
        Assert.AreEqual(0, MailShellFormatting.NextFindIndex(2, 1, true));
        Assert.AreEqual(1, MailShellFormatting.NextFindIndex(2, 0, false));
        Assert.IsEmpty(MailShellFormatting.FindInConversation([first], " "));
        Assert.AreEqual(-1, MailShellFormatting.NextFindIndex(0, 0, true));

        var htmlOnly = Card(bodyText: "", bodyHtml: "<p>Secret payload</p>");
        var htmlHits = MailShellFormatting.FindInConversation([htmlOnly], "payload");
        Assert.HasCount(1, htmlHits);
        Assert.IsTrue(htmlHits[0].InBody);
        Assert.AreEqual(0, MailShellFormatting.CurrentBodyFindOccurrence(hits, 0));
        Assert.AreEqual(0, MailShellFormatting.CurrentBodyFindOccurrence(hits, 1));
        Assert.AreEqual(-1, MailShellFormatting.CurrentBodyFindOccurrence(subjectHits, 0));
        Assert.AreEqual("invoice", MailShellFormatting.SearchHighlightNeedle("invoice"));
        Assert.AreEqual("hello", MailShellFormatting.SearchHighlightNeedle("in:sent hello"));
        Assert.AreEqual("quarterly report", MailShellFormatting.SearchHighlightNeedle("\"quarterly report\""));
        Assert.IsNull(MailShellFormatting.SearchHighlightNeedle("is:unread"));
        Assert.IsNull(MailShellFormatting.SearchHighlightNeedle("from:bob"));
        Assert.IsNull(MailShellFormatting.SearchHighlightNeedle(""));
    }

    [TestMethod]
    public void WindowTitle_uses_list_header_without_counts()
    {
        Assert.AreEqual("Mailtide", MailShellFormatting.WindowTitle(null));
        Assert.AreEqual("Mailtide", MailShellFormatting.WindowTitle(""));
        Assert.AreEqual("Inbox — Mailtide", MailShellFormatting.WindowTitle("Inbox"));
        Assert.AreEqual("(12) Inbox — Mailtide", MailShellFormatting.WindowTitle("Inbox (12)"));
        Assert.AreEqual("(3) Unified Inbox — Mailtide", MailShellFormatting.WindowTitle("Unified Inbox (3)"));
        Assert.AreEqual("(4) Search results — Mailtide", MailShellFormatting.WindowTitle("Search results (4)"));
        Assert.AreEqual("Draft — Mailtide", MailShellFormatting.ComposeWindowTitle(" "));
        Assert.AreEqual("Hello — Mailtide", MailShellFormatting.ComposeWindowTitle("  Hello  "));
    }

    [TestMethod]
    public void EmptyListCopy_describes_filter_chips()
    {
        Assert.AreEqual("No messages", MailShellFormatting.EmptyListCopy(false, false, ""));
        Assert.AreEqual("No sent messages", MailShellFormatting.EmptyListCopy(false, false, "", MailboxRole.Sent));
        Assert.AreEqual("No drafts", MailShellFormatting.EmptyListCopy(false, false, "", MailboxRole.Drafts));
        Assert.AreEqual("No messages in Trash", MailShellFormatting.EmptyListCopy(false, false, "", MailboxRole.Trash));
        Assert.AreEqual("No junk messages", MailShellFormatting.EmptyListCopy(false, false, "", MailboxRole.Junk));
        Assert.AreEqual("No archived messages", MailShellFormatting.EmptyListCopy(false, false, "", MailboxRole.Archive));
        Assert.AreEqual("No messages in Unified Inbox", MailShellFormatting.EmptyListCopy(false, true, ""));
        Assert.AreEqual("Outbox is empty", MailShellFormatting.EmptyListCopy(true, false, ""));
        Assert.AreEqual("No unread messages", MailShellFormatting.EmptyListCopy(false, false, "is:unread"));
        Assert.AreEqual("No flagged messages", MailShellFormatting.EmptyListCopy(false, false, "is:flagged"));
        Assert.AreEqual("No messages with files", MailShellFormatting.EmptyListCopy(false, false, "has:attachment"));
        Assert.AreEqual("Unread", MailShellFormatting.ChipFilterTitle("is:unread"));
        Assert.AreEqual("Unread, Flagged", MailShellFormatting.ChipFilterTitle("is:unread is:flagged"));
        Assert.AreEqual("Files", MailShellFormatting.ChipFilterTitle("has:attachments"));
        Assert.IsNull(MailShellFormatting.ChipFilterTitle("from:bob is:unread"));
        Assert.AreEqual("No unread, flagged messages", MailShellFormatting.EmptyListCopy(false, false, "is:unread is:flagged"));
        Assert.AreEqual("Unread (3)", MailShellFormatting.WithCount(MailShellFormatting.ChipFilterTitle("is:unread")!, 3));
        Assert.AreEqual("Search results", MailShellFormatting.WithCount("Search results", 0));
        Assert.AreEqual("Search results (4)", MailShellFormatting.WithCount("Search results", 4));
        Assert.AreEqual("1:00 PM", MailShellFormatting.ListRightLabel(ThreadListSort.Newest, "1:00 PM", 2048));
        Assert.AreEqual("2 KB", MailShellFormatting.ListRightLabel(ThreadListSort.Size, "1:00 PM", 2048));
        Assert.AreEqual("1:00 PM", MailShellFormatting.ListRightLabel(ThreadListSort.Size, "1:00 PM", 0));
        Assert.AreEqual("No matching messages", MailShellFormatting.EmptyListCopy(false, false, "from:bob"));
        Assert.AreEqual("No messages that large", MailShellFormatting.EmptyListCopy(false, false, "larger:10M"));
        Assert.AreEqual("No messages that small", MailShellFormatting.EmptyListCopy(false, false, "smaller:1k"));
        Assert.AreEqual("No messages from you", MailShellFormatting.EmptyListCopy(false, false, "from:me"));
        Assert.AreEqual("No messages to you", MailShellFormatting.EmptyListCopy(false, false, "to:me"));
        Assert.AreEqual("No sent messages", MailShellFormatting.EmptyListCopy(false, false, "in:sent"));
        Assert.AreEqual("No drafts", MailShellFormatting.EmptyListCopy(false, false, "in:draft"));
        Assert.AreEqual("No messages in Trash", MailShellFormatting.EmptyListCopy(false, false, "in:bin"));
        Assert.AreEqual("No junk messages", MailShellFormatting.EmptyListCopy(false, false, "in:spam"));
        Assert.AreEqual("No archived messages", MailShellFormatting.EmptyListCopy(false, false, "in:archive"));
        Assert.AreEqual("No messages", MailShellFormatting.EmptyListCopy(false, false, "in:inbox"));
        Assert.AreEqual("No unread messages", MailShellFormatting.EmptyListCopy(false, false, "in:sent is:unread"));
        Assert.AreEqual("No matching messages", MailShellFormatting.EmptyListCopy(false, false, "in:sent from:bob"));
        Assert.AreEqual("No messages in that date range", MailShellFormatting.EmptyListCopy(false, false, "after:2020-01-01"));
        Assert.AreEqual("No messages with that file", MailShellFormatting.EmptyListCopy(false, false, "filename:report.pdf"));
    }

    [TestMethod]
    public void SizeLabel_uses_1024_units()
    {
        Assert.AreEqual("0 B", MailShellFormatting.SizeLabel(0));
        Assert.AreEqual("512 B", MailShellFormatting.SizeLabel(512));
        Assert.AreEqual("1 KB", MailShellFormatting.SizeLabel(1024));
        Assert.AreEqual("1.5 KB", MailShellFormatting.SizeLabel(1536));
        Assert.AreEqual("1 MB", MailShellFormatting.SizeLabel(1024 * 1024));
        Assert.AreEqual("2 GB", MailShellFormatting.SizeLabel(2L * 1024 * 1024 * 1024));
        var tip = MailShellFormatting.DateTip(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), "Jun 1", 2048);
        StringAssert.Contains(tip, "2 KB");
        Assert.AreEqual("Jun 1", MailShellFormatting.DateTip(default, "Jun 1", 0));
    }

    [TestMethod]
    public void Headers_include_from_to_subject_and_date()
    {
        var message = new MessageInfo(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "r",
            "Invoice",
            "Bob <bob@example.com>",
            new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            true)
        {
            ToAddresses = ["alice@example.com"],
            CcAddresses = ["carol@example.com"],
            ReplyToAddresses = ["list@example.com"],
            InternetMessageId = "invoice@example.com",
        };
        var headers = MailShellFormatting.Headers(message);
        StringAssert.Contains(headers, "From: Bob <bob@example.com>");
        StringAssert.Contains(headers, "To: alice@example.com");
        StringAssert.Contains(headers, "Cc: carol@example.com");
        StringAssert.Contains(headers, "Reply-To: list@example.com");
        StringAssert.Contains(headers, "Subject: Invoice");
        StringAssert.Contains(headers, "Date: ");
        StringAssert.Contains(headers, "Message-ID: <invoice@example.com>");
        Assert.DoesNotContain("Bcc:", headers);
        Assert.AreEqual("> Hello", MailShellFormatting.AsQuote("Hello"));
        Assert.AreEqual("> one" + Environment.NewLine + ">" + Environment.NewLine + "> two", MailShellFormatting.AsQuote("one\n\ntwo"));
        Assert.AreEqual(string.Empty, MailShellFormatting.AsQuote("  "));
        var sent = new MessageInfo(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "r", "s", "me@example.com", DateTimeOffset.UtcNow, true)
        {
            ToAddresses = ["bob@example.com"],
        };
        var sentRow = new ThreadRow
        {
            Id = sent.Id,
            Sender = "bob@example.com",
            FromAddress = "bob@example.com",
            Subject = "s",
            Preview = "",
            DateLabel = "t",
            Thread = new MessageThreadInfo(sent, [sent]),
        };
        Assert.AreEqual("from:me@example.com", MailShellFormatting.FromSearchQuery(MailShellFormatting.ThreadSenderAddress(sentRow)));
        Assert.AreEqual("from:bob@example.com", MailShellFormatting.FromSearchQuery("Bob <bob@example.com>"));
        Assert.AreEqual("from:bob@example.com", MailShellFormatting.FromSearchQuery("bob@example.com"));
        Assert.AreEqual(string.Empty, MailShellFormatting.FromSearchQuery(" "));
        Assert.AreEqual("to:alice@example.com", MailShellFormatting.ToSearchQuery("Alice <alice@example.com>"));
        Assert.AreEqual("to:alice@example.com", MailShellFormatting.ToSearchQuery(new[] { "alice@example.com", "carol@example.com" }));
        Assert.AreEqual(string.Empty, MailShellFormatting.ToSearchQuery(Array.Empty<string>()));
        Assert.AreEqual("<keep@example.com>", MailShellFormatting.FormatMessageId("keep@example.com"));
        Assert.AreEqual("<keep@example.com>", MailShellFormatting.FormatMessageId("<keep@example.com>"));
        Assert.AreEqual(string.Empty, MailShellFormatting.FormatMessageId(null));
    }

    [TestMethod]
    public void NavPersistenceKey_is_stable_for_each_kind()
    {
        var accountId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var mailboxId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.AreEqual("unified", MailShellFormatting.NavPersistenceKey(new ShellNavItem
        {
            Kind = ShellNavKind.UnifiedInbox,
            Title = "Unified Inbox",
        }));
        Assert.AreEqual("account:" + accountId.ToString("D"), MailShellFormatting.NavPersistenceKey(new ShellNavItem
        {
            Kind = ShellNavKind.Account,
            Title = "Personal",
            AccountId = accountId,
        }));
        Assert.AreEqual("mailbox:" + mailboxId.ToString("D"), MailShellFormatting.NavPersistenceKey(new ShellNavItem
        {
            Kind = ShellNavKind.Mailbox,
            Title = "INBOX",
            AccountId = accountId,
            MailboxId = mailboxId,
        }));
        Assert.AreEqual("favorites", MailShellFormatting.NavPersistenceKey(new ShellNavItem
        {
            Kind = ShellNavKind.Favorites,
            Title = "Favorites",
        }));
    }

    [TestMethod]
    public void IsCurrentNav_matches_the_selected_Mailbox_or_view()
    {
        var accountId = Guid.NewGuid();
        var inboxId = Guid.NewGuid();
        var sentId = Guid.NewGuid();
        var unified = new ShellNavItem { Kind = ShellNavKind.UnifiedInbox, Title = "Unified Inbox" };
        var inbox = new ShellNavItem
        {
            Kind = ShellNavKind.Mailbox,
            Title = "INBOX",
            AccountId = accountId,
            MailboxId = inboxId,
        };
        var sent = new ShellNavItem
        {
            Kind = ShellNavKind.Mailbox,
            Title = "Sent",
            AccountId = accountId,
            MailboxId = sentId,
        };
        var account = new ShellNavItem
        {
            Kind = ShellNavKind.Account,
            Title = "Personal",
            AccountId = accountId,
        };
        var outbox = new ShellNavItem
        {
            Kind = ShellNavKind.Outbox,
            Title = "Outbox",
            AccountId = accountId,
        };

        Assert.IsTrue(MailShellFormatting.IsCurrentNav(unified, true, false, null, null, null));
        Assert.IsFalse(MailShellFormatting.IsCurrentNav(unified, false, false, accountId, inboxId, inboxId));
        Assert.IsTrue(MailShellFormatting.IsCurrentNav(inbox, false, false, accountId, inboxId, inboxId));
        Assert.IsFalse(MailShellFormatting.IsCurrentNav(sent, false, false, accountId, inboxId, inboxId));
        Assert.IsTrue(MailShellFormatting.IsCurrentNav(account, false, false, accountId, inboxId, inboxId));
        Assert.IsFalse(MailShellFormatting.IsCurrentNav(account, false, false, accountId, sentId, inboxId));
        Assert.IsTrue(MailShellFormatting.IsCurrentNav(outbox, false, true, accountId, null, inboxId));
        Assert.IsFalse(MailShellFormatting.IsCurrentNav(outbox, false, true, Guid.NewGuid(), null, inboxId));
        Assert.IsTrue(MailShellFormatting.IsCurrentNav(
            new ShellNavItem { Kind = ShellNavKind.Favorites, Title = "Favorites" },
            false, false, accountId, inboxId, inboxId));
    }

    [TestMethod]
    public void FindSelectedNav_prefers_the_Mailbox_over_its_Favorite_pin()
    {
        var accountId = Guid.NewGuid();
        var inboxId = Guid.NewGuid();
        var pin = new ShellNavItem
        {
            Kind = ShellNavKind.Mailbox,
            Title = "INBOX",
            AccountId = accountId,
            MailboxId = inboxId,
            IsFavoritePin = true,
        };
        var inbox = new ShellNavItem
        {
            Kind = ShellNavKind.Mailbox,
            Title = "INBOX",
            AccountId = accountId,
            MailboxId = inboxId,
        };
        var favorites = new ShellNavItem
        {
            Kind = ShellNavKind.Favorites,
            Title = "Favorites",
            Children = [pin],
        };
        var account = new ShellNavItem
        {
            Kind = ShellNavKind.Account,
            Title = "Personal",
            AccountId = accountId,
            Children = [inbox],
        };
        ShellNavItem[] roots = [favorites, account];
        Assert.AreSame(inbox, MailShellFormatting.FindSelectedNav(roots, false, false, accountId, inboxId));
        Assert.AreSame(pin, MailShellFormatting.FindSelectedNav(roots, false, false, accountId, inboxId, pin));
        Assert.AreSame(inbox, MailShellFormatting.FindSelectedNav(roots, false, false, accountId, inboxId, inbox));
    }

    [TestMethod]
    public void IsCurrentListRow_matches_selected_Thread()
    {
        var accountId = Guid.NewGuid();
        var mailboxId = Guid.NewGuid();
        var row = Thread(accountId, mailboxId);
        Assert.IsTrue(MailShellFormatting.IsCurrentListRow(row, row.Id, null));
        Assert.IsFalse(MailShellFormatting.IsCurrentListRow(row, Guid.NewGuid(), null));
        var hit = new MessageInfo(
            Guid.NewGuid(), accountId, mailboxId, "r", "s", "a@b.com", DateTimeOffset.UtcNow, true);
        var searchRow = new ThreadRow
        {
            Id = hit.Id,
            SearchHit = hit,
            Sender = "Bob",
            Subject = "s",
            Preview = "",
            DateLabel = "t",
        };
        Assert.IsTrue(MailShellFormatting.IsCurrentListRow(searchRow, null, hit.Id));
        Assert.IsFalse(MailShellFormatting.IsCurrentListRow(searchRow, null, Guid.NewGuid()));
    }

    [TestMethod]
    public void ObjectMenuOutboxOverride_hides_message_verbs_in_Outbox()
    {
        Assert.IsTrue(MailShellFormatting.ObjectMenuOutboxOverride("Retry", true));
        Assert.IsTrue(MailShellFormatting.ObjectMenuOutboxOverride("Discard", true));
        Assert.IsFalse(MailShellFormatting.ObjectMenuOutboxOverride("Copy", true));
        Assert.IsFalse(MailShellFormatting.ObjectMenuOutboxOverride("Archive", true));
        Assert.IsTrue(MailShellFormatting.ObjectMenuOutboxOverride("Archive", false));
        Assert.IsTrue(MailShellFormatting.ObjectMenuOutboxOverride("Copy", false));
        Assert.IsFalse(MailShellFormatting.ObjectMenuOutboxOverride("Reply", true));
        Assert.IsFalse(MailShellFormatting.ObjectMenuOutboxOverride("Edit as New", true));
        Assert.IsFalse(MailShellFormatting.ObjectMenuOutboxOverride("Save as .eml", true));
        Assert.IsFalse(MailShellFormatting.ObjectMenuOutboxOverride("Copy headers", true));
        Assert.IsTrue(MailShellFormatting.ObjectMenuOutboxOverride("Copy headers", false));
        Assert.IsFalse(MailShellFormatting.ObjectMenuOutboxOverride("Show original", true));
        Assert.IsTrue(MailShellFormatting.ObjectMenuOutboxOverride("Show original", false));
        Assert.IsFalse(MailShellFormatting.ObjectMenuOutboxOverride("Copy Message-ID", true));
        Assert.IsTrue(MailShellFormatting.ObjectMenuOutboxOverride("Copy Message-ID", false));
        Assert.IsFalse(MailShellFormatting.ObjectMenuOutboxOverride("Search from sender", true));
        Assert.IsTrue(MailShellFormatting.ObjectMenuOutboxOverride("Search from sender", false));
        Assert.IsFalse(MailShellFormatting.ObjectMenuOutboxOverride("Search to recipient", true));
        Assert.IsTrue(MailShellFormatting.ObjectMenuOutboxOverride("Search to recipient", false));
        Assert.IsTrue(MailShellFormatting.ObjectMenuOutboxOverride("Reply", false));
        Assert.IsFalse(MailShellFormatting.ObjectMenuOutboxOverride("Retry", false));
        Assert.IsNull(MailShellFormatting.ObjectMenuOutboxOverride("Mark Unread", true));
        Assert.IsNull(MailShellFormatting.ObjectMenuOutboxOverride("Expand all", true));
        Assert.IsTrue(MailShellFormatting.ObjectMenuConversationOverride("Expand all", true, false));
        Assert.IsFalse(MailShellFormatting.ObjectMenuConversationOverride("Expand all", false, false));
        Assert.IsFalse(MailShellFormatting.ObjectMenuConversationOverride("Expand all", true, true));
        Assert.IsTrue(MailShellFormatting.ObjectMenuConversationOverride("Collapse all", true, true));
        Assert.IsFalse(MailShellFormatting.ObjectMenuConversationOverride("Collapse all", true, false));
        Assert.IsNull(MailShellFormatting.ObjectMenuConversationOverride("Reply", true, false));
    }

    private static ThreadRow Thread(Guid accountId, Guid mailboxId)
    {
        var message = new MessageInfo(
            Guid.NewGuid(),
            accountId,
            mailboxId,
            "r",
            "s",
            "bob@example.com",
            DateTimeOffset.UtcNow,
            true);
        return new ThreadRow
        {
            Id = message.Id,
            Thread = new MessageThreadInfo(message, [message]),
            Sender = "Bob",
            Subject = "s",
            Preview = string.Empty,
            DateLabel = "t",
        };
    }

    private static ShellNavItem Nav(
        ShellNavKind kind,
        Guid? accountId,
        Guid? mailboxId,
        MailboxRole? role) =>
        new()
        {
            Kind = kind,
            Title = "n",
            AccountId = accountId,
            MailboxId = mailboxId,
            Role = role,
        };

    private static ConversationCard Card(
        bool selected = false,
        string bodyText = "",
        string? bodyHtml = null,
        bool unavailable = false,
        bool remote = false,
        bool flagged = false,
        string subject = "s",
        bool conversationExpanded = false,
        bool isRead = true,
        IReadOnlyList<AttachmentInfo>? attachments = null,
        DateTimeOffset? receivedAt = null,
        string? searchNeedle = null) =>
        new()
        {
            Message = new MessageInfo(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "r",
                subject,
                "bob@example.com",
                receivedAt ?? DateTimeOffset.UtcNow,
                isRead,
                flagged),
            Sender = "Bob",
            DateLabel = "1:00 PM",
            IsSelected = selected,
            ConversationExpanded = conversationExpanded,
            ToLine = "To: alice@example.com",
            CcLine = "Cc: cc@example.com",
            BodyText = bodyText,
            BodyHtml = bodyHtml,
            SearchNeedle = searchNeedle,
            BodyUnavailable = unavailable,
            HasRemoteImages = remote,
            Attachments = attachments ?? [],
        };
}
