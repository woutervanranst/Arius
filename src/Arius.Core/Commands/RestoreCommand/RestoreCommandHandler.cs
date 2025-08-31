using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace Arius.Core.Commands.RestoreCommand;

public record ProgressUpdate;

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

                await using var targetFileStream = fp.BinaryFile.OpenWrite(pfe.BinaryProperties.OriginalSize);

                await decryptionStream.CopyToAsync(targetFileStream, innerCancellationToken);
                await targetFileStream.FlushAsync(innerCancellationToken); // Explicitly flush


                // todo hydrate

                // todo parenthash
            });
}