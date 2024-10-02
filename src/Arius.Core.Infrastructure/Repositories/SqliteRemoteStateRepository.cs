using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Storage.Azure;
using Arius.Core.Infrastructure.Storage.LocalFileSystem;
using Azure;

namespace Arius.Core.Infrastructure.Repositories;

public class SqliteRemoteStateRepository : IRemoteStateRepository
{
    private readonly AzureRemoteRepository                remoteRepository;
    private readonly AzureContainerFolder                 stateDbContainerFolder;
    private readonly AriusConfiguration                   config;
    private readonly ILogger<SqliteRemoteStateRepository> logger;
    private readonly ILoggerFactory                       loggerFactory;

    internal SqliteRemoteStateRepository(
        AzureRemoteRepository remoteRepository,
        AzureContainerFolder stateDbContainerFolder,
        AriusConfiguration config,
        ILoggerFactory loggerFactory,
        ILogger<SqliteRemoteStateRepository> logger)
    {
        this.remoteRepository       = remoteRepository;
        this.stateDbContainerFolder = stateDbContainerFolder;
        this.config                 = config;
        this.logger                 = logger;
        this.loggerFactory          = loggerFactory;
    }

    public IAsyncEnumerable<RepositoryVersion> GetStateDatabaseVersions()
        => stateDbContainerFolder.GetBlobs().Select(blob => RepositoryVersion.FromName(blob.Name));

    private async Task<RepositoryVersion?> GetLatestStateDatabaseVersionAsync()
        => await GetStateDatabaseVersions().OrderBy(b => b.Name).LastOrDefaultAsync();

    private IBlob GetStateDatabaseBlobForVersion(RepositoryVersion version)
        => stateDbContainerFolder.GetBlob(version.Name);


    public async Task<ILocalStateRepository> GetStateRepositoryAsync(RepositoryVersion? version = null)
    {
        var (sdbf, effectiveVersion) = await GetLocalStateRepositoryFileFullNameAsync(version);

        return new SqliteLocalStateRepository(sdbf, effectiveVersion, loggerFactory.CreateLogger<SqliteLocalStateRepository>());
    }

    public Task<ILocalStateRepository> CreateNewStateRepositoryAsync(
        RepositoryVersion version, 
        RepositoryVersion? basedOn = null)
    {
        throw new NotImplementedException();
    }

    private async Task<(IStateDatabaseFile dbFile, RepositoryVersion effectiveVersion)> GetLocalStateRepositoryFileFullNameAsync(RepositoryVersion? requestedVersion)
    {
        if (requestedVersion is null)
        {
            var effectiveVersion = await GetLatestStateDatabaseVersionAsync();
            if (effectiveVersion is null)
            {
                // No states yet remotely - this is a fresh archive
                effectiveVersion = DateTimeRepositoryVersion.FromUtcNow();
                return (GetStateDatabaseFile(effectiveVersion), effectiveVersion);
            }
            return (await GetLocallyCachedStateDatabaseFileAsync(effectiveVersion), effectiveVersion);
        }
        else
        {
            return (await GetLocallyCachedStateDatabaseFileAsync(requestedVersion), requestedVersion);
        }
    }

    private async Task<IStateDatabaseFile> GetLocallyCachedStateDatabaseFileAsync(
        RepositoryVersion version)
    {
        var sdbf = GetStateDatabaseFile(version);

        if (sdbf.Exists)
        {
            // Cached locally, ASSUME it’s the same version
            return sdbf;
        }

        try
        {
            var blob = GetStateDatabaseBlobForVersion(version);
            await stateDbContainerFolder.DownloadAsync(blob, sdbf);
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

    private IStateDatabaseFile GetStateDatabaseFile(RepositoryVersion version)
    {
        var stateDbFolder = config.GetLocalStateDatabaseFolderForContainerName(remoteRepository.ContainerName);
        return StateDatabaseFile.FromRepositoryVersion(stateDbFolder, version);
    }



    public async Task<bool> SaveChangesAsync(ILocalStateRepository localStateRepository)
    {
        if (localStateRepository.HasChanges)
        {
            localStateRepository.Vacuum();

            await UploadStateDatabaseAsync(localStateRepository.StateDatabaseFile, localStateRepository.Version);
            return true;
        }
        else
        {
            logger.LogInformation("No changes made in this version, skipping upload.");
            return false;
        }
    }

    private async Task UploadStateDatabaseAsync(IStateDatabaseFile file, RepositoryVersion version, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Uploading State Database {version}...", version.Name);

        var blob     = stateDbContainerFolder.GetBlob(version.Name);
        var metadata = AzureBlob.CreateStateDatabaseMetadata();

        await stateDbContainerFolder.UploadAsync(file, blob, metadata, cancellationToken);

        await blob.SetStorageTierAsync(StorageTier.Cold);

        logger.LogInformation("Uploading State Database {version}... done", version.Name);
    }
}