using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
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
    private readonly ILoggerFactory                          loggerFactory;

    public SqliteStateDbRepositoryFactory(
        IStorageAccountFactory storageAccountFactory,
        IOptions<AriusConfiguration> config,
        ILogger<SqliteStateDbRepositoryFactory> logger,
        ILoggerFactory loggerFactory)

    {
        this.storageAccountFactory = storageAccountFactory;
        this.config                = config.Value;
        this.logger                = logger;
        this.loggerFactory         = loggerFactory;
    }

    public async Task<IStateDbRepository> CreateAsync(RepositoryOptions repositoryOptions, RepositoryVersion? version = null)
    {
        await new RepositoryOptionsValidator().ValidateAndThrowAsync(repositoryOptions);

        var repository = storageAccountFactory.GetRepository(repositoryOptions);

        var (sdbf, effectiveVersion) = await GetLocalStateDbFullNameAsync(repository, repositoryOptions, version);
        sdbf = sdbf.IsTemp ? sdbf : sdbf.GetTempCopy();

        /* Database is locked -> Cache = shared as per https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors
         *  NOTE if it still fails, try 'pragma temp_store=memory'
         *
         * Set command timeout to 60s to avoid concurrency errors on 'table is locked' 
         */
        var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDbContext>();
        optionsBuilder.UseSqlite($"Data Source={sdbf.FullName}" /*+ ";Cache=Shared"*/, sqliteOptions => { sqliteOptions.CommandTimeout(60); });

        return new StateDbRepository(optionsBuilder.Options, effectiveVersion, loggerFactory.CreateLogger<StateDbRepository>());
    }

    private async Task<(StateDatabaseFile dbFile, RepositoryVersion effectiveVersion)> GetLocalStateDbFullNameAsync(IRepository repository, RepositoryOptions repositoryOptions, RepositoryVersion? requestedVersion)
    {
        var localStateDbFolder = config.GetLocalStateDbFolderForRepository(repositoryOptions);

        if (requestedVersion is null)
        {
            var effectiveVersion = await GetLatestVersionAsync();
            if (effectiveVersion == null)
            {
                // No states yet remotely - this is a fresh archive
                effectiveVersion = DateTime.UtcNow;
                return (StateDatabaseFile.FromRepositoryVersion(localStateDbFolder, effectiveVersion, true), effectiveVersion);
            }
            return (await GetLocallyCachedAsync(repository, localStateDbFolder, effectiveVersion), effectiveVersion);
        }
        else
        {
            return (await GetLocallyCachedAsync(repository, localStateDbFolder, requestedVersion), requestedVersion);
        }

        async Task<RepositoryVersion?> GetLatestVersionAsync()
        {
            return await repository
                .GetRepositoryVersions()
                .OrderBy(b => b.Name)
                .LastOrDefaultAsync();
        }
    }

    private static async Task<StateDatabaseFile> GetLocallyCachedAsync(IRepository repository, DirectoryInfo localStateDbFolder, RepositoryVersion version)
    {
        var sdbf = StateDatabaseFile.FromRepositoryVersion(localStateDbFolder, version, false);

        if (sdbf.Exists)
        {
            // Cached locally, ASSUME it’s the same version
            return sdbf;
        }

        try
        {
            var blob = repository.GetRepositoryVersionBlob(version);
            await repository.DownloadAsync(blob, sdbf);
            return sdbf;
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

    {

    }
}