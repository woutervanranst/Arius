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
        public async Task CreateManifestPropertyAsync(BinaryFile bf, long archivedLength, int chunkCount)
        {
            var mp = new ManifestProperties()
            {
                Hash = bf.Hash,
                OriginalLength = bf.Length,
                ArchivedLength = archivedLength,
                ChunkCount = chunkCount
            };

            await mpRepo.Add(mp);
        }

        private class CachedManifestPropertiesRepository
        {

            public CachedManifestPropertiesRepository(ILogger logger, IOptions options)
            {
                this.logger = logger;

                entries = new(logger, 
                    options.AccountName, options.AccountKey, $"{options.Container}{TableNameSuffix}", 
                    ConvertToDto, ConvertFromDto);
            }

            internal const string TableNameSuffix = "manifestproperties";

            private readonly ILogger logger;

            private readonly EagerCachedConcurrentDataTableRepository<ManifestPropertiesDto, ManifestProperties> entries;

    
            public async Task Add(ManifestProperties item)
            {
                await entries.Add(item);
            }


            private ManifestProperties ConvertFromDto(ManifestPropertiesDto dto)
            {
                return new()
                {
                    Hash = new(dto.PartitionKey),
                    OriginalLength = dto.OriginalLength,
                    ArchivedLength = dto.ArchivedLength,
                    ChunkCount = dto.ChunkCount
                };
            }
            private ManifestPropertiesDto ConvertToDto(ManifestProperties mp)
            {
                return new()
                {
                    PartitionKey = mp.Hash.Value,
                    RowKey = "ManifestProperties",

                    OriginalLength = mp.OriginalLength,
                    ArchivedLength = mp.ArchivedLength,
                    ChunkCount = mp.ChunkCount
                };
            }

            private class ManifestPropertiesDto : TableEntity
            {
                public long OriginalLength { get; init; }
                public long ArchivedLength { get; init; }
                public int ChunkCount { get; init; }
            }
        }
    }
}
