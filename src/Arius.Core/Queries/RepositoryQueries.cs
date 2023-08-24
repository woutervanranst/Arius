using Arius.Core.Repositories;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Azure.Storage.Blobs.Models;

namespace Arius.Core.Queries;

public interface IEntryQueryResult
{
    public string RelativeParentPath { get; }
    public string DirectoryName { get; }
    public string Name { get; }
}

public interface IPointerFileEntryQueryResult : IEntryQueryResult
{
    public long           OriginalLength { get; }
    public HydrationState HydrationState { get; }
}

public enum HydrationState
{
    Hydrated,
    NotHydrated,
    NeedsToBeQueried
}


internal class RepositoryQueries
{
    private readonly ILoggerFactory loggerFactory;
    private readonly Repository     repository; 

    public RepositoryQueries(ILoggerFactory loggerFactory, Repository repository)
    {
        this.loggerFactory = loggerFactory;
        this.repository    = repository;
    }

    record GetPointerFileEntriesResult : IPointerFileEntryQueryResult
    {
        public string         RelativeParentPath { get; init; }
        public string         DirectoryName      { get; init; }
        public string         Name               { get; init; }
        public long           OriginalLength     { get; init; }
        public HydrationState HydrationState     { get; init; }
    }

    public IAsyncEnumerable<IPointerFileEntryQueryResult> QueryEntriesAsync(
        string? relativeParentPathEquals = null,
        string? directoryNameEquals = null,
        string? nameContains = null)
    {
        return repository.GetPointerFileEntriesAsync(DateTime.Now, false, relativeParentPathEquals, directoryNameEquals, nameContains, includeChunkEntry: true)
            .Select(pfe =>
            {
                return new GetPointerFileEntriesResult()
                {
                    RelativeParentPath = pfe.RelativeParentPath,
                    DirectoryName      = pfe.DirectoryName,
                    Name               = pfe.Name,

                    OriginalLength = pfe.Chunk.OriginalLength,
                    HydrationState = GetHydrationState(pfe.Chunk.AccessTier)
                };
            });

        HydrationState GetHydrationState(AccessTier? accessTier)
        {
            if (accessTier is null)
                return HydrationState.NeedsToBeQueried; // in case of chunked
            if (accessTier == AccessTier.Archive)
                return HydrationState.NotHydrated;

            return HydrationState.Hydrated;

        }
    }
}