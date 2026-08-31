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
