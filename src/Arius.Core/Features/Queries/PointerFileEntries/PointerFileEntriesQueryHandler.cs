using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

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
        var pointerFileEntries = handlerContext.StateRepository.GetPointerFileEntries(handlerContext.Request.Prefix, includeBinaryProperties: false);

        await foreach (var entry in pointerFileEntries.ToAsyncEnumerable().WithCancellation(cancellationToken))
        {
            yield return entry.RelativeName;
        }
    }
}