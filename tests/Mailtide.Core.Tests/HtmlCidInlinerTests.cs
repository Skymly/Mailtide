using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class HtmlCidInlinerTests
{
    [TestMethod]
    public void Inline_rewrites_cid_to_data_url()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var html = HtmlCidInliner.Inline(
            "<img src=\"cid:pic@example.com\">",
            [("pic@example.com", "image/png", png)]);

        StringAssert.Contains(html, "data:image/png;base64,");
        Assert.DoesNotContain("cid:", html, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task GetMessageHtmlForDisplay_inlines_stored_cid_attachment()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "cid-1",
                Subject: "Pic",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "pic")
            {
                BodyHtml = "<p><img src=\"cid:img1@mail\"></p>",
                Attachments =
                [
                    new RemoteAttachment("inline.png", "image/png", png) { ContentId = "img1@mail" },
                ],
            });

        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var inbox = (await app.ListMailboxesAsync(account.Id)).Single();
        var message = (await app.ListMessagesAsync(account.Id, inbox.Id)).Single();

        Assert.AreEqual(
            "<p><img src=\"cid:img1@mail\"></p>",
            await app.GetMessageHtmlAsync(account.Id, message.Id));
        var display = await app.GetMessageHtmlForDisplayAsync(account.Id, message.Id);
        StringAssert.Contains(display!, "data:image/png;base64,");
        Assert.DoesNotContain("cid:", display, StringComparison.OrdinalIgnoreCase);
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
