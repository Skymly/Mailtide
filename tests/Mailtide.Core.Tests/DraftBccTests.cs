using Mailtide.Core;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class DraftBccTests
{
    [TestMethod]
    public async Task SaveDraft_round_trips_Bcc_separately()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());

        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body")
            {
                CcAddresses = ["carol@example.com"],
                BccAddresses = ["hidden@example.com"],
            });

        CollectionAssert.AreEqual(new[] { "hidden@example.com" }, draft.BccAddresses.ToArray());
        CollectionAssert.AreEqual(
            new[] { "hidden@example.com" },
            (await app.ListDraftsAsync(account.Id)).Single().BccAddresses.ToArray());
    }

    [TestMethod]
    public async Task SendNow_submits_Bcc_on_OutboundMessage()
    {
        using var fixture = new CoreAppFixture();
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        var draft = await app.SaveDraftAsync(
            account.Id,
            new DraftContent(["bob@example.com"], "Hello", "Body")
            {
                BccAddresses = ["hidden@example.com"],
            });
        await app.SendAsync(account.Id, draft.Id);
        await app.SendNowAsync(account.Id);

        CollectionAssert.AreEqual(
            new[] { "bob@example.com" },
            fixture.Smtp.Submitted[0].ToAddresses.ToArray());
        CollectionAssert.AreEqual(
            new[] { "hidden@example.com" },
            fixture.Smtp.Submitted[0].BccAddresses.ToArray());
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
