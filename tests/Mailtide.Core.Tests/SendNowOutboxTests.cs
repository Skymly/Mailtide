using Mailtide.Core;
using Mailtide.Core.Smtp;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class SendNowOutboxTests
{
    [TestMethod]
    public async Task SendNow_submits_queued_Outbox_item_and_clears_it_on_success()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());

        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body"));
        await app.SendAsync(account.Id, draft.Id);

        await app.SendNowAsync(account.Id);

        Assert.AreEqual(0, (await app.ListOutboxAsync(account.Id)).Count);
        Assert.AreEqual(1, fixture.Smtp.Submitted.Count);
        Assert.AreEqual("alice@example.com", fixture.Smtp.Submitted[0].FromAddress);
        Assert.AreEqual("Hello", fixture.Smtp.Submitted[0].Subject);
        Assert.AreEqual("Body", fixture.Smtp.Submitted[0].BodyText);
        CollectionAssert.AreEqual(
            new[] { "bob@example.com" },
            fixture.Smtp.Submitted[0].ToAddresses.ToArray());
    }

    [TestMethod]
    public async Task SendNow_leaves_Failed_Outbox_item_with_readable_error_on_protocol_failure()
    {
        using var fixture = new CoreAppFixture();
        fixture.Smtp.FailWith = new SmtpProtocolException("452 4.3.1 Insufficient system storage");

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());

        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body"));
        await app.SendAsync(account.Id, draft.Id);

        await app.SendNowAsync(account.Id);

        var outbox = await app.ListOutboxAsync(account.Id);
        Assert.AreEqual(1, outbox.Count);
        Assert.AreEqual(OutboxItemState.Failed, outbox[0].State);
        Assert.AreEqual("Could not send this Message. Try again later.", outbox[0].ErrorMessage);
        Assert.DoesNotContain("452", outbox[0].ErrorMessage!, StringComparison.Ordinal);
        Assert.AreEqual(0, fixture.Smtp.Submitted.Count);
    }

    [TestMethod]
    public async Task SendNow_auth_failure_surfaces_without_protocol_details()
    {
        using var fixture = new CoreAppFixture();
        fixture.Smtp.FailWith = new SmtpAuthenticationException("535 5.7.8 Authentication failed");

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());

        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body"));
        await app.SendAsync(account.Id, draft.Id);

        await app.SendNowAsync(account.Id);

        var outbox = await app.ListOutboxAsync(account.Id);
        Assert.AreEqual(1, outbox.Count);
        Assert.AreEqual(OutboxItemState.Failed, outbox[0].State);
        Assert.AreEqual("Authentication failed. Sign in again.", outbox[0].ErrorMessage);
        Assert.DoesNotContain("535", outbox[0].ErrorMessage!, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task Outbox_item_is_Sending_while_Submit_is_in_flight()
    {
        using var fixture = new CoreAppFixture();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Smtp.BlockSubmitUntil = gate;

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());

        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body"));
        await app.SendAsync(account.Id, draft.Id);

        var sendTask = app.SendNowAsync(account.Id);

        await WaitUntilAsync(async () =>
        {
            var items = await app.ListOutboxAsync(account.Id);
            return items.Count == 1 && items[0].State == OutboxItemState.Sending;
        });

        var mid = await app.ListOutboxAsync(account.Id);
        Assert.AreEqual(OutboxItemState.Sending, mid[0].State);
        Assert.IsNull(mid[0].ErrorMessage);

        gate.SetResult();
        await sendTask;

        Assert.AreEqual(0, (await app.ListOutboxAsync(account.Id)).Count);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("Timed out waiting for condition.");
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
