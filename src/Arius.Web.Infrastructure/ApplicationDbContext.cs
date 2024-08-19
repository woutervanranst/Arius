using Arius.Web.Core;
using Microsoft.EntityFrameworkCore;

namespace Arius.Web.Infrastructure;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<RepositoryOptions> BackupConfigurations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<RepositoryOptions>().Property(b => b.AccountKey).HasConversion(
            v => Encrypt(v),
            v => Decrypt(v));
        modelBuilder.Entity<RepositoryOptions>().Property(b => b.Passphrase).HasConversion(
            v => Encrypt(v),
            v => Decrypt(v));
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