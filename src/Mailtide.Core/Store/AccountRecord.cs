namespace Mailtide.Core.Store;

internal sealed class AccountRecord
{
    public Guid Id { get; set; }

    public required string DisplayName { get; set; }

    public required string EmailAddress { get; set; }

    public required string ImapHost { get; set; }

    public int ImapPort { get; set; }

    public required string SmtpHost { get; set; }

    public int SmtpPort { get; set; }

    public CredentialKind CredentialKind { get; set; }

    public required string CredentialHandle { get; set; }
}
