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
        public async Task CreateManifestMetadataAsync(BinaryFile bf, long archivedLength, long incrementalLength, int chunkCount)
        {
            var mp = new ManifestMetadata()
            {
                Hash = bf.Hash,
                OriginalLength = bf.Length,
                ArchivedLength = archivedLength,
                IncrementalLength = incrementalLength,
                ChunkCount = chunkCount
            };

            await mpRepo.Add(mp);
        }

        private class CachedManifestMetadataRepository
        {

            public CachedManifestMetadataRepository(ILogger logger, IOptions options)
            {
                this.logger = logger;

                entries = new(logger, 
                    options.AccountName, options.AccountKey, $"{options.Container}{TableNameSuffix}", 
                    ConvertToDto, ConvertFromDto);
            }

            internal const string TableNameSuffix = "manifestmetadata";

            private readonly ILogger logger;

            private readonly EagerCachedConcurrentDataTableRepository<ManifestMetadataDto, ManifestMetadata> entries;

    
            public async Task Add(ManifestMetadata item)
            {
                await entries.Add(item);
            }

            private ManifestMetadata ConvertFromDto(ManifestMetadataDto dto)
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
            
            private ManifestMetadataDto ConvertToDto(ManifestMetadata mp)
            {
                return new()
                {
                    PartitionKey = mp.Hash.Value,
                    RowKey = "ManifestMetadata",

                    OriginalLength = mp.OriginalLength,
                    ArchivedLength = mp.ArchivedLength,
                    IncrementalLength = mp.IncrementalLength,
                    ChunkCount = mp.ChunkCount
                };
            }

            private class ManifestMetadataDto : TableEntity
            {
                public long OriginalLength { get; init; }
                public long ArchivedLength { get; init; }
                public long IncrementalLength { get; init; }
                public int ChunkCount { get; init; }
            }
        }
    }
}
