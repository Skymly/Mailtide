using Mailtide.Core.Imap;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MailSignatureTests
{
    [TestMethod]
    public void Apply_skips_blank_and_does_not_duplicate()
    {
        Assert.AreEqual("hello", MailSignature.Apply("hello", null));
        Assert.AreEqual("hello", MailSignature.Apply("hello", "  "));
        var once = MailSignature.Apply("", "Sent from Mailtide");
        StringAssert.Contains(once, "-- ");
        StringAssert.Contains(once, "Sent from Mailtide");
        Assert.AreEqual(once, MailSignature.Apply(once, "Sent from Mailtide"));
    }

    [TestMethod]
    public void Apply_puts_signature_before_quoted_reply()
    {
        var quoted = "On 2026-04-01 10:00 UTC, bob@example.com wrote:" + Environment.NewLine + Environment.NewLine + "> hi";
        var body = MailSignature.Apply(quoted, "Alice", beforeQuoted: true);
        Assert.IsTrue(body.IndexOf("Alice", StringComparison.Ordinal) < body.IndexOf("On 2026-04-01", StringComparison.Ordinal));
        Assert.AreEqual("hello", MailSignature.Without(MailSignature.Apply("hello", "Alice"), "Alice"));
    }

    [TestMethod]
    public void Replace_swaps_signature_and_keeps_quoted_reply_below()
    {
        var quoted = "On 2026-04-01 10:00 UTC, bob@example.com wrote:" + Environment.NewLine + Environment.NewLine + "> hi";
        var alice = MailSignature.Apply(quoted, "Alice", beforeQuoted: true);
        var bob = MailSignature.Replace(alice, "Alice", "Bob");
        StringAssert.Contains(bob, "Bob");
        Assert.IsFalse(bob.Contains("Alice", StringComparison.Ordinal));
        Assert.IsTrue(bob.IndexOf("Bob", StringComparison.Ordinal) < bob.IndexOf("> hi", StringComparison.Ordinal));
        Assert.AreEqual(
            MailSignature.Apply("hello", "Bob"),
            MailSignature.Replace(MailSignature.Apply("hello", "Alice"), "Alice", "Bob"));
    }

    [TestMethod]
    public async Task SetAccountSignature_round_trips_and_StartReply_includes_it()
    {
        using var fixture = new CoreAppFixture();
        fixture.Imap.SeedMailboxes(new RemoteMailbox("INBOX", "INBOX", MailboxRole.Inbox));
        fixture.Imap.SeedMessages(
            "INBOX",
            new RemoteMessage(
                RemoteId: "msg-1",
                Subject: "Hello",
                FromAddress: "bob@example.com",
                ReceivedAt: new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                IsRead: true,
                BodyText: "hi"));
        await using var app = await fixture.OpenAppAsync();
        var account = await app.AddManualAccountAsync(ValidDraft());
        var updated = await app.SetAccountSignatureAsync(account.Id, "Alice @ Mailtide");
        Assert.AreEqual("Alice @ Mailtide", updated!.Signature);
        Assert.AreEqual("Alice @ Mailtide", (await app.ListAccountsAsync()).Single().Signature);

        await app.SyncNowAsync(account.Id);
        var mailboxId = (await app.ListMailboxesAsync(account.Id)).Single().Id;
        var message = (await app.ListMessagesAsync(account.Id, mailboxId)).Single();
        var draft = await app.StartReplyAsync(account.Id, message.Id);
        StringAssert.Contains(draft.BodyText, "Alice @ Mailtide");
        StringAssert.Contains(draft.BodyText, "> hi");
        Assert.IsTrue(
            draft.BodyText.IndexOf("Alice @ Mailtide", StringComparison.Ordinal)
            < draft.BodyText.IndexOf("> hi", StringComparison.Ordinal));
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
