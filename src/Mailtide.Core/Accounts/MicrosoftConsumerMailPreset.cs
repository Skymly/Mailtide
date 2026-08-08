namespace Mailtide.Core;

/// <summary>
/// Built-in Microsoft consumer (Outlook.com / Hotmail / Live) endpoints and OAuth authority.
/// Consumer-only — not Entra work/school.
/// </summary>
public static class MicrosoftConsumerMailPreset
{
    public const string ImapHost = "outlook.office365.com";
    public const int ImapPort = 993;
    public const string SmtpHost = "smtp.office365.com";
    public const int SmtpPort = 587;
    public const string Authority = "https://login.microsoftonline.com/consumers";
}
