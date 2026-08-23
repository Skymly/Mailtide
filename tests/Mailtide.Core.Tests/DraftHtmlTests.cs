using Mailtide.Core;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class DraftHtmlTests
{
    [TestMethod]
    public async Task SaveDraft_round_trips_BodyHtml()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        var saved = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "plain") { BodyHtml = "<p>hi</p>" });
        Assert.AreEqual("<p>hi</p>", saved.BodyHtml);

        var listed = (await app.ListDraftsAsync(account.Id)).Single();
        Assert.AreEqual("<p>hi</p>", listed.BodyHtml);
    }

    [TestMethod]
    public async Task SendNow_submits_BodyHtml()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "plain") { BodyHtml = "<p>hi</p>" });
        await app.SendAsync(account.Id, draft.Id);
        await app.SendNowAsync(account.Id);
        Assert.AreEqual("<p>hi</p>", fixture.Smtp.Submitted.Single().BodyHtml);
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
