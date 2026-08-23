using Mailtide.Core;
using Mailtide.Core.Smtp;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class SendDraftAttachmentTests
{
    [TestMethod]
    public async Task SendNow_submits_Draft_attachments()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidAccountDraft());
        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body"));
        await app.AddDraftAttachmentAsync(
            account.Id,
            draft.Id,
            "notes.txt",
            "text/plain",
            "hello"u8.ToArray());

        await app.SendAsync(account.Id, draft.Id);
        await app.SendNowAsync(account.Id);

        var submitted = fixture.Smtp.Submitted.Single();
        Assert.AreEqual("notes.txt", submitted.Attachments.Single().FileName);
        Assert.AreEqual("text/plain", submitted.Attachments.Single().ContentType);
        CollectionAssert.AreEqual("hello"u8.ToArray(), submitted.Attachments.Single().Content);
        Assert.IsEmpty(await app.ListDraftAttachmentsAsync(account.Id, draft.Id));
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
