using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class ReplyAllDraftTests
{
    [TestMethod]
    public async Task StartReplyAll_addresses_From_To_and_Cc_except_self()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-ra",
                Subject: "Team thread",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "please reply all")
            {
                ToAddresses = ["alice@example.com", "carol@example.com"],
                CcAddresses = ["dave@example.com", "alice@example.com"],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var message = (await app.ListUnifiedInboxAsync()).Single();

        var draft = await app.StartReplyAllAsync(account.Id, message.Id);

        CollectionAssert.AreEqual(
            new[] { "bob@example.com", "carol@example.com" },
            draft.ToAddresses.ToArray());
        CollectionAssert.AreEqual(
            new[] { "dave@example.com" },
            draft.CcAddresses.ToArray());
        Assert.AreEqual("Re: Team thread", draft.Subject);
        StringAssert.Contains(draft.BodyText.ReplaceLineEndings("\n"), "> please reply all");
    }

    [TestMethod]
    public async Task StartReplyAll_excludes_self_when_addresses_have_display_names()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-ra-named",
                Subject: "Named",
                FromAddress: "Bob <bob@example.com>",
                ReceivedAt: new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "hello")
            {
                ToAddresses = ["Alice Example <alice@example.com>", "carol@example.com"],
                CcAddresses = ["Dave <dave@example.com>"],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var message = (await app.ListUnifiedInboxAsync()).Single();

        var draft = await app.StartReplyAllAsync(account.Id, message.Id);

        CollectionAssert.AreEqual(
            new[] { "Bob <bob@example.com>", "carol@example.com" },
            draft.ToAddresses.ToArray());
        CollectionAssert.AreEqual(
            new[] { "Dave <dave@example.com>" },
            draft.CcAddresses.ToArray());
    }

    [TestMethod]
    public async Task StartReplyAll_uses_ReplyTo_instead_of_From()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "rt-all",
                Subject: "List",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "hello")
            {
                ReplyToAddresses = ["list@example.com"],
                ToAddresses = ["alice@example.com", "carol@example.com"],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var message = (await app.ListUnifiedInboxAsync()).Single();
        var draft = await app.StartReplyAllAsync(account.Id, message.Id);
        CollectionAssert.AreEqual(
            new[] { "list@example.com", "carol@example.com" },
            draft.ToAddresses.ToArray());
        Assert.IsFalse(draft.ToAddresses.Any(item => item.Contains("bob@", StringComparison.Ordinal)));
    }

    private static ManualAccountDraft ValidDraft() =>
        new(
            DisplayName: "Personal",
            EmailAddress: "alice@example.com",
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}
