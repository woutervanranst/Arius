using Arius.Core.Models;
using Arius.Core.Repositories.BlobRepository;
using Arius.Core.Repositories.StateDb;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    public IAsyncEnumerable<ChunkBlobEntry> GetAllChunkBlobs() => container.Chunks.GetBlobEntriesAsync();

    public async Task<ChunkBlob> GetChunkBlobAsync(ChunkHash chunkHash) => await container.Chunks.GetBlobAsync(chunkHash);

    /// <summary>
    /// Get a hydrated chunk blob with the specified ChunkHash
    /// Returns null if no hydrated chunk exists.
    /// </summary>
    public async Task<ChunkBlob?> GetHydratedChunkBlobAsync(ChunkHash chunkHash)
    {
        var b = await container.Chunks.GetBlobAsync(chunkHash);

        if (!b.Exists)
            throw new InvalidOperationException($"Could not find Chunk {chunkHash.Value}");

        if (b.Hydrated)
            // the chunk in Chunks is hydrated
            return b;

        b = await container.RehydratedChunks.GetBlobAsync(chunkHash);

        if (b.Exists && b.Hydrated)
            // there is a hydrated chunk in the RehydratedChunks folder
            return b;

        // no hydrated chunk exists
        logger.LogDebug($"No hydrated chunk found for {chunkHash}");
        return null;
    }

    ///// <summary>
    ///// Get the RemoteEncryptedChunkBlobItem - either from permanent cold storage or from temporary rehydration storage
    ///// If the chunk does not exist, throws an InvalidOperationException
    ///// If requireHydrated is true and the chunk does not exist in cold storage, returns null
    ///// </summary>
    //public ChunkBlobBase GetChunkBlobByHash(ChunkHash chunkHash, bool requireHydrated)
    //{
    //    var blobName = GetChunkBlobName(ChunksFolderName, chunkHash);
    //    var cb1 = GetChunkBlobByName(blobName);

    //    if (cb1 is null)
    //        throw new InvalidOperationException($"Could not find Chunk {chunkHash.Value}");

    //    // if we don't need a hydrated chunk, return this one
    //    if (!requireHydrated)
    //        return cb1;

    //    // if we require a hydrated chunk, and this one is hydrated, return this one
    //    if (requireHydrated && cb1.Downloadable)
    //        return cb1;

    //    blobName = GetChunkBlobName(RehydratedChunksFolderName, chunkHash);
    //    var cb2 = GetChunkBlobByName(blobName);

    //    if (cb2 is null || !cb2.Downloadable)
    //    {
    //        // no hydrated chunk exists
    //        logger.LogDebug($"No hydrated chunk found for {chunkHash}");
    //        return null;
    //    }
    //    else
    //        return cb2;
    //}




    public async Task<bool> ChunkExistsAsync(ChunkHash ch)
    {
        await using var db = GetStateDbContext();
        return await db.ChunkEntries.AnyAsync(c => c.Hash == ch.Value);
    }

    /// <summary>
    /// Get the length of the chunk as it is stored (the archived length - not the original length)
    /// </summary>
    public async Task<long> GetChunkLengthAsync(ChunkHash chunkHash)
    {
        var ce = await GetChunkEntryAsync(chunkHash);
        return ce.ArchivedLength;
    }
    
    public async Task<long> TotalChunkIncrementalLengthAsync()
    {
        await using var db = GetStateDbContext();
        return await db.ChunkEntries.SumAsync(bp => bp.IncrementalLength);
    }

    public async Task<ChunkEntry> CreateChunkEntryAsync(BinaryFile bf, long archivedLength, long incrementalLength, int chunkCount, AccessTier? tier)
    {
        var ce = new ChunkEntry()
        {
            Hash              = bf.ChunkHash.Value,
            OriginalLength    = bf.Length,
            ArchivedLength    = archivedLength,
            IncrementalLength = incrementalLength,
            ChunkCount        = chunkCount,
            AccessTier        = tier
        };

        await SaveChunkEntryAsync(ce);

        return ce;
    }

    public async Task<ChunkEntry> CreateChunkEntryAsync(IChunk c, long archivedLength, AccessTier tier)
    {
        var ce = new ChunkEntry()
        {
            Hash              = c.ChunkHash.Value,
            OriginalLength    = c.Length,
            ArchivedLength    = archivedLength,
            IncrementalLength = archivedLength, // by definition - for a chunk the incremental length == the archived length
            ChunkCount        = 1, // by definition
            AccessTier        = tier
        };

        await SaveChunkEntryAsync(ce);

        return ce;
    }

    private async Task SaveChunkEntryAsync(ChunkEntry ce)
    {
        await using var db = GetStateDbContext();
        await db.ChunkEntries.AddAsync(ce);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Get the ChunkEntry for the given chunk.
    /// If it does not exist, throw an InvalidOperationException
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task<ChunkEntry> GetChunkEntryAsync(Hash hash)
    {
        await using var db = GetStateDbContext();
        var             r  = await db.ChunkEntries.SingleOrDefaultAsync(ce => ce.Hash == hash.Value);

        if (r == null)
            throw new InvalidOperationException($"Could not find ChunkEntry for '{hash}'");

        return r;
    }





    public async Task HydrateChunkAsync(ChunkHash chunkHash)
    {
        logger.LogDebug($"Checking hydration for chunk {chunkHash}");

        var blobToHydrate = await GetChunkBlobAsync(chunkHash);

        if (blobToHydrate.AccessTier != AccessTier.Archive)
            throw new InvalidOperationException($"Calling Hydrate on a blob that is already hydrated ({blobToHydrate.Name})");

        var hydratedItem = await container.RehydratedChunks.GetBlobAsync(blobToHydrate.ChunkHash);

        if (!hydratedItem.Exists)
        {
            //Start hydration
            await hydratedItem.StartCopyFromUriAsync(
                blobToHydrate.Uri,
                new BlobCopyFromUriOptions { AccessTier = AccessTier.Cool, RehydratePriority = RehydratePriority.Standard });

            logger.LogInformation($"Hydration started for '{blobToHydrate.ChunkHash}'");
        }
        else
        {
            // Get hydration status
            // https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blob-rehydration

            if (hydratedItem.HydrationPending)
                logger.LogInformation($"Hydration pending for '{blobToHydrate.ChunkHash}'");
            else
                logger.LogInformation($"Hydration done for '{blobToHydrate.ChunkHash}'");
        }
    }

    public async Task DeleteHydratedChunksFolderAsync()
    {
        logger.LogInformation("Deleting temporary hydration folder");

        await foreach (var be in container.RehydratedChunks.GetBlobEntriesAsync())
            await container.RehydratedChunks.DeleteBlobAsync(be);
    }
}
