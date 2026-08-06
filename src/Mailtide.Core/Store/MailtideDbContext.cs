using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core.Store;

internal sealed class MailtideDbContext : DbContext
{
    public MailtideDbContext(DbContextOptions<MailtideDbContext> options)
        : base(options)
    {
    }

    public DbSet<AccountRecord> Accounts => Set<AccountRecord>();

    public DbSet<MailboxRecord> Mailboxes => Set<MailboxRecord>();

    public DbSet<MessageRecord> Messages => Set<MessageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var account = modelBuilder.Entity<AccountRecord>();
        account.ToTable("Accounts");
        account.HasKey(a => a.Id);
        account.Property(a => a.DisplayName).IsRequired();
        account.Property(a => a.EmailAddress).IsRequired();
        account.Property(a => a.ImapHost).IsRequired();
        account.Property(a => a.SmtpHost).IsRequired();
        account.Property(a => a.CredentialHandle).IsRequired();
        account.Property(a => a.CredentialKind).HasConversion<string>();

        var mailbox = modelBuilder.Entity<MailboxRecord>();
        mailbox.ToTable("Mailboxes");
        mailbox.HasKey(m => m.Id);
        mailbox.Property(m => m.Name).IsRequired();
        mailbox.Property(m => m.Path).IsRequired();
        mailbox.Property(m => m.Role).HasConversion<string>();
        mailbox.HasIndex(m => new { m.AccountId, m.Path }).IsUnique();

        var message = modelBuilder.Entity<MessageRecord>();
        message.ToTable("Messages");
        message.HasKey(m => m.Id);
        message.Property(m => m.RemoteId).IsRequired();
        message.Property(m => m.Subject).IsRequired();
        message.Property(m => m.FromAddress).IsRequired();
        message.Property(m => m.BodyText).IsRequired();
        message.HasIndex(m => new { m.AccountId, m.MailboxId, m.RemoteId }).IsUnique();
    }
}
