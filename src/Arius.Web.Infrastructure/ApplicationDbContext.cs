using Arius.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arius.Web.Infrastructure;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<StorageAccount> StorageAccounts { get; set; }
    public DbSet<Repository>     Repositories    { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure the one-to-many relationship between StorageAccount and Repository
        modelBuilder.Entity<Repository>()
            .HasOne(r => r.StorageAccount)
            .WithMany(sa => sa.Repositories)
            .HasForeignKey(r => r.StorageAccountId);

        // Configure encryption for AccountKey and Passphrase fields
        modelBuilder.Entity<StorageAccount>().Property(sa => sa.AccountKey).HasConversion(
            v => Encrypt(v),
            v => Decrypt(v));

        modelBuilder.Entity<Repository>().Property(r => r.Passphrase).HasConversion(
            v => Encrypt(v),
            v => Decrypt(v));

        // Configure enum mapping
        modelBuilder.Entity<Repository>().Property(r => r.Tier)
            .HasConversion(
                v => v.ToString(),
                v => (StorageTier)Enum.Parse(typeof(StorageTier), v));
    }

    private string Encrypt(string value)
    {
        // Implement encryption logic
        return value;
    }

    private string Decrypt(string value)
    {
        // Implement decryption logic
        return value;
    }
}