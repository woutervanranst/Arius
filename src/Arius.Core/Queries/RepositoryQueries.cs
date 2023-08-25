using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Repositories.StateDb;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
    Hydrating,
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

        // Eager load the rehydrating chunks
        rehydratingChunks ??= Task.Run(async () =>
        {
            return await repository.GetRehydratedChunksAsync()
                .ToDictionaryAsync(c => c.ChunkHash, c => c.HydrationPending);
        });
    }

    record GetPointerFileEntriesResult : IPointerFileEntryQueryResult
    {
        public string         RelativeParentPath { get; init; }
        public string         DirectoryName      { get; init; }
        public string         Name               { get; init; }
        public long           OriginalLength     { get; init; }
        public HydrationState HydrationState     { get; init; }
    }

    private static Task<Dictionary<ChunkHash, bool>>? rehydratingChunks = default;

    public async IAsyncEnumerable<IPointerFileEntryQueryResult> QueryEntriesAsync(
        string? relativeParentPathEquals = null,
        string? directoryNameEquals = null,
        string? nameContains = null)
    {
        if (relativeParentPathEquals is not null)
            relativeParentPathEquals = PointerFileEntryConverter.ToPlatformNeutralPath(relativeParentPathEquals);

        await foreach (var pfe in repository.GetPointerFileEntriesAsync(
                           pointInTimeUtc: DateTime.Now,
                           includeDeleted: false,
                           relativeParentPathEquals: relativeParentPathEquals,
                           directoryNameEquals: directoryNameEquals,
                           nameContains: nameContains,
                           includeChunkEntry: true))
        {
            yield return new GetPointerFileEntriesResult()
            {
                RelativeParentPath = pfe.RelativeParentPath,
                DirectoryName      = pfe.DirectoryName,
                Name               = pfe.Name,

                OriginalLength = pfe.Chunk.OriginalLength,
                HydrationState = await GetHydrationStateAsync(pfe.Chunk)
            };
        }
            

        async Task<HydrationState> GetHydrationStateAsync(ChunkEntry c)
        {
            if (c.AccessTier is null)
                return HydrationState.NeedsToBeQueried; // in case of chunked
            if (c.AccessTier == AccessTier.Archive)
            {
                if ((await rehydratingChunks).TryGetValue(new ChunkHash(c.Hash), out var hydrationPending))
                {
                    if (hydrationPending)
                        return HydrationState.Hydrating; // It s in the archive tier but a hydrating copy is being made
                    else
                        return HydrationState.Hydrated; // It s in the archive tier but there is a hydrated copy
                }
                else
                    return HydrationState.NotHydrated; // It s in the archive tier
            }

            return HydrationState.Hydrated;
        }
    }
}