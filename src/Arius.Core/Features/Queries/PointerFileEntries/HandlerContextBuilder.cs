using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Features.Queries.PointerFileEntries;

internal class HandlerContextBuilder
{
    private readonly PointerFileEntriesQuery        query;
    private readonly ILogger<HandlerContextBuilder> logger;
    private readonly ILoggerFactory                 loggerFactory;

    private IArchiveStorage?  archiveStorage;
    private IStateRepository? stateRepository;
    private IFileSystem?      baseFileSystem;

    public HandlerContextBuilder(PointerFileEntriesQuery query, ILoggerFactory loggerFactory)
    {
        this.query         = query;
        this.loggerFactory = loggerFactory;
        this.logger        = loggerFactory.CreateLogger<HandlerContextBuilder>();
    }

    public HandlerContextBuilder WithArchiveStorage(IArchiveStorage archiveStorage)
    {
        this.archiveStorage = archiveStorage;
        return this;
    }

    public HandlerContextBuilder WithStateRepository(IStateRepository stateRepository)
    {
        this.stateRepository = stateRepository;
        return this;
    }

    public HandlerContextBuilder WithBaseFileSystem(IFileSystem fileSystem)
    {
        baseFileSystem = fileSystem;
        return this;
    }

    public async Task<HandlerContext> BuildAsync()
    {
        await new PointerFileEntriesQueryValidator().ValidateAndThrowAsync(query);

        // Archive Storage
        if (archiveStorage == null)
        {
            var blobStorage = new AzureBlobStorageContainer(query.AccountName, query.AccountKey, query.ContainerName, query.UseRetryPolicy, loggerFactory.CreateLogger<AzureBlobStorageContainer>());
            archiveStorage = new EncryptedCompressedStorage(blobStorage, query.Passphrase);
        }

        var exists = await archiveStorage.ContainerExistsAsync();
        if (!exists)
        {
            throw new InvalidOperationException($"The specified container '{query.ContainerName}' does not exist in the storage account.");
        }

        return new HandlerContext
        {
            Query           = query,
            StateRepository = stateRepository ??= await BuildStateRepositoryAsync(archiveStorage),
            LocalFileSystem = GetFilePairFileSystem()
        };


        async Task<IStateRepository> BuildStateRepositoryAsync(IArchiveStorage archiveStorage)
        {
            // Instantiate StateCache
            var stateCache = new StateCache(query.AccountName, query.ContainerName);

            // Get the latest state from blob storage
            var latestStateName = await archiveStorage.GetStates().LastOrDefaultAsync();
            if (latestStateName == null)
            {
                throw new InvalidOperationException("No state files found in the specified container. Cannot proceed with restore.");
            }

            var latestStateFile = stateCache.GetStateFileEntry(latestStateName);
            if (!latestStateFile.Exists)
            {
                logger.LogInformation($"Downloading latest state file '{latestStateName}' from blob storage...");
                await archiveStorage.DownloadStateAsync(latestStateName, latestStateFile);
            }
            else
            {
                logger.LogInformation($"Using cached state file '{latestStateName}' from local cache.");
            }

            var contextPool = new StateRepositoryDbContextPool(latestStateFile, false, NullLogger<StateRepositoryDbContextPool>.Instance);
            return new StateRepository(contextPool);
        }

        FilePairFileSystem GetFilePairFileSystem()
        {
            if (baseFileSystem == null)
            {
                var pfs = new PhysicalFileSystem();
                var root = pfs.ConvertPathFromInternal(query.LocalPath.FullName);
                baseFileSystem = new SubFileSystem(pfs, root, true);
            }

            return new FilePairFileSystem(baseFileSystem, loggerFactory.CreateLogger<FilePairFileSystem>(),true);
        }
    }
}