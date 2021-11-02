using Arius.Core.Models;
using Azure;
using Azure.Data.Tables;
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
        var bm = new BinaryMetadata()
        {
            Hash = bf.Hash,
            OriginalLength = bf.Length,
            ArchivedLength = archivedLength,
            IncrementalLength = incrementalLength,
            ChunkCount = chunkCount
        };

        await bmTable.AddEntityAsync(ConvertToDto(bm));
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
    private class BinaryMetadataDto : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public long OriginalLength { get; init; }
        public long ArchivedLength { get; init; }
        public long IncrementalLength { get; init; }
        public int ChunkCount { get; init; }
    }
}