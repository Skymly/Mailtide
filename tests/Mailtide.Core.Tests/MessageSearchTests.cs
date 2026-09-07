using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MessageSearchTests
{
    [TestMethod]
    public void Parse_strips_flagged_and_unread_operators()
    {
        var parsed = MessageSearch.Parse("is:flagged invoice");
        Assert.IsTrue(parsed.FlaggedOnly);
        Assert.IsTrue(MessageSearch.Parse("is:starred").FlaggedOnly);
        Assert.IsTrue(MessageSearch.Parse("has:attach").AttachmentOnly);
        Assert.IsFalse(parsed.UnreadOnly);
        Assert.IsNull(parsed.FromContains);
        Assert.AreEqual("invoice", parsed.Text);

        parsed = MessageSearch.Parse("IS:UNREAD keep");
        Assert.IsFalse(parsed.FlaggedOnly);
        Assert.IsTrue(parsed.UnreadOnly);
        Assert.AreEqual("keep", parsed.Text);

        parsed = MessageSearch.Parse("is:unread is:flagged");
        Assert.IsTrue(parsed.FlaggedOnly);
        Assert.IsTrue(parsed.UnreadOnly);
        Assert.AreEqual(string.Empty, parsed.Text);
        Assert.IsTrue(MessageSearch.Parse("is:failed").FailedOnly);
        Assert.AreEqual("is:failed", MessageSearch.ToggleFailed(""));
        Assert.AreEqual(string.Empty, MessageSearch.ToggleFailed("is:failed"));

        parsed = MessageSearch.Parse("from:bob@example.com to:alice cc:erin bcc:pat subject:Hello has:attachment invoice");
        Assert.AreEqual("bob@example.com", parsed.FromContains);
        Assert.AreEqual("alice", parsed.ToContains);
        Assert.AreEqual("erin", parsed.CcContains);
        Assert.AreEqual("pat", parsed.BccContains);
        Assert.AreEqual("Hello", parsed.SubjectContains);
        Assert.IsTrue(parsed.AttachmentOnly);
        Assert.AreEqual("invoice", parsed.Text);

        parsed = MessageSearch.Parse("is:read keep");
        Assert.IsTrue(parsed.ReadOnly);
        Assert.IsFalse(parsed.UnreadOnly);
        Assert.AreEqual("keep", parsed.Text);

        parsed = MessageSearch.Parse("after:2026-04-01 before:2026-04-03 invoice");
        Assert.AreEqual(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), parsed.After);
        Assert.AreEqual(new DateTimeOffset(2026, 4, 3, 0, 0, 0, TimeSpan.Zero), parsed.Before);
        Assert.AreEqual("invoice", parsed.Text);

        parsed = MessageSearch.Parse("older_than:7d invoice");
        Assert.AreEqual("invoice", parsed.Text);
        Assert.IsNotNull(parsed.Before);
        Assert.IsTrue(Math.Abs((parsed.Before.Value - DateTimeOffset.UtcNow.AddDays(-7)).TotalMinutes) < 2);
        parsed = MessageSearch.Parse("newer_than:2w");
        Assert.IsNotNull(parsed.After);
        Assert.IsTrue(Math.Abs((parsed.After.Value - DateTimeOffset.UtcNow.AddDays(-14)).TotalMinutes) < 2);
        Assert.IsTrue(MessageSearch.TryParseRelativeAge("1m", out var month));
        Assert.AreEqual(TimeSpan.FromDays(30), month);
        Assert.IsFalse(MessageSearch.TryParseRelativeAge("7", out _));

        parsed = MessageSearch.Parse("in:sent hello");
        Assert.AreEqual(MailboxRole.Sent, parsed.InRole);
        Assert.AreEqual("hello", parsed.Text);
        Assert.IsFalse(parsed.InAnywhere);
        Assert.AreEqual(MailboxRole.Junk, MessageSearch.Parse("in:spam").InRole);
        Assert.AreEqual(MailboxRole.Trash, MessageSearch.Parse("in:bin").InRole);
        Assert.AreEqual(MailboxRole.Trash, MessageSearch.Parse("in:deleted").InRole);
        Assert.AreEqual(MailboxRole.Drafts, MessageSearch.Parse("in:draft").InRole);
        Assert.AreEqual(MailboxRole.Archive, MessageSearch.Parse("in:archive").InRole);
        Assert.AreEqual(MailboxRole.Archive, MessageSearch.Parse("is:archive").InRole);
        Assert.AreEqual("Work", MessageSearch.Parse("in:Work invoice").InMailboxContains);
        Assert.AreEqual("invoice", MessageSearch.Parse("in:Work invoice").Text);
        Assert.AreEqual("INBOX/Work", MessageSearch.Parse("label:\"INBOX/Work\"").InMailboxContains);
        Assert.IsNull(MessageSearch.Parse("-in:Work").InMailboxContains);
        Assert.AreEqual(MailboxRole.Sent, MessageSearch.Parse("is:sent").InRole);
        Assert.AreEqual(MailboxRole.Inbox, MessageSearch.Parse("is:inbox").InRole);
        Assert.AreEqual(MailboxRole.Drafts, MessageSearch.Parse("is:draft").InRole);
        Assert.AreEqual(MailboxRole.Junk, MessageSearch.Parse("is:spam").InRole);
        Assert.AreEqual(MailboxRole.Trash, MessageSearch.Parse("label:trash").InRole);
        Assert.IsTrue(MessageSearch.Parse("has:attachments").AttachmentOnly);

        parsed = MessageSearch.Parse("in:anywhere invoice");
        Assert.IsTrue(parsed.InAnywhere);
        Assert.IsNull(parsed.InRole);
        Assert.AreEqual("invoice", parsed.Text);
        Assert.IsTrue(MessageSearch.Parse("in:all").InAnywhere);
        Assert.IsTrue(MessageSearch.Parse("in:any").InAnywhere);

        parsed = MessageSearch.Parse("filename:invoice.pdf keep");
        Assert.AreEqual("invoice.pdf", parsed.FilenameContains);
        Assert.AreEqual("keep", parsed.Text);
        Assert.AreEqual("notes.txt", MessageSearch.Parse("file:notes.txt").FilenameContains);

        parsed = MessageSearch.Parse("\"quarterly report\" is:unread");
        Assert.AreEqual("quarterly report", parsed.Text);
        Assert.IsTrue(parsed.UnreadOnly);

        parsed = MessageSearch.Parse("from:\"Bob Smith\" subject:\"Q3 invoice\"");
        Assert.AreEqual("Bob Smith", parsed.FromContains);
        Assert.AreEqual("Q3 invoice", parsed.SubjectContains);
        Assert.AreEqual(string.Empty, parsed.Text);

        parsed = MessageSearch.Parse("-from:notifications -invoice");
        Assert.AreEqual("notifications", parsed.FromExclude);
        CollectionAssert.AreEqual(new[] { "invoice" }, parsed.TextExcludes!.ToArray());

        parsed = MessageSearch.Parse("-cc:erin -bcc:pat");
        Assert.AreEqual("erin", parsed.CcExclude);
        Assert.AreEqual("pat", parsed.BccExclude);

        parsed = MessageSearch.Parse("-is:unread");
        Assert.IsTrue(parsed.ReadOnly);
        Assert.IsFalse(parsed.UnreadOnly);

        parsed = MessageSearch.Parse("-has:attachment");
        Assert.IsTrue(parsed.WithoutAttachment);

        parsed = MessageSearch.Parse("-is:flagged");
        Assert.IsTrue(parsed.NotFlagged);
        Assert.IsFalse(parsed.FlaggedOnly);
        Assert.IsTrue(MessageSearch.Parse("-is:starred").NotFlagged);

        parsed = MessageSearch.Parse("on:2026-04-01 invoice");
        Assert.AreEqual(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), parsed.After);
        Assert.AreEqual(new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero), parsed.Before);
        Assert.AreEqual("invoice", parsed.Text);

        parsed = MessageSearch.Parse("on:today");
        Assert.AreEqual(DateTime.Now.Date, parsed.After!.Value.LocalDateTime.Date);
        Assert.AreEqual(DateTime.Now.Date.AddDays(1), parsed.Before!.Value.LocalDateTime.Date);
        parsed = MessageSearch.Parse("after:yesterday");
        Assert.AreEqual(DateTime.Now.Date.AddDays(-1), parsed.After!.Value.LocalDateTime.Date);
        parsed = MessageSearch.Parse("before:tomorrow");
        Assert.AreEqual(DateTime.Now.Date.AddDays(1), parsed.Before!.Value.LocalDateTime.Date);

        parsed = MessageSearch.Parse("on:thisweek");
        Assert.AreEqual(DayOfWeek.Monday, parsed.After!.Value.DayOfWeek);
        Assert.AreEqual(7, (parsed.Before!.Value - parsed.After.Value).TotalDays, 0.001);
        parsed = MessageSearch.Parse("on:lastweek");
        Assert.AreEqual(DayOfWeek.Monday, parsed.After!.Value.DayOfWeek);
        Assert.AreEqual(7, (parsed.Before!.Value - parsed.After.Value).TotalDays, 0.001);
        Assert.IsTrue(parsed.After.Value < MessageSearch.Parse("on:thisweek").After);
        parsed = MessageSearch.Parse("after:thisweek");
        Assert.AreEqual(DayOfWeek.Monday, parsed.After!.Value.DayOfWeek);
        Assert.IsNull(parsed.Before);

        parsed = MessageSearch.Parse("on:thismonth");
        Assert.AreEqual(1, parsed.After!.Value.Day);
        Assert.AreEqual(DateTime.Now.Month, parsed.After.Value.Month);
        Assert.AreEqual(parsed.After.Value.AddMonths(1), parsed.Before);
        parsed = MessageSearch.Parse("on:lastmonth");
        Assert.AreEqual(1, parsed.After!.Value.Day);
        Assert.AreEqual(DateTime.Now.AddMonths(-1).Month, parsed.After.Value.Month);
        parsed = MessageSearch.Parse("on:thisyear");
        Assert.AreEqual(1, parsed.After!.Value.Month);
        Assert.AreEqual(1, parsed.After.Value.Day);
        Assert.AreEqual(DateTime.Now.Year, parsed.After.Value.Year);
        Assert.AreEqual(parsed.After.Value.AddYears(1), parsed.Before);
        parsed = MessageSearch.Parse("on:lastyear");
        Assert.AreEqual(DateTime.Now.Year - 1, parsed.After!.Value.Year);

        parsed = MessageSearch.Parse("rfc822msgid:<keep@example.com>");
        Assert.AreEqual("<keep@example.com>", parsed.MessageIdContains);
        Assert.AreEqual("keep@example.com", MessageSearch.Parse("msgid:keep@example.com").MessageIdContains);
        Assert.IsNull(MessageSearch.Parse("-msgid:keep@example.com").MessageIdContains);

        parsed = MessageSearch.Parse("from:me to:me invoice");
        Assert.IsTrue(parsed.FromMe);
        Assert.IsTrue(parsed.ToMe);
        Assert.AreEqual("invoice", parsed.Text);
        Assert.IsTrue(MessageSearch.Parse("cc:me").ToMe);
        Assert.IsFalse(MessageSearch.Parse("-from:me").FromMe);
        Assert.AreEqual("bob", MessageSearch.Parse("from:bob").FromContains);

        parsed = MessageSearch.Parse("larger:10M invoice");
        Assert.AreEqual(10L * 1024 * 1024, parsed.LargerThan);
        Assert.AreEqual("invoice", parsed.Text);
        Assert.AreEqual(1024L, MessageSearch.Parse("larger:1kb").LargerThan);
        Assert.AreEqual(2L * 1024 * 1024, MessageSearch.Parse("size:2m").LargerThan);
        Assert.AreEqual(500L, MessageSearch.Parse("smaller:500").SmallerThan);
        Assert.IsNull(MessageSearch.Parse("-larger:10M").LargerThan);
        Assert.IsTrue(MessageSearch.TryParseByteSize("10MB", out var tenMb));
        Assert.AreEqual(10L * 1024 * 1024, tenMb);
        Assert.IsFalse(MessageSearch.TryParseByteSize("nope", out _));
    }

    [TestMethod]
    public void OrClauses_splits_on_uppercase_OR_and_keeps_quoted_phrases()
    {
        CollectionAssert.AreEqual(new[] { "from:bob", "from:alice" }, MessageSearch.OrClauses("from:bob OR from:alice").ToArray());
        CollectionAssert.AreEqual(new[] { "invoice receipt" }, MessageSearch.OrClauses("invoice receipt").ToArray());
        Assert.AreEqual(1, MessageSearch.OrClauses("\"bob OR alice\"").Count);
        CollectionAssert.AreEqual(new[] { "invoice or receipt" }, MessageSearch.OrClauses("invoice or receipt").ToArray());
        CollectionAssert.AreEqual(new[] { "from:bob", "subject:Hello keep" }, MessageSearch.OrClauses("from:bob OR subject:Hello keep").ToArray());
    }

    [TestMethod]
    public void Matches_OR_unions_clauses()
    {
        Assert.IsTrue(MessageSearch.Matches(false, true, "Hi", "bob@example.com", "x", null, "from:bob OR from:alice"));
        Assert.IsTrue(MessageSearch.Matches(false, true, "Hi", "alice@example.com", "x", null, "from:bob OR from:alice"));
        Assert.IsFalse(MessageSearch.Matches(false, true, "Hi", "carol@example.com", "x", null, "from:bob OR from:alice"));
        Assert.IsTrue(MessageSearch.Matches(false, true, "Invoice", "a@b.com", "pay", null, "invoice OR receipt"));
        Assert.IsTrue(MessageSearch.Matches(false, true, "Receipt", "a@b.com", "pay", null, "invoice OR receipt"));
        Assert.IsFalse(MessageSearch.Matches(false, true, "Hello", "a@b.com", "pay", null, "invoice OR receipt"));
        Assert.IsFalse(MessageSearch.Matches(false, true, "Invoice", "a@b.com", "pay", null, "invoice or receipt"));
    }

    [TestMethod]
    public void Matches_msgid_ignores_angle_brackets()
    {
        Assert.IsTrue(MessageSearch.MatchesMessageId("<keep@example.com>", "keep@example.com"));
        Assert.IsTrue(MessageSearch.MatchesMessageId("keep@example.com", "<KEEP@example.com>"));
        Assert.IsFalse(MessageSearch.MatchesMessageId("<other@example.com>", "keep@example.com"));
        Assert.IsTrue(MessageSearch.Matches(
            false, true, "Hi", "a@b.com", "x", null, "msgid:keep@example.com", internetMessageId: "<keep@example.com>"));
        Assert.IsFalse(MessageSearch.Matches(
            false, true, "Hi", "a@b.com", "x", null, "rfc822msgid:keep@example.com", internetMessageId: "<other@example.com>"));
        Assert.IsTrue(MessageSearch.Matches(
            false, true, "Hi", "Alice <alice@example.com>", "x", null, "from:me", selfAddresses: ["alice@example.com"]));
        Assert.IsFalse(MessageSearch.Matches(
            false, true, "Hi", "bob@example.com", "x", null, "from:me", selfAddresses: ["alice@example.com"]));
        Assert.IsTrue(MessageSearch.Matches(
            false, true, "Hi", "bob@example.com", "x", null, "to:me", toAddresses: "alice@example.com", selfAddresses: ["alice@example.com"]));
        Assert.IsTrue(MessageSearch.Matches(
            false, true, "Hi", "a@b.com", "x", null, "larger:1M", sizeBytes: 2 * 1024 * 1024));
        Assert.IsFalse(MessageSearch.Matches(
            false, true, "Hi", "a@b.com", "x", null, "larger:1M", sizeBytes: 100));
        Assert.IsTrue(MessageSearch.Matches(
            false, true, "Hi", "a@b.com", "x", null, "smaller:1k", sizeBytes: 10));
        Assert.IsFalse(MessageSearch.Matches(
            false, true, "Hi", "a@b.com", "x", null, "smaller:1k", sizeBytes: 2048));
    }

    [TestMethod]
    public void ToggleUnread_adds_and_removes_the_operator_without_touching_other_tokens()
    {
        Assert.AreEqual("is:unread", MessageSearch.ToggleUnread(""));
        Assert.AreEqual("is:unread", MessageSearch.ToggleUnread("   "));
        Assert.AreEqual(string.Empty, MessageSearch.ToggleUnread("is:unread"));
        Assert.AreEqual("from:bob is:flagged", MessageSearch.ToggleUnread("from:bob is:unread is:flagged"));
        Assert.AreEqual("from:bob is:unread", MessageSearch.ToggleUnread("from:bob"));
        Assert.AreEqual("is:flagged", MessageSearch.ToggleFlagged(""));
        Assert.AreEqual("from:bob", MessageSearch.ToggleFlagged("from:bob is:flagged"));
        Assert.AreEqual("is:unread is:flagged", MessageSearch.ToggleFlagged("is:unread"));
        Assert.AreEqual("has:attachment", MessageSearch.ToggleAttachment(""));
        Assert.AreEqual(string.Empty, MessageSearch.ToggleAttachment("has:attach"));
        Assert.AreEqual(string.Empty, MessageSearch.ToggleAttachment("has:attachments"));
        Assert.AreEqual("from:bob has:attachment", MessageSearch.ToggleAttachment("from:bob"));
        Assert.AreEqual("from:\"Bob Smith\" is:unread", MessageSearch.ToggleUnread("from:\"Bob Smith\""));
        Assert.AreEqual("from:\"Bob Smith\"", MessageSearch.ToggleUnread("from:\"Bob Smith\" is:unread"));
        Assert.IsTrue(MessageSearch.IsChipFilterOnly("is:unread"));
        Assert.IsTrue(MessageSearch.IsChipFilterOnly("is:unread is:flagged has:attachment"));
        Assert.IsFalse(MessageSearch.IsChipFilterOnly("is:unread invoice"));
        Assert.IsFalse(MessageSearch.IsChipFilterOnly("from:bob"));
        Assert.AreEqual("is:unread", MessageSearch.KeepChipFilter("is:unread", outbox: false));
        Assert.IsNull(MessageSearch.KeepChipFilter("is:unread", outbox: true));
        Assert.AreEqual("is:failed", MessageSearch.KeepChipFilter("is:failed", outbox: true));
        Assert.IsNull(MessageSearch.KeepChipFilter("is:failed", outbox: false));
        Assert.IsNull(MessageSearch.KeepChipFilter("from:bob is:unread", outbox: false));
    }

    [TestMethod]
    public void Matches_unread_operator_ignores_read()
    {
        Assert.IsTrue(MessageSearch.Matches(false, false, "Invoice", "a@b.com", "pay", null, "is:unread"));
        Assert.IsFalse(MessageSearch.Matches(false, true, "Invoice", "a@b.com", "pay", null, "is:unread"));
        Assert.IsTrue(MessageSearch.Matches(true, false, "Invoice", "a@b.com", "pay", null, "is:unread is:flagged"));
        Assert.IsFalse(MessageSearch.Matches(false, false, "Invoice", "a@b.com", "pay", null, "is:unread is:flagged"));
        Assert.IsTrue(MessageSearch.Matches(false, true, "Invoice", "a@b.com", "pay", null, "invoice"));
        Assert.IsTrue(MessageSearch.Matches(false, true, "Invoice", "a@b.com", "pay", null, "is:read"));
        Assert.IsFalse(MessageSearch.Matches(false, false, "Invoice", "a@b.com", "pay", null, "is:read"));
        Assert.IsFalse(MessageSearch.Matches(
            false, true, "Invoice", "notify@example.com", "pay", null, "-from:notify"));
        Assert.IsTrue(MessageSearch.Matches(
            false, true, "Hello", "bob@example.com", "pay", null, "-from:notify"));
        Assert.IsFalse(MessageSearch.Matches(
            false, true, "Hi", "a@b.com", "x", null, "-cc:erin", ccAddresses: "erin@example.com"));
        Assert.IsTrue(MessageSearch.Matches(
            false, true, "Hi", "a@b.com", "x", null, "-cc:erin", ccAddresses: "pat@example.com"));
        Assert.IsFalse(MessageSearch.Matches(
            false, true, "Invoice due", "bob@example.com", "pay", null, "-invoice"));
        Assert.IsTrue(MessageSearch.Matches(
            false, true, "Hello", "bob@example.com", "pay", null, "-invoice"));
        Assert.IsFalse(MessageSearch.Matches(true, true, "Hi", "a@b.com", "x", null, "-is:flagged"));
        Assert.IsTrue(MessageSearch.Matches(false, true, "Hi", "a@b.com", "x", null, "-is:flagged"));
    }

    [TestMethod]
    public async Task SearchMessages_is_unread_filters_current_Mailbox()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            Message("1", "New", flagged: false, read: false, body: "keep"),
            Message("2", "Old", flagged: false, read: true, body: "keep"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var unread = await app.SearchMessagesAsync(account.Id, mailboxId, "is:unread");
        Assert.AreEqual("New", unread.Single().Subject);

        var read = await app.SearchMessagesAsync(account.Id, mailboxId, "is:read");
        Assert.AreEqual("Old", read.Single().Subject);

        var keep = await app.SearchMessagesAsync(account.Id, mailboxId, "keep");
        Assert.HasCount(2, keep);
    }

    [TestMethod]
    public async Task SearchMessages_is_flagged_filters_current_Mailbox()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            Message("1", "Star me", flagged: true, body: "keep"),
            Message("2", "Ordinary", flagged: false, body: "keep"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var flagged = await app.SearchMessagesAsync(account.Id, mailboxId, "is:flagged");
        Assert.AreEqual("Star me", flagged.Single().Subject);

        var flaggedKeep = await app.SearchMessagesAsync(account.Id, mailboxId, "is:flagged keep");
        Assert.AreEqual("Star me", flaggedKeep.Single().Subject);

        var keep = await app.SearchMessagesAsync(account.Id, mailboxId, "keep");
        Assert.HasCount(2, keep);
    }

    [TestMethod]
    public async Task SearchMessages_from_me_and_to_me_use_Account_address()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "mine",
                Subject: "Sent-like",
                FromAddress: "Alice <alice@example.com>",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "mine")
            {
                ToAddresses = ["bob@example.com"],
            },
            new RemoteMessage(
                RemoteId: "theirs",
                Subject: "To me",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 13, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "hello")
            {
                ToAddresses = ["alice@example.com"],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var fromMe = await app.SearchMessagesAsync(account.Id, mailboxId, "from:me");
        Assert.AreEqual("Sent-like", fromMe.Single().Subject);
        var toMe = await app.SearchMessagesAsync(account.Id, mailboxId, "to:me");
        Assert.AreEqual("To me", toMe.Single().Subject);
    }

    [TestMethod]
    public async Task SearchMessages_from_filters_sender()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "1",
                Subject: "Hello",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "keep"),
            new RemoteMessage(
                RemoteId: "2",
                Subject: "Hello",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "keep"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var fromBob = await app.SearchMessagesAsync(account.Id, mailboxId, "from:bob");
        Assert.AreEqual("bob@example.com", fromBob.Single().FromAddress);

        var helloFromCarol = await app.SearchMessagesAsync(account.Id, mailboxId, "from:carol Hello");
        Assert.AreEqual("carol@example.com", helloFromCarol.Single().FromAddress);

        var either = await app.SearchMessagesAsync(account.Id, mailboxId, "from:bob OR from:carol");
        CollectionAssert.AreEquivalent(
            new[] { "bob@example.com", "carol@example.com" },
            either.Select(item => item.FromAddress).ToArray());
    }

    [TestMethod]
    public async Task SearchMessages_subject_and_to_operators()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "1",
                Subject: "Invoice 99",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "pay")
            {
                ToAddresses = ["alice@example.com"],
            },
            new RemoteMessage(
                RemoteId: "2",
                Subject: "Hello",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "hi")
            {
                ToAddresses = ["dave@example.com"],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var bySubject = await app.SearchMessagesAsync(account.Id, mailboxId, "subject:Invoice");
        Assert.AreEqual("Invoice 99", bySubject.Single().Subject);

        var byTo = await app.SearchMessagesAsync(account.Id, mailboxId, "to:alice");
        Assert.AreEqual("Invoice 99", byTo.Single().Subject);
    }

    [TestMethod]
    public async Task SearchMessages_cc_filters_copied_recipients()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "1",
                Subject: "Copied",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "cc me")
            {
                CcAddresses = ["erin@example.com"],
            },
            new RemoteMessage(
                RemoteId: "2",
                Subject: "Direct",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "no cc"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var copied = await app.SearchMessagesAsync(account.Id, mailboxId, "cc:erin");
        Assert.AreEqual("Copied", copied.Single().Subject);
    }

    [TestMethod]
    public async Task SearchMessages_bcc_matches_blind_copies()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "b1",
                Subject: "Blind",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "secret")
            {
                BccAddresses = ["pat@example.com"],
            },
            new RemoteMessage(
                RemoteId: "b2",
                Subject: "Open",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "no bcc"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var found = await app.SearchMessagesAsync(account.Id, mailboxId, "bcc:pat");
        Assert.AreEqual("Blind", found.Single().Subject);
    }

    [TestMethod]
    public async Task SearchMessages_after_and_before_filter_by_ReceivedAt()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "old",
                Subject: "Old",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "early"),
            new RemoteMessage(
                RemoteId: "new",
                Subject: "New",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 4, 12, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "late"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var after = await app.SearchMessagesAsync(account.Id, mailboxId, "after:2026-04-03");
        Assert.AreEqual("New", after.Single().Subject);

        var before = await app.SearchMessagesAsync(account.Id, mailboxId, "before:2026-04-03");
        Assert.AreEqual("Old", before.Single().Subject);

        var onOld = await app.SearchMessagesAsync(account.Id, mailboxId, "on:2026-04-01");
        Assert.AreEqual("Old", onOld.Single().Subject);
        var onNew = await app.SearchMessagesAsync(account.Id, mailboxId, "on:2026-04-04");
        Assert.AreEqual("New", onNew.Single().Subject);
        Assert.IsEmpty(await app.SearchMessagesAsync(account.Id, mailboxId, "on:2026-04-02"));
    }

    [TestMethod]
    public async Task SearchMessages_on_today_matches_current_local_day()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "now",
                Subject: "Today",
                FromAddress: "bob@example.com",
                ReceivedAt: DateTimeOffset.Now,
                IsRead: true,
                BodyText: "now"),
            new RemoteMessage(
                RemoteId: "old",
                Subject: "Old",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2020, 1, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "old"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var today = await app.SearchMessagesAsync(account.Id, mailboxId, "on:today");
        Assert.AreEqual("Today", today.Single().Subject);
        var afterYesterday = await app.SearchMessagesAsync(account.Id, mailboxId, "after:yesterday");
        Assert.AreEqual("Today", afterYesterday.Single().Subject);
        var thisWeek = await app.SearchMessagesAsync(account.Id, mailboxId, "on:thisweek");
        Assert.AreEqual("Today", thisWeek.Single().Subject);
        Assert.IsEmpty(await app.SearchMessagesAsync(account.Id, mailboxId, "on:lastweek"));
        Assert.AreEqual("Today", (await app.SearchMessagesAsync(account.Id, mailboxId, "on:thismonth")).Single().Subject);
        Assert.AreEqual("Today", (await app.SearchMessagesAsync(account.Id, mailboxId, "on:thisyear")).Single().Subject);
        Assert.IsEmpty(await app.SearchMessagesAsync(account.Id, mailboxId, "on:lastyear"));
    }

    [TestMethod]
    public async Task SearchMessages_msgid_matches_InternetMessageId()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "keep",
                Subject: "Keep",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "keep")
            {
                InternetMessageId = "<keep@example.com>",
            },
            new RemoteMessage(
                RemoteId: "other",
                Subject: "Other",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 13, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "other")
            {
                InternetMessageId = "<other@example.com>",
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var byGmail = await app.SearchMessagesAsync(account.Id, mailboxId, "rfc822msgid:keep@example.com");
        Assert.AreEqual("Keep", byGmail.Single().Subject);
        var byAlias = await app.SearchMessagesAsync(account.Id, mailboxId, "msgid:<KEEP@example.com>");
        Assert.AreEqual("Keep", byAlias.Single().Subject);
        Assert.IsEmpty(await app.SearchMessagesAsync(account.Id, mailboxId, "msgid:missing@example.com"));
    }

    [TestMethod]
    public async Task SearchMessages_larger_and_smaller_filter_by_SizeBytes()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "tiny",
                Subject: "Tiny",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "small")
            {
                SizeBytes = 100,
            },
            new RemoteMessage(
                RemoteId: "huge",
                Subject: "Huge",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 13, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "big")
            {
                SizeBytes = 3_000_000,
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var larger = await app.SearchMessagesAsync(account.Id, mailboxId, "larger:1M");
        Assert.AreEqual("Huge", larger.Single().Subject);
        var smaller = await app.SearchMessagesAsync(account.Id, mailboxId, "smaller:1k");
        Assert.AreEqual("Tiny", smaller.Single().Subject);
        Assert.IsEmpty(await app.SearchMessagesAsync(account.Id, mailboxId, "larger:10M"));
    }

    [TestMethod]
    public async Task SearchMessages_older_than_and_newer_than_use_relative_age()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "old",
                Subject: "Old",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "early"),
            new RemoteMessage(
                RemoteId: "fresh",
                Subject: "Fresh",
                FromAddress: "carol@example.com",
                ReceivedAt: DateTimeOffset.UtcNow,
                IsRead: true,
                BodyText: "late"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var older = await app.SearchMessagesAsync(account.Id, mailboxId, "older_than:7d");
        Assert.AreEqual("Old", older.Single().Subject);

        var newer = await app.SearchMessagesAsync(account.Id, mailboxId, "newer_than:7d");
        Assert.AreEqual("Fresh", newer.Single().Subject);
    }

    [TestMethod]
    public void MailboxMatchesIn_matches_name_path_and_last_segment()
    {
        Assert.IsTrue(MessageSearch.MailboxMatchesIn("Work", "INBOX/Work", "Work"));
        Assert.IsTrue(MessageSearch.MailboxMatchesIn("Work", "INBOX.Work", "inbox/work"));
        Assert.IsTrue(MessageSearch.MailboxMatchesIn("Later", "Later", "later"));
        Assert.IsFalse(MessageSearch.MailboxMatchesIn("Work", "INBOX/Work", "INBOX"));
        Assert.IsFalse(MessageSearch.MailboxMatchesIn("Work", "INBOX/Work", "Sent"));
        Assert.IsFalse(MessageSearch.MailboxMatchesIn("Work", "INBOX/Work", null));
    }

    [TestMethod]
    public async Task SearchUnifiedInbox_in_sent_searches_Sent_role_Mailboxes()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "in-1",
                Subject: "Inbox only",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "inbox"));
        fixture.Imap.SeedMessages(
            "Sent",
            new RemoteMessage(
                RemoteId: "s-1",
                Subject: "Sent only",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "sent"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);

        var sent = await app.SearchUnifiedInboxAsync("in:sent");
        Assert.AreEqual("Sent only", sent.Single().Subject);

        var inbox = await app.SearchUnifiedInboxAsync("in:inbox");
        Assert.AreEqual("Inbox only", inbox.Single().Subject);

        var anywhere = await app.SearchUnifiedInboxAsync("in:anywhere");
        CollectionAssert.AreEquivalent(
            new[] { "Inbox only", "Sent only" },
            anywhere.Select(item => item.Subject).ToArray());

        var inboxId = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox).Id;
        var fromInbox = await app.SearchMessagesAsync(account.Id, inboxId, "in:anywhere sent");
        Assert.AreEqual("Sent only", fromInbox.Single().Subject);
        var scoped = await app.SearchMessagesAsync(account.Id, inboxId, "sent");
        Assert.HasCount(0, scoped);

        var fromInboxSent = await app.SearchMessagesAsync(account.Id, inboxId, "in:sent");
        Assert.AreEqual("Sent only", fromInboxSent.Single().Subject);
        var fromInboxIsSent = await app.SearchMessagesAsync(account.Id, inboxId, "is:sent");
        Assert.AreEqual("Sent only", fromInboxIsSent.Single().Subject);
    }

    [TestMethod]
    public async Task SearchMessages_in_named_Mailbox_matches_path_or_name()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Work", "INBOX/Work", Role: null));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "in-1",
                Subject: "Inbox only",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "inbox"));
        fixture.Imap.SeedMessages(
            "INBOX/Work",
            new RemoteMessage(
                RemoteId: "w-1",
                Subject: "Work only",
                FromAddress: "pat@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "work"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inboxId = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox).Id;

        var named = await app.SearchMessagesAsync(account.Id, inboxId, "in:Work");
        Assert.AreEqual("Work only", named.Single().Subject);
        var labeled = await app.SearchMessagesAsync(account.Id, inboxId, "label:\"INBOX/Work\"");
        Assert.AreEqual("Work only", labeled.Single().Subject);
        var unified = await app.SearchUnifiedInboxAsync("in:Work");
        Assert.AreEqual("Work only", unified.Single().Subject);
        Assert.IsEmpty(await app.SearchMessagesAsync(account.Id, inboxId, "in:Missing"));
    }

    [TestMethod]
    public async Task SearchMessages_has_attachment_filters()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "with-file",
                Subject: "Has file",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "see attached")
            {
                Attachments =
                [
                    new RemoteAttachment("notes.txt", "text/plain", "hi"u8.ToArray()),
                ],
            },
            new RemoteMessage(
                RemoteId: "plain",
                Subject: "No file",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "nothing"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var withFile = await app.SearchMessagesAsync(account.Id, mailboxId, "has:attachment");
        Assert.AreEqual("Has file", withFile.Single().Subject);
        Assert.IsTrue(withFile.Single().HasAttachments);
    }

    [TestMethod]
    public void Matches_filename_operator_and_free_text_use_attachment_names()
    {
        string[] names = ["invoice.pdf"];
        Assert.IsTrue(MessageSearch.Matches(
            false, true, "Has file", "a@b.com", "see attached", null, "filename:invoice.pdf",
            hasAttachment: true, attachmentNames: names));
        Assert.IsFalse(MessageSearch.Matches(
            false, true, "Has file", "a@b.com", "see attached", null, "filename:invoice.pdf",
            hasAttachment: true, attachmentNames: ["notes.txt"]));
        Assert.IsTrue(MessageSearch.Matches(
            false, true, "Has file", "a@b.com", "see attached", null, "invoice.pdf",
            hasAttachment: true, attachmentNames: names));
        Assert.IsFalse(MessageSearch.Matches(
            false, true, "Has file", "a@b.com", "see attached", null, "invoice.pdf"));
    }

    [TestMethod]
    public async Task SearchMessages_filename_and_free_text_match_attachment_names()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "with-file",
                Subject: "Has file",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "see attached")
            {
                Attachments =
                [
                    new RemoteAttachment("Q3-invoice.pdf", "application/pdf", "hi"u8.ToArray()),
                ],
            },
            new RemoteMessage(
                RemoteId: "plain",
                Subject: "No file",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "nothing about files"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var byOperator = await app.SearchMessagesAsync(account.Id, mailboxId, "filename:invoice.pdf");
        Assert.AreEqual("Has file", byOperator.Single().Subject);

        var byAlias = await app.SearchMessagesAsync(account.Id, mailboxId, "file:Q3-invoice");
        Assert.AreEqual("Has file", byAlias.Single().Subject);

        var byText = await app.SearchMessagesAsync(account.Id, mailboxId, "invoice.pdf");
        Assert.AreEqual("Has file", byText.Single().Subject);
    }

    [TestMethod]
    public async Task SearchMessages_quoted_subject_matches_phrase()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "q3",
                Subject: "Q3 invoice due",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "pay"),
            new RemoteMessage(
                RemoteId: "other",
                Subject: "Q3 summary",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "invoice later"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var hits = await app.SearchMessagesAsync(account.Id, mailboxId, "subject:\"Q3 invoice\"");
        Assert.AreEqual("Q3 invoice due", hits.Single().Subject);
    }

    [TestMethod]
    public async Task SearchMessages_minus_from_excludes_sender()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "n-1",
                Subject: "Alert",
                FromAddress: "notify@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "ping"),
            new RemoteMessage(
                RemoteId: "h-1",
                Subject: "Hello",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "hi"));

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;

        var hits = await app.SearchMessagesAsync(account.Id, mailboxId, "-from:notify");
        Assert.AreEqual("Hello", hits.Single().Subject);
    }

    private static RemoteMessage Message(
        string remoteId,
        string subject,
        bool flagged,
        bool read = true,
        string body = "body") =>
        new(
            RemoteId: remoteId,
            Subject: subject,
            FromAddress: "bob@example.com",
            ReceivedAt: new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
            IsRead: read,
            BodyText: body)
        {
            IsFlagged = flagged,
        };

    private static ManualAccountDraft ValidDraft(
        string displayName = "Personal",
        string email = "alice@example.com") =>
        new(
            DisplayName: displayName,
            EmailAddress: email,
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}
