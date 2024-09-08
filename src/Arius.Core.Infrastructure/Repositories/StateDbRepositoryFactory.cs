using Arius.Core.Domain;
using Arius.Core.Domain.Extensions;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Azure;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Options;
using File = Arius.Core.Domain.Storage.FileSystem.File;

namespace Arius.Core.Infrastructure.Repositories;

public class SqliteStateDbRepositoryFactory : IStateDbRepositoryFactory
{
    private readonly IStorageAccountFactory                  storageAccountFactory;
    private readonly AriusConfiguration                      config;
    private readonly ILogger<SqliteStateDbRepositoryFactory> logger;

    public SqliteStateDbRepositoryFactory(
        IStorageAccountFactory storageAccountFactory,
        IOptions<AriusConfiguration> config,
        ILogger<SqliteStateDbRepositoryFactory> logger)

    {
        this.storageAccountFactory = storageAccountFactory;
        this.config                = config.Value;
        this.logger                = logger;
    }

    public async Task<IStateDbRepository> CreateAsync(RepositoryOptions repositoryOptions, RepositoryVersion? version = null)
    {
        await new RepositoryOptionsValidator().ValidateAndThrowAsync(repositoryOptions);

        var repository = storageAccountFactory.GetRepository(repositoryOptions);

        var (file, effectiveVersion) = await GetLocalStateDbFullNameAsync(repository, repositoryOptions, version);

        /* Database is locked -> Cache = shared as per https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors
         *  NOTE if it still fails, try 'pragma temp_store=memory'
         *
         * Set command timeout to 60s to avoid concurrency errors on 'table is locked' 
         */
        var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDbContext>();
        optionsBuilder.UseSqlite($"Data Source={file.FullName};Cache=Shared", sqliteOptions => { sqliteOptions.CommandTimeout(60); });

        return new StateDbRepository(optionsBuilder.Options, effectiveVersion);
    }

    private async Task<(IFile fullName, RepositoryVersion effectiveVersion)> GetLocalStateDbFullNameAsync(IRepository repository, RepositoryOptions repositoryOptions, RepositoryVersion? requestedVersion)
    {
        var localStateDbFolder = config.GetLocalStateDbFolderForRepository(repositoryOptions);

        if (requestedVersion is null)
        {
            var effectiveVersion = await GetLatestVersionAsync();
            if (effectiveVersion == null)
            {
                // No states yet remotely - this is a fresh archive
                effectiveVersion = DateTime.UtcNow;
                return (localStateDbFolder.GetFile(effectiveVersion.GetFileSystemName()), effectiveVersion);
            }
            return (await GetLocallyCachedAsync(repository, repositoryOptions, localStateDbFolder, effectiveVersion), effectiveVersion);
        }
        else
        {
            return (await GetLocallyCachedAsync(repository, repositoryOptions, localStateDbFolder, requestedVersion), requestedVersion);
        }

        async Task<RepositoryVersion?> GetLatestVersionAsync()
        {
            return await repository
                .GetRepositoryVersions()
                .OrderBy(b => b.Name)
                .LastOrDefaultAsync();
        }
    }

    private static async Task<IFile> GetLocallyCachedAsync(IRepository repository, RepositoryOptions repositoryOptions, DirectoryInfo stateDbFolder, RepositoryVersion version)
    {
        var localFile = stateDbFolder.GetFile(version.GetFileSystemName());

        if (localFile.Exists)
        {
            // Cached locally, ASSUME it’s the same version
            return localFile;
        }

        try
        {
            var blob = repository.GetRepositoryVersionBlob(version);
            await repository.DownloadAsync(blob, localFile);
            return localFile;
        }
        catch (RequestFailedException e) when (e.ErrorCode == "BlobNotFound")
        {
            throw new ArgumentException("The requested version was not found", nameof(version), e);
        }
        catch (InvalidDataException e)
        {
            throw new ArgumentException("Could not load the state database. Probably a wrong passphrase was used.", e);
        }
    }
}

public static class DirectoryInfoExtensions
{
    public static IFile GetFile(this DirectoryInfo directoryInfo, string relativeName)
    {
        return File.FromRelativeName(directoryInfo, relativeName);
    }
}


public static class RepositoryVersionExtensions
{
    public static string GetFileSystemName(this RepositoryVersion version)
    {
        return version.Name.Replace(":", "");
    }
}

internal record PointerFileEntryDto
{
    public         byte[]              Hash             { get; init; }
    public         string              RelativeName     { get; init; }
    public         DateTime?           CreationTimeUtc  { get; init; }
    public         DateTime?           LastWriteTimeUtc { get; init; }
    public virtual BinaryPropertiesDto BinaryProperties { get; init; }
}

internal record BinaryPropertiesDto
{
    public         byte[]                           Hash               { get; init; }
    public         long                             OriginalLength     { get; init; }
    public         long                             ArchivedLength     { get; init; }
    public         long                             IncrementalLength  { get; init; }
    public         StorageTier                      StorageTier        { get; init; }
    public virtual ICollection<PointerFileEntryDto> PointerFileEntries { get; set; }
}

