using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Facade;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace Arius.Core.Queries;

internal record PointerFileEntriesQueryOptions : QueryOptions
{
    public string? RelativeDirectory { get; init; } = null; 

    public override void Validate()
    {
        // Always succeeds
    }
}


public interface IEntryQueryResult // also implemented by Arius.UI.FileService
{
    public string RelativeName { get; }
}
public interface IPointerFileEntryQueryResult : IEntryQueryResult // properties specific to PointerFileEntry. Public interface is required for type matching
{
    public long OriginalLength { get; }
    public HydrationState HydrationState { get; }
}


internal class PointerFileEntriesQuery : Query<PointerFileEntriesQueryOptions, IAsyncEnumerable<IPointerFileEntryQueryResult>>
{
    private readonly ILoggerFactory loggerFactory;
    private readonly Repository repository;

    private static AsyncLazy<Dictionary<ChunkHash, bool>>? rehydratingChunks;

    public PointerFileEntriesQuery(ILoggerFactory loggerFactory, Repository repository)
    {
        this.loggerFactory = loggerFactory;
        this.repository = repository;

        // Lazy load the rehydrating chunks
        rehydratingChunks ??= new AsyncLazy<Dictionary<ChunkHash, bool>>(async () =>
        {
            return await repository.GetRehydratedChunksAsync()
                .ToDictionaryAsync(c => c.ChunkHash, c => c.HydrationPending);
        });
    }

    protected override (QueryResultStatus Status, IAsyncEnumerable<IPointerFileEntryQueryResult>? Result) ExecuteImpl(PointerFileEntriesQueryOptions options)
    {
        return (QueryResultStatus.Success, GetPointerFilesEntriesAsync(repository, options));


        static async IAsyncEnumerable<IPointerFileEntryQueryResult> GetPointerFilesEntriesAsync(Repository repository, PointerFileEntriesQueryOptions options)
        {
            //await foreach (var pfe in repository.Kak(options.RelativeNameStartsWith.ToPlatformNeutralPath()))
            await foreach (var pfe in repository.GetPointerFileEntriesAsync(
                               pointInTimeUtc: DateTime.Now,
                               includeDeleted: false,
                               relativeDirectory: options.RelativeDirectory,
                               includeChunkEntry: true))
            {
                yield return new PointerFileEntryQueryResult
                {
                    RelativeName = pfe.RelativeName,

                    OriginalLength = pfe.Chunk.OriginalLength,
                    HydrationState = await GetHydrationStateAsync(pfe.Chunk)
                };
            }


            static async Task<HydrationState> GetHydrationStateAsync(ChunkEntry c)
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

    private record PointerFileEntryQueryResult : IPointerFileEntryQueryResult
    {
        public string         RelativeName       { get; init; }
        public long           OriginalLength     { get; init; }
        public HydrationState HydrationState     { get; init; }
    }
}