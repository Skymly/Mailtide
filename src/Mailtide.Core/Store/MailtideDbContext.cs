using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core.Store;

internal sealed class MailtideDbContext : DbContext
{
    public MailtideDbContext(DbContextOptions<MailtideDbContext> options)
        : base(options)
    {
    }

    public DbSet<AccountRecord> Accounts => Set<AccountRecord>();

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
    }
}
