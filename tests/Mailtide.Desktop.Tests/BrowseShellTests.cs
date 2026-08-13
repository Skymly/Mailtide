using Mailtide.Core;
using Mailtide.Core.Auth;
using Mailtide.Core.Imap;
using Mailtide.UI;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class BrowseShellTests
{
    private static readonly object HostBootstrapGate = new();

    [TestMethod]
    public async Task BrowseShell_lists_Accounts_already_in_the_store()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();

        Assert.HasCount(1, shell.Accounts);
        Assert.AreEqual(account.Id, shell.Accounts[0].Id);
        Assert.AreEqual("Personal", shell.Accounts[0].DisplayName);
    }

    [TestMethod]
    public async Task BrowseShell_selecting_Account_lists_its_Mailboxes()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.SelectAccountAsync(account.Id);

        Assert.AreEqual(account.Id, shell.SelectedAccountId);
        Assert.HasCount(2, shell.Mailboxes);
        Assert.IsTrue(shell.Mailboxes.Any(m => m.Role == MailboxRole.Inbox));
        Assert.IsTrue(shell.Mailboxes.Any(m => m.Role == MailboxRole.Sent));
    }

    [TestMethod]
    public async Task BrowseShell_selecting_Mailbox_lists_its_Messages()
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
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);

        Assert.AreEqual(inbox.Id, shell.SelectedMailboxId);
        Assert.IsFalse(shell.ShowingUnifiedInbox);
        Assert.HasCount(1, shell.Messages);
        Assert.AreEqual("Hello", shell.Messages[0].Subject);
    }

    [TestMethod]
    public async Task BrowseShell_shows_Unified_Inbox_as_query_view_not_a_Mailbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        await using var app = await fixture.OpenAppAsync();

        var accountA = await app.AddManualAccountAsync(ValidDraft("Alice", "alice@example.com"));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "a-1",
                Subject: "From Alice Inbox",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "a"));
        await app.SyncNowAsync(accountA.Id);

        var accountB = await app.AddManualAccountAsync(ValidDraft("Bob", "bob@example.com"));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "b-1",
                Subject: "From Bob Inbox",
                FromAddress: "dave@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "b"));
        await app.SyncNowAsync(accountB.Id);

        var mailboxCountBefore = (await app.ListMailboxesAsync(accountA.Id)).Count
            + (await app.ListMailboxesAsync(accountB.Id)).Count;

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(accountA.Id);
        Assert.IsNotEmpty(shell.Mailboxes);
        Assert.AreEqual(accountA.Id, shell.SelectedAccountId);

        await shell.ShowUnifiedInboxAsync();

        Assert.IsTrue(shell.ShowingUnifiedInbox);
        Assert.IsNull(shell.SelectedAccountId);
        Assert.IsNull(shell.SelectedMailboxId);
        Assert.IsEmpty(shell.Mailboxes);
        Assert.HasCount(2, shell.Messages);
        Assert.AreEqual("From Bob Inbox", shell.Messages[0].Subject);
        Assert.AreEqual("From Alice Inbox", shell.Messages[1].Subject);

        var mailboxCountAfter = (await app.ListMailboxesAsync(accountA.Id)).Count
            + (await app.ListMailboxesAsync(accountB.Id)).Count;
        Assert.AreEqual(mailboxCountBefore, mailboxCountAfter);
    }

    [TestMethod]
    public async Task BrowseShell_adds_Google_Account_via_Core_OAuth()
    {
        using var fixture = new DesktopAppFixture();
        fixture.OAuth.AuthorizeResult = new OAuthAuthorizationResult(
            EmailAddress: "alice@gmail.com",
            RefreshSecret: "shell-google-refresh",
            Metadata: new OAuthTokenMetadata(
                OAuthProvider.Google,
                GoogleMailPreset.Authority,
                "test-google-client"));
        await using var app = await fixture.OpenAppAsync();

        var shell = new BrowseShell(app);
        var account = await shell.AddGoogleAccountAsync("Gmail");

        Assert.AreEqual(account.Id, shell.Accounts[0].Id);
        Assert.AreEqual(CredentialKind.OAuth, shell.Accounts[0].CredentialKind);
        Assert.AreEqual(OAuthProvider.Google, shell.Accounts[0].OAuthProvider);
        Assert.AreEqual(AccountSyncState.Idle, shell.AccountStatuses[0].Status.State);
    }

    [TestMethod]
    public async Task BrowseShell_adds_Microsoft_consumer_Account_via_Core_OAuth()
    {
        using var fixture = new DesktopAppFixture();
        fixture.OAuth.AuthorizeResult = new OAuthAuthorizationResult(
            EmailAddress: "bob@outlook.com",
            RefreshSecret: "shell-ms-refresh",
            Metadata: new OAuthTokenMetadata(
                OAuthProvider.MicrosoftConsumer,
                MicrosoftConsumerMailPreset.Authority,
                "test-ms-client"));
        await using var app = await fixture.OpenAppAsync();

        var shell = new BrowseShell(app);
        var account = await shell.AddMicrosoftConsumerAccountAsync("Outlook");

        Assert.AreEqual(account.Id, shell.Accounts[0].Id);
        Assert.AreEqual(CredentialKind.OAuth, shell.Accounts[0].CredentialKind);
        Assert.AreEqual(OAuthProvider.MicrosoftConsumer, shell.Accounts[0].OAuthProvider);
        Assert.AreEqual(AccountSyncState.Idle, shell.AccountStatuses[0].Status.State);
    }

    [TestMethod]
    public async Task BrowseShell_exposes_Account_auth_error_status()
    {
        using var fixture = new DesktopAppFixture();
        fixture.OAuth.AuthorizeResult = new OAuthAuthorizationResult(
            EmailAddress: "dave@gmail.com",
            RefreshSecret: "shell-refresh",
            Metadata: new OAuthTokenMetadata(
                OAuthProvider.Google,
                GoogleMailPreset.Authority,
                "test-google-client"));
        fixture.OAuth.RefreshFailWith = new OAuthAuthenticationException("invalid_grant");
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();

        var shell = new BrowseShell(app);
        var account = await shell.AddGoogleAccountAsync("Gmail");
        await app.SyncNowAsync(account.Id);
        await shell.LoadAccountsAsync();

        Assert.AreEqual(AccountSyncState.Error, shell.GetAccountStatus(account.Id).State);
        Assert.AreEqual(
            "Authentication failed. Sign in again.",
            shell.AccountStatuses.Single(row => row.Account.Id == account.Id).Status.ErrorMessage);
    }

    [TestMethod]
    public async Task BrowseShell_AccountStatuses_include_Idle_Syncing_Error_State()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();

        Assert.AreEqual(AccountSyncState.Idle, shell.AccountStatuses[0].Status.State);
    }

    [TestMethod]
    public async Task BrowseShell_adds_Manual_and_QQ_Accounts()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var shell = new BrowseShell(app);

        var manual = await shell.AddManualAccountAsync(ValidDraft("Manual", "manual@example.com"));
        var qq = await shell.AddQqMailAccountAsync(
            new QqMailAccountDraft("QQ", "123456789@qq.com", "abcdefghijklmnop"));

        Assert.HasCount(2, shell.Accounts);
        Assert.IsTrue(shell.Accounts.Any(a => a.Id == manual.Id));
        Assert.IsTrue(shell.Accounts.Any(a => a.Id == qq.Id));
        Assert.AreEqual(CredentialKind.Password, shell.Accounts.Single(a => a.Id == qq.Id).CredentialKind);
        Assert.HasCount(2, shell.AccountStatuses);
        Assert.IsTrue(shell.AccountStatuses.All(row => row.Status.State == AccountSyncState.Idle));
    }

    [TestMethod]
    public async Task BrowseShell_RemoveAccount_requires_confirmation_and_cancels_without_removing()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();

        shell.AccountRemovalConfirmation = new FakeConfirmAccountRemoval(confirm: false);
        var removed = await shell.RemoveAccountAsync(account.Id);
        Assert.IsFalse(removed);
        await shell.LoadAccountsAsync();
        Assert.HasCount(1, shell.Accounts);
    }

    [TestMethod]
    public async Task BrowseShell_RemoveAccount_removes_after_confirmation()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();

        shell.AccountRemovalConfirmation = new FakeConfirmAccountRemoval(confirm: true);
        var removed = await shell.RemoveAccountAsync(account.Id);
        Assert.IsTrue(removed);
        Assert.IsEmpty(shell.Accounts);
    }

    [TestMethod]
    public async Task BrowseShell_SelectMessage_loads_plain_text_body_and_attachments()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-body",
                Subject: "Hello",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "plain body text")
            {
                Attachments =
                [
                    new RemoteAttachment(
                        FileName: "notes.txt",
                        ContentType: "text/plain",
                        Content: "file-bytes"u8.ToArray()),
                ],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);

        Assert.AreEqual("plain body text", shell.BodyText);
        Assert.IsFalse(shell.BodyUnavailable);
        Assert.HasCount(1, shell.Attachments);
        Assert.AreEqual("notes.txt", shell.Attachments[0].FileName);
    }

    [TestMethod]
    public async Task BrowseShell_SelectMessage_marks_missing_body_unavailable()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-empty",
                Subject: "No body",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: string.Empty));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);

        Assert.IsTrue(shell.BodyUnavailable);
    }

    [TestMethod]
    public async Task BrowseShell_OpenAttachment_calls_Host_port_with_content()
    {
        using var fixture = new DesktopAppFixture();
        var payload = "attach-payload"u8.ToArray();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-open",
                Subject: "Has file",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "see file")
            {
                Attachments =
                [
                    new RemoteAttachment(
                        FileName: "report.pdf",
                        ContentType: "application/pdf",
                        Content: payload),
                ],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var fakeOpen = new FakeOpenDownloadedAttachment();
        lock (HostBootstrapGate)
        {
            var previous = HostBootstrap.OpenDownloadedAttachment;
            HostBootstrap.OpenDownloadedAttachment = fakeOpen;
            try
            {
                var shell = new BrowseShell(app);
                shell.SelectAccountAsync(account.Id).GetAwaiter().GetResult();
                shell.SelectMailboxAsync(inbox.Id).GetAwaiter().GetResult();
                shell.SelectMessageAsync(shell.Messages[0].Id).GetAwaiter().GetResult();
                shell.OpenAttachmentAsync(shell.Attachments[0].Id).GetAwaiter().GetResult();

                Assert.IsNull(shell.AttachmentOpenError);
                Assert.AreEqual("report.pdf", fakeOpen.LastFileName);
                Assert.AreEqual("application/pdf", fakeOpen.LastContentType);
                CollectionAssert.AreEqual(payload, fakeOpen.LastContent);
            }
            finally
            {
                HostBootstrap.OpenDownloadedAttachment = previous;
            }
        }
    }

    [TestMethod]
    public async Task BrowseShell_OpenAttachment_surfaces_short_error_on_Host_failure()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-fail-open",
                Subject: "Has file",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "see file")
            {
                Attachments =
                [
                    new RemoteAttachment(
                        FileName: "a.txt",
                        ContentType: "text/plain",
                        Content: "x"u8.ToArray()),
                ],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        lock (HostBootstrapGate)
        {
            var previous = HostBootstrap.OpenDownloadedAttachment;
            HostBootstrap.OpenDownloadedAttachment = new FakeOpenDownloadedAttachment
            {
                FailWith = new OpenAttachmentException(
                    "Could not open the attachment.",
                    new Exception("native boom")),
            };
            try
            {
                var shell = new BrowseShell(app);
                shell.SelectAccountAsync(account.Id).GetAwaiter().GetResult();
                shell.SelectMailboxAsync(inbox.Id).GetAwaiter().GetResult();
                shell.SelectMessageAsync(shell.Messages[0].Id).GetAwaiter().GetResult();
                shell.OpenAttachmentAsync(shell.Attachments[0].Id).GetAwaiter().GetResult();

                Assert.AreEqual("Could not open the attachment.", shell.AttachmentOpenError);
            }
            finally
            {
                HostBootstrap.OpenDownloadedAttachment = previous;
            }
        }
    }

    [TestMethod]
    public async Task BrowseShell_projects_Idle_after_ComposeOutboxShell_SyncNow()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var browse = new BrowseShell(app);
        var compose = new ComposeOutboxShell(app);
        await compose.SelectAccountAsync(account.Id);
        await compose.SyncNowAsync();
        await browse.LoadAccountsAsync();

        var row = browse.AccountStatuses.Single(r => r.Account.Id == account.Id);
        Assert.AreEqual(AccountSyncState.Idle, row.Status.State);
        Assert.IsNull(row.Status.ErrorMessage);
    }

    [TestMethod]
    public async Task BrowseShell_projects_Error_after_ComposeOutboxShell_SyncNow_auth_failure()
    {
        using var fixture = new DesktopAppFixture();
        fixture.OAuth.AuthorizeResult = new OAuthAuthorizationResult(
            EmailAddress: "dave@gmail.com",
            RefreshSecret: "shell-refresh",
            Metadata: new OAuthTokenMetadata(
                OAuthProvider.Google,
                GoogleMailPreset.Authority,
                "test-google-client"));
        fixture.OAuth.RefreshFailWith = new OAuthAuthenticationException("invalid_grant");
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();

        var browse = new BrowseShell(app);
        var account = await browse.AddGoogleAccountAsync("Gmail");
        var compose = new ComposeOutboxShell(app);
        await compose.SelectAccountAsync(account.Id);
        await compose.SyncNowAsync();
        await browse.LoadAccountsAsync();

        var row = browse.AccountStatuses.Single(r => r.Account.Id == account.Id);
        Assert.AreEqual(AccountSyncState.Error, row.Status.State);
        Assert.AreEqual("Authentication failed. Sign in again.", row.Status.ErrorMessage);
    }

    [TestMethod]
    public async Task BrowseShell_projects_Syncing_while_ComposeOutboxShell_SyncNow_is_in_flight()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Imap.BlockConnectUntil = gate;
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var browse = new BrowseShell(app);
        var compose = new ComposeOutboxShell(app);
        await compose.SelectAccountAsync(account.Id);

        var syncTask = compose.SyncNowAsync();
        await WaitUntilAsync(() => app.GetAccountStatus(account.Id).State == AccountSyncState.Syncing);
        await browse.LoadAccountsAsync();

        Assert.AreEqual(
            AccountSyncState.Syncing,
            browse.AccountStatuses.Single(r => r.Account.Id == account.Id).Status.State);

        gate.SetResult();
        await syncTask;
        await browse.LoadAccountsAsync();

        Assert.AreEqual(
            AccountSyncState.Idle,
            browse.AccountStatuses.Single(r => r.Account.Id == account.Id).Status.State);
    }

    [TestMethod]
    public async Task BrowseShell_refreshes_status_snapshot_after_ComposeOutboxShell_SendNow()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var browse = new BrowseShell(app);
        var compose = new ComposeOutboxShell(app);
        await compose.SelectAccountAsync(account.Id);
        await compose.SaveDraftAsync("bob@example.com", "Hello", "Body");
        await compose.SendAsync(compose.Drafts[0].Id);
        await compose.SendNowAsync();
        await browse.LoadAccountsAsync();

        var row = browse.AccountStatuses.Single(r => r.Account.Id == account.Id);
        Assert.AreEqual(AccountSyncState.Idle, row.Status.State);
        Assert.IsNull(row.Status.ErrorMessage);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("Timed out waiting for condition.");
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

    private sealed class FakeConfirmAccountRemoval(bool confirm) : IConfirmAccountRemoval
    {
        public Task<bool> ConfirmAsync(string accountDisplayName, CancellationToken cancellationToken = default) =>
            Task.FromResult(confirm);
    }

    private sealed class FakeOpenDownloadedAttachment : IOpenDownloadedAttachment
    {
        public string? LastFileName { get; private set; }

        public string? LastContentType { get; private set; }

        public byte[]? LastContent { get; private set; }

        public OpenAttachmentException? FailWith { get; init; }

        public Task OpenAsync(
            string fileName,
            string contentType,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            if (FailWith is not null)
            {
                throw FailWith;
            }

            LastFileName = fileName;
            LastContentType = contentType;
            LastContent = content.ToArray();
            return Task.CompletedTask;
        }
    }
}
