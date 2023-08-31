﻿using Arius.Core.Extensions;
using Arius.Core.Facade;
using Arius.Core.Models;
using Arius.Core.Repositories.StateDb;
using Arius.Core.Repositories;
using Azure.Storage.Blobs.Models;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Queries;

internal record PointerFileEntriesQueryOptions : IQueryOptions
{
    public string? RelativeParentPathEquals { get; init; } = null;
    public string? DirectoryNameEquals { get; init; } = null;
    public string? NameContains { get; init; } = null;

    public void Validate()
    {
        // Always succeeds
    }
}


public interface IEntryQueryResult // also implemented by Arius.UI.FileService
{
    public string RelativeParentPath { get; }
    public string DirectoryName { get; }
    public string Name { get; }
}

public interface IPointerFileEntryQueryResult : IEntryQueryResult // properties specific to PointerFileEntry. Public interface is required for type matching
{
    public long OriginalLength { get; }
    public HydrationState HydrationState { get; }
}

internal record PointerFileEntriesQueryResult : IQueryResult
{
    public required QueryResultStatus Status { get; init; }


    internal record PointerFileEntryQueryResult : IPointerFileEntryQueryResult
    {
        public string RelativeParentPath { get; init; }
        public string DirectoryName { get; init; }
        public string Name { get; init; }
        public long OriginalLength { get; init; }
        public HydrationState HydrationState { get; init; }
    }
    public IAsyncEnumerable<PointerFileEntryQueryResult>? Result { get; init; }
}

internal class PointerFileEntriesQuery : IQuery<PointerFileEntriesQueryOptions, PointerFileEntriesQueryResult>
{
    private readonly ILoggerFactory loggerFactory;
    private readonly Repository repository;

    private static AsyncLazy<Dictionary<ChunkHash, bool>>? rehydratingChunks = default;

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

    public PointerFileEntriesQueryResult Execute(PointerFileEntriesQueryOptions options)
    {
        options.Validate();

        return new PointerFileEntriesQueryResult
        {
            Status = QueryResultStatus.Success,
            Result = GetPointerFilesEntriesAsync(repository, options)
        };


        static async IAsyncEnumerable<PointerFileEntriesQueryResult.PointerFileEntryQueryResult> GetPointerFilesEntriesAsync(Repository repository, PointerFileEntriesQueryOptions options)
        {
            var relativeParentPathEquals = options.RelativeParentPathEquals is not null
                ? PointerFileEntryConverter.ToPlatformNeutralPath(options.RelativeParentPathEquals)
                : null;

            await foreach (var pfe in repository.GetPointerFileEntriesAsync(
                               pointInTimeUtc: DateTime.Now,
                               includeDeleted: false,
                               relativeParentPathEquals: relativeParentPathEquals,
                               directoryNameEquals: options.DirectoryNameEquals,
                               nameContains: options.NameContains,
                               includeChunkEntry: true))
            {
                yield return new PointerFileEntriesQueryResult.PointerFileEntryQueryResult
                {
                    RelativeParentPath = pfe.RelativeParentPath,
                    DirectoryName = pfe.DirectoryName,
                    Name = pfe.Name,

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
}