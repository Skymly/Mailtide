using Mailtide.Core.Smtp;
using Mailtide.Core.Tests.Protocol;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MailKitSmtpAdapterTests
{
    [TestMethod]
    public async Task Real_SMTP_adapter_submits_OutboundMessage()
    {
        await using var server = LoopbackSmtpServer.Start();

        await using var client = new MailKitSmtpClientFactory().Create();
        await client.ConnectAndAuthenticateAsync(
            "127.0.0.1",
            server.Port,
            "alice@example.com",
            "s3cret-password");

        await client.SubmitAsync(
            new OutboundMessage(
                FromAddress: "alice@example.com",
                ToAddresses: ["bob@example.com"],
                Subject: "Hello",
                BodyText: "Body"));

        Assert.HasCount(1, server.AcceptedMessages);
        StringAssert.Contains(server.AcceptedMessages[0], "Subject: Hello");
        StringAssert.Contains(server.AcceptedMessages[0], "Body");
        StringAssert.Contains(server.AcceptedMessages[0], "bob@example.com");
    }

    [TestMethod]
    public async Task Real_SMTP_adapter_maps_authentication_failure()
    {
        await using var server = LoopbackSmtpServer.Start(rejectAuth: true);

        await using var client = new MailKitSmtpClientFactory().Create();

        await Assert.ThrowsAsync<SmtpAuthenticationException>(async () =>
            await client.ConnectAndAuthenticateAsync(
                "127.0.0.1",
                server.Port,
                "alice@example.com",
                "wrong-password"));
    }
}
