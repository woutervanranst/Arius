using System;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Models;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    internal async Task<BinaryProperties> CreateBinaryPropertiesAsync(BinaryFile bf, long archivedLength, long incrementalLength, int chunkCount)
    {
        var bp = new BinaryProperties()
        {
            Hash              = bf.BinaryHash,
            OriginalLength    = bf.Length,
            ArchivedLength    = archivedLength,
            IncrementalLength = incrementalLength,
            ChunkCount        = chunkCount
        };

        await using var db = GetStateDbContext();
        await db.BinaryProperties.AddAsync(bp);
        await db.SaveChangesAsync();

        return bp;
    }

    public async Task<BinaryProperties> GetBinaryPropertiesAsync(BinaryHash bh)
    {
        try
        {
            await using var db = GetStateDbContext();
            return db.BinaryProperties.Single(bp => bp.Hash == bh);
        }
        catch (InvalidOperationException e) when (e.Message == "Sequence contains no elements")
        {
            throw new InvalidOperationException($"Could not find BinaryProperties for '{bh}'", e);
        }
    }
}