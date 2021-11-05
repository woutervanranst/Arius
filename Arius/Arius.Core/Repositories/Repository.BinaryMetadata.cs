using Arius.Core.Models;
using Azure;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    public BinaryMetadataRepository BinaryMetadata { get; init; }
    internal class BinaryMetadataRepository
    {
        internal BinaryMetadataRepository(ILogger<BinaryMetadataRepository> logger)
        {
        }

        public async Task CreateAsync(BinaryFile bf, long archivedLength, long incrementalLength, int chunkCount)
        {
            var bm = new BinaryMetadata()
            {
                Hash = bf.Hash,
                OriginalLength = bf.Length,
                ArchivedLength = archivedLength,
                IncrementalLength = incrementalLength,
                ChunkCount = chunkCount
            };

            await using var db = await AriusDbContext.GetAriusDbContext();
            await db.BinaryMetadata.AddAsync(bm);
            await db.SaveChangesAsync();

        }

        //public async Task<BinaryMetadata> GetBinaryMetadataAsync(BinaryHash bh)
        //{
        //    var dto = await bmTable.GetEntityAsync<BinaryMetadataDto>(bh.Value, BinaryMetadataDto.ROW_KEY);
        //    var bm = ConvertFromDto(dto);
        //    return bm;
        //}

        public async Task<bool> ExistsAsync(BinaryHash bh)
        {
            await using var db = await AriusDbContext.GetAriusDbContext();
            return await db.BinaryMetadata.AnyAsync(bm => bm.Hash == bh);
        }
    }
    
}