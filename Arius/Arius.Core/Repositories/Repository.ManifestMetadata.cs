using Arius.Core.Models;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Repositories
{
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

            await bmRepo.Add(mp);
        }


        private class CachedBinaryMetadataRepository
        {
            public CachedBinaryMetadataRepository(ILogger logger, IOptions options)
            {
                this.logger = logger;

                entries = new(logger, 
                    options.AccountName, options.AccountKey, $"{options.Container}{TableNameSuffix}", 
                    ConvertToDto, ConvertFromDto);
            }

            internal const string TableNameSuffix = "binarymetadata";

            private readonly ILogger logger;

            private readonly EagerCachedConcurrentDataTableRepository<BinaryMetadataDto, BinaryMetadata> entries;

    
            public async Task Add(BinaryMetadata item)
            {
                await entries.Add(item);
            }

            private BinaryMetadata ConvertFromDto(BinaryMetadataDto dto)
            {
                return new()
                {
                    Hash = new(dto.PartitionKey),
                    OriginalLength = dto.OriginalLength,
                    ArchivedLength = dto.ArchivedLength,
                    IncrementalLength = dto.IncrementalLength,
                    ChunkCount = dto.ChunkCount
                };
            }
            
            private BinaryMetadataDto ConvertToDto(BinaryMetadata bm)
            {
                return new()
                {
                    PartitionKey = bm.Hash.Value,
                    RowKey = "BinaryMetadata",

                    OriginalLength = bm.OriginalLength,
                    ArchivedLength = bm.ArchivedLength,
                    IncrementalLength = bm.IncrementalLength,
                    ChunkCount = bm.ChunkCount
                };
            }

            private class BinaryMetadataDto : TableEntity
            {
                public long OriginalLength { get; init; }
                public long ArchivedLength { get; init; }
                public long IncrementalLength { get; init; }
                public int ChunkCount { get; init; }
            }
        }
    }
}
