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
        CollectionAssert.AreEqual(new[] { "bob@example.com" }, outbox[0].ToAddresses.ToArray());
        Assert.IsNull(outbox[0].ErrorMessage);
    }

    [TestMethod]
    public async Task Send_without_recipients_is_an_error()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());
        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(
                ToAddresses: [],
                Subject: "Hello",
                BodyText: "Body"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => app.SendAsync(account.Id, draft.Id));
        Assert.AreEqual("Add at least one recipient before sending.", ex.Message);
        Assert.HasCount(1, await app.ListDraftsAsync(account.Id));
        Assert.IsEmpty(await app.ListOutboxAsync(account.Id));
    }

    [TestMethod]
    public async Task Send_with_invalid_address_does_not_queue_Outbox()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());
        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(
                ToAddresses: ["not-an-email"],
                Subject: "Hello",
                BodyText: "Body"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => app.SendAsync(account.Id, draft.Id));
        StringAssert.Contains(ex.Message, "not-an-email");
        Assert.HasCount(1, await app.ListDraftsAsync(account.Id));
        Assert.IsEmpty(await app.ListOutboxAsync(account.Id));
    }

    [TestMethod]
    public async Task Send_with_display_name_address_queues_in_Outbox()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());
        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(
                ToAddresses: ["Alice <alice@example.com>"],
                Subject: "Hello",
                BodyText: "Body"));

        await app.SendAsync(account.Id, draft.Id);
        Assert.IsEmpty(await app.ListDraftsAsync(account.Id));
        Assert.HasCount(1, await app.ListOutboxAsync(account.Id));
    }

    [TestMethod]
    public async Task Send_with_only_Bcc_queues_in_Outbox()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());
        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(
                ToAddresses: [],
                Subject: "Secret",
                BodyText: "Body")
            {
                BccAddresses = ["hidden@example.com"],
            });

        await app.SendAsync(account.Id, draft.Id);
        Assert.IsEmpty(await app.ListDraftsAsync(account.Id));
        Assert.HasCount(1, await app.ListOutboxAsync(account.Id));

        await app.SendNowAsync(account.Id);
        Assert.IsEmpty(fixture.Smtp.Submitted[0].ToAddresses);
        CollectionAssert.AreEqual(
            new[] { "hidden@example.com" },
            fixture.Smtp.Submitted[0].BccAddresses.ToArray());
    }

    [TestMethod]
    public async Task Send_with_only_Cc_queues_in_Outbox()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());
        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(
                ToAddresses: [],
                Subject: "Copy",
                BodyText: "Body")
            {
                CcAddresses = ["carol@example.com"],
            });

        await app.SendAsync(account.Id, draft.Id);
        Assert.IsEmpty(await app.ListDraftsAsync(account.Id));
        Assert.HasCount(1, await app.ListOutboxAsync(account.Id));

        await app.SendNowAsync(account.Id);
        CollectionAssert.AreEqual(
            new[] { "carol@example.com" },
            fixture.Smtp.Submitted[0].CcAddresses.ToArray());
        Assert.IsEmpty(fixture.Smtp.Submitted[0].ToAddresses);
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
