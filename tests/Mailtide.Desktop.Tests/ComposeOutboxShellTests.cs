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
