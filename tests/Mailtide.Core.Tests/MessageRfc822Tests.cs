using System.Text;
using Mailtide.Core;
using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MessageRfc822Tests
{
    [TestMethod]
    public void FileName_sanitizes_subject()
    {
        Assert.AreEqual("message.eml", MessageRfc822.FileName(""));
        Assert.AreEqual("Hello world.eml", MessageRfc822.FileName("Hello world"));
        Assert.AreEqual("Invoice_ 99.eml", MessageRfc822.FileName("Invoice: 99"));
        Assert.DoesNotContain("/", MessageRfc822.FileName("a/b\\c"));
        Assert.EndsWith(".eml", MessageRfc822.FileName(new string('x', 200)));
        Assert.IsLessThanOrEqualTo(84, MessageRfc822.FileName(new string('x', 200)).Length);
    }

    [TestMethod]
    public void Write_includes_headers_body_and_attachment()
    {
        using var stream = new MemoryStream();
        MessageRfc822.Write(
            stream,
            "Alice <alice@example.com>",
            ["bob@example.com"],
            ["carol@example.com"],
            ["hidden@example.com"],
            ["list@example.com"],
            "Hello",
            new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            "Please pay.",
            "<p>Please pay.</p>",
            "<sent-1@example.com>",
            ["<root@example.com>"],
            [("notes.txt", "text/plain", "hi"u8.ToArray())]);

        var raw = Encoding.UTF8.GetString(stream.ToArray());
        StringAssert.Contains(raw, "alice@example.com");
        StringAssert.Contains(raw, "bob@example.com");
        StringAssert.Contains(raw, "carol@example.com");
        StringAssert.Contains(raw, "hidden@example.com");
        StringAssert.Contains(raw, "list@example.com");
        StringAssert.Contains(raw, "Hello");
        StringAssert.Contains(raw, "Please pay.");
        StringAssert.Contains(raw, "notes.txt");
        StringAssert.Contains(raw, "sent-1@example.com");
    }

    [TestMethod]
    public async Task WriteMessageRfc822_exports_a_stored_Message()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "m-1",
                Subject: "Invoice",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "Please pay.")
            {
                ToAddresses = ["alice@example.com"],
                Attachments =
                [
                    new RemoteAttachment("notes.txt", "text/plain", "hi"u8.ToArray()),
                ],
            });
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();

        using var stream = new MemoryStream();
        await app.WriteMessageRfc822Async(account.Id, message.Id, stream);
        var raw = Encoding.UTF8.GetString(stream.ToArray());
        StringAssert.Contains(raw, "Invoice");
        StringAssert.Contains(raw, "bob@example.com");
        StringAssert.Contains(raw, "alice@example.com");
        StringAssert.Contains(raw, "Please pay.");
        StringAssert.Contains(raw, "notes.txt");
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
