using Mailtide.Core;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class SendToOutboxTests
{
    [TestMethod]
    public async Task Send_moves_draft_into_Outbox_as_Queued_without_SMTP()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());

        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(
                ToAddresses: ["bob@example.com"],
                Subject: "Hello",
                BodyText: "Body"));

        await app.SendAsync(account.Id, draft.Id);

        Assert.AreEqual(0, (await app.ListDraftsAsync(account.Id)).Count);
        Assert.AreEqual(0, fixture.Smtp.Submitted.Count);

        var outbox = await app.ListOutboxAsync(account.Id);
        Assert.AreEqual(1, outbox.Count);
        Assert.AreEqual(OutboxItemState.Queued, outbox[0].State);
        Assert.AreEqual("Hello", outbox[0].Subject);
        Assert.IsNull(outbox[0].ErrorMessage);
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
