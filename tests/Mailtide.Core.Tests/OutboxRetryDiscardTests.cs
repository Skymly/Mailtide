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

    [TestMethod]
    public async Task Discard_removes_Outbox_attachment_blobs()
    {
        using var fixture = new CoreAppFixture();
        fixture.Smtp.FailWith = new SmtpProtocolException("temporary");

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());
        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body"));
        var payload = "discard-me"u8.ToArray();
        await app.AddDraftAttachmentAsync(
            account.Id,
            draft.Id,
            "notes.txt",
            "text/plain",
            payload);
        await app.SendAsync(account.Id, draft.Id);
        await app.SendNowAsync(account.Id);

        Assert.IsTrue(FolderContainsBytes(fixture.AppDataDirectory, payload));
        var failed = (await app.ListOutboxAsync(account.Id)).Single();
        await app.DiscardOutboxItemAsync(account.Id, failed.Id);

        Assert.AreEqual(0, (await app.ListOutboxAsync(account.Id)).Count);
        Assert.IsFalse(
            FolderContainsBytes(fixture.AppDataDirectory, payload),
            "Discarded Outbox attachment blobs must not remain on disk.");
    }

    private static bool FolderContainsBytes(string root, byte[] payload)
    {
        if (!Directory.Exists(root))
        {
            return false;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(file);
            if (bytes.AsSpan().SequenceEqual(payload))
            {
                return true;
            }
        }

        return false;
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
