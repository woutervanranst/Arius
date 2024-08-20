using System.Collections.Generic;
using Arius.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Queries.PointerFileEntriesSubdirectories;

internal class PointerFileEntriesSubdirectoriesQueryHandler : Query<PointerFileEntriesSubdirectoriesQuery, IAsyncEnumerable<string>>
{
    private readonly ILoggerFactory loggerFactory;
    private readonly Repository repository;

    public PointerFileEntriesSubdirectoriesQueryHandler(ILoggerFactory loggerFactory, Repository repository)
    {
        this.loggerFactory = loggerFactory;
        this.repository = repository;
    }

    protected override (QueryResultStatus Status, IAsyncEnumerable<string>? Result) ExecuteImpl(PointerFileEntriesSubdirectoriesQuery options)
    {
        var r = repository.GetPointerFileEntriesSubdirectoriesAsync(options.Prefix, options.Depth, options.VersionUtc);

        return (QueryResultStatus.Success, r);
    }
}