internal class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SqliteStateDbContext> // used only for EF Core tools (e.g. dotnet ef migrations add ...)
{
    public SqliteStateDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SqliteStateDbContext>();
        builder.UseSqlite();

        return new SqliteStateDbContext(builder.Options);
    }
}

internal class SqliteStateDbContext : DbContext
{
    public SqliteStateDbContext(DbContextOptions<SqliteStateDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<PointerFileEntryDto> PointerFileEntries { get; set; }
    public virtual DbSet<BinaryPropertiesDto> BinaryProperties   { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var cemb = modelBuilder.Entity<BinaryPropertiesDto>();
        cemb.ToTable("BinaryProperties");
        cemb.HasKey(c => c.Hash);
        cemb.HasIndex(c => c.Hash).IsUnique();

        cemb.Property(c => c.StorageTier)
            .HasConversion(new AccessTierConverter());


        var pfemb = modelBuilder.Entity<PointerFileEntryDto>();
        pfemb.ToTable("PointerFileEntries");
        pfemb.HasKey(pfe => new { pfe.Hash, pfe.RelativeName });

        pfemb.HasIndex(pfe => pfe.Hash);     // NOT unique
        pfemb.HasIndex(pfe => pfe.RelativeName);  // to facilitate GetPointerFileEntriesAtVersionAsync

        pfemb.Property(pfe => pfe.RelativeName)
            .HasConversion(new RemovePointerFileExtensionConverter());

        // PointerFileEntries * -- 1 Chunk
        pfemb.HasOne(pfe => pfe.BinaryProperties)
            .WithMany(c => c.PointerFileEntries)
            .HasForeignKey(pfe => pfe.Hash);
    }

    private class RemovePointerFileExtensionConverter : ValueConverter<string, string>
    {
        public RemovePointerFileExtensionConverter()
            : base(
                v => v.RemoveSuffix(PointerFile.Extension, StringComparison.InvariantCultureIgnoreCase).ToPlatformNeutralPath(), // Convert from Model to Provider (code to db)
                v => $"{v}{PointerFile.Extension}".ToPlatformSpecificPath()) // Convert from Provider to Model (db to code)
        {
        }
    }

    private class AccessTierConverter : ValueConverter<StorageTier, int>
    {
        public AccessTierConverter() : base(
            tier => ConvertTierToNumber(tier),
            number => ConvertNumberToTier(number))
        { }

        private static int ConvertTierToNumber(StorageTier tier)
        {
            return tier switch
            {
                StorageTier.Archive => 10,
                StorageTier.Cold    => 20,
                StorageTier.Cool    => 30,
                StorageTier.Hot     => 40,
                _                   => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown storage tier")
            };
        }

        private static StorageTier ConvertNumberToTier(int number)
        {
            return number switch
            {
                10 => StorageTier.Archive,
                20 => StorageTier.Cold,
                30 => StorageTier.Cool,
                40 => StorageTier.Hot,
                _  => throw new ArgumentOutOfRangeException(nameof(number), number, "Unknown storage tier")
            };
        }
    }
}


internal class StateDbRepository : IStateDbRepository
{
    private readonly DbContextOptions<SqliteStateDbContext> dbContextOptions;

    public StateDbRepository(DbContextOptions<SqliteStateDbContext> dbContextOptions, RepositoryVersion version)
    {
        Version               = version;
        this.dbContextOptions = dbContextOptions;

        using var context = new SqliteStateDbContext(dbContextOptions);
        //context.Database.EnsureCreated();
        context.Database.Migrate();
    }

    public RepositoryVersion Version { get; }

    public long CountPointerFileEntries()
    {
        using var context = new SqliteStateDbContext(dbContextOptions);
        return context.PointerFileEntries.LongCount();
    }


    public long CountBinaryProperties()
    {
        using var context = new SqliteStateDbContext(dbContextOptions);
        return context.BinaryProperties.LongCount();
    }

    //public IEnumerable<BinaryProperties> GetBinaryProperties()
    //{
    //    using var context = new SqliteStateDbContext(dbContextOptions);
    //    foreach (var bp in context.BinaryProperties.Select(dto => dto.ToEntity()))
    //        yield return bp;
    //}

    public void AddBinary(BinaryProperties bp)
    {
        using var context = new SqliteStateDbContext(dbContextOptions);
        context.BinaryProperties.Add(bp.ToDto());
    }

    public bool BinaryExists(Hash binaryFileHash)
    {
        using var context = new SqliteStateDbContext(dbContextOptions);
        return context.BinaryProperties.Any(bp => bp.Hash == binaryFileHash.Value);
    }

    public void AddPointerFileEntry(PointerFileEntry pfe)
    {
        using var context = new SqliteStateDbContext(dbContextOptions);
        context.PointerFileEntries.Add(pfe.ToDto());
    }

    public void DeletePointerFileEntry(PointerFileEntry pfe)
    {
        using var context = new SqliteStateDbContext(dbContextOptions);

        var dto = context.PointerFileEntries.Find(pfe.Hash.Value, pfe.RelativeName);

        context.PointerFileEntries.Remove(dto);
        context.SaveChanges();
    }
}