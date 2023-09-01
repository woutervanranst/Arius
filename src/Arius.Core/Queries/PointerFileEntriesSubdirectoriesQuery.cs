using Arius.Core.Repositories;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace Arius.Core.Queries;

internal record PointerFileEntriesSubdirectoriesQueryOptions : QueryOptions
{
    public required string Prefix { get; init; }

    public override void Validate()
    {
        if (!Prefix.EndsWith('/'))
            throw new ArgumentException($"{nameof(Prefix)} argument must end with '/'");
    }
}

internal class PointerFileEntriesSubdirectoriesQuery : Query<PointerFileEntriesSubdirectoriesQueryOptions, IAsyncEnumerable<string>>
{
    private readonly ILoggerFactory loggerFactory;
    private readonly Repository repository;

    public PointerFileEntriesSubdirectoriesQuery(ILoggerFactory loggerFactory, Repository repository)
    {
        this.loggerFactory = loggerFactory;
        this.repository    = repository;
    }


    protected override (QueryResultStatus Status, IAsyncEnumerable<string>? Result) ExecuteImpl(PointerFileEntriesSubdirectoriesQueryOptions options)
    {
        var r = repository.GetPointerFileEntriesSubdirectoriesAsync(options.Prefix);

        return (QueryResultStatus.Success, r);
    }
}