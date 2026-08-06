namespace Mailtide.Core;

public sealed record AccountInfo(
    Guid Id,
    string DisplayName,
    string EmailAddress,
    string ImapHost,
    int ImapPort,
    string SmtpHost,
    int SmtpPort,
    CredentialKind CredentialKind,
    string CredentialHandle);

public enum CredentialKind
{
    Password = 0,
}
