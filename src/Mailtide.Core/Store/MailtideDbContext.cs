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

    public DbSet<AttachmentRecord> Attachments => Set<AttachmentRecord>();

    public DbSet<DraftRecord> Drafts => Set<DraftRecord>();

    public DbSet<OutboxItemRecord> OutboxItems => Set<OutboxItemRecord>();

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
        account.Property(a => a.OAuthProvider).HasConversion<string>();

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

        var attachment = modelBuilder.Entity<AttachmentRecord>();
        attachment.ToTable("Attachments");
        attachment.HasKey(a => a.Id);
        attachment.Property(a => a.FileName).IsRequired();
        attachment.Property(a => a.ContentType).IsRequired();
        attachment.Property(a => a.BlobRelativePath).IsRequired();
        attachment.HasIndex(a => new { a.AccountId, a.MessageId });

        var draft = modelBuilder.Entity<DraftRecord>();
        draft.ToTable("Drafts");
        draft.HasKey(d => d.Id);
        draft.Property(d => d.ToAddresses).IsRequired();
        draft.Property(d => d.Subject).IsRequired();
        draft.Property(d => d.BodyText).IsRequired();
        draft.HasIndex(d => d.AccountId);

        var outbox = modelBuilder.Entity<OutboxItemRecord>();
        outbox.ToTable("OutboxItems");
        outbox.HasKey(o => o.Id);
        outbox.Property(o => o.ToAddresses).IsRequired();
        outbox.Property(o => o.Subject).IsRequired();
        outbox.Property(o => o.BodyText).IsRequired();
        outbox.Property(o => o.State).HasConversion<string>();
        outbox.HasIndex(o => o.AccountId);
    }
}
