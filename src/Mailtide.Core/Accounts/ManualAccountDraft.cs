namespace Mailtide.Core;

public sealed record ManualAccountDraft(
    string DisplayName,
    string EmailAddress,
    string ImapHost,
    int ImapPort,
    string SmtpHost,
    int SmtpPort,
    string Password);
