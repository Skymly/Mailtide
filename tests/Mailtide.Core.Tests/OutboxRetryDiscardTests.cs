using Mailtide.Core;
using Mailtide.Core.Smtp;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class OutboxRetryDiscardTests
{
    [TestMethod]
    public async Task Retry_requeues_failed_item_and_SendNow_submits_it()
    {
        using var fixture = new CoreAppFixture();
        fixture.Smtp.FailWith = new SmtpProtocolException("temporary");

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());

        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body"));
        await app.SendAsync(account.Id, draft.Id);
        await app.SendNowAsync(account.Id);

        var failed = (await app.ListOutboxAsync(account.Id)).Single();
        Assert.AreEqual(OutboxItemState.Failed, failed.State);

        await app.RetryOutboxItemAsync(account.Id, failed.Id);

        var retried = (await app.ListOutboxAsync(account.Id)).Single();
        Assert.AreEqual(OutboxItemState.Queued, retried.State);
        Assert.IsNull(retried.ErrorMessage);

        fixture.Smtp.FailWith = null;
        await app.SendNowAsync(account.Id);

        Assert.AreEqual(0, (await app.ListOutboxAsync(account.Id)).Count);
        Assert.AreEqual(1, fixture.Smtp.Submitted.Count);
    }

    [TestMethod]
    public async Task Discard_removes_failed_Outbox_item()
    {
        using var fixture = new CoreAppFixture();
        fixture.Smtp.FailWith = new SmtpProtocolException("temporary");

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());

        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body"));
        await app.SendAsync(account.Id, draft.Id);
        await app.SendNowAsync(account.Id);

        var failed = (await app.ListOutboxAsync(account.Id)).Single();
        await app.DiscardOutboxItemAsync(account.Id, failed.Id);

        Assert.AreEqual(0, (await app.ListOutboxAsync(account.Id)).Count);
        Assert.AreEqual(0, (await app.ListDraftsAsync(account.Id)).Count);
    }

    private static ManualAccountDraft ValidAccountDraft() =>
        new(
            DisplayName: "Personal",
            EmailAddress: "alice@example.com",
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}
