using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Features.Queries.PointerFileEntries;

internal class PointerFileEntriesQueryHandler : IStreamQueryHandler<PointerFileEntriesQuery, string>
{
    private readonly ILoggerFactory loggerFactory;

    public PointerFileEntriesQueryHandler(ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory;
    }

    public async IAsyncEnumerable<string> Handle(PointerFileEntriesQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var handlerContext = await new HandlerContextBuilder(request, loggerFactory)
            .BuildAsync();

        await new PointerFileEntriesQueryValidator().ValidateAndThrowAsync(request, cancellationToken); 

        await foreach(var entry in Handle(handlerContext, cancellationToken))
        {
            yield return entry;
        }
    }

    internal async IAsyncEnumerable<string> Handle(HandlerContext handlerContext, CancellationToken cancellationToken)
    {
        var afs = new AggregateFileSystem();
        afs.AddFileSystem(handlerContext.LocalFileSystem);
        afs.AddFileSystem(handlerContext.RemoteFileSystem);

        foreach (var entry in afs.EnumerateFileEntries(handlerContext.Query.Prefix, "*", SearchOption.AllDirectories))
        {
            yield return entry.FullName;
        }
    }
}