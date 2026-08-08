namespace Mailtide.Core;

/// <summary>
/// Built-in Google Mail (Gmail) IMAP/SMTP endpoints and OAuth authority.
/// </summary>
public static class GoogleMailPreset
{
    public const string ImapHost = "imap.gmail.com";
    public const int ImapPort = 993;
    public const string SmtpHost = "smtp.gmail.com";
    public const int SmtpPort = 465;
    public const string Authority = "https://accounts.google.com";
}
