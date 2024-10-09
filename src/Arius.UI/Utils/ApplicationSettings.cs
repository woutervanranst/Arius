using Arius.UI.Extensions;
using Arius.UI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;

namespace Arius.UI.Utils;

internal class ApplicationSettings
{
    private readonly string dbPath;

    public ApplicationSettings(string dbPath)
    {
        this.dbPath = dbPath;

        using var db = GetContext();
        Directory.CreateDirectory(dbPath);
        db.Database.EnsureCreated();
    }

    private ApplicationSettingsDbContext GetContext() => new(dbPath);

    public bool KeepPointersOnRestore
    {
        get
        {
            var value = GetSetting(KEEP_POINTERS_ON_RESTORE_KEY);
            if (value is not null)
                return bool.Parse(value);
            else
                return false;
        }
        set
        {
            SetSetting(KEEP_POINTERS_ON_RESTORE_KEY, value.ToString());
        }
    }

    /// <summary>
    /// Get the most recently used repositories
    /// The most recently used is first
    /// </summary>
    public IEnumerable<RepositoryOptionsDto> RecentRepositories
    {
        get
        {
            using var context = GetContext();
            return context.RecentRepositories.OrderByDescending(r => r.LastOpened).ToList();
        }
    }

    public void AddLastUsedRepository(IRepositoryOptionsProvider ro)
    {
        using var context  = GetContext();
        var rodto = context.RecentRepositories.SingleOrDefault(r => 
            r.LocalDirectory == ro.LocalDirectory && 
            r.AccountName == ro.AccountName && 
            r.ContainerName == ro.ContainerName);

        if (rodto is null)
        {
            // Add a new entry
            rodto                     = new RepositoryOptionsDto
            {
                LocalDirectory      = ro.LocalDirectory,
                AccountName         = ro.AccountName,
                AccountKeyProtected = ro.AccountKey.Protect(),
                ContainerName       = ro.ContainerName,
                PassphraseProtected = ro.Passphrase.Protect(),
                LastOpened          = DateTime.Now
            };
        }
        else
        {
            // Update
            //rodto.LocalDirectory      = ro.LocalDirectory;        // do not update this key property
            //rodto.AccountName         = ro.AccountName;           // do not update this key property
            rodto.AccountKeyProtected = ro.AccountKey.Protect();
            //rodto.ContainerName       = ro.ContainerName;         // do not update this key property
            rodto.PassphraseProtected = ro.Passphrase.Protect();
            rodto.LastOpened          = DateTime.Now;
        }

        

        if (!context.RecentRepositories.Contains(rodto))
            context.RecentRepositories.Add(rodto);

        context.SaveChanges();
    }

    private const string KEEP_POINTERS_ON_RESTORE_KEY = "KeepPointersOnRestore";

    private string? GetSetting(string key)
    {
        using var context = GetContext();
        return context.Settings.FirstOrDefault(s => s.Key == key)?.Value;
    }

    private void SetSetting(string key, string value)
    {
        using var context = GetContext();
        var       setting = context.Settings.FirstOrDefault(s => s.Key == key);
        if (setting == null)
        {
            context.Settings.Add(new Setting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }
        context.SaveChanges();
    }
}

internal class ApplicationSettingsDbContext : DbContext
{
    private readonly string dbPath;

    public ApplicationSettingsDbContext(string dbPath)
    {
        this.dbPath = dbPath;
    }

    public DbSet<Setting> Settings { get; set; }
    public DbSet<RepositoryOptionsDto> RecentRepositories { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var romb = modelBuilder.Entity<RepositoryOptionsDto>();
        romb.ToTable("RecentRepositories");
        romb.HasKey(e => new { e.LocalDirectory, e.AccountName, e.ContainerName });
        romb.Property(e => e.LocalDirectory).HasConversion(new DirectoryInfoConverter());

        var smb = modelBuilder.Entity<Setting>();
        smb.ToTable("Settings");
        smb.HasKey(e => e.Key);
    }
}

public class DirectoryInfoConverter : ValueConverter<DirectoryInfo, string>
{
    public DirectoryInfoConverter() : base(
            dirInfo => dirInfo.FullName, // Convert DirectoryInfo to string
            path => new DirectoryInfo(path)) // Convert string to DirectoryInfo
    { }
}

internal record Setting
{
    public string Key { get; set; }
    public string Value { get; set; }
}

internal record RepositoryOptionsDto : IRepositoryOptionsProvider
{
    public DirectoryInfo LocalDirectory { get; set; }

    public string AccountName         { get; set; }
    public string AccountKeyProtected { get; set; }
    public string ContainerName       { get; set; }
    public string PassphraseProtected { get; set; }

    public DateTime LastOpened { get; set; }

    [NotMapped]
    public string AccountKey => AccountKeyProtected.Unprotect();
    [NotMapped]
    public string Passphrase => PassphraseProtected.Unprotect();
}