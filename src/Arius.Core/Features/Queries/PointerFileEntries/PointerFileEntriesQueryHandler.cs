using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Arius.Core.Shared.FileSystem;
using Zio;

namespace Arius.Core.Features.Queries.PointerFileEntries;

public abstract record Result
{
}

public record Directory : Result
{
    public required string RelativeName { get; init; }
}

public record File : Result
{
    public string? PointerFileEntry { get; init; }
    public string? BinaryFileName   { get; init; }
    public string? PointerFileName  { get; init; }
}

internal class PointerFileEntriesQueryHandler : IStreamQueryHandler<PointerFileEntriesQuery, Result>
{
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<PointerFileEntriesQueryHandler> logger;

    public PointerFileEntriesQueryHandler(ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory;
        this.logger = loggerFactory.CreateLogger<PointerFileEntriesQueryHandler>();
    }

    public async IAsyncEnumerable<Result> Handle(PointerFileEntriesQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var handlerContext = await new HandlerContextBuilder(request, loggerFactory)
            .BuildAsync();

        await new PointerFileEntriesQueryValidator().ValidateAndThrowAsync(request, cancellationToken); 

        await foreach(var entry in Handle(handlerContext, cancellationToken))
        {
            yield return entry;
        }
    }

    internal async IAsyncEnumerable<Result> Handle(HandlerContext handlerContext, CancellationToken cancellationToken)
    {
        var resultChannel = Channel.CreateUnbounded<Result>(new UnboundedChannelOptions() { /*TODO QUID ?? AllowSynchronousContinuations = true, */SingleReader = true, SingleWriter = false});

        var directoryTask = Task.Run(async () =>
        {
            var yielded = new HashSet<string>();
            foreach (var pfd in handlerContext.StateRepository.GetPointerFileDirectories(handlerContext.Query.Prefix, topDirectoryOnly: true))
            {
                var rn = pfd.RelativeName;

                yielded.Add(rn);
                await resultChannel.Writer.WriteAsync(new Directory
                {
                    RelativeName = rn
                });
            }

            foreach (var path in handlerContext.LocalFileSystem.EnumerateDirectories(handlerContext.Query.Prefix, "*", SearchOption.TopDirectoryOnly))
            {
                var rn = path.FullName + "/";
                
                if (yielded.Contains(rn))
                    continue;
                await resultChannel.Writer.WriteAsync(new Directory
                {
                    RelativeName = rn
                });
            }
        }, cancellationToken);

        var entryTask = Task.Run(async () =>
        {
            // DEBUG
            await directoryTask;

            foreach (var pfe in handlerContext.StateRepository.GetPointerFileEntries(handlerContext.Query.Prefix, topDirectoryOnly: true, includeBinaryProperties: true))
            {
                var r = new File
                {
                    PointerFileEntry = pfe.RelativeName
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
        }, cancellationToken);

        Task.WhenAll(directoryTask, entryTask).ContinueWith(task =>
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