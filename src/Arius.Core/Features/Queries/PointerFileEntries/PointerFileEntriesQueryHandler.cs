using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Storage;
using Zio;

namespace Arius.Core.Features.Queries.PointerFileEntries;

internal class PointerFileEntriesQueryHandler : IStreamQueryHandler<PointerFileEntriesQuery, PointerFileEntriesQueryResult>
{
    private readonly ILoggerFactory                          loggerFactory;
    private readonly ILogger<PointerFileEntriesQueryHandler> logger;

    public PointerFileEntriesQueryHandler(ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory;
        this.logger        = loggerFactory.CreateLogger<PointerFileEntriesQueryHandler>();
    }

    public async IAsyncEnumerable<PointerFileEntriesQueryResult> Handle(PointerFileEntriesQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var handlerContext = await new HandlerContextBuilder(request, loggerFactory)
            .BuildAsync();

        await new PointerFileEntriesQueryValidator().ValidateAndThrowAsync(request, cancellationToken);

        await foreach (var entry in Handle(handlerContext, cancellationToken))
        {
            yield return entry;
        }
    }

    internal async IAsyncEnumerable<PointerFileEntriesQueryResult> Handle(HandlerContext handlerContext, CancellationToken cancellationToken)
    {
        var resultChannel = Channel.CreateUnbounded<PointerFileEntriesQueryResult>(new UnboundedChannelOptions()
        {
            /*TODO QUID ?? AllowSynchronousContinuations = true, */SingleReader = true, SingleWriter = false
        });

        var directoryTask = Task.Run(async () =>
        {
            var yielded = new HashSet<string>();
            foreach (var pfd in handlerContext.StateRepository.GetPointerFileDirectories(handlerContext.Query.Prefix, topDirectoryOnly: true))
            {
                var rn = pfd.RelativeName;

                yielded.Add(rn);
                await resultChannel.Writer.WriteAsync(new PointerFileEntriesQueryDirectoryResult
                {
                    RelativeName = rn
                });
            }

            foreach (var path in handlerContext.LocalFileSystem.EnumerateDirectories(handlerContext.Query.Prefix, "*", SearchOption.TopDirectoryOnly))
            {
                var rn = path.FullName + "/";

                if (yielded.Contains(rn))
                    continue; // this directory was already yielded when iterating on the StateRepository
                await resultChannel.Writer.WriteAsync(new PointerFileEntriesQueryDirectoryResult
                {
                    RelativeName = rn
                });
            }
        }, cancellationToken);

        var fileTask = Task.Run(async () =>
        {
#if DEBUG
            await directoryTask;
#endif

            // 1. Iterate over the PointerFileEntries that are in Azure
            var yielded = new HashSet<string>();
            foreach (var pfe in handlerContext.StateRepository.GetPointerFileEntries(handlerContext.Query.Prefix, topDirectoryOnly: true, includeBinaryProperties: true))
            {
                var rn = pfe.RelativeName;

                yielded.Add(rn);
                var r = new PointerFileEntriesQueryFileResult
                {
                    PointerFileEntry = rn,
                    OriginalSize     = pfe.BinaryProperties.OriginalSize,
                    Hydrated         = pfe.BinaryProperties.StorageTier != StorageTier.Archive
                };

                var fp = FilePair.FromPointerFileEntry(handlerContext.LocalFileSystem, pfe);
                r = fp.Type switch
                {
                    FilePairType.PointerFileOnly           => r with { PointerFileName = fp.PointerFile.FullName },
                    FilePairType.BinaryFileOnly            => r with { BinaryFileName = fp.BinaryFile.FullName },
                    FilePairType.BinaryFileWithPointerFile => r with { PointerFileName = fp.PointerFile.FullName, BinaryFileName = fp.BinaryFile.FullName },
                    FilePairType.None                      => r,
                    _                                      => throw new ArgumentOutOfRangeException()
                };

                await resultChannel.Writer.WriteAsync(r);
            }

            // 2. Complement these with the ones that are (only) on disk
            foreach (var path in handlerContext.LocalFileSystem.EnumerateFiles(handlerContext.Query.Prefix, "*", SearchOption.TopDirectoryOnly))
            {
                var fp = FilePair.FromBinaryFilePath(handlerContext.LocalFileSystem, path);

                if (yielded.Contains(fp.PointerFile.FullName))
                    continue; // this file was already yielded when iterating on the StateRepository

                // This filepair exists only on disk

                var r = fp.Type switch
                {
                    FilePairType.PointerFileOnly           => new PointerFileEntriesQueryFileResult { PointerFileName = fp.PointerFile.FullName, OriginalSize   = -1 }, // NOTE this is an orphaned file
                    FilePairType.BinaryFileOnly            => new PointerFileEntriesQueryFileResult { BinaryFileName  = fp.BinaryFile.FullName, OriginalSize    = fp.BinaryFile.Length },
                    FilePairType.BinaryFileWithPointerFile => new PointerFileEntriesQueryFileResult { PointerFileName = fp.PointerFile.FullName, BinaryFileName = fp.BinaryFile.FullName, OriginalSize = fp.BinaryFile.Length },
                    FilePairType.None                      => throw new InvalidOperationException("This should not happen"),
                    _                                      => throw new ArgumentOutOfRangeException()
                };

                await resultChannel.Writer.WriteAsync(r);
            }
        }, cancellationToken);

        Task.WhenAll(directoryTask, fileTask).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                logger.LogError(task.Exception, "Tasks failed during pointer file entries query");
                resultChannel.Writer.Complete(task.Exception.GetBaseException());
            }
            else
            {
                resultChannel.Writer.Complete();
            }
        }, TaskContinuationOptions.ExecuteSynchronously);

        await foreach (var r in resultChannel.Reader.ReadAllAsync(cancellationToken))
            yield return r;
    }
}