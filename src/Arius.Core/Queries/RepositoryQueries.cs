using Arius.Core.Repositories;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Arius.Core.Queries;

public interface IGetEntriesResult
{
    public string RelativeParentPath { get; }
    public string DirectoryName { get; }
    public string Name { get; }
}

public interface IGetPointerFileEntriesResult : IGetEntriesResult
{
    public long OriginalLength { get; }
}

internal record GetPointerFileEntriesResponse(string RelativeParentPath, string DirectoryName, string Name, long OriginalLength) : IGetPointerFileEntriesResult;

internal class RepositoryQueries
{
    private readonly ILoggerFactory loggerFactory;
    private readonly Repository     repository; 

    public RepositoryQueries(ILoggerFactory loggerFactory, Repository repository)
    {
        this.loggerFactory = loggerFactory;
        this.repository    = repository;
    }

    public IAsyncEnumerable<IGetPointerFileEntriesResult> GetEntriesAsync(
        string? relativeParentPathEquals = null,
        string? directoryNameEquals = null,
        string? nameContains = null)
    {
        return repository.GetPointerFileEntriesAsync(DateTime.Now, false, relativeParentPathEquals, directoryNameEquals, nameContains, includeChunkEntry: true)
            .Select(pfe => new GetPointerFileEntriesResponse(pfe.RelativeParentPath, pfe.DirectoryName, pfe.Name, pfe.Chunk.OriginalLength));
    }
}