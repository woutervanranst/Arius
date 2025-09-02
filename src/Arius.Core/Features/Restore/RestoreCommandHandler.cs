using Arius.Core.Shared.Extensions;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.StateRepositories;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace Arius.Core.Features.Restore;

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

    private readonly Channel<PointerFileEntry> pointerFileEntriesToRestoreChannel = ChannelExtensions.CreateBounded<PointerFileEntry>(capacity: 25, singleWriter: true, singleReader: false);

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

        var pointerFileEntriesToRestoreTask = CreatePointerFileEntriesToRestoreTask(handlerContext, errorCancellationToken, errorCancellationTokenSource);
        var downloadBinariesTask            = CreateDownloadBinariesTask(handlerContext, errorCancellationToken, errorCancellationTokenSource);

        try
        {
            await Task.WhenAll(pointerFileEntriesToRestoreTask, downloadBinariesTask);
        }
        catch (Exception)
        {
            // TODO
            throw;
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
                    var pfes           = handlerContext.StateRepository.GetPointerFileEntries(fp.FullName, true);

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
                if (pfe.BinaryProperties.ParentHash is not null)
                    return;

                // 1. Get the decrypted blob stream from storage
                await using var ss = await handlerContext.ArchiveStorage.OpenReadChunkAsync(pfe.BinaryProperties.Hash, cancellationToken);

                // 2. Write to the target file
                var fp = FilePair.FromPointerFileEntry(handlerContext.FileSystem, pfe);
                fp.BinaryFile.Directory.Create();

                await using var ts = fp.BinaryFile.OpenWrite(pfe.BinaryProperties.OriginalSize);

                await ss.CopyToAsync(ts, innerCancellationToken);
                await ts.FlushAsync(innerCancellationToken); // Explicitly flush

                // todo should it overwrite the binary?

                // todo hydrate

                // todo parenthash
            });
}