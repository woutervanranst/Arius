using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Storage.LocalFileSystem;
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

    public async Task<ILocalStateRepository> CreateAsync(RemoteRepositoryOptions remoteRepositoryOptions, RepositoryVersion? version = null)
    {
        await new RepositoryOptionsValidator().ValidateAndThrowAsync(remoteRepositoryOptions);

        var remoteRepository = storageAccountFactory.GetRemoteRepository(remoteRepositoryOptions);

        var (sdbf, effectiveVersion) = await GetLocalStateRepositoryFileFullNameAsync(remoteRepository, remoteRepositoryOptions, version);

        return new SqliteLocalStateRepository(sdbf, effectiveVersion, loggerFactory.CreateLogger<SqliteLocalStateRepository>());
    }

    private async Task<(IStateDatabaseFile dbFile, RepositoryVersion effectiveVersion)> GetLocalStateRepositoryFileFullNameAsync(IRemoteRepository remoteRepository, RemoteRepositoryOptions remoteRepositoryOptions, RepositoryVersion? requestedVersion)
    {
        if (requestedVersion is null)
        {
            var effectiveVersion = await remoteRepository.GetLatestStateDatabaseVersionAsync();
            if (effectiveVersion is null)
            {
                // No states yet remotely - this is a fresh archive
                effectiveVersion = DateTimeRepositoryVersion.FromUtcNow();
                return (StateDatabaseFile.FromRepositoryVersion(config, remoteRepositoryOptions, effectiveVersion), effectiveVersion);
            }
            return (await GetLocallyCachedStateDatabaseFileAsync(remoteRepository, remoteRepositoryOptions, effectiveVersion), effectiveVersion);
        }
        else
        {
            return (await GetLocallyCachedStateDatabaseFileAsync(remoteRepository, remoteRepositoryOptions, requestedVersion), requestedVersion);
        }
    }

    private async Task<IStateDatabaseFile> GetLocallyCachedStateDatabaseFileAsync(IRemoteRepository remoteRepository, RemoteRepositoryOptions remoteRepositoryOptions, RepositoryVersion version)
    {
        var sdbf = StateDatabaseFile.FromRepositoryVersion(config, remoteRepositoryOptions, version);

        if (sdbf.Exists)
        {
            // Cached locally, ASSUME it’s the same version
            return sdbf;
        }

        try
        {
            var blob = remoteRepository.GetStateDatabaseBlobForVersion(version);
            await remoteRepository.DownloadAsync(blob, sdbf);
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

    public async Task<bool> SaveChangesAsync(ILocalStateRepository localStateRepository, IRemoteRepository remoteRepository)
    {
        if (localStateRepository.HasChanges)
        {
            localStateRepository.Vacuum();

            await remoteRepository.UploadStateDatabaseAsync(localStateRepository.StateDatabaseFile, localStateRepository.Version);
            return true;
        }
        else
        {
            logger.LogInformation("No changes made in this version, skipping upload.");
            return false;
        }
    }
}