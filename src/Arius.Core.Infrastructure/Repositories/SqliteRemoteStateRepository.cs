using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Storage.Azure;
using Arius.Core.Infrastructure.Storage.LocalFileSystem;
using Azure;

namespace Arius.Core.Infrastructure.Repositories;

public class SqliteRemoteStateRepository : IRemoteStateRepository
{
    private readonly IAzureContainerFolder                stateDbContainerFolder;
    private readonly ILogger<SqliteRemoteStateRepository> logger;
    private readonly ILoggerFactory                       loggerFactory;

    internal SqliteRemoteStateRepository(
        IAzureContainerFolder stateDbContainerFolder,
        ILoggerFactory loggerFactory,
        ILogger<SqliteRemoteStateRepository> logger)
    {
        this.stateDbContainerFolder = stateDbContainerFolder;
        this.logger                 = logger;
        this.loggerFactory          = loggerFactory;
    }

    public IAsyncEnumerable<RepositoryVersion> GetStateDatabaseVersions()
        => stateDbContainerFolder.GetBlobs().Select(blob => RepositoryVersion.FromName(blob.Name));

    private async Task<RepositoryVersion?> GetLatestStateDatabaseVersionAsync()
        => await GetStateDatabaseVersions().OrderBy(b => b.Name).LastOrDefaultAsync();

    private IAzureBlob GetStateDatabaseBlobForVersion(RepositoryVersion version)
        => stateDbContainerFolder.GetBlob(version.Name);


    public async Task<ILocalStateRepository?> GetLocalStateRepositoryAsync(
        DirectoryInfo localStateDatabaseCacheDirectory, 
        RepositoryVersion? version = null)
    {
        var stateDatabaseFile = await GetLocalStateRepositoryFileFullNameAsync(localStateDatabaseCacheDirectory, version);

        if (!stateDatabaseFile.Exists)
            return null;

        return new SqliteLocalStateRepository(stateDatabaseFile, loggerFactory.CreateLogger<SqliteLocalStateRepository>());
    }

    public async Task<ILocalStateRepository> CreateNewLocalStateRepositoryAsync(
        DirectoryInfo localStateDatabaseCacheDirectory,
        RepositoryVersion version, 
        RepositoryVersion? basedOn = null)
    {
        var basedOnFile = await GetLocalStateRepositoryFileFullNameAsync(localStateDatabaseCacheDirectory, basedOn);
        var newVersionFile = StateDatabaseFile.FromRepositoryVersion(localStateDatabaseCacheDirectory, version);

        if (basedOn is not null && !basedOnFile.Exists)
                throw new InvalidOperationException($"The requested version {basedOn} does not exist.");

        if (basedOnFile.Exists)
                basedOnFile.CopyTo(newVersionFile);

        return new SqliteLocalStateRepository(newVersionFile, loggerFactory.CreateLogger<SqliteLocalStateRepository>());
    }

    private async Task<IStateDatabaseFile> GetLocalStateRepositoryFileFullNameAsync(
        DirectoryInfo localStateDatabaseCacheDirectory,
        RepositoryVersion? requestedVersion)
    {
        if (requestedVersion is null)
        {
            var effectiveVersion = await GetLatestStateDatabaseVersionAsync();
            if (effectiveVersion is null)
            {
                // No states yet remotely - this is a fresh archive
                effectiveVersion = DateTimeRepositoryVersion.FromUtcNow();
                return StateDatabaseFile.FromRepositoryVersion(localStateDatabaseCacheDirectory, effectiveVersion);
            }
            return await GetLocallyCachedStateDatabaseFileAsync(localStateDatabaseCacheDirectory, effectiveVersion);
        }
        else
        {
            return await GetLocallyCachedStateDatabaseFileAsync(localStateDatabaseCacheDirectory, requestedVersion);
        }
    }

    private async Task<IStateDatabaseFile> GetLocallyCachedStateDatabaseFileAsync(
        DirectoryInfo localStateDatabaseCacheDirectory,
        RepositoryVersion version)
    {
        var sdbf = StateDatabaseFile.FromRepositoryVersion(localStateDatabaseCacheDirectory, version);

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