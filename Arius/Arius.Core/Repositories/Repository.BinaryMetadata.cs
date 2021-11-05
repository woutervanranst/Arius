using Arius.Core.Models;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Arius.Core.Extensions;

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

    public async Task<BinaryMetadata> GetBinaryMetadataAsync(BinaryHash bh)
    {
        var dto = await bmTable.GetEntityAsync<BinaryMetadataDto>(bh.Value, BinaryMetadataDto.ROW_KEY);
        var bm = ConvertFromDto(dto);
        return bm;
    }

    public async Task<bool> BinaryMetadataExistsAsync(BinaryHash bh)
    {
        try
        {
            //TODO cache this -- gets visited twice if a binary and pointer exist

            var dto = await bmTable.GetEntityAsync<BinaryMetadataDto>(
                partitionKey: bh.Value,
                rowKey: BinaryMetadataDto.ROW_KEY,
                select: "PartitionKey".SingleToArray()); //we dont need any data from the dto, selecting an empty string doesnt work so only request the PartitionKey
            return dto is not null;
        }
        catch (RequestFailedException e)
        {
            return false;
        }
    }

    private static BinaryMetadata ConvertFromDto(BinaryMetadataDto dto)
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

    private static BinaryMetadataDto ConvertToDto(BinaryMetadata bm)
    {
        return new()
        {
            PartitionKey = bm.Hash.Value,
            RowKey = BinaryMetadataDto.ROW_KEY,

            OriginalLength = bm.OriginalLength,
            ArchivedLength = bm.ArchivedLength,
            IncrementalLength = bm.IncrementalLength,
            ChunkCount = bm.ChunkCount
        };
    }
    private class BinaryMetadataDto : ITableEntity
    {
        public const string ROW_KEY = "BinaryMetadata";

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