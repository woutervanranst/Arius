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

    public IAsyncEnumerable<StateVersion> GetStateVersions()
        => stateDbContainerFolder.GetBlobs().Select(blob => StateVersion.FromName(blob.Name));

    private async Task<StateVersion?> GetLatestStateVersionAsync()
        => await GetStateVersions().OrderBy(b => b.Name).LastOrDefaultAsync();

    private IAzureBlob GetStateDatabaseBlobForVersion(StateVersion version)
        => stateDbContainerFolder.GetBlob(version.Name);


    public async Task<ILocalStateRepository?> GetLocalStateRepositoryAsync(
        DirectoryInfo localStateDatabaseCacheDirectory, 
        StateVersion? version = null)
    {
        var stateDatabaseFile = await GetStateDatabaseFileForVersionAsync(localStateDatabaseCacheDirectory, version);

        if (stateDatabaseFile is null)
            return null;
        
        return new SqliteLocalStateRepository(stateDatabaseFile, loggerFactory.CreateLogger<SqliteLocalStateRepository>());
    }

    public async Task<ILocalStateRepository> CreateNewLocalStateRepositoryAsync(
        DirectoryInfo localStateDatabaseCacheDirectory,
        StateVersion version, 
        StateVersion? basedOn = null)
    {
        var basedOnFile = await GetStateDatabaseFileForVersionAsync(localStateDatabaseCacheDirectory, basedOn);
        var newVersionFile = StateDatabaseFile.FromRepositoryVersion(localStateDatabaseCacheDirectory, version);
        
        // if there is an existing basedOnFile, use that as the baseline for the new version
        basedOnFile?.CopyTo(newVersionFile);

        return new SqliteLocalStateRepository(newVersionFile, loggerFactory.CreateLogger<SqliteLocalStateRepository>());
    }

    /// <summary>
    /// Get the local IStateDatabaseFile for the requested version.
    /// If no `requestedVersion` is specified, it will return the latest version. If there is no version, it will return null.
    /// If `requestedVersion` is specified but does not exist, it will throw an exception.
    /// </summary>
    private async Task<IStateDatabaseFile?> GetStateDatabaseFileForVersionAsync(
        DirectoryInfo localStateDatabaseCacheDirectory,
        StateVersion? requestedVersion)
    {
        var effectiveVersion = await GetEffectiveVersionAsync();

        if (effectiveVersion is null)
            return null;
        else
            return await GetLocallyCachedStateDatabaseFileAsync(effectiveVersion);

        async Task<StateVersion?> GetEffectiveVersionAsync()
        {
            if (requestedVersion is null)
                return await GetLatestStateVersionAsync();
            else
                return requestedVersion;
        }

        async Task<IStateDatabaseFile> GetLocallyCachedStateDatabaseFileAsync(StateVersion version)
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
            // TODO delete the file

            logger.LogInformation("No changes made in this version, skipping upload.");
            return false;
        }


        async Task UploadStateDatabaseAsync(IStateDatabaseFile file, StateVersion version, CancellationToken cancellationToken = default)
        {
            logger.LogInformation("Uploading State Database {version}...", version.Name);

            var blob     = stateDbContainerFolder.GetBlob(version.Name);
            var metadata = AzureBlob.CreateStateDatabaseMetadata();

            await stateDbContainerFolder.UploadAsync(file, blob, metadata, cancellationToken);

            await blob.SetStorageTierAsync(StorageTier.Cold);

            logger.LogInformation("Uploading State Database {version}... done", version.Name);
        }
    }
}