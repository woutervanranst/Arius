using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Azure;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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

        var fullName = await GetLocalRepositoryFullName();

        /* Database is locked -> Cache = shared as per https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors
         *  NOTE if it still fails, try 'pragma temp_store=memory'
         */
        var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDbContext>();
        optionsBuilder.UseSqlite($"Data Source={fullName};Cache=Shared",
            sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(60); //set command timeout to 60s to avoid concurrency errors on 'table is locked'
            });

        // Create the repository with the configured DbContext
        return new StateDbRepository(optionsBuilder.Options, version);

        async Task<string> GetLocalRepositoryFullName()
        {
            var localStateDbFolder = config.GetLocalStateDbFolderForRepositoryName(repositoryOptions.ContainerName);

            if (version is null)
            {
                version = await GetLatestVersionAsync(); //  TODO this is a side effect
                if (version == null)
                {
                    // No states yet remotely - this is a fresh archive
                    version = new RepositoryVersion { Name = $"{DateTime.UtcNow:s}" }; // TODO: this is a side effect
                    return localStateDbFolder.GetFullName(version.GetFileSystemName());
                }
                return await GetLocallyCachedAsync(localStateDbFolder, version);
            }
            else
            {
                return await GetLocallyCachedAsync(localStateDbFolder, version);
            }

            async Task<RepositoryVersion?> GetLatestVersionAsync()
            {
                return await repository
                    .GetRepositoryVersions()
                    .OrderBy(b => b.Name)
                    .LastOrDefaultAsync();
            }

            async Task<string> GetLocallyCachedAsync(DirectoryInfo stateDbFolder, RepositoryVersion version)
            {
                var localPath = stateDbFolder.GetFullName(version.Name);

                if (File.Exists(localPath))
                {
                    // Cached locally, ASSUME it’s the same version
                    return localPath;
                }

                try
                {
                    var blob = repository.GetRepositoryVersionBlob(version);
                    await repository.DownloadAsync(blob, localPath, repositoryOptions.Passphrase);
                    return localPath;
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
    }
}


public static class RepositoryVersionExtensions
{
    public static string GetFileSystemName(this RepositoryVersion version)
    {
        return version.Name.Replace(":", "");
    }
}

internal class SqliteStateDbContext : DbContext
{
    public SqliteStateDbContext(DbContextOptions<SqliteStateDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<PointerFileEntry> PointerFileEntries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        //var cemb = modelBuilder.Entity<ChunkEntry>();
        //cemb.ToTable("ChunkEntries");
        //cemb.HasKey(c => c.Hash);
        //cemb.HasIndex(c => c.Hash).IsUnique();

        //cemb.Property(c => c.AccessTier)
        //    .HasConversion(new AccessTierConverter());


        var pfemb = modelBuilder.Entity<PointerFileEntry>();
        pfemb.ToTable("PointerFileEntries");
        pfemb.HasKey(pfe => new { pfe.HashValue, pfe.RelativeName });

        pfemb.HasIndex(pfe => pfe.HashValue);     // NOT unique
        pfemb.HasIndex(pfe => pfe.RelativeName);  // to facilitate GetPointerFileEntriesAtVersionAsync

        //pfemb.Property(pfe => pfe.RelativeName)
        //    .HasConversion(new RemovePointerFileExtensionConverter());

        //// PointerFileEntries * -- 1 Chunk
        //pfemb.HasOne(pfe => pfe.Chunk)
        //    .WithMany(c => c.PointerFileEntries)
        //    .HasForeignKey(pfe => pfe.BinaryHashValue);
    }

    //private class RemovePointerFileExtensionConverter : ValueConverter<string, string>
    //{
    //    public RemovePointerFileExtensionConverter()
    //        : base(
    //            v => v.RemoveSuffix(PointerFileInfo.Extension, StringComparison.InvariantCultureIgnoreCase).ToPlatformNeutralPath(), // Convert from Model to Provider (code to db)
    //            v => $"{v}{PointerFileInfo.Extension}".ToPlatformSpecificPath()) // Convert from Provider to Model (db to code)
    //    {
    //    }
    //}

    //private class AccessTierConverter : ValueConverter<AccessTier, int>
    //{
    //    public AccessTierConverter() : base(
    //        tier => ConvertTierToNumber(tier),
    //        number => ConvertNumberToTier(number))
    //    { }

    //    private static int ConvertTierToNumber(AccessTier tier)
    //    {
    //        if (tier == AccessTier.Archive)
    //            return 10;
    //        if (tier == AccessTier.Cold)
    //            return 20;
    //        if (tier == AccessTier.Cool)
    //            return 30;
    //        if (tier == AccessTier.Hot)
    //            return 40;

    //        return -1;
    //    }

    //    private static AccessTier ConvertNumberToTier(int number)
    //    {
    //        return number switch
    //        {
    //            10 => AccessTier.Archive,
    //            20 => AccessTier.Cold,
    //            30 => AccessTier.Cool,
    //            40 => AccessTier.Hot,
    //            _  => (AccessTier)"unknown"
    //        };
    //    }
    //}
}


internal class StateDbRepository : IStateDbRepository
{
    private readonly DbContextOptions<SqliteStateDbContext> dbContextOptions;

    public StateDbRepository(DbContextOptions<SqliteStateDbContext> dbContextOptions, RepositoryVersion version)
    {
        Version               = version;
        this.dbContextOptions = dbContextOptions;

        using var context = new SqliteStateDbContext(dbContextOptions);
        context.Database.EnsureCreated();
        context.Database.Migrate();
    }

    public RepositoryVersion Version { get; }

    public IAsyncEnumerable<PointerFileEntry> GetPointerFileEntries()
    {
        var context = new SqliteStateDbContext(dbContextOptions); // not with using, maybe detach them all?
        return context.PointerFileEntries.ToAsyncEnumerable();
    }

    public IAsyncEnumerable<string> GetBinaryEntries() => AsyncEnumerable.Empty<string>();
}