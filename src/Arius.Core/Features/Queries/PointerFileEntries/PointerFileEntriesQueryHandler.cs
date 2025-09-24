using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Arius.Core.Features.Queries.PointerFileEntries;

public abstract record Result
{
}

public record Directory : Result
{
    public string RelativeName { get; init; }
}

public record File : Result
{
    public string RelativeName { get; init; }
}

internal class PointerFileEntriesQueryHandler : IStreamQueryHandler<PointerFileEntriesQuery, Result>
{
    private readonly ILoggerFactory loggerFactory;

    public PointerFileEntriesQueryHandler(ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory;
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
        //var afs = new AggregateFileSystem(); // https://github.com/xoofx/zio/tree/main/doc#aggregatefilesystem
        //afs.AddFileSystem(handlerContext.LocalFileSystem);
        //afs.AddFileSystem(handlerContext.RemoteFileSystem);

        //foreach (var entry in afs.EnumerateFileEntries(handlerContext.Query.Prefix, "*", SearchOption.TopDirectoryOnly))
        //{
        //    yield return entry.FullName;
        //}

        //var sr = handlerContext.StateRepository.

        var resultChannel = Channel.CreateUnbounded<Result>(new UnboundedChannelOptions() { /*TODO QUID ?? AllowSynchronousContinuations = true, */SingleReader = true, SingleWriter = false});

        var directoryTask = Task.Run(() =>
        {
            foreach (var pfd in handlerContext.StateRepository.GetPointerFileDirectories(handlerContext.Query.Prefix, topDirectoryOnly: true))
            {
                resultChannel.Writer.TryWrite(new Directory
                {
                    RelativeName = pfd.RelativeName
                });

            }
        }, cancellationToken);

        var entryTask = Task.Run(() =>
        {
            foreach (var pfe in handlerContext.StateRepository.GetPointerFileEntries(handlerContext.Query.Prefix, topDirectoryOnly: true, includeBinaryProperties: true))
            {
                resultChannel.Writer.TryWrite(new File
                {
                    RelativeName = pfe.RelativeName
                });
            }
        }, cancellationToken);

        Task.WhenAll(directoryTask, entryTask).ContinueWith(_ => resultChannel.Writer.Complete());

        await foreach (var r in resultChannel.Reader.ReadAllAsync(cancellationToken))
            yield return r;
    }
}