using Mailtide.Core;
using Mailtide.Core.Auth;
using Mailtide.Core.Imap;
using Mailtide.Desktop;
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
    public async Task BrowseShell_CreateMailbox_adds_to_selected_Account()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        var created = await shell.CreateMailboxAsync("Projects");

        Assert.AreEqual("Projects", created.Name);
        Assert.IsTrue(shell.Mailboxes.Any(m => m.Id == created.Id));
    }

    [TestMethod]
    public async Task BrowseShell_CreateMailbox_requires_selected_Account()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var shell = new BrowseShell(app);

        await Assert.ThrowsAsync<InvalidOperationException>(() => shell.CreateMailboxAsync("Projects"));
    }

    [TestMethod]
    public async Task BrowseShell_RenameMailbox_updates_selected_Mailbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        var created = await shell.CreateMailboxAsync("Projects");
        await shell.SelectMailboxAsync(created.Id);

        var renamed = await shell.RenameMailboxAsync("Work");

        Assert.AreEqual("Work", renamed.Name);
        Assert.IsTrue(shell.Mailboxes.Any(m => m.Id == created.Id && m.Name == "Work"));
        Assert.IsFalse(shell.Mailboxes.Any(m => m.Name == "Projects"));
    }

    [TestMethod]
    public async Task BrowseShell_RenameMailbox_requires_selected_Mailbox()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var shell = new BrowseShell(app);

        await Assert.ThrowsAsync<InvalidOperationException>(() => shell.RenameMailboxAsync("Work"));
    }

    [TestMethod]
    public async Task BrowseShell_DeleteMailbox_removes_selected_Mailbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        var created = await shell.CreateMailboxAsync("Projects");
        await shell.SelectMailboxAsync(created.Id);

        await shell.DeleteMailboxAsync();

        Assert.IsNull(shell.SelectedMailboxId);
        Assert.IsFalse(shell.Mailboxes.Any(m => m.Id == created.Id));
    }

    [TestMethod]
    public async Task BrowseShell_DeleteMailbox_requires_selected_Mailbox()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var shell = new BrowseShell(app);

        await Assert.ThrowsAsync<InvalidOperationException>(() => shell.DeleteMailboxAsync());
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
    public async Task BrowseShell_Mailbox_groups_reply_thread_until_selected()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "root")
            {
                InternetMessageId = "<root@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "reply")
            {
                InternetMessageId = "<reply@example.com>",
                References = ["<root@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);

        Assert.HasCount(1, shell.Messages);
        Assert.AreEqual("Re: Root", shell.Messages[0].Subject);

        await shell.SelectThreadAsync(shell.Messages[0].Id);

        Assert.HasCount(2, shell.Messages);
        Assert.AreEqual(shell.Messages[0].Id, shell.SelectedThreadId);
    }

    [TestMethod]
    public async Task BrowseShell_Unified_Inbox_groups_reply_thread_until_selected()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "root")
            {
                InternetMessageId = "<root@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "reply")
            {
                InternetMessageId = "<reply@example.com>",
                References = ["<root@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);

        var shell = new BrowseShell(app);
        await shell.ShowUnifiedInboxAsync();

        Assert.HasCount(1, shell.Messages);
        Assert.AreEqual("Re: Root", shell.Messages[0].Subject);
        Assert.IsNull(shell.SelectedThreadId);

        await shell.SelectThreadAsync(shell.Messages[0].Id);

        Assert.HasCount(2, shell.Messages);
        Assert.AreEqual(shell.Messages[0].Id, shell.SelectedThreadId);

        await shell.SelectAccountAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();
        await shell.SelectMailboxAsync(inbox.Id);
        Assert.HasCount(1, shell.Messages);
        Assert.AreEqual("Re: Root", shell.Messages[0].Subject);
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

        var alice = shell.Threads.Single(item => item.Latest.Subject == "From Alice Inbox");
        await shell.SelectThreadAsync(alice.Latest.Id);
        await shell.RevealInMailboxAsync();
        Assert.IsFalse(shell.ShowingUnifiedInbox);
        Assert.AreEqual(accountA.Id, shell.SelectedAccountId);
        Assert.AreEqual(alice.Latest.MailboxId, shell.SelectedMailboxId);
        Assert.AreEqual(alice.Latest.Id, shell.SelectedThreadId);
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
        Assert.IsTrue(shell.AccountStatuses.Single(row => row.Account.Id == account.Id).Status.RequiresSignIn);
        Assert.IsNotNull(shell.AuthenticationFailurePrompt);
        Assert.AreEqual(account.Id, shell.AuthenticationFailurePrompt.AccountId);
        Assert.AreEqual(AuthenticationFailureAction.Reauthorize, shell.AuthenticationFailurePrompt.Action);
    }

    [TestMethod]
    public async Task BrowseShell_prompts_Edit_Account_on_Password_authentication_failure()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.FailWith = new ImapAuthenticationException("NO [AUTHENTICATIONFAILED] Invalid credentials");
        await using var app = await fixture.OpenAppAsync();
        var shell = new BrowseShell(app);
        var account = await shell.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        await shell.LoadAccountsAsync();

        Assert.IsTrue(shell.GetAccountStatus(account.Id).RequiresSignIn);
        Assert.IsNotNull(shell.AuthenticationFailurePrompt);
        Assert.AreEqual(account.Id, shell.AuthenticationFailurePrompt.AccountId);
        Assert.AreEqual("Personal", shell.AuthenticationFailurePrompt.DisplayName);
        Assert.AreEqual(AuthenticationFailureAction.EditAccount, shell.AuthenticationFailurePrompt.Action);
    }

    [TestMethod]
    public async Task BrowseShell_does_not_prompt_on_non_auth_sync_error()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.FailWith = new ImapProtocolException("BAD FETCH");
        await using var app = await fixture.OpenAppAsync();
        var shell = new BrowseShell(app);
        var account = await shell.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        await shell.LoadAccountsAsync();

        Assert.AreEqual(AccountSyncState.Error, shell.GetAccountStatus(account.Id).State);
        Assert.IsFalse(shell.GetAccountStatus(account.Id).RequiresSignIn);
        Assert.IsNull(shell.AuthenticationFailurePrompt);
    }

    [TestMethod]
    public async Task BrowseShell_dismissing_auth_prompt_keeps_Error_row()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.FailWith = new ImapAuthenticationException("NO [AUTHENTICATIONFAILED]");
        await using var app = await fixture.OpenAppAsync();
        var shell = new BrowseShell(app);
        var account = await shell.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        await shell.LoadAccountsAsync();

        shell.DismissAuthenticationFailurePrompt();
        await shell.LoadAccountsAsync();

        Assert.IsNull(shell.AuthenticationFailurePrompt);
        Assert.AreEqual(AccountSyncState.Error, shell.GetAccountStatus(account.Id).State);
        Assert.IsTrue(shell.GetAccountStatus(account.Id).RequiresSignIn);
    }

    [TestMethod]
    public async Task BrowseShell_Reauthorize_clears_auth_prompt()
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
        Assert.IsNotNull(shell.AuthenticationFailurePrompt);

        fixture.OAuth.RefreshFailWith = null;
        fixture.OAuth.AuthorizeResult = new OAuthAuthorizationResult(
            EmailAddress: "dave@gmail.com",
            RefreshSecret: "new-refresh",
            Metadata: new OAuthTokenMetadata(
                OAuthProvider.Google,
                GoogleMailPreset.Authority,
                "test-google-client"));

        await shell.ReauthorizeAccountAsync(account.Id);

        Assert.IsNull(shell.AuthenticationFailurePrompt);
        Assert.AreEqual(AccountSyncState.Idle, shell.GetAccountStatus(account.Id).State);
        Assert.IsFalse(shell.GetAccountStatus(account.Id).RequiresSignIn);
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
    public async Task BrowseShell_UpdateManual_keeps_secret_when_Password_blank_and_reloads()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var shell = new BrowseShell(app);
        var account = await shell.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var updated = await shell.UpdateManualAccountAsync(
            account.Id,
            new ManualAccountDraft(
                DisplayName: "Renamed",
                EmailAddress: "alice@example.com",
                ImapHost: "imap.example.com",
                ImapPort: 993,
                SmtpHost: "smtp.example.com",
                SmtpPort: 587,
                Password: ""));

        Assert.IsNotNull(updated);
        Assert.AreEqual("Renamed", updated.DisplayName);
        Assert.AreEqual(account.Id, shell.Accounts.Single().Id);
        Assert.AreEqual("Renamed", shell.Accounts.Single().DisplayName);
        Assert.AreEqual(
            "s3cret-password",
            await fixture.SecureStorage.RetrieveSecretAsync(account.CredentialHandle));
    }

    [TestMethod]
    public async Task BrowseShell_UpdateManual_replaces_secret_when_Password_set()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var shell = new BrowseShell(app);
        var account = await shell.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        await shell.UpdateManualAccountAsync(
            account.Id,
            new ManualAccountDraft(
                DisplayName: "Personal",
                EmailAddress: "alice@example.com",
                ImapHost: "imap.example.com",
                ImapPort: 993,
                SmtpHost: "smtp.example.com",
                SmtpPort: 587,
                Password: "rotated-password"));

        Assert.AreEqual(
            "rotated-password",
            await fixture.SecureStorage.RetrieveSecretAsync(account.CredentialHandle));
    }

    [TestMethod]
    public async Task BrowseShell_UpdateQqMail_keeps_preset_and_blank_code_keeps_secret()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var shell = new BrowseShell(app);
        var account = await shell.AddQqMailAccountAsync(
            new QqMailAccountDraft("QQ", "123456789@qq.com", "abcdefghijklmnop"));

        var updated = await shell.UpdateQqMailAccountAsync(
            account.Id,
            new QqMailAccountDraft("QQ Home", "123456789@qq.com", ""));

        Assert.IsNotNull(updated);
        Assert.AreEqual("QQ Home", updated.DisplayName);
        Assert.AreEqual(QqMailPreset.ImapHost, updated.ImapHost);
        Assert.AreEqual(QqMailPreset.SmtpPort, updated.SmtpPort);
        Assert.AreEqual(
            "abcdefghijklmnop",
            await fixture.SecureStorage.RetrieveSecretAsync(account.CredentialHandle));
    }

    [TestMethod]
    public async Task BrowseShell_Reauthorize_replaces_refresh_keeps_Id_and_handle()
    {
        using var fixture = new DesktopAppFixture();
        fixture.OAuth.AuthorizeResult = new OAuthAuthorizationResult(
            EmailAddress: "alice@gmail.com",
            RefreshSecret: "old-refresh",
            Metadata: new OAuthTokenMetadata(
                OAuthProvider.Google,
                Authority: GoogleMailPreset.Authority,
                ClientId: "test-google-client"));
        await using var app = await fixture.OpenAppAsync();
        var shell = new BrowseShell(app);
        var account = await shell.AddGoogleAccountAsync("Gmail");
        var handle = account.CredentialHandle;

        fixture.OAuth.AuthorizeResult = new OAuthAuthorizationResult(
            EmailAddress: "alice.new@gmail.com",
            RefreshSecret: "new-refresh",
            Metadata: new OAuthTokenMetadata(
                OAuthProvider.Google,
                Authority: GoogleMailPreset.Authority,
                ClientId: "test-google-client"));

        var updated = await shell.ReauthorizeAccountAsync(account.Id);

        Assert.IsNotNull(updated);
        Assert.AreEqual(account.Id, updated.Id);
        Assert.AreEqual(handle, updated.CredentialHandle);
        Assert.AreEqual("alice.new@gmail.com", updated.EmailAddress);
        Assert.AreEqual("new-refresh", await fixture.SecureStorage.RetrieveSecretAsync(handle));
        Assert.AreEqual(account.Id, shell.Accounts.Single().Id);
        Assert.AreEqual("alice.new@gmail.com", shell.Accounts.Single().EmailAddress);
    }

    [TestMethod]
    public async Task BrowseShell_UpdateManual_on_OAuth_is_an_error()
    {
        using var fixture = new DesktopAppFixture();
        fixture.OAuth.AuthorizeResult = new OAuthAuthorizationResult(
            EmailAddress: "alice@gmail.com",
            RefreshSecret: "refresh",
            Metadata: new OAuthTokenMetadata(
                OAuthProvider.Google,
                Authority: GoogleMailPreset.Authority,
                ClientId: "test-google-client"));
        await using var app = await fixture.OpenAppAsync();
        var shell = new BrowseShell(app);
        var account = await shell.AddGoogleAccountAsync("Gmail");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await shell.UpdateManualAccountAsync(
                account.Id,
                ValidDraft("Nope", "alice@gmail.com") with { Password = "" }));
    }

    [TestMethod]
    public async Task BrowseShell_Reauthorize_on_Password_is_an_error()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var shell = new BrowseShell(app);
        var account = await shell.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await shell.ReauthorizeAccountAsync(account.Id));
    }

    [TestMethod]
    public async Task BrowseShell_Update_missing_Account_is_a_no_op()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var shell = new BrowseShell(app);

        Assert.IsNull(await shell.UpdateManualAccountAsync(
            Guid.NewGuid(),
            ValidDraft("X", "x@example.com") with { Password = "" }));
        Assert.IsNull(await shell.ReauthorizeAccountAsync(Guid.NewGuid()));
        Assert.IsEmpty(shell.Accounts);
    }

    [TestMethod]
    public async Task BrowseShell_Search_filters_current_Mailbox_by_Subject()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "keep",
                Subject: "Invoice March",
                FromAddress: "billing@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "pay"),
            new RemoteMessage(
                RemoteId: "drop",
                Subject: "Hello",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "hi"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SearchAsync("invoice");

        Assert.AreEqual("invoice", shell.SearchQuery);
        Assert.HasCount(1, shell.Messages);
        Assert.AreEqual("Invoice March", shell.Messages[0].Subject);

        await shell.SearchAsync(" ");
        Assert.HasCount(2, shell.Messages);
    }

    [TestMethod]
    public async Task BrowseShell_SelectMessage_refreshes_Mailbox_unread_count()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "unread-1",
                Subject: "Unread",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "secret"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        Assert.AreEqual(1, shell.Mailboxes.Single().UnreadCount);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);

        Assert.AreEqual(0, shell.Mailboxes.Single().UnreadCount);
        Assert.IsTrue(shell.Messages[0].IsRead);
    }

    [TestMethod]
    public async Task BrowseShell_SelectMessage_refreshes_Account_unread_count()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "unread-acct",
                Subject: "Unread",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "secret"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        Assert.AreEqual(1, shell.Accounts.Single().UnreadCount);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);

        Assert.AreEqual(0, shell.Accounts.Single().UnreadCount);
    }

    [TestMethod]
    public async Task BrowseShell_MarkSelectedUnread_restores_unread_row()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "read-1",
                Subject: "Was read",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "old"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);
        Assert.IsTrue(shell.Messages[0].IsRead);
        await shell.MarkSelectedUnreadAsync();

        Assert.IsFalse(shell.Messages[0].IsRead);
        Assert.AreEqual(1, shell.Mailboxes.Single().UnreadCount);
        Assert.AreEqual(1, shell.Accounts.Single().UnreadCount);
    }

    [TestMethod]
    public async Task BrowseShell_MarkCurrentRead_marks_Mailbox_read()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage("u1", "Unread", "a@b.com", new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero), false, "x"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();
        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        Assert.IsFalse(shell.Messages[0].IsRead);
        await shell.MarkCurrentReadAsync();
        Assert.IsTrue(shell.Messages[0].IsRead);
        Assert.AreEqual(0, shell.Mailboxes.Single().UnreadCount);
    }

    [TestMethod]
    public async Task BrowseShell_ToggleSelectedFlag_marks_Message_flagged()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "flag-1",
                Subject: "Star",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "x"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);
        await shell.ToggleSelectedFlagAsync();

        Assert.IsTrue(shell.Messages[0].IsFlagged);
        await shell.ToggleSelectedFlagAsync();
        Assert.IsFalse(shell.Messages[0].IsFlagged);
    }

    [TestMethod]
    public async Task BrowseShell_ToggleFlag_stars_a_Thread_without_opening_it()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "flag-list",
                Subject: "Star from list",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "x"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);

        Assert.IsNull(shell.SelectedThreadId);
        Assert.IsFalse(shell.Threads[0].Latest.IsFlagged);
        Assert.IsFalse(shell.Threads[0].Latest.IsRead);
        await shell.ToggleFlagAsync(shell.Threads[0].Latest.Id);

        Assert.IsTrue(shell.Threads[0].Latest.IsFlagged);
        Assert.IsFalse(shell.Threads[0].Latest.IsRead);
        Assert.IsNull(shell.SelectedThreadId);
    }

    [TestMethod]
    public async Task BrowseShell_MoveSelectedToTrash_removes_Message_from_Inbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Trash", "Trash", MailboxRole.Trash));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "del-1",
                Subject: "Delete me",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "gone"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);
        await shell.MoveSelectedToTrashAsync();

        Assert.IsEmpty(shell.Messages);
        Assert.IsNull(shell.SelectedMessageId);
        var trash = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Trash);
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, trash.Id));
    }

    [TestMethod]
    public async Task BrowseShell_MoveSelectedToMailbox_refiles_Message()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", Role: null));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "mv-1",
                Subject: "File me",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "filed"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var archive = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Name == "Archive");

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);
        var destinations = await shell.ListMoveDestinationsAsync();
        Assert.IsTrue(destinations.Any(m => m.Id == archive.Id));
        await shell.MoveSelectedToMailboxAsync(archive.Id);

        Assert.IsEmpty(shell.Messages);
        Assert.IsNull(shell.SelectedMessageId);
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, archive.Id));
    }

    [TestMethod]
    public async Task BrowseShell_CopySelectedToMailbox_keeps_source_and_adds_destination()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", Role: null));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "cp-1",
                Subject: "Keep me",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "keep"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var archive = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Name == "Archive");

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);
        var sourceId = shell.SelectedMessageId;
        await shell.CopySelectedToMailboxAsync(archive.Id);

        Assert.AreEqual(sourceId, shell.SelectedMessageId);
        Assert.HasCount(1, shell.Messages);
        Assert.AreEqual("Keep me", shell.Messages[0].Subject);
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, inbox.Id));
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, archive.Id));
        Assert.AreEqual(archive.Id, shell.RecentMoveMailboxIds[0]);
    }

    [TestMethod]
    public async Task BrowseShell_MoveSelectedToMailbox_refiles_Mailbox_reply_thread()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", Role: null));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "root")
            {
                InternetMessageId = "<root@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "reply")
            {
                InternetMessageId = "<reply@example.com>",
                References = ["<root@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var archive = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Name == "Archive");

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Messages[0].Id);
        await shell.MoveSelectedToMailboxAsync(archive.Id);

        Assert.IsEmpty(shell.Messages);
        Assert.IsNull(shell.SelectedThreadId);
        Assert.HasCount(2, await app.ListMessagesAsync(account.Id, archive.Id));
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, inbox.Id));
    }

    [TestMethod]
    public async Task BrowseShell_MoveSelectedToMailbox_from_Unified_Inbox_leaves_Inbox_view()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", Role: null));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "mv-u",
                Subject: "Leave inbox",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "out"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var archive = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Name == "Archive");

        var shell = new BrowseShell(app);
        await shell.ShowUnifiedInboxAsync();
        await shell.SelectMessageAsync(shell.Messages[0].Id);
        await shell.MoveSelectedToMailboxAsync(archive.Id);

        Assert.IsEmpty(shell.Messages);
        Assert.IsTrue(shell.ShowingUnifiedInbox);
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, archive.Id));
    }

    [TestMethod]
    public async Task BrowseShell_RestoreSelectedFromTrash_returns_Message_to_Inbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Trash", "Trash", MailboxRole.Trash));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "del-1",
                Subject: "Delete me",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "gone"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var trash = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Trash);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);
        await shell.MoveSelectedToTrashAsync();
        await shell.SelectMailboxAsync(trash.Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);
        await shell.RestoreSelectedFromTrashAsync();

        Assert.IsEmpty(shell.Messages);
        Assert.IsNull(shell.SelectedMessageId);
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, inbox.Id));
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, trash.Id));
    }

    [TestMethod]
    public async Task BrowseShell_EmptyTrash_clears_Trash_list()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Trash", "Trash", MailboxRole.Trash));
        fixture.Imap.SeedMessages(
            "Trash",
            new RemoteMessage(
                RemoteId: "gone-1",
                Subject: "Old",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "dust"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var trash = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Trash);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(trash.Id);
        Assert.HasCount(1, shell.Messages);
        await shell.EmptyTrashAsync();

        Assert.IsEmpty(shell.Messages);
        Assert.IsNull(shell.SelectedMessageId);
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, trash.Id));
    }

    [TestMethod]
    public async Task BrowseShell_EmptyJunk_clears_Junk_list()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Junk", "Junk", MailboxRole.Junk));
        fixture.Imap.SeedMessages(
            "Junk",
            new RemoteMessage(
                RemoteId: "spam-1",
                Subject: "Spam",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "buy"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var junk = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Junk);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(junk.Id);
        Assert.HasCount(1, shell.Messages);
        await shell.EmptyJunkAsync();

        Assert.IsEmpty(shell.Messages);
        Assert.IsNull(shell.SelectedMessageId);
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, junk.Id));
    }

    [TestMethod]
    public async Task BrowseShell_PermanentlyDeleteSelected_removes_Trash_Message_and_keeps_Inbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Trash", "Trash", MailboxRole.Trash));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "keep-1",
                Subject: "Keep",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "keep"));
        fixture.Imap.SeedMessages(
            "Trash",
            new RemoteMessage(
                RemoteId: "gone-1",
                Subject: "Old",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "dust"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var trash = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Trash);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(trash.Id);
        await shell.SelectThreadAsync(shell.Messages.Single().Id);
        await shell.PermanentlyDeleteSelectedAsync();

        Assert.IsEmpty(shell.Messages);
        Assert.IsNull(shell.SelectedMessageId);
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, trash.Id));
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, inbox.Id));
    }

    [TestMethod]
    public async Task BrowseShell_PermanentlyDeleteSelected_on_Thread_removes_every_Message()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Trash", "Trash", MailboxRole.Trash));
        fixture.Imap.SeedMessages(
            "Trash",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "root")
            {
                InternetMessageId = "<root@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "reply")
            {
                InternetMessageId = "<reply@example.com>",
                References = ["<root@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var trash = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Trash);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(trash.Id);
        await shell.SelectThreadAsync(shell.Messages[0].Id);
        await shell.PermanentlyDeleteSelectedAsync();

        Assert.IsEmpty(shell.Messages);
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, trash.Id));
    }

    [TestMethod]
    public async Task BrowseShell_PermanentlyDeleteSelected_selects_the_next_Thread()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Trash", "Trash", MailboxRole.Trash));
        fixture.Imap.SeedMessages(
            "Trash",
            new RemoteMessage(
                RemoteId: "newer",
                Subject: "Newer",
                FromAddress: "a@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "one"),
            new RemoteMessage(
                RemoteId: "older",
                Subject: "Older",
                FromAddress: "c@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "three"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var trash = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Trash);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(trash.Id);
        await shell.SelectThreadAsync(shell.Messages[0].Id);
        await shell.PermanentlyDeleteSelectedAsync();

        Assert.AreEqual("Older", shell.Messages.Single(message => message.Id == shell.SelectedMessageId).Subject);
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, trash.Id));
    }

    [TestMethod]
    public async Task BrowseShell_PermanentlyDeleteSelected_expunges_Inbox_Message()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Trash", "Trash", MailboxRole.Trash));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "gone-1",
                Subject: "Gone",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "bye"),
            new RemoteMessage(
                RemoteId: "keep-1",
                Subject: "Keep",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "stay"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var trash = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Trash);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        var gone = shell.Messages.Single(message => message.Subject == "Gone");
        await shell.SelectThreadAsync(gone.Id);
        await shell.PermanentlyDeleteSelectedAsync();

        Assert.AreEqual("Keep", shell.Messages.Single().Subject);
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, trash.Id));
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, inbox.Id));
    }

    [TestMethod]
    public async Task BrowseShell_MoveSelectedToJunk_removes_Message_from_Inbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Junk", "Junk", MailboxRole.Junk));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "spam-1",
                Subject: "Spam",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "buy now"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var junk = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Junk);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Messages.Single().Id);
        await shell.MoveSelectedToJunkAsync();

        Assert.IsEmpty(shell.Messages);
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, inbox.Id));
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, junk.Id));
    }

    [TestMethod]
    public async Task BrowseShell_RestoreSelectedFromJunk_returns_Message_to_Inbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Junk", "Junk", MailboxRole.Junk));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "spam-1",
                Subject: "Spam",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "buy now"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var junk = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Junk);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Messages.Single().Id);
        await shell.MoveSelectedToJunkAsync();
        await shell.SelectMailboxAsync(junk.Id);
        await shell.SelectThreadAsync(shell.Messages.Single().Id);
        await shell.RestoreSelectedFromJunkAsync();

        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, junk.Id));
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, inbox.Id));
    }

    [TestMethod]
    public async Task BrowseShell_MoveSelectedToJunk_on_Thread_moves_every_Message()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Junk", "Junk", MailboxRole.Junk));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "root")
            {
                InternetMessageId = "<root@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "reply")
            {
                InternetMessageId = "<reply@example.com>",
                References = ["<root@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var junk = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Junk);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Messages[0].Id);
        await shell.MoveSelectedToJunkAsync();

        Assert.IsEmpty(shell.Messages);
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, inbox.Id));
        Assert.HasCount(2, await app.ListMessagesAsync(account.Id, junk.Id));
    }

    [TestMethod]
    public async Task BrowseShell_SelectNextUnread_skips_read_and_selects_the_next_unread()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "newer",
                Subject: "Newer unread",
                FromAddress: "a@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "one"),
            new RemoteMessage(
                RemoteId: "mid",
                Subject: "Read",
                FromAddress: "b@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "two"),
            new RemoteMessage(
                RemoteId: "older",
                Subject: "Older unread",
                FromAddress: "c@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "three"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        Assert.AreEqual("Newer unread", shell.Messages[0].Subject);
        await shell.SelectMessageAsync(shell.Messages[0].Id);
        await shell.SelectNextUnreadAsync();

        Assert.AreEqual("Older unread", shell.Messages.Single(m => m.Id == shell.SelectedMessageId).Subject);
    }

    [TestMethod]
    public async Task BrowseShell_SelectPreviousUnread_skips_read_and_selects_the_previous_unread()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "newer",
                Subject: "Newer unread",
                FromAddress: "a@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "one"),
            new RemoteMessage(
                RemoteId: "mid",
                Subject: "Read",
                FromAddress: "b@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "two"),
            new RemoteMessage(
                RemoteId: "older",
                Subject: "Older unread",
                FromAddress: "c@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "three"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectMessageAsync(shell.Messages.Single(m => m.Subject == "Older unread").Id);
        await shell.SelectPreviousUnreadAsync();

        Assert.AreEqual("Newer unread", shell.Messages.Single(m => m.Id == shell.SelectedMessageId).Subject);
    }

    [TestMethod]
    public async Task BrowseShell_SelectNextFlagged_skips_unflagged_and_selects_the_next_flagged()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "newer",
                Subject: "Newer flagged",
                FromAddress: "a@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "one")
            {
                IsFlagged = true,
            },
            new RemoteMessage(
                RemoteId: "mid",
                Subject: "Plain",
                FromAddress: "b@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "two"),
            new RemoteMessage(
                RemoteId: "older",
                Subject: "Older flagged",
                FromAddress: "c@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "three")
            {
                IsFlagged = true,
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Threads.Single(thread => thread.Latest.Subject == "Newer flagged").Latest.Id);
        await shell.SelectNextFlaggedAsync();

        Assert.AreEqual("Older flagged", shell.Threads.Single(thread => thread.Latest.Id == shell.SelectedThreadId).Latest.Subject);
        await shell.SelectPreviousFlaggedAsync();
        Assert.AreEqual("Newer flagged", shell.Threads.Single(thread => thread.Latest.Id == shell.SelectedThreadId).Latest.Subject);
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
        Assert.IsNull(shell.BodyHtml);
        Assert.IsFalse(shell.BodyUnavailable);
        Assert.HasCount(1, shell.Attachments);
        Assert.AreEqual("notes.txt", shell.Attachments[0].FileName);
    }

    [TestMethod]
    public async Task BrowseShell_SelectMessage_exposes_To_and_Cc()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-addr",
                Subject: "Hello",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "plain")
            {
                ToAddresses = ["alice@example.com"],
                CcAddresses = ["cc@example.com"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);

        CollectionAssert.AreEqual(new[] { "alice@example.com" }, shell.SelectedToAddresses.ToArray());
        CollectionAssert.AreEqual(new[] { "cc@example.com" }, shell.SelectedCcAddresses.ToArray());
    }

    [TestMethod]
    public async Task BrowseShell_SelectMessage_loads_html_body()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-html",
                Subject: "Hello html",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "Hello world")
            {
                BodyHtml = "<p>Hello <b>world</b></p>",
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);

        Assert.AreEqual("<p>Hello <b>world</b></p>", shell.BodyHtml);
        Assert.AreEqual("Hello world", shell.BodyText);
        Assert.IsFalse(shell.BodyUnavailable);
    }

    [TestMethod]
    public async Task BrowseShell_SelectMessage_from_Unified_Inbox_loads_body_and_attachments()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-unified",
                Subject: "Unified Hello",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "unified body text")
            {
                Attachments =
                [
                    new RemoteAttachment(
                        FileName: "invoice.pdf",
                        ContentType: "application/pdf",
                        Content: "invoice-bytes"u8.ToArray()),
                ],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);

        var shell = new BrowseShell(app);
        await shell.ShowUnifiedInboxAsync();
        await shell.SelectMessageAsync(shell.Messages[0].Id);

        Assert.IsTrue(shell.ShowingUnifiedInbox);
        Assert.IsNull(shell.SelectedAccountId);
        Assert.AreEqual("unified body text", shell.BodyText);
        Assert.IsFalse(shell.BodyUnavailable);
        Assert.HasCount(1, shell.Attachments);
        Assert.AreEqual("invoice.pdf", shell.Attachments[0].FileName);
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
        Assert.IsTrue(string.IsNullOrEmpty(shell.BodyText));
        Assert.IsTrue(string.IsNullOrEmpty(shell.BodyHtml));
    }

    [TestMethod]
    public async Task BrowseShell_SelectMessage_html_only_is_available()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-html-only",
                Subject: "Html only",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: string.Empty)
            {
                BodyHtml = "<p>only html</p>",
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);

        Assert.IsFalse(shell.BodyUnavailable);
        Assert.AreEqual("<p>only html</p>", shell.BodyHtml);
    }

    [TestMethod]
    public async Task BrowseShell_conversation_html_cards_keep_snippets_and_expand_the_selected_Message()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-root",
                Subject: "Root html",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: string.Empty)
            {
                BodyHtml = "<p>older html</p>",
                InternetMessageId = "<root-html@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-reply",
                Subject: "Re: Root html",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: string.Empty)
            {
                BodyHtml = "<p>later html</p>",
                InternetMessageId = "<reply-html@example.com>",
                References = ["<root-html@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        Assert.HasCount(1, shell.Threads);
        await shell.SelectThreadAsync(shell.Threads[0].Latest.Id);
        await shell.SelectMessageAsync(shell.Threads[0].Latest.Id);
        await shell.RefreshConversationAsync();

        Assert.HasCount(2, shell.Conversation);
        var split = MailShellFormatting.SplitConversation(shell.Conversation);
        Assert.HasCount(1, split.Before);
        Assert.AreEqual("<p>older html</p>", split.Before[0].BodyHtml);
        Assert.AreEqual("older html", split.Before[0].BodyText);
        Assert.IsTrue(split.Before[0].ShowBodyText);
        Assert.IsNotNull(split.Selected);
        Assert.AreEqual("<p>later html</p>", split.Selected.BodyHtml);
        Assert.IsFalse(split.Selected.ShowBodyText);
        Assert.IsEmpty(split.After);

        await shell.SelectMessageAsync(split.Before[0].Message.Id);
        await shell.RefreshConversationAsync();
        var olderSelected = MailShellFormatting.SplitConversation(shell.Conversation);
        Assert.IsEmpty(olderSelected.Before);
        Assert.AreEqual("<p>older html</p>", olderSelected.Selected!.BodyHtml);
        Assert.IsFalse(olderSelected.Selected.ShowBodyText);
        Assert.HasCount(1, olderSelected.After);
        Assert.IsTrue(olderSelected.After[0].ShowBodyText);
        Assert.AreEqual("<p>later html</p>", olderSelected.After[0].BodyHtml);
    }

    [TestMethod]
    public async Task BrowseShell_conversation_cards_keep_attachments_on_the_owning_Message()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-root",
                Subject: "Root files",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "root")
            {
                InternetMessageId = "<root-files@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-reply",
                Subject: "Re: Root files",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "reply")
            {
                InternetMessageId = "<reply-files@example.com>",
                References = ["<root-files@example.com>"],
                Attachments =
                [
                    new RemoteAttachment(
                        FileName: "invoice.pdf",
                        ContentType: "application/pdf",
                        Content: "pdf-bytes"u8.ToArray()),
                ],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Threads[0].Latest.Id);
        await shell.SelectMessageAsync(shell.Threads[0].Latest.Id);
        await shell.RefreshConversationAsync();

        Assert.HasCount(2, shell.Conversation);
        Assert.IsEmpty(shell.Conversation[0].Attachments);
        Assert.IsTrue(shell.Conversation[0].ShowAttachmentClip is false);
        Assert.AreEqual("invoice.pdf", shell.Conversation[1].Attachments.Single().FileName);
        Assert.IsTrue(shell.Conversation[1].ShowAttachmentChips);
        Assert.IsFalse(shell.Conversation[1].ShowAttachmentClip);
        Assert.AreEqual("invoice.pdf", shell.ThreadAttachments.Single().FileName);
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
    public async Task BrowseShell_MaterializeAttachment_writes_a_temp_file()
    {
        using var fixture = new DesktopAppFixture();
        var payload = "drag-payload"u8.ToArray();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-drag",
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

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);
        var path = await shell.MaterializeAttachmentAsync(shell.Attachments[0].Id);

        Assert.IsNotNull(path);
        Assert.AreEqual("report.pdf", Path.GetFileName(path));
        CollectionAssert.AreEqual(payload, await File.ReadAllBytesAsync(path));
        Assert.IsNull(shell.AttachmentOpenError);
    }

    [TestMethod]
    public async Task BrowseShell_OpenFirstThreadAttachment_opens_the_latest_file()
    {
        using var fixture = new DesktopAppFixture();
        var payload = "clip-payload"u8.ToArray();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-clip",
                Subject: "Has file",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "see file")
            {
                Attachments =
                [
                    new RemoteAttachment(
                        FileName: "notes.txt",
                        ContentType: "text/plain",
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
                shell.OpenFirstThreadAttachmentAsync(shell.Threads[0].Latest.Id).GetAwaiter().GetResult();

                Assert.IsNull(shell.AttachmentOpenError);
                Assert.AreEqual("notes.txt", fakeOpen.LastFileName);
                Assert.AreEqual("text/plain", fakeOpen.LastContentType);
                CollectionAssert.AreEqual(payload, fakeOpen.LastContent);
            }
            finally
            {
                HostBootstrap.OpenDownloadedAttachment = previous;
            }
        }
    }

    [TestMethod]
    public async Task BrowseShell_SaveAttachment_writes_bytes_without_opening()
    {
        using var fixture = new DesktopAppFixture();
        var payload = "save-payload"u8.ToArray();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-save",
                Subject: "Has file",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "see file")
            {
                Attachments =
                [
                    new RemoteAttachment(
                        FileName: "notes.txt",
                        ContentType: "text/plain",
                        Content: payload),
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
        using var saved = new MemoryStream();
        await shell.SaveAttachmentAsync(shell.Attachments[0].Id, saved);

        Assert.IsNull(shell.AttachmentOpenError);
        CollectionAssert.AreEqual(payload, saved.ToArray());
    }

    [TestMethod]
    public async Task BrowseShell_SaveAllAttachments_writes_each_file_and_renames_collisions()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-save-all",
                Subject: "Has files",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "see files")
            {
                Attachments =
                [
                    new RemoteAttachment(
                        FileName: "notes.txt",
                        ContentType: "text/plain",
                        Content: "one"u8.ToArray()),
                    new RemoteAttachment(
                        FileName: "notes.txt",
                        ContentType: "text/plain",
                        Content: "two"u8.ToArray()),
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
        var directory = Path.Combine(Path.GetTempPath(), "mailtide-save-all-" + Guid.NewGuid().ToString("N"));
        try
        {
            var saved = await shell.SaveAllAttachmentsAsync(directory);
            Assert.AreEqual(2, saved);
            var texts = new HashSet<string>
            {
                await File.ReadAllTextAsync(Path.Combine(directory, "notes.txt")),
                await File.ReadAllTextAsync(Path.Combine(directory, "notes (1).txt")),
            };
            Assert.IsTrue(texts.SetEquals(["one", "two"]));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task BrowseShell_SaveAttachment_surfaces_error_when_Attachment_is_missing()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var shell = new BrowseShell(app);
        using var saved = new MemoryStream();
        await shell.SaveAttachmentAsync(Guid.NewGuid(), saved);
        Assert.AreEqual("Attachment is not available.", shell.AttachmentOpenError);
        Assert.AreEqual(0, saved.Length);
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
                Assert.DoesNotContain(
                    "native boom",
                    shell.AttachmentOpenError!,
                    StringComparison.Ordinal);
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

    [TestMethod]
    public async Task BrowseShell_refreshes_Unified_Inbox_after_AccountWorkCompleted()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "fg-ui-1",
                Subject: "Self-driven",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 3, 8, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "hi"));
        await using var app = await fixture.OpenAppAsync();
        await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var browse = new BrowseShell(app);
        await browse.ShowUnifiedInboxAsync();
        Assert.IsEmpty(browse.Messages);

        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.AccountWorkCompleted += async (_, _) =>
        {
            await browse.RefreshAfterAccountWorkAsync();
            refreshed.TrySetResult();
        };
        app.StartForegroundSync(TimeSpan.FromHours(1));
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.HasCount(1, browse.Messages);
        Assert.AreEqual("Self-driven", browse.Messages[0].Subject);
        Assert.IsTrue(browse.ShowingUnifiedInbox);
    }

    [TestMethod]
    public async Task BrowseShell_OpenArrivedMessage_selects_the_Inbox_Message()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "seed",
                Subject: "Seed",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "old"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);

        InboxArrival? arrival = null;
        app.InboxMessageArrived += (_, value) => arrival = value;
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "seed",
                Subject: "Seed",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "old"),
            new RemoteMessage(
                RemoteId: "new-1",
                Subject: "Just arrived",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 4, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "hi"));
        await app.SyncNowAsync(account.Id);
        Assert.IsNotNull(arrival);

        var shown = new List<InboxArrivalNotification>();
        var notifier = new DesktopNotifyInboxArrival(show: shown.Add);
        await notifier.ShowAsync(
            new InboxArrivalNotification(
                arrival.MessageId,
                arrival.AccountId,
                arrival.MailboxId,
                arrival.Subject,
                arrival.FromAddress,
                arrival.AccountDisplayName));
        Assert.HasCount(1, shown);

        var shell = new BrowseShell(app);
        await shell.OpenArrivedMessageAsync(arrival.AccountId, arrival.MailboxId, arrival.MessageId);

        Assert.AreEqual(arrival.AccountId, shell.SelectedAccountId);
        Assert.AreEqual(arrival.MailboxId, shell.SelectedMailboxId);
        Assert.AreEqual(arrival.MessageId, shell.SelectedMessageId);
        Assert.AreEqual("Just arrived", shell.Messages.Single(m => m.Id == arrival.MessageId).Subject);
    }

    [TestMethod]
    public async Task BrowseShell_SelectMessage_marks_unread_Message_read()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "uid-9",
                Subject: "Please read me",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "secret"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        Assert.IsFalse(shell.Messages[0].IsRead);

        await shell.SelectMessageAsync(shell.Messages[0].Id);

        Assert.IsTrue(shell.Messages[0].IsRead);
        Assert.IsTrue((await app.ListMessagesAsync(account.Id, inbox.Id)).Single().IsRead);
    }
    [TestMethod]
    public async Task BrowseShell_MoveSelectedToMailbox_from_Unified_Inbox_moves_the_Thread()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", Role: null));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "root")
            {
                InternetMessageId = "<root@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "reply")
            {
                InternetMessageId = "<reply@example.com>",
                References = ["<root@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var archive = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Name == "Archive");

        var shell = new BrowseShell(app);
        await shell.ShowUnifiedInboxAsync();
        await shell.SelectThreadAsync(shell.Messages[0].Id);
        await shell.MoveSelectedToMailboxAsync(archive.Id);

        Assert.IsTrue(shell.ShowingUnifiedInbox);
        Assert.IsEmpty(await app.ListUnifiedInboxAsync());
        Assert.HasCount(2, await app.ListMessagesAsync(account.Id, archive.Id));
    }

    [TestMethod]
    public async Task BrowseShell_MarkSelectedUnread_on_Thread_marks_every_Message()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "root")
            {
                InternetMessageId = "<root@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "reply")
            {
                InternetMessageId = "<reply@example.com>",
                References = ["<root@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Messages[0].Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);
        await shell.MarkSelectedUnreadAsync();

        Assert.IsTrue(shell.Messages.All(message => !message.IsRead));
        Assert.IsTrue(shell.Threads[0].Messages.All(message => !message.IsRead));
        Assert.IsTrue((await app.ListMessagesAsync(account.Id, inbox.Id)).All(message => !message.IsRead));
    }

    [TestMethod]
    public async Task BrowseShell_ToggleSelectedRead_marks_unread_Thread_read()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "root")
            {
                InternetMessageId = "<root@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "reply")
            {
                InternetMessageId = "<reply@example.com>",
                References = ["<root@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Messages[0].Id);
        await shell.SelectMessageAsync(shell.Messages[0].Id);
        await shell.MarkSelectedUnreadAsync();
        await shell.ToggleSelectedReadAsync();

        Assert.IsTrue(shell.Messages.All(message => message.IsRead));
        Assert.IsTrue((await app.ListMessagesAsync(account.Id, inbox.Id)).All(message => message.IsRead));
    }

    [TestMethod]
    public async Task BrowseShell_MarkSelectedRead_marks_the_Thread_read()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "u-1",
                Subject: "Unread",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "body"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Messages[0].Id);
        await shell.MarkSelectedUnreadAsync();
        await shell.MarkSelectedReadAsync();

        Assert.IsTrue(shell.Messages.Single().IsRead);
        Assert.AreEqual(0, shell.Mailboxes.Single().UnreadCount);
        await shell.MarkSelectedReadAsync();
        Assert.IsTrue(shell.Messages.Single().IsRead);
    }

    [TestMethod]
    public async Task BrowseShell_SetConversationExpanded_shows_other_bodies()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "older body")
            {
                InternetMessageId = "<root@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "newer body")
            {
                InternetMessageId = "<reply@example.com>",
                References = ["<root@example.com>"],
            },
            new RemoteMessage(
                RemoteId: "other",
                Subject: "Other",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "other"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        var root = shell.Threads.Single(thread => thread.Latest.Subject == "Re: Root" || thread.Latest.Subject == "Root");
        await shell.SelectThreadAsync(root.Latest.Id);
        await shell.SelectMessageAsync(root.Latest.Id);
        await shell.RefreshConversationAsync();
        Assert.IsFalse(shell.ConversationExpanded);
        Assert.AreEqual(1, shell.Conversation.Count(card => card.ShowBodyText));
        Assert.AreEqual(0, shell.Conversation.Count(card => card.ShowExpandedBody));

        await shell.SetConversationExpandedAsync(true);
        Assert.IsTrue(shell.ConversationExpanded);
        Assert.AreEqual(0, shell.Conversation.Count(card => card.ShowBodyText));
        Assert.AreEqual(1, shell.Conversation.Count(card => card.ShowExpandedBody));
        Assert.AreEqual("older body", shell.Conversation.Single(card => card.ShowExpandedBody).ExpandedBody);

        await shell.SelectThreadAsync(root.Latest.Id);
        Assert.IsTrue(shell.ConversationExpanded);

        var other = shell.Threads.Single(thread => thread.Latest.Subject == "Other");
        await shell.SelectThreadAsync(other.Latest.Id);
        Assert.IsFalse(shell.ConversationExpanded);

        await shell.SelectThreadAsync(root.Latest.Id);
        Assert.IsTrue(shell.ConversationExpanded);

        await shell.SetConversationExpandedAsync(false);
        await shell.SelectThreadAsync(other.Latest.Id);
        await shell.SelectThreadAsync(root.Latest.Id);
        Assert.IsFalse(shell.ConversationExpanded);
    }

    [TestMethod]
    public async Task BrowseShell_ConversationNewestFirst_reverses_cards_and_persists()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "older body")
            {
                InternetMessageId = "<root-order@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "newer body")
            {
                InternetMessageId = "<reply-order@example.com>",
                References = ["<root-order@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Threads[0].Latest.Id);
        await shell.SelectMessageAsync(shell.Threads[0].Latest.Id);
        await shell.RefreshConversationAsync();
        Assert.AreEqual("older body", shell.Conversation[0].BodyText);
        Assert.AreEqual("newer body", shell.Conversation[1].BodyText);

        shell.ConversationNewestFirst = true;
        await shell.PersistConversationOrderAsync();
        await shell.RefreshConversationAsync();
        Assert.AreEqual("newer body", shell.Conversation[0].BodyText);
        Assert.AreEqual("older body", shell.Conversation[1].BodyText);

        var restored = new BrowseShell(app);
        await restored.RestoreBrowseAsync();
        Assert.IsTrue(restored.ConversationNewestFirst);
    }

    public async Task BrowseShell_MoveSelectedToTrash_selects_the_next_Thread()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Trash", "Trash", MailboxRole.Trash));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "newer",
                Subject: "Newer",
                FromAddress: "a@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "one"),
            new RemoteMessage(
                RemoteId: "older",
                Subject: "Older",
                FromAddress: "c@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "three"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Messages[0].Id);
        await shell.MoveSelectedToTrashAsync();

        Assert.AreEqual("Older", shell.Messages.Single(message => message.Id == shell.SelectedMessageId).Subject);
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, inbox.Id));
    }

    [TestMethod]
    public async Task BrowseShell_UndoLastTrash_restores_the_Thread()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Trash", "Trash", MailboxRole.Trash));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "keep",
                Subject: "Keep",
                FromAddress: "a@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "one"),
            new RemoteMessage(
                RemoteId: "gone",
                Subject: "Gone",
                FromAddress: "c@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "three"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var trash = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Trash);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        var gone = shell.Messages.Single(message => message.Subject == "Gone");
        await shell.SelectThreadAsync(gone.Id);
        await shell.MoveSelectedToTrashAsync();
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, trash.Id));

        Assert.IsTrue(await shell.UndoLastTrashAsync());
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, trash.Id));
        Assert.HasCount(2, await app.ListMessagesAsync(account.Id, inbox.Id));
        Assert.AreEqual("Gone", shell.Messages.Single(message => message.Id == shell.SelectedMessageId).Subject);
        Assert.IsFalse(await shell.UndoLastTrashAsync());
    }

    [TestMethod]
    public async Task BrowseShell_UndoLastTrash_undoes_a_Move()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", Role: null));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Keep",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "keep"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);
        var archive = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Name == "Archive");

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Threads[0].Latest.Id);
        await shell.MoveSelectedToMailboxAsync(archive.Id);

        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, inbox.Id));
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, archive.Id));
        Assert.AreEqual(archive.Id, shell.RecentMoveMailboxIds[0]);

        Assert.IsTrue(await shell.UndoLastTrashAsync());
        Assert.AreEqual("Move undone.", shell.LastRelocateUndoStatus);
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, inbox.Id));
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, archive.Id));
        Assert.AreEqual("Keep", shell.Messages.Single(message => message.Id == shell.SelectedMessageId).Subject);
    }

    [TestMethod]
    public async Task BrowseShell_UndoLastTrash_undoes_Junk()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Junk", "Junk", MailboxRole.Junk));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "spam",
                Subject: "Spam",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "buy"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);
        var junk = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Junk);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Threads[0].Latest.Id);
        await shell.MoveSelectedToJunkAsync();
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, junk.Id));

        Assert.IsTrue(await shell.UndoLastTrashAsync());
        Assert.AreEqual("Restored from Junk.", shell.LastRelocateUndoStatus);
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, junk.Id));
        Assert.HasCount(1, await app.ListMessagesAsync(account.Id, inbox.Id));
    }

    [TestMethod]
    public async Task BrowseShell_ListMoveDestinations_puts_recent_Mailbox_first()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", Role: null),
            new RemoteMailbox("Later", "Later", Role: null));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Keep",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "keep"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);
        var later = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Name == "Later");

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Threads[0].Latest.Id);
        await shell.MoveSelectedToMailboxAsync(later.Id);

        var destinations = await shell.ListMoveDestinationsAsync();
        Assert.AreEqual("Later", destinations[0].Name);
    }

    [TestMethod]
    public async Task BrowseShell_SelectThread_marks_every_unread_Message_read()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "root")
            {
                InternetMessageId = "<root@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "reply")
            {
                InternetMessageId = "<reply@example.com>",
                References = ["<root@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Messages[0].Id);

        Assert.IsTrue(shell.Messages.All(message => message.IsRead));
        Assert.IsTrue((await app.ListMessagesAsync(account.Id, inbox.Id)).All(message => message.IsRead));
    }

    [TestMethod]
    public async Task BrowseShell_MoveSelectedToTrash_on_Thread_moves_every_Message()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Trash", "Trash", MailboxRole.Trash));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "root")
            {
                InternetMessageId = "<root@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "reply")
            {
                InternetMessageId = "<reply@example.com>",
                References = ["<root@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Inbox);
        var trash = (await app.ListMailboxesAsync(account.Id)).Single(m => m.Role == MailboxRole.Trash);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Messages[0].Id);
        await shell.MoveSelectedToTrashAsync();

        Assert.IsEmpty(shell.Messages);
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, inbox.Id));
        Assert.HasCount(2, await app.ListMessagesAsync(account.Id, trash.Id));
    }

    [TestMethod]
    public async Task BrowseShell_SelectNextUnread_after_Thread_moves_to_the_next_Thread()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "newer",
                Subject: "Newer unread",
                FromAddress: "a@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "one"),
            new RemoteMessage(
                RemoteId: "older",
                Subject: "Older unread",
                FromAddress: "c@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "three"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Messages[0].Id);
        await shell.SelectNextUnreadAsync();

        Assert.AreEqual("Older unread", shell.Messages.Single(m => m.Id == shell.SelectedMessageId).Subject);
    }

    [TestMethod]
    public async Task BrowseShell_SelectNextUnread_crosses_to_the_next_Mailbox_with_unread()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", Role: null));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "inbox-unread",
                Subject: "Inbox unread",
                FromAddress: "a@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "one"));
        fixture.Imap.SeedMessages(
            "Archive",
            new RemoteMessage(
                RemoteId: "archive-unread",
                Subject: "Archive unread",
                FromAddress: "b@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "two"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);
        var archive = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Name == "Archive");

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Threads[0].Latest.Id);
        await shell.SelectNextUnreadAsync();

        Assert.AreEqual(archive.Id, shell.SelectedMailboxId);
        Assert.AreEqual("Archive unread", shell.Threads.Single(thread => thread.Latest.Id == shell.SelectedThreadId).Latest.Subject);
    }

    [TestMethod]
    public async Task BrowseShell_unread_chip_survives_Mailbox_switch_but_text_search_does_not()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", Role: null));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "inbox-unread",
                Subject: "Inbox unread",
                FromAddress: "a@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "one"),
            new RemoteMessage(
                RemoteId: "inbox-read",
                Subject: "Inbox read",
                FromAddress: "a@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "two"));
        fixture.Imap.SeedMessages(
            "Archive",
            new RemoteMessage(
                RemoteId: "archive-unread",
                Subject: "Archive unread",
                FromAddress: "b@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "three"),
            new RemoteMessage(
                RemoteId: "archive-read",
                Subject: "Archive read",
                FromAddress: "b@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 7, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "four"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);
        var archive = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Name == "Archive");

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SearchAsync("is:unread");
        Assert.AreEqual("is:unread", shell.SearchQuery);
        Assert.IsTrue(shell.Messages.All(message => !message.IsRead));

        await shell.SelectMailboxAsync(archive.Id);
        Assert.AreEqual("is:unread", shell.SearchQuery);
        Assert.AreEqual("Archive unread", shell.Messages.Single().Subject);

        await shell.SearchAsync("read");
        await shell.SelectMailboxAsync(inbox.Id);
        Assert.AreEqual(string.Empty, shell.SearchQuery);
        Assert.HasCount(2, shell.Threads);
    }

    [TestMethod]
    public async Task BrowseShell_unread_chip_does_not_mark_the_restored_Thread_read()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", Role: null));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "inbox-unread",
                Subject: "Inbox unread",
                FromAddress: "a@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "one"));
        fixture.Imap.SeedMessages(
            "Archive",
            new RemoteMessage(
                RemoteId: "archive-unread",
                Subject: "Archive unread",
                FromAddress: "b@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "three"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);
        var archive = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Name == "Archive");

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(archive.Id);
        await shell.SelectThreadAsync(shell.Threads.Single(thread => thread.Latest.Subject == "Archive unread").Latest.Id);
        await shell.MarkSelectedUnreadAsync();
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SearchAsync("is:unread");
        await shell.SelectMailboxAsync(archive.Id);

        Assert.AreEqual("is:unread", shell.SearchQuery);
        Assert.AreEqual("Archive unread", shell.Messages.Single().Subject);
        Assert.IsFalse(shell.Messages.Single().IsRead);
    }

    [TestMethod]
    public async Task BrowseShell_clearing_Search_restores_the_selected_Thread()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "keep",
                Subject: "Keep me",
                FromAddress: "a@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "one"),
            new RemoteMessage(
                RemoteId: "other",
                Subject: "Other",
                FromAddress: "c@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "three"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        var keep = shell.Messages.Single(message => message.Subject == "Keep me");
        await shell.SelectThreadAsync(keep.Id);
        await shell.SearchAsync("no-such-token-xyz");
        Assert.IsEmpty(shell.Messages);
        Assert.IsNull(shell.SelectedThreadId);

        await shell.SearchAsync("");
        Assert.AreEqual(keep.Id, shell.SelectedThreadId);
        Assert.AreEqual("Keep me", shell.Messages.Single(message => message.Id == shell.SelectedMessageId).Subject);
    }

    [TestMethod]
    public async Task BrowseShell_Search_on_Outbox_keeps_Outbox_scope()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.ShowOutboxAsync(account.Id);
        await shell.SearchAsync("invoice");

        Assert.IsTrue(shell.ShowingOutbox);
        Assert.AreEqual(account.Id, shell.SelectedAccountId);
        Assert.IsEmpty(shell.Threads);
    }

    [TestMethod]
    public async Task BrowseShell_Search_hit_conversation_is_not_every_hit()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "a",
                Subject: "invoice one",
                FromAddress: "a@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "first invoice"),
            new RemoteMessage(
                RemoteId: "b",
                Subject: "invoice two",
                FromAddress: "b@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "second invoice"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SearchAsync("invoice");
        Assert.HasCount(2, shell.Messages);
        Assert.HasCount(2, shell.Threads);
        await shell.OpenSearchHitAsync(shell.Messages[0].Id);

        Assert.HasCount(1, shell.Conversation);
        Assert.AreEqual(shell.Messages[0].Id, shell.Conversation[0].Message.Id);
        Assert.HasCount(2, shell.Messages);
        Assert.HasCount(2, shell.Threads);
    }

    [TestMethod]
    public async Task BrowseShell_Search_hit_opens_the_reply_Thread()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root invoice",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "root")
            {
                InternetMessageId = "<root@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root invoice",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "reply")
            {
                InternetMessageId = "<reply@example.com>",
                References = ["<root@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SearchAsync("invoice");
        Assert.HasCount(2, shell.Messages);
        Assert.HasCount(1, shell.Threads);
        Assert.HasCount(2, shell.Threads[0].Messages);
        var hit = shell.Messages.Single(message => message.Subject.StartsWith("Re:"));
        await shell.OpenSearchHitAsync(hit.Id);

        Assert.HasCount(2, shell.Conversation);
        Assert.HasCount(2, shell.Messages);
        Assert.HasCount(1, shell.Threads);
    }

    [TestMethod]
    public async Task BrowseShell_Search_thread_row_opens_like_a_Mailbox_Thread()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root invoice",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "root")
            {
                InternetMessageId = "<root-search-row@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root invoice",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "reply")
            {
                InternetMessageId = "<reply-search-row@example.com>",
                References = ["<root-search-row@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SearchAsync("invoice");
        await shell.OpenListedThreadAsync(shell.Threads.Single().Latest.Id);

        Assert.AreEqual("invoice", shell.SearchQuery);
        Assert.AreEqual(shell.Threads.Single().Latest.Id, shell.SelectedThreadId);
        Assert.HasCount(2, shell.Messages);
        Assert.HasCount(2, shell.Conversation);
        Assert.AreEqual(shell.Threads.Single().Latest.Id, shell.SelectedMessageId);
    }

    [TestMethod]
    public async Task BrowseShell_Search_thread_row_opens_the_matching_Message()
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
                IsRead: true,
                BodyText: new string('x', 200) + " secret-token in the root")
            {
                InternetMessageId = "<root-search-match@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Hello",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "thanks")
            {
                InternetMessageId = "<reply-search-match@example.com>",
                References = ["<root-search-match@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SearchAsync("secret-token");
        Assert.HasCount(1, shell.Threads);
        Assert.HasCount(1, shell.Messages);
        var match = shell.MatchingSearchMessage(shell.Threads[0]);
        Assert.IsNotNull(match);
        Assert.AreEqual("Hello", match!.Subject);
        StringAssert.Contains(match.Preview, "secret-token");
        Assert.AreNotEqual(shell.Threads[0].Latest.Id, match.Id);

        await shell.OpenListedThreadAsync(shell.Threads[0].Latest.Id);

        Assert.AreEqual(match.Id, shell.SelectedMessageId);
        var selected = shell.Conversation.Single(card => card.Message.Id == match.Id);
        Assert.IsTrue(selected.IsSelected);
        StringAssert.Contains(selected.Snippet, "secret-token");
    }

    [TestMethod]
    public async Task BrowseShell_Search_hit_can_select_a_sibling_in_the_Thread()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root thread",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "root")
            {
                InternetMessageId = "<root-sibling@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root thread",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "secret-reply")
            {
                InternetMessageId = "<reply-sibling@example.com>",
                References = ["<root-sibling@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SearchAsync("secret-reply");
        Assert.HasCount(1, shell.Messages);
        Assert.HasCount(1, shell.Threads);
        Assert.HasCount(2, shell.Threads[0].Messages);
        var hit = shell.Messages[0];
        await shell.OpenSearchHitAsync(hit.Id);

        Assert.HasCount(2, shell.Conversation);
        Assert.HasCount(1, shell.Messages);
        var sibling = shell.Conversation.Single(card => card.Message.Id != hit.Id).Message;
        await shell.SelectMessageAsync(sibling.Id);
        await shell.RefreshConversationAsync();

        Assert.AreEqual(sibling.Id, shell.SelectedMessageId);
        Assert.IsTrue(shell.Conversation.Single(card => card.Message.Id == sibling.Id).IsSelected);
        Assert.AreEqual(hit.Id, shell.Messages[0].Id);
    }

    [TestMethod]
    public async Task BrowseShell_Search_hit_Trash_moves_the_whole_Thread()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Trash", "Trash", MailboxRole.Trash));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Root thread",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "root")
            {
                InternetMessageId = "<root-search-trash@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: Root thread",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "secret-reply")
            {
                InternetMessageId = "<reply-search-trash@example.com>",
                References = ["<root-search-trash@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);
        var trash = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Trash);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SearchAsync("secret-reply");
        var hit = shell.Messages.Single();
        await shell.OpenSearchHitAsync(hit.Id);
        await shell.MoveSelectedToTrashAsync();

        Assert.AreEqual("secret-reply", shell.SearchQuery);
        Assert.IsEmpty(shell.Messages);
        Assert.IsEmpty(await app.ListMessagesAsync(account.Id, inbox.Id));
        Assert.HasCount(2, await app.ListMessagesAsync(account.Id, trash.Id));
    }

    [TestMethod]
    public async Task BrowseShell_Search_hit_Trash_keeps_search_and_opens_the_next_hit()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Trash", "Trash", MailboxRole.Trash));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "keep",
                Subject: "keep unique",
                FromAddress: "keep@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "keep"),
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "trash unique",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "root")
            {
                InternetMessageId = "<root-search-next@example.com>",
            },
            new RemoteMessage(
                RemoteId: "m-2",
                Subject: "Re: trash unique",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "reply")
            {
                InternetMessageId = "<reply-search-next@example.com>",
                References = ["<root-search-next@example.com>"],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SearchAsync("unique");
        var trashHit = shell.Messages.First(message => message.Subject.Contains("trash"));
        await shell.OpenSearchHitAsync(trashHit.Id);
        await shell.MoveSelectedToTrashAsync();

        Assert.AreEqual("unique", shell.SearchQuery);
        Assert.HasCount(1, shell.Messages);
        Assert.AreEqual("keep unique", shell.Messages[0].Subject);
        Assert.AreEqual(shell.Messages[0].Id, shell.SelectedMessageId);
        Assert.IsTrue(await shell.UndoLastTrashAsync());
        Assert.AreEqual("unique", shell.SearchQuery);
        Assert.IsTrue(shell.Messages.Any(message => message.Subject.Contains("trash")));
    }

    [TestMethod]
    public async Task BrowseShell_ShowOutbox_is_not_a_Mailbox()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.ShowOutboxAsync(account.Id);

        Assert.IsTrue(shell.ShowingOutbox);
        Assert.IsFalse(shell.ShowingUnifiedInbox);
        Assert.AreEqual(account.Id, shell.SelectedAccountId);
        Assert.IsNull(shell.SelectedMailboxId);
        Assert.IsEmpty(shell.Threads);
        Assert.AreEqual(0, shell.OutboxCounts[account.Id]);
    }

    [TestMethod]
    public async Task BrowseShell_BuildNavItems_nests_Mailboxes_and_Outbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();

        var nav = shell.BuildNavItems();
        Assert.AreEqual(ShellNavKind.Favorites, nav[0].Kind);
        Assert.AreEqual(ShellNavKind.UnifiedInbox, nav[1].Kind);
        Assert.AreEqual(account.Id, nav[2].AccountId);
        Assert.IsTrue(nav[2].Children.Any(child => child.Kind == ShellNavKind.Mailbox && child.Role == MailboxRole.Inbox));
        Assert.IsTrue(nav[2].Children.Any(child => child.Kind == ShellNavKind.Outbox));
    }

    [TestMethod]
    public async Task BrowseShell_BuildNavItems_nests_IMAP_child_Mailboxes()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Work", "INBOX/Work", null));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        var accountNav = shell.BuildNavItems().Single(item => item.AccountId == account.Id);
        var inbox = accountNav.Children.Single(child => child.Role == MailboxRole.Inbox);
        Assert.AreEqual("Work", inbox.Children.Single(child => child.Kind == ShellNavKind.Mailbox).Title);
        Assert.IsTrue(accountNav.Children.Any(child => child.Kind == ShellNavKind.Outbox));
    }

    [TestMethod]
    public async Task BrowseShell_restores_list_sort_and_last_Mailbox()
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
        var sent = shell.Mailboxes.Single(mailbox => mailbox.Role == MailboxRole.Sent);
        await shell.SelectMailboxAsync(sent.Id);
        shell.ListNewestFirst = false;
        await shell.PersistListSortAsync();

        var restored = new BrowseShell(app);
        await restored.LoadAccountsAsync();
        await restored.RestoreBrowseAsync();

        Assert.IsFalse(restored.ListNewestFirst);
        Assert.AreEqual(ThreadListSort.Oldest, restored.ListSort);
        Assert.IsFalse(restored.ShowingUnifiedInbox);
        Assert.AreEqual(account.Id, restored.SelectedAccountId);
        Assert.AreEqual(sent.Id, restored.SelectedMailboxId);

        shell.ListSort = ThreadListSort.From;
        await shell.PersistListSortAsync();
        var fromSort = new BrowseShell(app);
        await fromSort.LoadAccountsAsync();
        await fromSort.RestoreBrowseAsync();
        Assert.AreEqual(ThreadListSort.From, fromSort.ListSort);
    }

    [TestMethod]
    public async Task BrowseShell_restores_Outbox_nav()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.ShowOutboxAsync(account.Id);

        var restored = new BrowseShell(app);
        await restored.LoadAccountsAsync();
        await restored.RestoreBrowseAsync();

        Assert.IsTrue(restored.ShowingOutbox);
        Assert.AreEqual(account.Id, restored.SelectedAccountId);
        Assert.IsFalse(restored.ShowingUnifiedInbox);
    }

    [TestMethod]
    public async Task BrowseShell_SelectMailbox_first_visit_does_not_auto_select_a_Thread()
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
                IsRead: true,
                BodyText: "hi"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);

        Assert.IsNull(shell.SelectedThreadId);
        Assert.HasCount(1, shell.Threads);
    }

    [TestMethod]
    public async Task BrowseShell_restores_last_Thread_when_returning_to_a_Mailbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "in-1",
                Subject: "Keep me",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "keep")
            {
                InternetMessageId = "<keep@example.com>",
            },
            new RemoteMessage(
                RemoteId: "in-2",
                Subject: "Other",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "other")
            {
                InternetMessageId = "<other@example.com>",
            });
        fixture.Imap.SeedMessages(
            "Sent",
            new RemoteMessage(
                RemoteId: "s-1",
                Subject: "Sent",
                FromAddress: "alice@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "sent")
            {
                InternetMessageId = "<sent@example.com>",
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);
        var sent = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Sent);

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        var kept = shell.Threads.Single(thread => thread.Latest.Subject == "Keep me");
        await shell.SelectThreadAsync(kept.Latest.Id);
        Assert.AreEqual(kept.Latest.Id, shell.SelectedThreadId);

        await shell.SelectMailboxAsync(sent.Id);
        Assert.AreNotEqual(kept.Latest.Id, shell.SelectedThreadId);

        await shell.SelectMailboxAsync(inbox.Id);
        Assert.AreEqual(kept.Latest.Id, shell.SelectedThreadId);
        Assert.AreEqual("Keep me", shell.Conversation.Single(card => card.IsSelected).Message.Subject);
    }

    [TestMethod]
    public async Task BrowseShell_RestoreBrowse_restores_last_Thread_in_the_Mailbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Keep me",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "keep"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Threads[0].Latest.Id);
        var kept = shell.SelectedThreadId;

        var restored = new BrowseShell(app);
        await restored.LoadAccountsAsync();
        await restored.RestoreBrowseAsync();

        Assert.AreEqual(inbox.Id, restored.SelectedMailboxId);
        Assert.AreEqual(kept, restored.SelectedThreadId);
    }

    [TestMethod]
    public async Task BrowseShell_RevealInMailbox_does_not_open_a_different_remembered_Thread()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "older",
                Subject: "Older",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "old")
            {
                InternetMessageId = "<older@example.com>",
            },
            new RemoteMessage(
                RemoteId: "newer",
                Subject: "Newer",
                FromAddress: "carol@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero),
                IsRead: false,
                BodyText: "new")
            {
                InternetMessageId = "<newer@example.com>",
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        var older = shell.Threads.Single(thread => thread.Latest.Subject == "Older");
        await shell.SelectThreadAsync(older.Latest.Id);
        await shell.MarkSelectedUnreadAsync();

        await shell.ShowUnifiedInboxAsync();
        var newer = shell.Threads.Single(thread => thread.Latest.Subject == "Newer");
        await shell.SelectThreadAsync(newer.Latest.Id);
        await shell.RevealInMailboxAsync();

        Assert.AreEqual(inbox.Id, shell.SelectedMailboxId);
        Assert.AreEqual(newer.Latest.Id, shell.SelectedThreadId);
        var olderAfter = (await app.ListMessagesAsync(account.Id, inbox.Id))
            .Single(message => message.Subject == "Older");
        Assert.IsFalse(olderAfter.IsRead);
    }

    [TestMethod]
    public async Task BrowseShell_GoToMailbox_selects_the_Mailbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var sent = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Sent);

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.ShowUnifiedInboxAsync();
        await shell.GoToMailboxAsync(sent.Id);

        Assert.AreEqual(sent.Id, shell.SelectedMailboxId);
        Assert.AreEqual(account.Id, shell.SelectedAccountId);
        Assert.IsFalse(shell.ShowingUnifiedInbox);
    }

    [TestMethod]
    public async Task BrowseShell_GoToMailbox_same_Mailbox_keeps_search()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Hello",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "hi"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SearchAsync("Hello");
        Assert.AreEqual("Hello", shell.SearchQuery);

        await shell.GoToMailboxAsync(inbox.Id);
        Assert.AreEqual("Hello", shell.SearchQuery);
        Assert.AreEqual(inbox.Id, shell.SelectedMailboxId);
    }

    [TestMethod]
    public async Task BrowseShell_GoToInbox_opens_the_Account_Inbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);
        var sent = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Sent);

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(sent.Id);
        await shell.GoToInboxAsync();

        Assert.AreEqual(inbox.Id, shell.SelectedMailboxId);
        Assert.IsFalse(shell.ShowingUnifiedInbox);
        Assert.IsFalse(shell.ShowingOutbox);
        Assert.IsTrue(shell.IsOnAccountInbox());
        await shell.SelectMailboxAsync(sent.Id);
        Assert.IsFalse(shell.IsOnAccountInbox());
    }

    [TestMethod]
    public async Task BrowseShell_GoToInbox_from_Unified_Inbox_stays_put()
    {
        using var fixture = new DesktopAppFixture();
        await using var app = await fixture.OpenAppAsync();
        await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.ShowUnifiedInboxAsync();
        await shell.GoToInboxAsync();

        Assert.IsTrue(shell.ShowingUnifiedInbox);
        await shell.GoToUnifiedInboxAsync();
        Assert.IsTrue(shell.ShowingUnifiedInbox);
    }

    [TestMethod]
    public async Task BrowseShell_GoToAccountIndex_opens_that_Account_Inbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        await using var app = await fixture.OpenAppAsync();
        var personal = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        var work = await app.AddManualAccountAsync(ValidDraft("Work", "work@example.com"));
        await app.SyncNowAsync(personal.Id);
        await app.SyncNowAsync(work.Id);

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        var personalIndex = -1;
        var workIndex = -1;
        for (var i = 0; i < shell.Accounts.Count; i++)
        {
            if (shell.Accounts[i].Id == personal.Id)
            {
                personalIndex = i;
            }

            if (shell.Accounts[i].Id == work.Id)
            {
                workIndex = i;
            }
        }

        Assert.IsTrue(personalIndex >= 0);
        Assert.IsTrue(workIndex >= 0);
        await shell.ShowUnifiedInboxAsync();
        await shell.GoToAccountIndexAsync(workIndex);

        Assert.AreEqual(work.Id, shell.SelectedAccountId);
        Assert.IsFalse(shell.ShowingUnifiedInbox);
        Assert.IsTrue(shell.IsOnAccountInbox());
        var workInbox = shell.SelectedMailboxId;
        await shell.GoToAccountIndexAsync(workIndex);
        Assert.AreEqual(workInbox, shell.SelectedMailboxId);
        await shell.GoToAccountIndexAsync(personalIndex);
        Assert.AreEqual(personal.Id, shell.SelectedAccountId);
        Assert.IsTrue(shell.IsOnAccountInbox());
        await shell.GoToAccountIndexAsync(9);
        Assert.AreEqual(personal.Id, shell.SelectedAccountId);
    }

    [TestMethod]
    public async Task BrowseShell_GoToAccountOutbox_opens_the_current_Account_Outbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.GoToAccountOutboxAsync();

        Assert.IsTrue(shell.ShowingOutbox);
        Assert.AreEqual(account.Id, shell.SelectedAccountId);
        Assert.IsFalse(shell.ShowingUnifiedInbox);
    }

    [TestMethod]
    public async Task BrowseShell_ExportSelectedMessage_writes_eml()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Hello / world",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "hi"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SelectThreadAsync(shell.Threads[0].Latest.Id);

        Assert.AreEqual("Hello _ world.eml", shell.SelectedMessageEmlFileName());
        using var stream = new MemoryStream();
        await shell.ExportSelectedMessageAsync(stream);
        var raw = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        StringAssert.Contains(raw, "Hello / world");
        StringAssert.Contains(raw, "bob@example.com");
        StringAssert.Contains(raw, "hi");
        var source = await shell.ReadSelectedMessageSourceAsync();
        StringAssert.Contains(source, "Hello / world");
        StringAssert.Contains(source, "bob@example.com");
        StringAssert.Contains(source, "hi");
    }

    [TestMethod]
    public async Task BrowseShell_MaterializeMessageEml_writes_a_temp_eml()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Hello / world",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "hi"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();

        var shell = new BrowseShell(app);
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        var message = shell.Threads[0].Latest;
        var path = await shell.MaterializeMessageEmlAsync(message.AccountId, message.Id, message.Subject);

        Assert.IsNotNull(path);
        Assert.AreEqual("Hello _ world.eml", Path.GetFileName(path));
        var raw = await File.ReadAllTextAsync(path);
        StringAssert.Contains(raw, "Hello / world");
        StringAssert.Contains(raw, "bob@example.com");
        StringAssert.Contains(raw, "hi");
    }

    [TestMethod]
    public async Task BrowseShell_remembers_collapsed_nav_nodes()
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
        var accountNav = shell.BuildNavItems().Single(item => item.AccountId == account.Id);
        Assert.IsTrue(accountNav.IsExpanded);
        await shell.SetNavExpandedAsync(accountNav, false);

        var rebuilt = shell.BuildNavItems().Single(item => item.AccountId == account.Id);
        Assert.IsFalse(rebuilt.IsExpanded);
        Assert.IsTrue(rebuilt.Children.All(child => child.IsExpanded));

        var reloaded = new BrowseShell(app);
        await reloaded.LoadAccountsAsync();
        Assert.IsFalse(reloaded.BuildNavItems().Single(item => item.AccountId == account.Id).IsExpanded);

        var restored = new BrowseShell(app);
        await restored.LoadAccountsAsync();
        await restored.RestoreBrowseAsync();
        var restoredNav = restored.BuildNavItems().Single(item => item.AccountId == account.Id);
        Assert.IsFalse(restoredNav.IsExpanded);
    }

    [TestMethod]
    public async Task BrowseShell_pin_Favorite_Mailbox_appears_under_Favorites()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Work", "INBOX/Work", null));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var work = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Name == "Work");

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        Assert.IsEmpty(shell.BuildNavItems().Single(item => item.Kind == ShellNavKind.Favorites).Children);
        await shell.PinFavoriteAsync(work.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);
        await shell.PinFavoriteAsync(inbox.Id);
        Assert.AreEqual(inbox.Id, shell.BuildNavItems().Single(item => item.Kind == ShellNavKind.Favorites).Children[0].MailboxId);
        Assert.AreEqual(work.Id, shell.BuildNavItems().Single(item => item.Kind == ShellNavKind.Favorites).Children[1].MailboxId);
        await shell.UnpinFavoriteAsync(inbox.Id);

        var reloaded = new BrowseShell(app);
        await reloaded.LoadAccountsAsync();
        Assert.IsTrue(reloaded.IsFavorite(work.Id));
        Assert.AreEqual(
            work.Id,
            reloaded.BuildNavItems().Single(item => item.Kind == ShellNavKind.Favorites).Children.Single().MailboxId);

        var nav = shell.BuildNavItems();
        var favorites = nav.Single(item => item.Kind == ShellNavKind.Favorites);
        Assert.AreEqual("Favorites", favorites.Title);
        Assert.AreEqual(work.Id, favorites.Children.Single().MailboxId);
        Assert.IsTrue(shell.IsFavorite(work.Id));

        var restored = new BrowseShell(app);
        await restored.LoadAccountsAsync();
        await restored.RestoreBrowseAsync();
        Assert.IsTrue(restored.IsFavorite(work.Id));
        Assert.AreEqual(
            work.Id,
            restored.BuildNavItems().Single(item => item.Kind == ShellNavKind.Favorites).Children.Single().MailboxId);

        await restored.UnpinFavoriteAsync(work.Id);
        Assert.IsFalse(restored.IsFavorite(work.Id));
        Assert.IsEmpty(restored.BuildNavItems().Single(item => item.Kind == ShellNavKind.Favorites).Children);
    }

    [TestMethod]
    public async Task BrowseShell_MoveFavorite_reorders_and_persists()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Work", "INBOX/Work", null),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);
        var work = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Name == "Work");
        var sent = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Sent);

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.PinFavoriteAsync(work.Id);
        await shell.PinFavoriteAsync(inbox.Id);
        await shell.PinFavoriteAsync(sent.Id);
        var before = shell.BuildNavItems().Single(item => item.Kind == ShellNavKind.Favorites).Children
            .Select(item => item.MailboxId)
            .ToList();
        Assert.AreEqual(sent.Id, before[0]);
        Assert.AreEqual(inbox.Id, before[1]);
        Assert.AreEqual(work.Id, before[2]);
        Assert.IsTrue(before.All(id => shell.BuildNavItems()
            .Single(item => item.Kind == ShellNavKind.Favorites).Children
            .Single(child => child.MailboxId == id).IsFavoritePin));

        var dest = MailShellFormatting.FavoriteDestinationIndex(shell.FavoriteMailboxIds, sent.Id, false, work.Id);
        Assert.AreEqual(1, dest);
        await shell.MoveFavoriteAsync(sent.Id, dest!.Value);

        var after = shell.BuildNavItems().Single(item => item.Kind == ShellNavKind.Favorites).Children
            .Select(item => item.MailboxId)
            .ToList();
        CollectionAssert.AreEqual(new Guid?[] { inbox.Id, sent.Id, work.Id }, after);

        var reloaded = new BrowseShell(app);
        await reloaded.LoadAccountsAsync();
        CollectionAssert.AreEqual(
            new Guid?[] { inbox.Id, sent.Id, work.Id },
            reloaded.BuildNavItems().Single(item => item.Kind == ShellNavKind.Favorites).Children
                .Select(item => item.MailboxId)
                .ToList());
    }

    [TestMethod]
    public async Task BrowseShell_GoToSent_opens_the_Account_Sent_Mailbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent),
            new RemoteMailbox("Drafts", "Drafts", MailboxRole.Drafts));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);
        var sent = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Sent);

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SearchAsync("Hello");
        await shell.GoToMailboxRoleAsync(MailboxRole.Sent);

        Assert.AreEqual(sent.Id, shell.SelectedMailboxId);
        Assert.AreEqual(account.Id, shell.SelectedAccountId);
        Assert.IsFalse(shell.ShowingUnifiedInbox);
        Assert.IsFalse(shell.ShowingOutbox);
        Assert.IsTrue(shell.IsOnAccountRole(MailboxRole.Sent));
        Assert.AreEqual(string.Empty, shell.SearchQuery);
    }

    [TestMethod]
    public async Task BrowseShell_GoToArchive_opens_the_Account_Archive_Mailbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Archive", "Archive", MailboxRole.Archive));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);
        var archive = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Archive);

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.GoToMailboxRoleAsync(MailboxRole.Archive);

        Assert.AreEqual(archive.Id, shell.SelectedMailboxId);
        Assert.AreEqual(account.Id, shell.SelectedAccountId);
        Assert.IsTrue(shell.IsOnAccountRole(MailboxRole.Archive));
        Assert.IsFalse(shell.ShowingUnifiedInbox);
    }

    [TestMethod]
    public async Task BrowseShell_GoToSent_same_Mailbox_keeps_search()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var sent = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Sent);

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(sent.Id);
        await shell.SearchAsync("Hello");
        await shell.GoToMailboxRoleAsync(MailboxRole.Sent);

        Assert.AreEqual("Hello", shell.SearchQuery);
        Assert.AreEqual(sent.Id, shell.SelectedMailboxId);
    }

    [TestMethod]
    public async Task BrowseShell_GoToSent_from_Unified_Inbox_opens_Sent()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Sent", "Sent", MailboxRole.Sent));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var sent = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Sent);

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.ShowUnifiedInboxAsync();
        await shell.GoToMailboxRoleAsync(MailboxRole.Sent);

        Assert.AreEqual(sent.Id, shell.SelectedMailboxId);
        Assert.AreEqual(account.Id, shell.SelectedAccountId);
        Assert.IsFalse(shell.ShowingUnifiedInbox);
    }

    [TestMethod]
    public async Task BrowseShell_GoToSent_without_Sent_Mailbox_stays_put()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SearchAsync("Hello");
        await shell.GoToMailboxRoleAsync(MailboxRole.Sent);

        Assert.AreEqual(inbox.Id, shell.SelectedMailboxId);
        Assert.AreEqual("Hello", shell.SearchQuery);
        Assert.IsNull(shell.AccountMailbox(MailboxRole.Sent));
    }

    [TestMethod]
    public async Task BrowseShell_GoToDrafts_opens_the_Account_Drafts_Mailbox()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Drafts", "Drafts", MailboxRole.Drafts));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);
        var drafts = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Drafts);

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.GoToMailboxRoleAsync(MailboxRole.Drafts);

        Assert.AreEqual(drafts.Id, shell.SelectedMailboxId);
        Assert.IsTrue(shell.IsOnAccountRole(MailboxRole.Drafts));
        Assert.IsFalse(shell.IsOnAccountInbox());
    }

    [TestMethod]
    public async Task BrowseShell_GoToTrash_and_Junk_open_those_Mailboxes()
    {
        using var fixture = new DesktopAppFixture();
        fixture.Imap.SeedMailboxes(
            new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox),
            new RemoteMailbox("Trash", "Trash", MailboxRole.Trash),
            new RemoteMailbox("Junk", "Junk", MailboxRole.Junk));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft("Personal", "alice@example.com"));
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Inbox);
        var trash = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Trash);
        var junk = (await app.ListMailboxesAsync(account.Id)).Single(item => item.Role == MailboxRole.Junk);

        var shell = new BrowseShell(app);
        await shell.LoadAccountsAsync();
        await shell.SelectAccountAsync(account.Id);
        await shell.SelectMailboxAsync(inbox.Id);
        await shell.SearchAsync("Hello");
        await shell.GoToMailboxRoleAsync(MailboxRole.Trash);
        Assert.AreEqual(trash.Id, shell.SelectedMailboxId);
        Assert.IsTrue(shell.IsOnAccountRole(MailboxRole.Trash));
        await shell.SearchAsync("Hello");
        await shell.GoToMailboxRoleAsync(MailboxRole.Trash);
        Assert.AreEqual("Hello", shell.SearchQuery);
        await shell.GoToMailboxRoleAsync(MailboxRole.Junk);
        Assert.AreEqual(junk.Id, shell.SelectedMailboxId);
        Assert.IsTrue(shell.IsOnAccountRole(MailboxRole.Junk));
        Assert.AreEqual(string.Empty, shell.SearchQuery);
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
