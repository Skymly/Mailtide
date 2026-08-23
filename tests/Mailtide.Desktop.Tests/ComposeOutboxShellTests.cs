using Mailtide.Core;
using Mailtide.UI;
using Mailtide.Core.Imap;
using Mailtide.Core.Smtp;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class ComposeOutboxShellTests
{
    [TestMethod]
    public async Task ComposeOutboxShell_saves_a_local_Draft_from_compose_fields()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var shell = new ComposeOutboxShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SaveDraftAsync(
            toAddresses: "bob@example.com",
            subject: "Hello",
            bodyText: "Body");

        Assert.AreEqual(account.Id, shell.SelectedAccountId);
        Assert.HasCount(1, shell.Drafts);
        Assert.AreEqual("Hello", shell.Drafts[0].Subject);
        Assert.AreEqual("Body", shell.Drafts[0].BodyText);
        CollectionAssert.AreEqual(new[] { "bob@example.com" }, shell.Drafts[0].ToAddresses.ToArray());
        Assert.AreEqual(0, (await app.ListOutboxAsync(account.Id)).Count);
        Assert.AreEqual(shell.Drafts[0].Id, shell.SelectedDraftId);
    }

    [TestMethod]
    public async Task ComposeOutboxShell_DiscardDraft_removes_selected_Draft()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        var shell = new ComposeOutboxShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SaveDraftAsync("bob@example.com", "Hello", "Body");
        var draftId = shell.SelectedDraftId!.Value;

        await shell.DiscardDraftAsync(draftId);

        Assert.IsEmpty(shell.Drafts);
        Assert.IsNull(shell.SelectedDraftId);
    }

    [TestMethod]
    public async Task ComposeOutboxShell_SaveDraft_updates_the_selected_Reply_Draft()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Hello",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "hi")
            {
                InternetMessageId = "<orig@example.com>",
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var message = (await app.ListUnifiedInboxAsync()).Single();

        var shell = new ComposeOutboxShell(app);
        var created = await shell.StartReplyAsync(message.AccountId, message.Id);
        await shell.SaveDraftAsync("bob@example.com", "Re: Hello", "edited");

        Assert.AreEqual(created.Id, shell.SelectedDraftId);
        Assert.HasCount(1, shell.Drafts);
        Assert.AreEqual(created.Id, shell.Drafts[0].Id);
        Assert.AreEqual("edited", shell.Drafts[0].BodyText);
        Assert.AreEqual("<orig@example.com>", shell.Drafts[0].InReplyTo);
    }

    [TestMethod]
    public async Task ComposeOutboxShell_Send_moves_Draft_into_Outbox_as_Queued()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var shell = new ComposeOutboxShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SaveDraftAsync("bob@example.com", "Hello", "Body");
        var draftId = shell.Drafts[0].Id;

        await shell.SendAsync(draftId);

        Assert.IsEmpty(shell.Drafts);
        Assert.HasCount(1, shell.OutboxItems);
        Assert.AreEqual(OutboxItemState.Queued, shell.OutboxItems[0].State);
        Assert.AreEqual("Hello", shell.OutboxItems[0].Subject);
        Assert.IsNull(shell.OutboxItems[0].ErrorMessage);
    }

    [TestMethod]
    public async Task ComposeOutboxShell_shows_failed_Outbox_and_Retry_requeues()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Smtp.FailWith = new SmtpProtocolException("temporary");
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var shell = new ComposeOutboxShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SaveDraftAsync("bob@example.com", "Hello", "Body");
        await shell.SendAsync(shell.Drafts[0].Id);
        await shell.SendNowAsync();

        Assert.HasCount(1, shell.OutboxItems);
        Assert.AreEqual(OutboxItemState.Failed, shell.OutboxItems[0].State);
        Assert.IsFalse(string.IsNullOrWhiteSpace(shell.OutboxItems[0].ErrorMessage));

        var failedId = shell.OutboxItems[0].Id;
        await shell.RetryOutboxItemAsync(failedId);

        Assert.HasCount(1, shell.OutboxItems);
        Assert.AreEqual(OutboxItemState.Queued, shell.OutboxItems[0].State);
        Assert.IsNull(shell.OutboxItems[0].ErrorMessage);
    }

    [TestMethod]
    public async Task ComposeOutboxShell_Discard_removes_failed_Outbox_item()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Smtp.FailWith = new SmtpProtocolException("temporary");
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var shell = new ComposeOutboxShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SaveDraftAsync("bob@example.com", "Hello", "Body");
        await shell.SendAsync(shell.Drafts[0].Id);
        await shell.SendNowAsync();

        var failedId = shell.OutboxItems[0].Id;
        await shell.DiscardOutboxItemAsync(failedId);

        Assert.IsEmpty(shell.OutboxItems);
        Assert.IsEmpty(shell.Drafts);
    }

    [TestMethod]
    public async Task ComposeOutboxShell_SyncNow_and_SendNow_are_available()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Synced",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "hi"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var shell = new ComposeOutboxShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SyncNowAsync();

        Assert.HasCount(1, await app.ListMailboxesAsync(account.Id));
        Assert.AreEqual(AccountSyncState.Idle, app.GetAccountStatus(account.Id).State);

        await shell.SaveDraftAsync("bob@example.com", "Hello", "Body");
        await shell.SendAsync(shell.Drafts[0].Id);
        await shell.SendNowAsync();

        Assert.IsEmpty(shell.OutboxItems);
        Assert.HasCount(1, fixture.Smtp.Submitted);
        Assert.AreEqual("Hello", fixture.Smtp.Submitted[0].Subject);
    }

    [TestMethod]
    public async Task ComposeOutboxShell_ClearSelection_drops_account_and_lists()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var shell = new ComposeOutboxShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SaveDraftAsync("bob@example.com", "Hello", "Body");

        shell.ClearSelection();

        Assert.IsNull(shell.SelectedAccountId);
        Assert.IsEmpty(shell.Drafts);
        Assert.IsEmpty(shell.OutboxItems);
    }


    [TestMethod]
    public async Task ComposeOutboxShell_StartReply_creates_Draft_on_the_Message_Account()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Hello",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "hi")
            {
                InternetMessageId = "<orig@example.com>",
                References = ["<root@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var message = (await app.ListUnifiedInboxAsync()).Single();

        var shell = new ComposeOutboxShell(app);
        var draft = await shell.StartReplyAsync(message.AccountId, message.Id);

        Assert.AreEqual(account.Id, shell.SelectedAccountId);
        Assert.AreEqual(account.Id, draft.AccountId);
        Assert.AreEqual("Re: Hello", draft.Subject);
        Assert.AreEqual("<orig@example.com>", draft.InReplyTo);
        CollectionAssert.AreEqual(
            new[] { "<root@example.com>", "<orig@example.com>" },
            draft.References.ToArray());
        CollectionAssert.AreEqual(new[] { "bob@example.com" }, draft.ToAddresses.ToArray());
        Assert.HasCount(1, shell.Drafts);
        Assert.AreEqual(draft.Id, shell.Drafts[0].Id);
    }

    [TestMethod]
    public async Task ComposeOutboxShell_StartReply_from_Unified_Inbox_targets_the_Message_Account()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();

        var alice = await app.AddManualAccountAsync(ValidDraft("Alice", "alice@example.com"));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "a-1",
                Subject: "For Alice",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "a"));
        await app.SyncNowAsync(alice.Id);

        var bob = await app.AddManualAccountAsync(ValidDraft("Bob", "bob@example.com"));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "b-1",
                Subject: "For Bob",
                FromAddress: "dave@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "b"));
        await app.SyncNowAsync(bob.Id);

        var browse = new BrowseShell(app);
        await browse.ShowUnifiedInboxAsync();
        var forBob = browse.Messages.Single(m => m.Subject == "For Bob");

        var compose = new ComposeOutboxShell(app);
        await compose.SelectAccountAsync(alice.Id);
        var draft = await compose.StartReplyAsync(forBob.AccountId, forBob.Id);

        Assert.IsTrue(browse.ShowingUnifiedInbox);
        Assert.AreEqual(bob.Id, compose.SelectedAccountId);
        Assert.AreEqual(bob.Id, draft.AccountId);
        CollectionAssert.AreEqual(new[] { "dave@example.com" }, draft.ToAddresses.ToArray());
        Assert.AreEqual("Re: For Bob", draft.Subject);
        Assert.IsEmpty(await app.ListDraftsAsync(alice.Id));
        Assert.HasCount(1, await app.ListDraftsAsync(bob.Id));
    }
    [TestMethod]
    public async Task ComposeOutboxShell_StartForward_creates_Draft_with_empty_To()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Hello",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "hi"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var message = (await app.ListUnifiedInboxAsync()).Single();

        var shell = new ComposeOutboxShell(app);
        var draft = await shell.StartForwardAsync(message.AccountId, message.Id);

        Assert.AreEqual(account.Id, shell.SelectedAccountId);
        Assert.AreEqual("Fwd: Hello", draft.Subject);
        Assert.IsEmpty(draft.ToAddresses);
        Assert.HasCount(1, shell.Drafts);
        Assert.AreEqual(draft.Id, shell.Drafts[0].Id);
    }
    [TestMethod]
    public async Task ComposeOutboxShell_StartReplyAll_drops_self_and_keeps_other_recipients()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-ra",
                Subject: "Hello",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "hi")
            {
                ToAddresses = ["alice@example.com", "carol@example.com"],
                CcAddresses = ["dave@example.com"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var message = (await app.ListUnifiedInboxAsync()).Single();

        var shell = new ComposeOutboxShell(app);
        var draft = await shell.StartReplyAllAsync(message.AccountId, message.Id);

        CollectionAssert.AreEqual(
            new[] { "bob@example.com", "carol@example.com" },
            draft.ToAddresses.ToArray());
        CollectionAssert.AreEqual(
            new[] { "dave@example.com" },
            draft.CcAddresses.ToArray());
        Assert.AreEqual("Re: Hello", draft.Subject);
        Assert.HasCount(1, shell.Drafts);
    }

    [TestMethod]
    public async Task ComposeOutboxShell_SaveDraft_keeps_Cc_separate()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        var shell = new ComposeOutboxShell(app);
        await shell.SelectAccountAsync(account.Id);

        await shell.SaveDraftAsync(
            toAddresses: "bob@example.com",
            subject: "Hello",
            bodyText: "Body",
            ccAddresses: "carol@example.com");

        Assert.HasCount(1, shell.Drafts);
        CollectionAssert.AreEqual(new[] { "bob@example.com" }, shell.Drafts[0].ToAddresses.ToArray());
        CollectionAssert.AreEqual(new[] { "carol@example.com" }, shell.Drafts[0].CcAddresses.ToArray());
    }
    private static ManualAccountDraft ValidDraft(string displayName, string email) =>
        new(
            DisplayName: displayName,
            EmailAddress: email,
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}
