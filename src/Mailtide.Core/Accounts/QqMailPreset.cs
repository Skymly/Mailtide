namespace Mailtide.Core;

/// <summary>
/// Built-in QQ Mail server endpoints. Authorization code (授权码) is the password Credential.
/// </summary>
public static class QqMailPreset
{
    public const string ImapHost = "imap.qq.com";
    public const int ImapPort = 993;
    public const string SmtpHost = "smtp.qq.com";
    public const int SmtpPort = 465;
}
