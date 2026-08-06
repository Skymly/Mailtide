using Mailtide.Core;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class DraftLocalityTests
{
    [TestMethod]
    public async Task Saved_draft_stays_local_across_reopen_and_SendNow_does_not_submit()
    {
        using var fixture = new CoreAppFixture();

        Guid accountId;
        Guid draftId;
        await using (var app = await fixture.OpenAppAsync())
        {
            var account = await app.AddManualAccountAsync(ValidAccountDraft());
            accountId = account.Id;

            var draft = await app.SaveDraftAsync(
                account.Id,
                new DraftContent(
                    ToAddresses: ["bob@example.com"],
                    Subject: "Hello",
                    BodyText: "Draft body"));
            draftId = draft.Id;

            await app.SendNowAsync(account.Id);
            Assert.AreEqual(0, fixture.Smtp.Submitted.Count);
        }

        await using (var app = await fixture.OpenAppAsync())
        {
            var drafts = await app.ListDraftsAsync(accountId);
            Assert.AreEqual(1, drafts.Count);
            Assert.AreEqual(draftId, drafts[0].Id);
            Assert.AreEqual("Hello", drafts[0].Subject);
            Assert.AreEqual("Draft body", drafts[0].BodyText);
            CollectionAssert.AreEqual(new[] { "bob@example.com" }, drafts[0].ToAddresses.ToArray());

            await app.SendNowAsync(accountId);
            Assert.AreEqual(0, fixture.Smtp.Submitted.Count);
        }
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
