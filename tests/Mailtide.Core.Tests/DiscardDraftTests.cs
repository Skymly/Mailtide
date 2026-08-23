using Mailtide.Core;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class DiscardDraftTests
{
    [TestMethod]
    public async Task DiscardDraft_removes_the_local_Draft()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body"));

        await app.DiscardDraftAsync(account.Id, draft.Id);

        Assert.IsEmpty(await app.ListDraftsAsync(account.Id));
    }

    [TestMethod]
    public async Task DiscardDraft_missing_Id_is_a_no_op()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body"));

        await app.DiscardDraftAsync(account.Id, Guid.NewGuid());

        Assert.HasCount(1, await app.ListDraftsAsync(account.Id));
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
