using Arius.Core.Models;
using Azure;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    public async Task CreateBinaryMetadataAsync(BinaryFile bf, long archivedLength, long incrementalLength, int chunkCount)
    {
        var mp = new BinaryMetadata()
        {
            Hash = bf.Hash,
            OriginalLength = bf.Length,
            ArchivedLength = archivedLength,
            IncrementalLength = incrementalLength,
            ChunkCount = chunkCount
        };

        await bmRepo.AddAsync(mp);
    }
}