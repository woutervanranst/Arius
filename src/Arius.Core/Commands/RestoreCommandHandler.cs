using Arius.Core.Extensions;
using Arius.Core.Repositories;
using Arius.Core.Services;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Channels;
using Arius.Core.Models;
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

    private readonly Channel<PointerFileEntryDto> pointerFileEntriesToRestoreChannel = ChannelExtensions.CreateBounded<PointerFileEntryDto>(capacity: 25, singleWriter: true, singleReader: false);

    public async ValueTask<Unit> Handle(RestoreCommand request, CancellationToken cancellationToken)
    {
        var handlerContext = await HandlerContext.CreateAsync(request, loggerFactory);

        return await Handle(handlerContext, cancellationToken);
    }

    internal async ValueTask<Unit> Handle(HandlerContext handlerContext, CancellationToken cancellationToken)
    {
        using var errorCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var       errorCancellationToken       = errorCancellationTokenSource.Token;

        var pointerFileEntriesToRestoreTask = CreatePointerFileEntriesToRestoreTask(handlerContext, errorCancellationToken, errorCancellationTokenSource);
        var downloadBinariesTask            = CreateDownloadBinariesTask(handlerContext, errorCancellationToken, errorCancellationTokenSource);

        try
        {
            await Task.WhenAll(pointerFileEntriesToRestoreTask, downloadBinariesTask);
        }
        catch (Exception)
        {
            // TODO
        }

        return Unit.Value;
    }

    private Task CreatePointerFileEntriesToRestoreTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource) =>
        Task.Run(async () =>
        {
            try
            {
                // 1. Get all PointerFileEntries to restore
                foreach (var targetPath in handlerContext.Targets)
                {
                    var binaryFilePath = targetPath.IsPointerFilePath() ? targetPath.GetBinaryFilePath() : targetPath;
                    var fp             = handlerContext.FileSystem.FromBinaryFilePath(binaryFilePath);
                    var pfes           = handlerContext.StateRepo.GetPointerFileEntries(fp.FullName, true);

                    foreach (var pfe in pfes)
                    {
                        await pointerFileEntriesToRestoreChannel.Writer.WriteAsync(pfe, cancellationToken);
                    }
                }

                pointerFileEntriesToRestoreChannel.Writer.Complete();
            }
            catch (OperationCanceledException)
            {
                pointerFileEntriesToRestoreChannel.Writer.Complete();
                throw;
            }
            catch (Exception)
            {
                pointerFileEntriesToRestoreChannel.Writer.Complete();
                errorCancellationTokenSource.Cancel();
                throw;
            }


        }, cancellationToken);

    private Task CreateDownloadBinariesTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource) =>
        Parallel.ForEachAsync(pointerFileEntriesToRestoreChannel.Reader.ReadAllAsync(cancellationToken),
            new ParallelOptions() { MaxDegreeOfParallelism = handlerContext.Request.DownloadParallelism, CancellationToken = cancellationToken },
            async (pfe, innerCancellationToken) =>
            {
                // 1. Get the blob from storage
                await using var blobStream = await handlerContext.BlobStorage.OpenReadChunkAsync(pfe.BinaryProperties.Hash, cancellationToken);

                // 2. Get the decrypted and decompressed stream
                await using var decryptionStream = await blobStream.GetDecryptionStreamAsync(handlerContext.Request.Passphrase, cancellationToken);

                // 3. Write to the target file
                var fp = FilePair.FromPointerFileEntry(handlerContext.FileSystem, pfe);
                fp.BinaryFile.Directory.Create();
                
                await using var targetFileStream = fp.BinaryFile.OpenWrite();

                await decryptionStream.CopyToAsync(targetFileStream, innerCancellationToken);
                await targetFileStream.FlushAsync(innerCancellationToken); // Explicitly flush

            });




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
                var pfs = new PhysicalFileSystem();
                var root = pfs.ConvertPathFromInternal(request.LocalRoot.FullName);
                var sfs  = new SubFileSystem(pfs, root, true);
                return new FilePairFileSystem(sfs, true);
            }
        }

        public required RestoreCommand     Request     { get; init; }
        public required IBlobStorage       BlobStorage { get; init; }
        public required StateRepository    StateRepo   { get; init; }
        public required Sha256Hasher       Hasher      { get; init; }
        public required UPath[]            Targets     { get; init; }
        public required FilePairFileSystem FileSystem  { get; init; }
    }
}
