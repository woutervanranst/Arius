using Arius.Core.Repositories;
using Arius.Core.Services;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Commands.RestoreCommand;

internal class HandlerContext
{
    public static async Task<HandlerContext> CreateAsync(RestoreCommand request, ILoggerFactory loggerFactory)
    {
        // Use default implementation with dependency injection
        var blobStorage    = new BlobStorage(request.AccountName, request.AccountKey, request.ContainerName);
        var stateCacheRoot = new DirectoryInfo("statecache");

        return await CreateAsync(request, loggerFactory, blobStorage, stateCacheRoot);
    }

    public static async Task<HandlerContext> CreateAsync(RestoreCommand request, ILoggerFactory loggerFactory, IBlobStorage blobStorage, DirectoryInfo stateCacheRoot)
    {
        await new RestoreCommandValidator().ValidateAndThrowAsync(request);

        var logger = loggerFactory.CreateLogger<HandlerContext>();

        // Check if container exists
        var exists = await blobStorage.ContainerExistsAsync();
        if (!exists)
        {
            throw new InvalidOperationException($"The specified container '{request.ContainerName}' does not exist in the storage account.");
        }

        // Instantiate StateCache
        var stateCache = new StateCache(stateCacheRoot);

        // Get the lateste state from blob storage
        var latestStateName = await blobStorage.GetStates().LastOrDefaultAsync();

        if (latestStateName == null)
        {
            throw new InvalidOperationException("No state files found in the specified container. Cannot proceed with restore.");
        }

        var latestStateFile = stateCache.GetStateFilePath(latestStateName);
        if (!latestStateFile.Exists)
        {
            logger.LogInformation($"Downloading latest state file '{latestStateName}' from blob storage...");
            await blobStorage.DownloadStateAsync(latestStateName, latestStateFile);
        }
        else
        {
            logger.LogInformation($"Using cached state file '{latestStateName}' from local cache.");
        }

        var stateRepo = new StateRepository(latestStateFile, false, loggerFactory.CreateLogger<StateRepository>());

        var fs = GetFileSystem();

        return new HandlerContext
        {
            Request     = request,
            BlobStorage = blobStorage,
            StateRepo   = stateRepo,
            Hasher      = new Sha256Hasher(request.Passphrase),
            Targets     = GetTargets(),
            FileSystem  = fs
        };

        UPath[] GetTargets()
        {
            return request.Targets.Select(target => (UPath)target[1..] /* remove the leading '.' - it must be an absolute path*/).ToArray();
        }

        FilePairFileSystem GetFileSystem()
        {
            var pfs  = new PhysicalFileSystem();
            var root = pfs.ConvertPathFromInternal(request.LocalRoot.FullName);
            var sfs  = new SubFileSystem(pfs, root, true);
            return new FilePairFileSystem(sfs, true);
        }
    }

    public required RestoreCommand     Request     { get; init; }
    public required IBlobStorage       BlobStorage { get; init; }
    public required IStateRepository   StateRepo   { get; init; }
    public required Sha256Hasher       Hasher      { get; init; }
    public required UPath[]            Targets     { get; init; }
    public required FilePairFileSystem FileSystem  { get; init; }
}