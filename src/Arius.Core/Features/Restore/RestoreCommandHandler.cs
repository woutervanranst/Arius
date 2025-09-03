using Arius.Core.Shared.Extensions;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.StateRepositories;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace Arius.Core.Features.Restore;

public sealed record ProgressUpdate;

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

    private readonly Channel<FilePairWithPointerFileEntry> filePairsToRestoreChannel = ChannelExtensions.CreateBounded<FilePairWithPointerFileEntry>(capacity: 25, singleWriter: true, singleReader: false);
    private readonly Channel<FilePairWithPointerFileEntry> filePairsToHashChannel    = ChannelExtensions.CreateBounded<FilePairWithPointerFileEntry>(capacity: 25, singleWriter: true, singleReader: false);

    private record FilePairWithPointerFileEntry(FilePair FilePair, PointerFileEntry PointerFileEntry);

    public async ValueTask<Unit> Handle(RestoreCommand request, CancellationToken cancellationToken)
    {
        var handlerContext = await new HandlerContextBuilder(request, loggerFactory.CreateLogger<HandlerContextBuilder>())
            .BuildAsync();

        return await Handle(handlerContext, cancellationToken);
    }

    internal async ValueTask<Unit> Handle(HandlerContext handlerContext, CancellationToken cancellationToken)
    {
        using var errorCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var       errorCancellationToken       = errorCancellationTokenSource.Token;

        var indexTask            = CreateIndexTask(handlerContext, errorCancellationToken, errorCancellationTokenSource);
        var hashTask             = CreateHashTask(handlerContext, errorCancellationToken, errorCancellationTokenSource);
        var downloadBinariesTask = CreateDownloadBinariesTask(handlerContext, errorCancellationToken, errorCancellationTokenSource);

        try
        {
            Task.WhenAll(indexTask, hashTask).ContinueWith(x =>
                {
                    // when both the indexTask and the hashTask are completed, nothing else will be written to the filePairsToRestoreChannel
                    filePairsToRestoreChannel.Writer.Complete();
                });
            await Task.WhenAll(indexTask, hashTask, downloadBinariesTask);
        }
        catch (Exception)
        {
            // TODO
            throw;
        }

        return Unit.Value;
    }

    private Task CreateIndexTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource) =>
        Task.Run(async () =>
        {
            try
            {
                // 1. Iterate over all targets, and establish which binaryfiles need to be restored
                foreach (var targetPath in handlerContext.Targets)
                {
                    var binaryFileTargetPath = targetPath.IsPointerFilePath() ? targetPath.GetBinaryFilePath() : targetPath; // trim the pointerfile extension if present
                    var pfes = handlerContext.StateRepository.GetPointerFileEntries(binaryFileTargetPath.FullName, true).ToArray();

                    if (!pfes.Any())
                    {
                        logger.LogWarning($"Target {targetPath} was specified but no matching PointerFileEntry");
                    }

                    foreach (var pfe in pfes)
                    {
                        var fp = FilePair.FromPointerFileEntry(handlerContext.FileSystem, pfe);

                        if (fp.BinaryFile.Exists)
                        {
                            // BinaryFile exists -- check the hash before restoring
                            await filePairsToHashChannel.Writer.WriteAsync(new (fp, pfe), cancellationToken);
                        }
                        else
                        {
                            // BinaryFile does not exist -- restore it
                            await filePairsToRestoreChannel.Writer.WriteAsync(new (fp, pfe), cancellationToken);

                        }
                    }
                }

                // only CreateIndexTask writes to the filePairsToHashChannel
                filePairsToHashChannel.Writer.Complete();
            }
            catch (OperationCanceledException)
            {
                filePairsToHashChannel.Writer.Complete(); // TODO can this be in the finally block?
                throw;
            }
            catch (Exception)
            {
                filePairsToHashChannel.Writer.Complete();
                errorCancellationTokenSource.Cancel();
                throw;
            }


        }, cancellationToken);

    private Task CreateHashTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource) =>
        Parallel.ForEachAsync(filePairsToHashChannel.Reader.ReadAllAsync(cancellationToken),
            new ParallelOptions() { MaxDegreeOfParallelism = handlerContext.Request.HashParallelism, CancellationToken = cancellationToken },
            async (filePairWithPointerFileEntry, innerCancellationToken) =>
            {
                var (filePair, pointerFileEntry) = filePairWithPointerFileEntry;

                var h = await handlerContext.Hasher.GetHashAsync(filePair);

                if (h == pointerFileEntry.Hash)
                {
                    // The hash matches - this binaryfile is already restored
                }
                else
                {
                    // The hash does not match - we need to restore this binaryfile
                    await filePairsToRestoreChannel.Writer.WriteAsync(filePairWithPointerFileEntry, innerCancellationToken);
                }

            });

    private Task CreateDownloadBinariesTask(HandlerContext handlerContext, CancellationToken cancellationToken, CancellationTokenSource errorCancellationTokenSource) =>
        Parallel.ForEachAsync(filePairsToRestoreChannel.Reader.ReadAllAsync(cancellationToken),
            new ParallelOptions() { MaxDegreeOfParallelism = handlerContext.Request.DownloadParallelism, CancellationToken = cancellationToken },
            async (filePairWithPointerFileEntry, innerCancellationToken) =>
            {
                var (filePair, pointerFileEntry) = filePairWithPointerFileEntry;

                try
                {
                    if (pointerFileEntry.BinaryProperties.ParentHash is not null)
                        return;

                    // 1. Get the decrypted blob stream from storage
                    await using var ss = await handlerContext.ArchiveStorage.OpenReadChunkAsync(pointerFileEntry.BinaryProperties.Hash, cancellationToken);

                    // 2. Write to the target file
                    var fp = FilePair.FromPointerFileEntry(handlerContext.FileSystem, pointerFileEntry);
                    fp.BinaryFile.Directory.Create();

                    await using var ts = fp.BinaryFile.OpenWrite(pointerFileEntry.BinaryProperties.OriginalSize);
                    await ss.CopyToAsync(ts, innerCancellationToken);
                    await ts.FlushAsync(innerCancellationToken); // Explicitly flush

                    //    // to rehydrate list

                    //    // todo should it overwrite the binary?

                    //    // todo hydrate

                    //    // todo parenthash
                }
                catch (InvalidDataException e) when (e.Message.Contains("The archive entry was compressed using an unsupported compression method."))
                {
                    logger.LogError($"Decryption failed for file '{filePair.BinaryFile.FullName}'. The passphrase may be incorrect or the file may be corrupted.");
                    errorCancellationTokenSource.Cancel();
                    throw;
                }
                catch (Exception e)
                {
                    throw;
                }
            });
}