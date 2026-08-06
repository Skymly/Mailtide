namespace Mailtide.Core.Store;

internal sealed class MailboxRecord
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public required string Name { get; set; }

    public required string Path { get; set; }

    public MailboxRole? Role { get; set; }
}
