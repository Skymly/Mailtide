using MailKit.Security;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class MailTlsTests
{
    [TestMethod]
    public void Loopback_may_be_plaintext()
    {
        Assert.AreEqual(SecureSocketOptions.None, MailTls.SocketOptions("127.0.0.1", 143));
        Assert.AreEqual(SecureSocketOptions.None, MailTls.SocketOptions("localhost", 993));
        Assert.AreEqual(SecureSocketOptions.None, MailTls.SocketOptions("::1", 587));
    }

    [TestMethod]
    public void Remote_implicit_ssl_ports_never_fall_back()
    {
        Assert.AreEqual(SecureSocketOptions.SslOnConnect, MailTls.SocketOptions("imap.gmail.com", 993));
        Assert.AreEqual(SecureSocketOptions.SslOnConnect, MailTls.SocketOptions("smtp.gmail.com", 465));
    }

    [TestMethod]
    public void Remote_submission_and_imap_require_starttls()
    {
        Assert.AreEqual(SecureSocketOptions.StartTls, MailTls.SocketOptions("imap.example.com", 143));
        Assert.AreEqual(SecureSocketOptions.StartTls, MailTls.SocketOptions("smtp.example.com", 587));
        Assert.AreEqual(SecureSocketOptions.StartTls, MailTls.SocketOptions("smtp.example.com", 25));
        Assert.AreEqual(SecureSocketOptions.StartTls, MailTls.SocketOptions("imap.example.com", 1143));
    }
}
