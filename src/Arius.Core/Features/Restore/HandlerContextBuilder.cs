using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Zio;
using Zio.FileSystems;
using FileSystemExtensions = Arius.Core.Shared.FileSystem.FileSystemExtensions;

namespace Arius.Core.Features.Restore;

internal class HandlerContextBuilder
{
    private readonly RestoreCommand                 request;
    private readonly ILogger<HandlerContextBuilder> logger;
    private readonly ILoggerFactory                 loggerFactory;

    private IArchiveStorage?  archiveStorage;
    private IStateRepository? stateRepository;
    private IFileSystem?      baseFileSystem;

    public HandlerContextBuilder(RestoreCommand request, ILoggerFactory loggerFactory)
    {
        this.request       = request;
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
        await new RestoreCommandValidator().ValidateAndThrowAsync(request);

        // Blob Storage
        if (archiveStorage == null)
        {
            var blobStorage = new AzureBlobStorage(request.AccountName, request.AccountKey, request.ContainerName, request.UseRetryPolicy, loggerFactory.CreateLogger<AzureBlobStorage>());
            archiveStorage = new EncryptedCompressedStorage(blobStorage, request.Passphrase);
        }

        var exists = await archiveStorage.ContainerExistsAsync();
        if (!exists)
        {
            throw new InvalidOperationException($"The specified container '{request.ContainerName}' does not exist in the storage account.");
        }

        var fileSystem = GetFileSystem();
        
        return new HandlerContext
        {
            Request         = request,
            ArchiveStorage  = archiveStorage,
            StateRepository = stateRepository ?? await BuildStateRepositoryAsync(archiveStorage),
            Hasher          = new Sha256Hasher(request.Passphrase),
            Targets         = GetTargets(),
            FileSystem      = fileSystem,
            BinaryCache     = FileSystemExtensions.CreateTempSubdirectory("binarycache", false)
        };


        async Task<IStateRepository> BuildStateRepositoryAsync(IArchiveStorage archiveStorage)
        {
            // Instantiate StateCache
            var stateCache = new StateCache(request.AccountName, request.ContainerName);

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

        FilePairFileSystem GetFileSystem()
        {
            if (baseFileSystem == null)
            {
                var pfs  = new PhysicalFileSystem();
                var root = pfs.ConvertPathFromInternal(request.LocalRoot.FullName);
                baseFileSystem = new SubFileSystem(pfs, root, true);
            }

            return new FilePairFileSystem(baseFileSystem, true);
        }

        UPath[] GetTargets()
        {
            return request.Targets.Select(target => (UPath)target[1..] /* remove the leading '.' - it must be an absolute path*/).ToArray();
        }
    }
}