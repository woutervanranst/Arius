using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Azure;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arius.Core.Infrastructure.Repositories;

public class SqliteRemoteStateRepository : IRemoteStateRepository
{
    private readonly IStorageAccountFactory                  storageAccountFactory;
    private readonly AriusConfiguration                      config;
    private readonly ILogger<SqliteRemoteStateRepository> logger;
    private readonly ILoggerFactory                          loggerFactory;

    public SqliteRemoteStateRepository(
        IStorageAccountFactory storageAccountFactory,
        IOptions<AriusConfiguration> config,
        ILogger<SqliteRemoteStateRepository> logger,
        ILoggerFactory loggerFactory)

    {
        this.storageAccountFactory = storageAccountFactory;
        this.config                = config.Value;
        this.logger                = logger;
        this.loggerFactory         = loggerFactory;
    }

    public async Task<ILocalStateRepository> CreateAsync(CloudRepositoryOptions cloudRepositoryOptions, RepositoryVersion? version = null)
    {
        await new RepositoryOptionsValidator().ValidateAndThrowAsync(cloudRepositoryOptions);

        var cloudRepository = storageAccountFactory.GetCloudRepository(cloudRepositoryOptions);

        var (sdbf, effectiveVersion) = await GetLocalStateRepositoryFileFullNameAsync(cloudRepository, cloudRepositoryOptions, version);
        sdbf = sdbf.IsTemp ? sdbf : sdbf.GetTempCopy();

        /* Database is locked -> Cache = shared as per https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors
         *  NOTE if it still fails, try 'pragma temp_store=memory'
         *
         * Set command timeout to 60s to avoid concurrency errors on 'table is locked' 
         */
        var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDbContext>();
        optionsBuilder.UseSqlite($"Data Source={sdbf.FullName}" /*+ ";Cache=Shared"*/, sqliteOptions => { sqliteOptions.CommandTimeout(60); });

        return new LocalStateRepository(optionsBuilder.Options, effectiveVersion, loggerFactory.CreateLogger<LocalStateRepository>());
    }

    private async Task<(StateDatabaseFile dbFile, RepositoryVersion effectiveVersion)> GetLocalStateRepositoryFileFullNameAsync(ICloudRepository cloudRepository, CloudRepositoryOptions cloudRepositoryOptions, RepositoryVersion? requestedVersion)
    {
        if (requestedVersion is null)
        {
            var effectiveVersion = await GetLatestRemoteVersionAsync();
            if (effectiveVersion is null)
            {
                // No states yet remotely - this is a fresh archive
                effectiveVersion = DateTime.UtcNow;
                return (StateDatabaseFile.FromRepositoryVersion(config, cloudRepositoryOptions, effectiveVersion, true), effectiveVersion);
                //return (StateDatabaseFile.FromRepositoryVersion(localStateDbFolder, effectiveVersion, true), effectiveVersion);
            }
            return (await GetLocallyCachedStateDatabaseFileAsync(cloudRepository, cloudRepositoryOptions, effectiveVersion), effectiveVersion);
        }
        else
        {
            return (await GetLocallyCachedStateDatabaseFileAsync(cloudRepository, cloudRepositoryOptions, requestedVersion), requestedVersion);
        }

        async Task<RepositoryVersion?> GetLatestRemoteVersionAsync()
        {
            return await cloudRepository
                .GetStateDatabaseVersions()
                .OrderBy(b => b.Name)
                .LastOrDefaultAsync();
        }
    }

    private async Task<StateDatabaseFile> GetLocallyCachedStateDatabaseFileAsync(ICloudRepository cloudRepository, CloudRepositoryOptions cloudRepositoryOptions, RepositoryVersion version)
    {
        var sdbf = StateDatabaseFile.FromRepositoryVersion(config, cloudRepositoryOptions, version, false);

        if (sdbf.Exists)
        {
            // Cached locally, ASSUME it’s the same version
            return sdbf;
        }

        try
        {
            var blob = cloudRepository.GetStateDatabaseBlobForVersion(version);
            await cloudRepository.DownloadAsync(blob, sdbf);
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

    public async Task SaveChangesAsync(ILocalStateRepository localStateRepository, ICloudRepository cloudRepository)
    {
        if (!localStateRepository.HasChanges)
        {
            logger.LogInformation("No changes made in this version, skipping upload.");
            return;
        }

        localStateRepository.Vacuum();

        var blob            = cloudRepository.GetStateDatabaseBlobForVersion(localStateRepository.Version);

        var localStateDbFolder = config.GetLocalStateDatabaseFolderForRepositoryOptions(cloudRepositoryOptions);
        var sdbf = StateDatabaseFile.FromRepositoryVersion(config, cloudRepositoryOptions, localStateRepository.Version, false);

        await cloudRepository.UploadStateDatabaseAsync(localStateRepository, blob);


        await Task.CompletedTask;
    }
}