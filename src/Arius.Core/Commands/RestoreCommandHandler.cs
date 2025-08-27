using Arius.Core.Extensions;
using Arius.Core.Repositories;
using Arius.Core.Services;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WouterVanRanst.Utils.Extensions;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Commands;

internal class RestoreCommandHandler : ICommandHandler<RestoreCommand>
{
    private readonly ILogger<RestoreCommandHandler> logger;
    private readonly ILoggerFactory                 loggerFactory;
    private readonly IOptions<AriusConfiguration>   config;

    public RestoreCommandHandler(
        ILogger<RestoreCommandHandler> logger,
        ILoggerFactory loggerFactory,
        IOptions<AriusConfiguration> config)
    {
        this.logger        = logger;
        this.loggerFactory = loggerFactory;
        this.config        = config;
    }

    public async ValueTask<Unit> Handle(RestoreCommand request, CancellationToken cancellationToken)
    {
        var handlerContext = await HandlerContext.CreateAsync(request, loggerFactory);

        return await Handle(handlerContext, cancellationToken);
    }

    private IEnumerable<PointerFileEntryDto> GetPointerFileEntriesToRestore(HandlerContext handlerContext, CancellationToken cancellationToken)
    {
        foreach (var targetPath in handlerContext.TargetPaths)
        {
            var binaryFilePath = targetPath.IsPointerFilePath() ? targetPath.GetBinaryFilePath() : targetPath;
            var fp             = handlerContext.FileSystem.FromBinaryFilePath(binaryFilePath);
            var pfes           = handlerContext.StateRepo.GetPointerFileEntries(fp.FullName, true);

            foreach (var pfe in pfes)
            {
                yield return pfe;

            }
        }
    }

    internal async ValueTask<Unit> Handle(HandlerContext handlerContext, CancellationToken cancellationToken)
    {


        // 1. Get all PointerFileEntries to restore
        var x = GetPointerFileEntriesToRestore(handlerContext, cancellationToken).ToArray();



        

        // Example code to restore a single chunk to a temporary file

        //var chunkHash = (Hash)"3e12370e300aef3a239a8a063dc618e581f8f1f5e16f690ed73b3ca5d627369e";
        //var targetFilePath = Path.GetTempFileName();

        //logger.LogInformation($"Restoring chunk '{chunkHash.ToShortString()}' to '{targetFilePath}'");

        //try
        //{
        //    // 1. Get the blob from storage
        //    await using var blobStream = await handlerContext.BlobStorage.OpenReadChunkAsync(chunkHash, cancellationToken);

        //    // 2. Get the decrypted and decompressed stream
        //    await using var decryptionStream = await blobStream.GetDecryptionStreamAsync(handlerContext.Request.Passphrase, cancellationToken);

        //    // 3. Write to the target file
        //    await using var targetFileStream = File.OpenWrite(targetFilePath);
        //    await decryptionStream.CopyToAsync(targetFileStream, cancellationToken);
        //    await targetFileStream.FlushAsync(cancellationToken); // Explicitly flush

        //    logger.LogInformation($"Successfully restored chunk '{chunkHash.ToShortString()}' to '{targetFilePath}'");
        //}
        //catch (Exception e)
        //{
        //    logger.LogError(e, $"Error restoring chunk '{chunkHash.ToShortString()}'");
        //    throw;
        //}

        return Unit.Value;
    }

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
                TargetPaths = GetTargetPaths(),
                FileSystem  = fs
            };

            UPath[] GetTargetPaths()
            {
                return request.Targets.Select(target => fs.ConvertPathFromInternal(target[2..] /*remove the leading './'*/)).ToArray();
            }

            FilePairFileSystem GetFileSystem()
            {
                var pfs = new PhysicalFileSystem();
                var root = pfs.ConvertPathFromInternal(Environment.CurrentDirectory);
                var sfs  = new SubFileSystem(pfs, root, true);
                return new FilePairFileSystem(sfs, true);
            }
        }

        public required RestoreCommand     Request     { get; init; }
        public required IBlobStorage       BlobStorage { get; init; }
        public required StateRepository    StateRepo   { get; init; }
        public required Sha256Hasher       Hasher      { get; init; }
        public required UPath[]            TargetPaths { get; init; }
        public required FilePairFileSystem FileSystem  { get; init; }
    }
}
