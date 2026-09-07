using Mailtide.Core;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class DraftAttachmentTests
{
    [TestMethod]
    public async Task Add_list_and_remove_Draft_attachments()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body"));

        var added = await app.AddDraftAttachmentAsync(
            account.Id,
            draft.Id,
            "notes.txt",
            "text/plain",
            "hello"u8.ToArray());
        var listed = await app.ListDraftAttachmentsAsync(account.Id, draft.Id);
        Assert.AreEqual("notes.txt", listed.Single().FileName);
        Assert.AreEqual(added.Id, listed.Single().Id);

        await app.RemoveDraftAttachmentAsync(account.Id, added.Id);
        Assert.IsEmpty(await app.ListDraftAttachmentsAsync(account.Id, draft.Id));
    }

    [TestMethod]
    public async Task AddDraftAttachment_infers_ContentType_from_file_name()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body"));

        var added = await app.AddDraftAttachmentAsync(
            account.Id,
            draft.Id,
            "photo.PNG",
            "application/octet-stream",
            [1, 2, 3]);
        Assert.AreEqual("image/png", added.ContentType);
        Assert.AreEqual("image/png", (await app.ListDraftAttachmentsAsync(account.Id, draft.Id)).Single().ContentType);
    }

    [TestMethod]
    public async Task DiscardDraft_removes_attachments()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body"));
        await app.AddDraftAttachmentAsync(
            account.Id,
            draft.Id,
            "notes.txt",
            "text/plain",
            "hello"u8.ToArray());

        await app.DiscardDraftAsync(account.Id, draft.Id);

        Assert.IsEmpty(await app.ListDraftAttachmentsAsync(account.Id, draft.Id));
        Assert.IsEmpty(await app.ListDraftsAsync(account.Id));
    }

    private static ManualAccountDraft ValidDraft() =>
        new(
            DisplayName: "Personal",
            EmailAddress: "alice@example.com",
            ImapHost: "imap.example.com",
            ImapPort: 993,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Password: "s3cret-password");
}
