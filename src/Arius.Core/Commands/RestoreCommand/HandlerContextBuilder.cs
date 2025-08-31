using Arius.Core.Repositories;
using Arius.Core.Services;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Commands.RestoreCommand;

internal class HandlerContextBuilder
{
    private readonly RestoreCommand                 request;
    private readonly ILogger<HandlerContextBuilder> logger;
    
    private IBlobStorage?     blobStorage;
    private IStateRepository? stateRepository;
    private IFileSystem?      baseFileSystem;

    public HandlerContextBuilder(RestoreCommand request, ILogger<HandlerContextBuilder>? logger = null)
    {
        this.request = request;
        this.logger = logger ?? NullLogger<HandlerContextBuilder>.Instance;
    }

    public HandlerContextBuilder WithBlobStorage(IBlobStorage blobStorage)
    {
        this.blobStorage = blobStorage;
        return this;
    }

    public HandlerContextBuilder WithStateRepository(IStateRepository stateRepository)
    {
        this.stateRepository = stateRepository;
        return this;
    }

    public HandlerContextBuilder WithBaseFileSystem(IFileSystem fileSystem)
    {
        this.baseFileSystem = fileSystem;
        return this;
    }

    public async Task<HandlerContext> BuildAsync()
    {
        await new RestoreCommandValidator().ValidateAndThrowAsync(request);

        // Blob Storage
        blobStorage ??= new BlobStorage(request.AccountName, request.AccountKey, request.ContainerName);
        var exists = await blobStorage.ContainerExistsAsync();
        if (!exists)
        {
            throw new InvalidOperationException($"The specified container '{request.ContainerName}' does not exist in the storage account.");
        }

        return new HandlerContext
        {
            Request     = request,
            BlobStorage = blobStorage,
            StateRepo   = stateRepository ?? await BuildStateRepositoryAsync(blobStorage),
            Hasher      = new Sha256Hasher(request.Passphrase),
            Targets     = GetTargets(),
            FileSystem  = GetFileSystem()
        };


        async Task<IStateRepository> BuildStateRepositoryAsync(IBlobStorage blobStorage)
        {
            var stateCacheRoot = new DirectoryInfo("statecache");

            // Instantiate StateCache
            var stateCache = new StateCache(stateCacheRoot);

            // Get the latest state from blob storage
            var latestStateName = await blobStorage.GetStates().LastOrDefaultAsync();

            if (latestStateName == null)
            {
                throw new InvalidOperationException("No state files found in the specified container. Cannot proceed with restore.");
            }

            var latestStateFile = stateCache.GetStateFilePath(latestStateName);
            if (!latestStateFile.Exists)
            {
                logger.LogInformation($"Downloading latest state file '{latestStateName}' from blob storage...");
                await blobStorage.DownloadStateAsync(latestStateName, latestStateFile, request.Passphrase);
            }
            else
            {
                logger.LogInformation($"Using cached state file '{latestStateName}' from local cache.");
            }

            return new StateRepository(latestStateFile, false, NullLogger<StateRepository>.Instance);
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