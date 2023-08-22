using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Models;
using Arius.Core.Repositories.StateDb;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using PostSharp.Constraints;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    // -- CREATE --

    public async Task<ChunkEntry> CreateChunkEntryAsync(BinaryFile bf, long archivedLength, long incrementalLength, int chunkCount, AccessTier? tier)
    {
        var ce = new ChunkEntry
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

    public async Task<ChunkEntry> CreateChunkEntryAsync(IChunk c, long originalLength, long archivedLength, AccessTier tier)
    {
        var ce = new ChunkEntry
        {
            Hash              = c.ChunkHash.Value,
            OriginalLength    = originalLength,
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

    // -- GET / READ ---

    /// <summary>
    /// Get the ChunkEntry for the given chunk.
    /// If it does not exist, throw an InvalidOperationException
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task<ChunkEntry> GetChunkEntryAsync(Hash hash)
    {
        await using var db = GetStateDbContext();
        var r = await db.ChunkEntries.SingleOrDefaultAsync(ce => ce.Hash == hash.Value);

        if (r == null)
            throw new InvalidOperationException($"Could not find ChunkEntry for '{hash}'");

        return r;
    }

    // -- UPDATE --

    // -- DELETE --
    /// <summary>
    /// Delete a ChunkEntry from the database
    /// WARNING - Only for use for testing
    /// </summary>
    internal async Task DeleteChunkEntryAsync(Hash h)
    {
        await using var db = GetStateDbContext();

        await db.ChunkEntries.Where(ce => ce.Hash == h.Value).ExecuteDeleteAsync();
    }

    // --- QUERY ---

    //[ComponentInternal(typeof(Repository))] // only for Unit testing
    //internal async IAsyncEnumerable<ChunkEntry> GetChunkEntriesAsync()
    //{
    //    await using var db = GetStateDbContext();
    //    foreach (var ce in db.ChunkEntries)
    //        yield return ce;
    //}

    /// <summary>
    /// Get the Chunk Count (by counting the ChunkEntries in the database)
    /// </summary>
    /// <returns></returns>
    public async Task<int> CountChunkEntriesAsync()
    {
        await using var db = GetStateDbContext();
        return await db.ChunkEntries.CountAsync();
    }

    public async Task<bool> ChunkExistsAsync(ChunkHash ch)
    {
        await using var db = GetStateDbContext();
        return await db.ChunkEntries.AnyAsync(c => c.Hash == ch.Value);
    }

    public async Task<long> TotalChunkIncrementalLengthAsync()
    {
        await using var db = GetStateDbContext();
        return await db.ChunkEntries.SumAsync(bp => bp.IncrementalLength);
    }

    //public async IAsyncEnumerable<ChunkEntry> QueryChunkEntries(Func<ChunkEntry, bool> filter)
    //{
    //    await using var db = GetStateDbContext();
    //    foreach (var ce in db.ChunkEntries.Where(filter))
    //        yield return ce;
    //}
}